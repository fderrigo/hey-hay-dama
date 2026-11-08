"""Synthetic model requests only: no games are created or played."""
import concurrent.futures
import copy
import json
import pathlib
import sys
import time
import urllib.error
import urllib.request

url = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:9013"
output = pathlib.Path(__file__).parent / "results" / "http-verification.json"
checks = []
board = [-1] * 12 + [0] * 8 + [1] * 12
paths = [[21, 17], [21, 18], [22, 18], [22, 19], [23, 19], [23, 20], [24, 20]]


def payload(black=False):
    candidates = [[33 - x for x in p] for p in paths] if black else paths
    side = "black" if black else "white"
    return dict(protocolVersion="dama-model/v1", rules="italian-draughts-8x8",
                player=side, requestId="arithmetic-fixture", gameId="no-game-created",
                position=dict(board=board[:], turn=side, ply=0, quietKingPlyCount=0, repetitionCount=1),
                legalMoves=[dict(id=f"opaque-{side}-{i}", **{"from": p[0]}, to=p[-1], path=p[:], captures=[], promotes=False)
                            for i, p in enumerate(candidates)], thinkTimeMs=1000)


def post(data, content_type="application/json", raw=False):
    body = data.encode() if raw else json.dumps(data).encode()
    request = urllib.request.Request(url + "/choose-move", body, headers={"Content-Type": content_type})
    before = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return response.status, json.load(response), (time.perf_counter() - before) * 1000
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode(), (time.perf_counter() - before) * 1000


with urllib.request.urlopen(url + "/health", timeout=5) as response:
    health = json.load(response)
assert health["protocolVersion"] == "dama-model/v1"
checks.append("health and protocol")
for black in [False, True]:
    body = payload(black)
    status, response, elapsed = post(body)
    assert status == 200 and response["moveId"] in [m["id"] for m in body["legalMoves"]]
    checks.append(f"opaque ID returned, {'black' if black else 'white'}, {elapsed:.2f}ms")

multi = payload()
multi["position"]["board"] = [0] * 32
for square, piece in [(21, 1), (18, -1), (11, -1)]:
    multi["position"]["board"][square - 1] = piece
multi["legalMoves"] = [dict(id="opaque-complete-double", **{"from": 21}, to=7, path=[21, 14, 7], captures=[18, 11], promotes=False)]
status, response, _ = post(multi)
assert status == 200 and response["moveId"] == "opaque-complete-double"
checks.append("complete multi-capture opaque ID")

invalid = []
bad = payload(); bad["protocolVersion"] = "wrong"; invalid.append(bad)
bad = payload(); bad["position"]["board"] = []; invalid.append(bad)
bad = payload(); bad["position"]["ply"] = -1; invalid.append(bad)
bad = payload(); bad["position"] = None; invalid.append(bad)
bad = payload(); bad["legalMoves"] = [None]; invalid.append(bad)
bad = payload(); bad["legalMoves"][0]["path"] = [100, 17]; invalid.append(bad)
bad = payload(); bad["legalMoves"].append(copy.deepcopy(bad["legalMoves"][0])); invalid.append(bad)
bad = payload(); bad["player"] = "green"; invalid.append(bad)
for body in invalid:
    assert post(body)[0] == 400
assert post("{", raw=True)[0] == 400
assert post("text", content_type="text/plain", raw=True)[0] == 415
checks.append("10 invalid requests rejected with 400/415")

def concurrent_call(index):
    body = payload(index % 2 == 1)
    status, response, elapsed = post(body)
    assert status == 200
    assert response["moveId"] in [m["id"] for m in body["legalMoves"]]
    return index % 2, response["moveId"], elapsed


with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
    responses = list(executor.map(concurrent_call, range(32)))
for side in [0, 1]:
    assert len({move for colour, move, _ in responses if colour == side}) == 1
checks.append("32 concurrent requests deterministic and legal")
output.parent.mkdir(parents=True, exist_ok=True)
report = dict(success=True, health=health, checks=checks, gamesPlayed=0,
              concurrentMaxMs=max(r[2] for r in responses))
output.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
