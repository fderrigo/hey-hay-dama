"""Summarize a predeclared comparison; never choose a version or change weights."""
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])
selection_file = sys.argv[2] if len(sys.argv) > 2 else "qualification-selection.json"
selection = json.loads((root / selection_file).read_text(encoding="utf-8-sig"))
baseline_variant = selection.get("baselineVariant", "v4")
candidate_variant = selection.get("candidateVariant", "v13")
totals = {}
pending = []
openings = {}
pair_scores = {}
for item in selection["schedule"]:
    directory = Path(item["output"])
    audit_path = directory / "audit.json"
    if not audit_path.exists():
        pending.append(item["output"])
        continue
    audit = json.loads(audit_path.read_text())
    if not all(audit.get(k) for k in ("allRecorded", "legalReplay", "fullSchedule", "pairedOpenings")):
        raise ValueError(f"Incomplete or failed independent audit: {directory}")
    config = json.loads((directory / "manifest.json").read_text())["config"]
    assert config["source"] == "test" and config["seeds"] == [item["seed"]]
    assert config["a"]["id"] == "ametista" and config["b"]["id"] == "kingsrow"
    assert config["a"]["searchMs"] == config["b"]["searchMs"] == 1000
    assert item["variant"] in (baseline_variant, candidate_variant)
    assert config["a"]["version"] == selection["baseline" if item["variant"] == baseline_variant else "candidate"]
    records = [json.loads(line) for line in (directory / "games.jsonl").read_text().splitlines()]
    assert len(records) == 2 and {g["white"] for g in records} == {"ametista", "kingsrow"}
    score = 0
    total = totals.setdefault(item["variant"], dict(games=0, wins=0, draws=0, losses=0, points=0))
    for game in records:
        assert game["completed"] and game["error"] is None and game["openingSeed"] == item["seed"]
        initial = [(f["ply"], f["board"], f["turn"]) for f in game["frames"] if f["ply"] <= 6]
        assert len(initial) == 7
        if item["seed"] in openings:
            assert openings[item["seed"]] == initial, "Different opening positions within paired comparison"
        openings[item["seed"]] = initial
        side = 1 if game["white"] == "ametista" else -1
        outcome = "draws" if game["winner"] == 0 else "wins" if game["winner"] == side else "losses"
        points = 1 if outcome == "draws" else 2 if outcome == "wins" else 0
        total["games"] += 1
        total[outcome] += 1
        total["points"] += points
        score += points
    pair_scores.setdefault(item["seed"], {})[item["variant"]] = score / 4

for total in totals.values():
    total["score"] = total["points"] / (2 * total["games"])
paired = [dict(seed=seed, **scores, candidateMinusBaseline=scores[candidate_variant] - scores[baseline_variant])
          for seed, scores in pair_scores.items() if len(scores) == 2]
result = dict(complete=not pending, totals=totals, pairedComparisons=paired,
              pending=pending, realGamesCreated=0, automaticallySelectedVersion=None,
              limitations=["These opening pairs are development, not a confirmation of superiority.",
                           "All numbers are descriptive; no automatic model or weight selection.",
                           "Concurrent load and different hardware resources across agents remain relevant."])
if "recovery" in selection:
    recovery = selection["recovery"]
    original = Path(recovery["originalAttempt"]["output"])
    audit = json.loads((original / "audit.json").read_text())
    assert audit["allRecorded"] and audit["legalReplay"]
    attempts = [json.loads(line) for line in (original / "games.jsonl").read_text().splitlines()]
    assert len(attempts) == recovery["originalAttemptGames"]
    assert sum(g["completed"] for g in attempts) == recovery["originalAttemptCompleted"]
    assert sum(not g["completed"] for g in attempts) == recovery["originalAttemptInterrupted"]
    assert any(g["matchId"] == recovery["interruptedMatchId"] and not g["completed"] for g in attempts)
    result["recovery"] = recovery
    result["retainedOriginalAttemptsExcludedFromPairedComparison"] = [
        {key: game[key] for key in ("matchId", "white", "black", "completed", "winner", "reason", "plies", "error")}
        for game in attempts]
    result["auditedStartsIncludingOriginalAttempts"] = sum(t["games"] for t in totals.values()) + len(attempts)
(root / selection_file.replace("-selection.json", "-report.json")).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
