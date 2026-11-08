"""Original, fixed-budget outcome learner. Never starts games or loads existing weights."""
import os
for variable in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS"):
    os.environ[variable] = "1"
import argparse
import collections
import datetime
import hashlib
import json
import pathlib
import time
import numpy as np


def require(condition, message):
    if not condition:
        raise ValueError(message)


def encode(board, turn):
    require(len(board) == 32 and turn in (-1, 1), "Invalid board/turn")
    pieces = np.asarray(board, dtype=np.int8)
    require(all(int(p) in (-2, -1, 0, 1, 2) for p in board), "Invalid pieces")
    if turn == -1:
        pieces = -pieces[::-1]
    return np.concatenate([pieces == p for p in (1, 2, -1, -2)]).astype(float) / np.sqrt(24)


def arrays(rows):
    counts = collections.Counter(r["gameId"] for r in rows)
    require(bool(counts), "Empty split after filtering")
    x = np.array([encode(r["board"], r["turn"]) for r in rows])
    y = np.array([r["result"] * r["turn"] for r in rows], dtype=float)
    q = np.array([1 / (len(counts) * counts[r["gameId"]]) for r in rows])
    return x, y, q


def loss_gradient(x, y, q, weights, bias, l2):
    prediction = np.tanh(x @ weights + bias)
    error = prediction - y
    derivative = 2 * q * error * (1 - prediction * prediction)
    loss = float(q @ (error * error) + l2 * (weights @ weights))
    return loss, x.T @ derivative + 2 * l2 * weights, float(derivative.sum())


def verify():
    rng = np.random.default_rng(177)
    x = rng.normal(size=(11, 7)); y = rng.uniform(-1, 1, size=11)
    q = np.full(11, 1/11); w = rng.normal(size=7) * .1; b = .07
    _, grad, bg = loss_gradient(x, y, q, w, b, .001)
    eps = 1e-6
    errors = []
    for i in range(7):
        d = np.zeros(7); d[i] = eps
        finite = (loss_gradient(x,y,q,w+d,b,.001)[0] - loss_gradient(x,y,q,w-d,b,.001)[0])/(2*eps)
        errors.append(abs(finite-grad[i]))
    finite = (loss_gradient(x,y,q,w,b+eps,.001)[0] - loss_gradient(x,y,q,w,b-eps,.001)[0])/(2*eps)
    errors.append(abs(finite-bg))
    require(max(errors) < 1e-8, "Gradient check failed")
    board = [0]*32; board[2]=1;board[20]=-2
    require(np.array_equal(encode(board,1), encode([-p for p in board[::-1]],-1)), "Perspective rotation failed")
    xx=np.array([[1.],[-1.]]); yy=np.array([1.,-1.]);qq=np.array([.5,.5]);ww=np.zeros(1);bb=0.
    initial=loss_gradient(xx,yy,qq,ww,bb,0)[0]
    for _ in range(100):
        _,gw,gb=loss_gradient(xx,yy,qq,ww,bb,0);ww-=.1*gw;bb-=.1*gb
    final=loss_gradient(xx,yy,qq,ww,bb,0)[0]
    require(final < initial*.02, "Synthetic convergence failed")
    return dict(maxGradientError=max(errors),perspectiveEquivalent=True,syntheticInitialLoss=initial,syntheticFinalLoss=final,realGames=0)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--verify",action="store_true")
    parser.add_argument("--input");parser.add_argument("--plan");parser.add_argument("--output")
    parser.add_argument("--cpu",type=int)
    args=parser.parse_args()
    checks=verify()
    if args.verify:
        print(json.dumps(checks));return
    require(all((args.input,args.plan,args.output)), "Input, plan and new output directory required")
    require(args.cpu is not None and args.cpu >= 0, "Explicit logical CPU required")
    if os.name == "nt":
        import ctypes
        k=ctypes.WinDLL("kernel32",use_last_error=True)
        k.GetCurrentProcess.restype=ctypes.c_void_p
        k.SetProcessAffinityMask.argtypes=[ctypes.c_void_p,ctypes.c_size_t]
        k.SetPriorityClass.argtypes=[ctypes.c_void_p,ctypes.c_uint]
        process=k.GetCurrentProcess()
        require(k.SetProcessAffinityMask(process,1<<args.cpu), "Cannot set CPU affinity")
        require(k.SetPriorityClass(process,0x4000), "Cannot set BelowNormal priority")
    else:
        os.sched_setaffinity(0,{args.cpu})
    plan_path=pathlib.Path(args.plan); input_path=pathlib.Path(args.input)
    plan_bytes=plan_path.read_bytes()
    script_path=pathlib.Path(__file__)
    script_bytes=script_path.read_bytes()
    plan=json.loads(plan_bytes.decode("utf-8-sig"))
    require(plan["name"]=="ametista-human-linear-v1", "Unexpected plan")
    require(plan["training"]["epochs"]==100 and plan["training"]["learningRate"]==.1 and plan["training"]["l2"]==.001, "Frozen training configuration changed")
    require(datetime.datetime.now(datetime.timezone.utc) < datetime.datetime.fromisoformat(plan["deadline"].replace("Z","+00:00")), "Deadline expired")
    output=pathlib.Path(args.output);output.mkdir(parents=True,exist_ok=False)
    started=datetime.datetime.now(datetime.timezone.utc).isoformat(); clock=time.monotonic()
    data=input_path.read_bytes();rows=[json.loads(line) for line in data.splitlines() if line.strip()]
    game_meta={};seen_samples=set()
    for r in rows:
        require(r["sourceYear"] in (2004,2005) and r["result"] in (-1,0,1), "Unexpected source/result")
        require(len(r["gameId"])==64, "Expected SHA256 game identity")
        int(r["gameId"],16)
        meta=(r["sourceYear"],r["result"])
        require(game_meta.setdefault(r["gameId"],meta)==meta, "Inconsistent game metadata")
        sample=(r["gameId"],r["ply"]);require(sample not in seen_samples,"Duplicate sample");seen_samples.add(sample)
        encode(r["board"],r["turn"])
    training=[r for r in rows if r["sourceYear"]==2004 and int(r["gameId"][:8],16)%5!=0]
    validation=[r for r in rows if r["sourceYear"]==2004 and int(r["gameId"][:8],16)%5==0]
    testing=[r for r in rows if r["sourceYear"]==2005]
    # Same representation means the learner has seen the same input, including
    # color-swapped rotated equivalents; exclude those across splits as well.
    def key(r):
        return tuple(r["board"]) if r["turn"]==1 else tuple(-p for p in r["board"][::-1])
    train_keys={key(r) for r in training};validation_keys={key(r) for r in validation}
    original_counts=dict(train=len(training),validation=len(validation),test=len(testing))
    validation=[r for r in validation if key(r) not in train_keys]
    testing=[r for r in testing if key(r) not in train_keys and key(r) not in validation_keys]
    prepared={name:arrays(part) for name,part in (("train",training),("validation",validation),("test",testing))}
    x,y,q=prepared["train"];weights=np.zeros(128);bias=0.;constant=float(q@y)
    history=[]
    for epoch in range(100):
        require(datetime.datetime.now(datetime.timezone.utc) < datetime.datetime.fromisoformat(plan["deadline"].replace("Z","+00:00")), "Deadline reached during training")
        loss,gradient,bg=loss_gradient(x,y,q,weights,bias,.001)
        require(np.isfinite(loss) and np.isfinite(gradient).all() and np.isfinite(bg),"Nonfinite training")
        weights-=.1*gradient;bias-=.1*bg
        history.append(dict(epoch=epoch,objectiveBeforeUpdate=loss))
    metrics={}
    for name,part in (("train",training),("validation",validation),("test",testing)):
        xx,yy,qq=prepared[name];prediction=np.tanh(xx@weights+bias)
        metrics[name]=dict(positions=len(part),games=len({r['gameId'] for r in part}),excludedOverlap=original_counts[name]-len(part),mse=float(qq@((prediction-yy)**2)),constantMse=float(qq@((constant-yy)**2)))
    model=dict(format="ametista-human-linear/v1",name=plan["name"],initialization="zero",inputs=128,activation="tanh",featureScale=1/np.sqrt(24),weights=weights.tolist(),bias=bias,trainingEpochs=100)
    model_bytes=(json.dumps(model,indent=2)+"\n").encode()
    require(plan_path.read_bytes()==plan_bytes and script_path.read_bytes()==script_bytes,"Plan or script changed during run")
    require(input_path.read_bytes()==data,"Input changed during run")
    (output/'model.json').write_bytes(model_bytes)
    report=dict(startedAt=started,finishedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),elapsedSeconds=time.monotonic()-clock,logicalProcessor=args.cpu,threads=1,planSha256=hashlib.sha256(plan_bytes).hexdigest(),inputSha256=hashlib.sha256(data).hexdigest(),scriptSha256=hashlib.sha256(script_bytes).hexdigest(),modelSha256=hashlib.sha256(model_bytes).hexdigest(),initialWeights="all zero",constantFromTraining=constant,metrics=metrics,history=history,verification=checks,realGames=0,promoted=False,playingStrengthMeasured=False,limitation="Offline temporal holdout only. Published human outcomes; overlapping encoded positions, including color-swapped rotated equivalents, excluded. No source PDN or exported positions redistributed.")
    (output/'report.json').write_text(json.dumps(report,indent=2)+"\n",encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='history'}))


if __name__=="__main__":
    main()
