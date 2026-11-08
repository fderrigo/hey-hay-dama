"""Read audited final matches; never start games, select models, or change weights."""
import json
import hashlib
import math
import statistics
import sys
from pathlib import Path

if not __debug__:
    raise RuntimeError("Run without Python optimization: validation assertions are required.")


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def summarize(directory, model, seeds):
    audit = read(directory / "audit.json")
    for key in ("allRecorded", "legalReplay", "fullSchedule", "pairedOpenings"):
        if audit.get(key) is not True:
            raise ValueError(f"Missing completed independent audit: {directory}: {key}")
    manifest = read(directory / "manifest.json")
    cfg = manifest["config"]
    assert cfg["source"] == "benchmark" and cfg["seeds"] == seeds
    assert cfg["a"]["id"] == model and cfg["b"]["id"] == "kingsrow"
    assert cfg["a"]["searchMs"] == cfg["b"]["searchMs"] == 1000
    assert cfg["a"].get("protocolReserveMs", 100) == (25 if model == "opale" else 100)
    assert cfg["b"].get("protocolReserveMs", 100) == 100
    games = [json.loads(line) for line in (directory / "games.jsonl").read_text().splitlines()]
    assert len(games) == len(seeds) * 2
    ids, colors, openings, points = set(), {}, {}, {}
    totals = dict(games=len(games), wins=0, draws=0, losses=0)
    for game in games:
        assert game["completed"] and game["error"] is None
        assert game["matchId"] not in ids
        ids.add(game["matchId"])
        seed = game["openingSeed"]
        assert seed in seeds
        side = 1 if game["white"] == model else -1
        assert {game["white"], game["black"]} == {model, "kingsrow"}
        assert side not in colors.setdefault(seed, set())
        colors[seed].add(side)
        prefix = [(f["ply"], f["board"], f["turn"]) for f in game["frames"] if f["ply"] <= 6]
        assert len(prefix) == 7
        assert openings.setdefault(seed, prefix) == prefix
        outcome = "draws" if game["winner"] == 0 else "wins" if game["winner"] == side else "losses"
        totals[outcome] += 1
        points[seed] = points.get(seed, 0) + (1 if outcome == "draws" else 2 if outcome == "wins" else 0)
    assert all(colors.get(seed) == {-1, 1} for seed in seeds)
    totals["winRate"] = totals["wins"] / len(games)
    totals["score"] = (2 * totals["wins"] + totals["draws"]) / (2 * len(games))
    totals["version"] = cfg["a"]["version"]
    totals["weightsSha256"] = cfg["a"]["sha256"]
    totals["superiorityOverKingsrowSupported"] = audit["superioritySupported"]
    return totals, {seed: points[seed] / 4 for seed in seeds}, openings, ids, cfg


def main():
    if len(sys.argv) != 5:
        raise SystemExit("confirmation-plan.json opale-directory ametista-directory new-report.json")
    plan, control, candidate, output = map(Path, sys.argv[1:])
    if output.exists():
        raise FileExistsError("Do not overwrite a final report.")
    seeds = read(plan)["seeds"]
    assert len(seeds) == 18 and len(set(seeds)) == 18
    old, old_pairs, old_openings, old_ids, old_cfg = summarize(control, "opale", seeds)
    new, new_pairs, new_openings, new_ids, new_cfg = summarize(candidate, "ametista", seeds)
    assert old_openings == new_openings and old_ids.isdisjoint(new_ids)
    for field in ("version", "sha256", "endpoint", "searchMs"):
        assert old_cfg["b"][field] == new_cfg["b"][field]
    assert old_cfg["logicalProcessor"] == new_cfg["logicalProcessor"]
    differences = [new_pairs[seed] - old_pairs[seed] for seed in seeds]
    mean = statistics.mean(differences)
    margin = 2.11 * statistics.stdev(differences) / math.sqrt(len(seeds))
    # Pair differences lie in [-1,1]; unlike the t estimate this bound does
    # not collapse to zero when the observed variance happens to be zero.
    bounded_margin = math.sqrt(2 * math.log(40) / len(seeds))
    lower = max(-1, mean - margin)
    bounded_lower = max(-1, mean - bounded_margin)
    result = dict(
        complete=True, realGamesCreated=0, opale=old, ametista=new,
        inputSha256={str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                     for path in [plan] + [directory / name
                         for directory in (control, candidate)
                         for name in ("manifest.json", "games.jsonl", "audit.json")]},
        pairedScoreDifference=mean,
        approximate95Interval=[lower, min(1, mean + margin)],
        bounded95Interval=[bounded_lower, min(1, mean + bounded_margin)],
        measuredImprovementSupported=lower > 0 and bounded_lower > 0,
        pairs=[dict(seed=s, opale=old_pairs[s], ametista=new_pairs[s],
                    difference=new_pairs[s] - old_pairs[s]) for s in seeds],
        limitations=[
            "Inference assumes independent sampled opening pairs; colors within a pair are not independent.",
            "The t interval is approximate. The Hoeffding bound is conservative and uses range [-1,1].",
            "Different execution times and concurrent system load can affect timed engine strength.",
            "Improvement over Opale and superiority over Kingsrow are separate questions.",
            "This report checks audited data; it does not certify runtime binary provenance or CPU timing.",
        ])
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
