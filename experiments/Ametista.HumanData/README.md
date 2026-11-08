# Human archive replay audit

Original importer for the publicly downloadable FID 2004/2005 Italian draughts
archives. This is a separate experiment under the later authorization for public
datasets; it does not change Ametista, run an engine, train weights, or register
historical games as new competition matches.

```
dotnet run --project experiments/Ametista.HumanData --artifacts-path artifacts/human-data-build -- --verify
dotnet run --project experiments/Ametista.HumanData --artifacts-path artifacts/human-data-build -- artifacts/ametista-dataset-research/ci2004ita.zip artifacts/ametista-dataset-research/ci2005ita.zip artifacts/ametista-human-data-audit
```

Output directory must be new and outside competition storage. Outputs include
per-game acceptance/rejection, archive and PDN hashes, and JSONL positions before
each move of accepted games. A later rejection removes the entire game's samples.
`result` is the published result from White's perspective, not side-to-move value.
Abbreviated captures are accepted only when exactly one legal referee move
matches. Unknown syntax is rejected rather than silently removed.

The repository contains importer code only, not the PDN corpus. Public download
does not establish an open license; no redistribution permission is asserted.
Do not commit generated datasets without resolving their publication conditions.
For subsequent learning, split by whole games and address repeated positions
across splits; this importer performs no split or model selection.
