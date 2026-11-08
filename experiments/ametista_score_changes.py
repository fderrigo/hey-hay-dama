"""Read completed replay records; locate changes in Ametista's own evaluations.

This is a diagnostic index, not an oracle or a weight-selection procedure.
It does not query any engine, generate moves, play games, or write weights.
"""
import json
import sys
from pathlib import Path

directory = Path(sys.argv[1])
records = [json.loads(line) for line in (directory / "games.jsonl").read_text().splitlines()]
report = []
for game in records:
    frames = game["frames"]
    decisions = [f for f in frames if (f.get("diagnostics") or {}).get("depth", 0) > 0]
    changes = []
    for previous, current in zip(decisions, decisions[1:]):
        before, after = previous["diagnostics"], current["diagnostics"]
        # Mate-distance values belong to a different scale from static values.
        if abs(before["score"]) >= 29000 or abs(after["score"]) >= 29000:
            continue
        delta = after["score"] - before["score"]
        if delta >= -100:
            continue
        position = next(f for f in frames if f["ply"] == current["ply"] - 1)
        changes.append({
            "fromPly": previous["ply"] - 1, "toPly": current["ply"] - 1,
            "scoreBefore": before["score"], "scoreAfter": after["score"],
            "delta": delta, "depthBefore": before["depth"], "depthAfter": after["depth"],
            "rootBoard": position["board"], "rootTurn": position["turn"],
            "chosenMove": current["moveId"],
        })
    changes.sort(key=lambda c: c["delta"])
    report.append({"matchId": game["matchId"], "seed": game["openingSeed"],
                   "white": game["white"], "winner": game["winner"],
                   "scoreDrops": changes})

result = {"realGamesCreated": 0, "games": report, "limits": [
    "Scores come only from the candidate's own recorded diagnostics.",
    "Different root positions and depths: a score drop does not prove the previous move was an error.",
    "Forced moves are excluded; consecutive retained decisions may span more than two plies.",
    "Root boards are an index into full replay history, not standalone repetition-aware positions.",
    "No weights or moves are selected by this script.",
]}
target = directory / "score-changes.json"
target.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"games": len(report), "scoreDrops": sum(len(g["scoreDrops"]) for g in report),
                  "artifact": str(target)}))
