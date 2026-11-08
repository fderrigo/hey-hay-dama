# Actual-DLL inference diagnostic

Prepare/build/run only after competition processes release the selected core.
No engine or weights are rebuilt: `extern alias` references load the published
baseline and human-combination DLLs. Model JSON paths are explicit. Runtime
copies are checked against those original DLLs by SHA256, and all input hashes
are checked again before the report is created. No file is overwritten.

```
dotnet build experiments/Ametista.InferenceProbe -c Release --artifacts-path artifacts/inference-probe-build
dotnet artifacts/inference-probe-build/bin/Ametista.InferenceProbe/release/Ametista.InferenceProbe.dll MANUAL.json HUMAN.json NEW_REPORT.json 0
```

The parent directory of NEW_REPORT must already exist. The final argument binds
this Windows-only diagnostic to one logical CPU with BelowNormal priority. The
operator must coordinate exclusive use of that core; affinity alone is not a
resource reservation.

1000 synthetic boards, seed 271828, alternating turns, 1–12 pieces per side,
promotion-row pieces forced to kings. The diagnostic first checks the actual
DLL combination and unchanged risk on every board. After 20 passes of warmup,
seven rounds each evaluate every board 200 times for each implementation,
alternating execution order. Checksums are reported and checked for stability.

Reports include per-round ns/evaluation, median costs, ratio of medians and median
round ratio. These are **synthetic, not game-weighted** measurements of static
inference, not search speed or playing strength, and cannot establish the cause
of observed game outcomes. No new games, training, server or registry access.

The recorded session result is [inference-cost.json](../../arena/ametista/2026-11-07/human-data/inference-cost.json). The first build was rejected by platform analysis; the diagnostic now explicitly guards its Windows-only affinity calls. No engine source was changed.
