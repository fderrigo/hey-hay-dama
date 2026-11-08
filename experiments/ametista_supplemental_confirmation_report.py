"""Analyze completed supplemental pairs; never play, select, or promote a model.

Usage: python ametista_supplemental_confirmation_report.py ROOT SELECTION NEW_REPORT
Relative paths, including schedule paths, are relative to the repository ROOT.
Audits are prerequisites, not a replacement for independent legal replay.
"""
import hashlib
import json
import math
import statistics
import sys
import uuid
from datetime import datetime
from pathlib import Path


PLAN = Path("arena/ametista/2026-11-07/supplemental-plan.json")
MANUAL_V4 = "7e8ea1c6a7c897e5d3d5c42a43511a26ad0ebd32db904fff670be488c7913138"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def interval(values, low, high):
    require(len(values) == 18, "Exactly 18 opening pairs required")
    mean = statistics.mean(values)
    t_margin = 2.11 * statistics.stdev(values) / math.sqrt(18)
    bounded_margin = (high - low) * math.sqrt(math.log(40) / 36)
    return {
        "mean": mean,
        "approximate95Interval": [max(low, mean - t_margin), min(high, mean + t_margin)],
        "bounded95Interval": [max(low, mean - bounded_margin), min(high, mean + bounded_margin)],
    }


def normalize_config(config):
    # The runner materializes optional defaults and serializes UTC as +00:00.
    result = dict(config)
    result["deadline"] = datetime.fromisoformat(config["deadline"].replace("Z", "+00:00"))
    for side in ("a", "b"):
        result[side] = dict(config[side])
        result[side].setdefault("protocolReserveMs", 100)
        result[side].setdefault("expectedHealth", None)
    return result


def analyze(root, selection_path):
    root = Path(root).resolve()
    hashes = {}

    def resolve(path):
        return (root / path).resolve()

    def read_bytes(path):
        path = resolve(path)
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        require(path not in hashes or hashes[path] == digest, f"Input changed: {path}")
        hashes[path] = digest
        return raw.decode("utf-8-sig")

    def read(path):
        return json.loads(read_bytes(path))

    plan = read(PLAN)
    selection = read(selection_path)
    seeds = selection["seeds"]
    require(len(seeds) == 18 and len(set(seeds)) == 18, "Require 18 distinct seeds")
    require(seeds == plan["reservedConfirmationSeeds"], "Reserved seeds differ from plan")
    require(selection["source"] == plan["confirmationSource"] == "benchmark", "Wrong source")
    require(selection["baselineVariant"] == "v14" and selection["candidateVariant"] == "v17", "Wrong variants")
    versions = {"v14": selection["baseline"], "v17": selection["candidate"]}
    require(all(isinstance(v, str) and v for v in versions.values()) and versions["v14"] != versions["v17"], "Missing/distinct version aliases required")
    schedule = selection["schedule"]
    require(len(schedule) == 36, "Require 36 schedule entries (72 games)")
    totals = {v: dict(games=0, wins=0, draws=0, losses=0, version=versions[v], weightsSha256=MANUAL_V4) for v in versions}
    pair_scores, openings, opponents, entries = {}, {}, {}, set()
    ids, directories = set(), set()
    king_identity = None
    for entry in schedule:
        variant, seed = entry["variant"], entry["seed"]
        require(variant in versions and seed in seeds, "Unexpected variant/seed")
        key = (variant, seed)
        require(key not in entries, "Duplicate scheduled variant/seed")
        entries.add(key)
        directory = resolve(entry["output"])
        require(directory not in directories, "Reused output directory")
        directories.add(directory)
        supplied_config = read(entry["config"])
        manifest = read(directory / "manifest.json")
        cfg = manifest["config"]
        require(normalize_config(supplied_config) == normalize_config(cfg), f"Config/manifest mismatch: {key}")
        require(cfg["source"] == "benchmark" and cfg["seeds"] == [seed], f"Wrong config schedule: {key}")
        require(cfg["logicalProcessor"] == entry["logicalProcessor"], f"Schedule/core mismatch: {key}")
        require(manifest["openingPlies"] == 6, "Wrong opening length")
        require(cfg["a"]["id"] == "ametista" and cfg["b"]["id"] == "kingsrow", "Wrong participants")
        require(cfg["a"]["version"] == versions[variant] and cfg["a"]["sha256"] == MANUAL_V4, "Wrong candidate version/weights")
        for side in ("a", "b"):
            require(cfg[side]["searchMs"] == 1000 and cfg[side].get("protocolReserveMs", 100) == 100, "Unequal configured budgets")
        opponent = cfg["b"]
        identity = (opponent["version"], opponent["sha256"])
        if king_identity is None:
            king_identity = identity
        require(identity == king_identity, "Kingsrow hash/version differs between lanes")
        paired_conditions = (cfg["logicalProcessor"], opponent["endpoint"], opponent["processId"], manifest["plyLimit"])
        require(opponents.setdefault(seed, paired_conditions) == paired_conditions, "Paired core/opponent/ply-limit mismatch")
        audit = read(directory / "audit.json")
        for flag in ("allRecorded", "legalReplay", "fullSchedule", "pairedOpenings"):
            require(audit.get(flag) is True, f"Incomplete audit {key}: {flag}")
        require(audit["games"] == audit["completed"] == 2, "Audit does not cover two completed games")
        games = [json.loads(line) for line in read_bytes(directory / "games.jsonl").splitlines() if line.strip()]
        require(len(games) == 2, "Each entry must contain two games")
        colors, points = set(), 0
        for game in games:
            require(game["completed"] is True and game["error"] is None, "Interrupted/failed match")
            require(game["openingSeed"] == seed, "Wrong game seed")
            match_id = game["matchId"]
            require(isinstance(match_id, str) and len(match_id) == 32 and uuid.UUID(match_id).hex == match_id, "Malformed match ID")
            require(match_id not in ids, "Duplicate match ID")
            ids.add(match_id)
            require({game["white"], game["black"]} == {"ametista", "kingsrow"}, "Wrong game participants")
            side = 1 if game["white"] == "ametista" else -1
            require(side not in colors, "Repeated color")
            colors.add(side)
            require(type(game["winner"]) is int and game["winner"] in (-1, 0, 1), "Missing/invalid outcome")
            frames = game["frames"]
            prefix = [(f["ply"], f["board"], f["turn"]) for f in frames if f["ply"] <= 6]
            require([f[0] for f in prefix] == list(range(7)), "Incomplete opening prefix")
            require(openings.setdefault(seed, prefix) == prefix, "Opening mismatch across colors/variants")
            outcome = "draws" if game["winner"] == 0 else "wins" if game["winner"] == side else "losses"
            totals[variant][outcome] += 1
            totals[variant]["games"] += 1
            points += 1 if outcome == "draws" else 2 if outcome == "wins" else 0
        require(colors == {-1, 1}, "Both colors required")
        pair_scores[key] = points / 4
    require(entries == {(v, s) for v in versions for s in seeds} and len(ids) == 72, "Incomplete full schedule")
    for variant, total in totals.items():
        scores = [pair_scores[(variant, s)] for s in seeds]
        estimate = interval(scores, 0, 1)
        total.update(winRate=total["wins"] / 36, score=estimate.pop("mean"), **estimate)
        total["superiorityOverKingsrowSupported"] = total["approximate95Interval"][0] > .5 and total["bounded95Interval"][0] > .5
    differences = [pair_scores[("v17", s)] - pair_scores[("v14", s)] for s in seeds]
    estimate = interval(differences, -1, 1)
    # Refuse a result if any source changed during this read-only analysis.
    for path, digest in hashes.items():
        require(hashlib.sha256(path.read_bytes()).hexdigest() == digest, f"Input changed: {path}")
    return dict(
        complete=True, realGamesCreated=0, gamesAnalyzed=72, automaticallySelected=False,
        baselineVariant="v14", candidateVariant="v17", variants=totals,
        pairedScoreDifference=estimate["mean"], approximate95Interval=estimate["approximate95Interval"],
        bounded95Interval=estimate["bounded95Interval"],
        measuredImprovementSupported=estimate["approximate95Interval"][0] > 0 and estimate["bounded95Interval"][0] > 0,
        pairs=[dict(seed=s, baseline=pair_scores[("v14", s)], candidate=pair_scores[("v17", s)], difference=pair_scores[("v17", s)] - pair_scores[("v14", s)]) for s in seeds],
        inputSha256={str(p.relative_to(root)) if p.is_relative_to(root) else str(p): digest for p, digest in hashes.items()},
        limitations=[
            "Inference assumes independent sampled opening pairs; colors within a pair are not independent.",
            "The t interval is approximate. Two-sided Hoeffding bounds use [0,1] for scores and [-1,1] for differences.",
            "Shared machine load and runtime warmup may differ; configured budgets do not certify identical CPU time.",
            "Audit flags are trusted prerequisites; this analysis does not replay rules, re-read the shared registry or certify loaded binaries.",
            "Input hashes identify documents examined, not independent historical provenance of their audit.",
            "No automatic selection or promotion. The first confirmation remains separate and unchanged.",
        ])


def main():
    if len(sys.argv) != 4:
        raise SystemExit("Usage: ROOT SELECTION.json NEW_REPORT.json")
    root = Path(sys.argv[1]).resolve()
    output = (root / sys.argv[3]).resolve()
    if output.exists():
        raise FileExistsError("Refusing to overwrite a report")
    result = analyze(root, sys.argv[2])
    with output.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
