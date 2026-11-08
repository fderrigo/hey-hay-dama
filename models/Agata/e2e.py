"""End-to-end check against the real game (specification, section 8).

Starts the Agata service, a stub that misbehaves on purpose and an unmodified Dama.Web. The test
models are added through environment variables only, so no file of the game changes.

    dotnet build ../../src/Dama.Web -c Release
    dotnet build Agata -c Release
    python e2e.py results/e2e.json
"""
import http.server, json, os, random, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
GAME_PORT, MODEL_PORT, STUB_PORT = 5391, 9392, 9393
GAME = f"http://127.0.0.1:{GAME_PORT}"


class Stub(http.server.BaseHTTPRequestHandler):
    """/slow answers too late; /illegal names a move that was not offered."""

    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        if self.path == "/slow":
            time.sleep(1)
        body = json.dumps({"protocolVersion": "dama-model/v1", "moveId": "1-2"}).encode()
        try:
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except OSError:
            pass

    def log_message(self, *_):
        pass


def call(method, url, body=None, token=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {"Content-Type": "application/json"} if data else {}
    if token:
        headers["X-Game-Token"] = token
    while True:
        try:
            with urllib.request.urlopen(urllib.request.Request(url, data=data, method=method, headers=headers), timeout=40) as r:
                return r.status, json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 429:  # The game limits writes per address.
                time.sleep(5)
                continue
            raw = e.read()
            return e.code, json.loads(raw) if raw else None


def wait(url):
    for _ in range(300):
        try:
            return urllib.request.urlopen(url, timeout=1).read()
        except OSError:
            time.sleep(0.2)
    raise RuntimeError(url)


def new_game(white, black):
    status, created = call("POST", f"{GAME}/api/games", {"white": white, "black": black, "access": "local", "humanSide": "white"})
    assert status == 200, created
    return created["token"], created["game"]


def play(white, black, seed):
    rng = random.Random(seed)
    token, game = new_game(white, black)
    errors = promotions = 0
    while game["status"] == "active" and errors <= 3:
        ply = game["position"]["ply"]
        if game["permissions"]["canMove"]:
            status, answer = call("POST", f"{GAME}/api/games/{game['id']}/move", {"expectedPly": ply, "moveId": rng.choice(game["legalMoves"])["id"]}, token)
        else:
            status, answer = call("POST", f"{GAME}/api/games/{game['id']}/advance", {"expectedPly": ply}, token)
        if status != 200:
            errors += 1
            print("turn failed:", status, answer)
            continue
        game = answer
        promotions += game["history"][-1]["promotes"]
    return {"white": white, "black": black, "result": game.get("result"), "plies": game["position"]["ply"], "errors": errors, "promotions": promotions}


def failure(model):
    token, game = new_game(model, "human")
    status, answer = call("POST", f"{GAME}/api/games/{game['id']}/advance", {"expectedPly": 0}, token)
    _, after = call("GET", f"{GAME}/api/games/{game['id']}", token=token)
    return {"status": status, "error": answer.get("error"), "unchanged": status == 502 and after["position"]["ply"] == 0 and after["status"] == "active"}


def model_env():
    entries = [("agata", f"http://127.0.0.1:{MODEL_PORT}/choose-move", "1000"),
               ("stub-lento", f"http://127.0.0.1:{STUB_PORT}/slow", "300"),
               ("stub-illegale", f"http://127.0.0.1:{STUB_PORT}/illegal", "1000")]
    env = {}
    for i, (identifier, endpoint, think) in enumerate(entries, start=10):  # Past the configured models.
        p = f"Arena__Models__{i}__"
        env.update({p + "Id": identifier, p + "Name": identifier, p + "Description": "Prova end-to-end", p + "Kind": "http",
                    p + "Endpoint": endpoint, p + "MaxThinkTimeMs": think, p + "Enabled": "true"})
    return env


def main():
    stub = http.server.ThreadingHTTPServer(("127.0.0.1", STUB_PORT), Stub)
    threading.Thread(target=stub.serve_forever, daemon=True).start()
    game_dir = Path(os.environ.get("DAMA_GAME_DIR", ROOT / "src" / "Dama.Web" / "bin" / "Release" / "net10.0"))
    model_dir = HERE / "Agata" / "bin" / "Release" / "net10.0"
    model = subprocess.Popen(["dotnet", "Agata.dll", "--urls", f"http://127.0.0.1:{MODEL_PORT}"], cwd=model_dir, stdout=subprocess.DEVNULL)
    env = {**os.environ, **model_env(), "Arena__DataDirectory": tempfile.mkdtemp(prefix="dama-e2e-"), "ASPNETCORE_URLS": GAME, "ASPNETCORE_ENVIRONMENT": "Production", "Arena__DefaultMatchSource": "test", "DAMA_COMPETITION_DIRECTORY": str(ROOT / "src" / "Dama.Web" / "App_Data" / "competition")}
    game = subprocess.Popen(["dotnet", "Dama.Web.dll"], cwd=game_dir, env=env, stdout=open(env["Arena__DataDirectory"] + "-game.log", "w"), stderr=subprocess.STDOUT)
    print("game log:", env["Arena__DataDirectory"] + "-game.log")
    try:
        wait(f"http://127.0.0.1:{MODEL_PORT}/health")
        wait(f"{GAME}/health")
        _, listed = call("GET", f"{GAME}/api/models")
        report = {"modelsInGame": [m["id"] for m in listed["models"]], "timeout": failure("stub-lento"), "illegalMove": failure("stub-illegale"),
                  "games": [play("agata", "opale", 1), play("opale", "agata", 2), play("human", "agata", 3), play("agata", "human", 4)]}
        print(json.dumps(report, ensure_ascii=False, indent=1))
        if len(sys.argv) > 1:
            Path(sys.argv[1]).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        passed = report["timeout"]["unchanged"] and report["illegalMove"]["unchanged"] \
            and all(g["errors"] == 0 and g["result"] for g in report["games"]) and any(g["promotions"] for g in report["games"])
        print("E2E", "PASSED" if passed else "FAILED")
        return 0 if passed else 1
    finally:
        game.terminate()
        model.terminate()
        stub.shutdown()


if __name__ == "__main__":
    sys.exit(main())
