"""Summarize recorded Ametista games; never play moves or modify weights."""
import json
import statistics
import sys
from pathlib import Path

directory = Path(sys.argv[1])
manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
candidate = manifest["config"]["a"]["id"]
raw = (directory / "games.jsonl").read_bytes()
# Ignore an unfinished final record while the registered runner is writing.
records = [json.loads(line) for line in raw[:raw.rfind(b"\n") + 1].splitlines()]
reports = []
for game in records:
    side = 1 if game["white"] == candidate else -1
    decisions = [frame for frame in game["frames"] if frame.get("diagnostics")]
    searched = [frame["diagnostics"] for frame in decisions if frame["diagnostics"]["depth"] > 0]
    def mean(field):
        return round(statistics.mean(item[field] for item in searched), 2) if searched else None
    material = []
    for frame in game["frames"]:
        board = frame["board"]
        material.append({"ply": frame["ply"],
            "menDifference": sum(p == side for p in board) - sum(p == -side for p in board),
            "kingDifference": sum(p == 2 * side for p in board) - sum(p == -2 * side for p in board)})
    reports.append({"matchId": game["matchId"], "seed": game["openingSeed"],
        "candidateColor": "white" if side == 1 else "black", "winner": game["winner"],
        "plies": game["plies"], "decisions": len(decisions), "searchedDecisions": len(searched),
        "meanDepthSearchedOnly": mean("depth"), "meanMsSearchedOnly": mean("milliseconds"),
        "meanNodesSearchedOnly": mean("nodes"),
        "openingMaterial": next((m for m in material if m["ply"] == 6), None),
        "firstCandidateKingPly": next((f["ply"] for f in game["frames"] if 2 * side in f["board"]), None),
        "firstOpponentKingPly": next((f["ply"] for f in game["frames"] if -2 * side in f["board"]), None),
        "finalMaterial": material[-1],
        "candidatePeakBytes": game["whitePeakBytes" if side == 1 else "blackPeakBytes"],
        "opponentPeakBytes": game["blackPeakBytes" if side == 1 else "whitePeakBytes"]})
report = {"candidate": candidate, "games": reports,
    "notes": ["Only complete JSONL records; this is not an independent legality audit.",
        "Time/depth means exclude depth-zero decisions, generally forced moves.",
        "Peak working set is a process-lifetime upper bound, not a per-game isolated measurement.",
        "Material differences are counts, not engine evaluation labels."]}
(directory / "diagnostics.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"games": len(reports), "artifact": str(directory / "diagnostics.json")}))
