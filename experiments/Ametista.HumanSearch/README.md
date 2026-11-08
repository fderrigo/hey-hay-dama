# Separate human-outcome combination (experimental)

Separately built and verified experimental variant; not adopted in the web catalog. Search is the project's original search-v14 with
only its version string changed to `search-v18-human-outcome`. The original
manual-v4 static value receives the fixed contribution `100 * tanh(linear128)`;
the learned contribution is rounded before adding it, with risk unchanged. Search terminals
and draw handling are unchanged. No weights are trained or rewritten here.

The learned neuron must be supplied explicitly using `--human-weights PATH`.
It is the separate model trained from zero on published human results. The
manual evaluator must match the frozen manual-v4 SHA256. Health exposes both
hashes, both versions, and the fixed contribution scale. The human file is not
bundled automatically. No fallback to another evaluator is permitted.

Synthetic verification commands:

```
dotnet PATH/Ametista.HumanSearch.dll --verify-human
dotnet PATH/Ametista.HumanSearch.dll --predict-human MODEL.json FIXTURES.json
dotnet PATH/Ametista.HumanSearch.dll --verify-human-search MANUAL_V4.json HUMAN_MODEL.json
```

The prediction bridge accepts a JSON array of `{ "board": [32 integers],
"turn": 1 }` objects; turn is 1 or -1. It emits raw predictions and unrounded
contributions for comparison with Python. These fixtures are not games.

The loaded-model search check uses a copy of our original verification suite.
It verifies the manual file's frozen hash, loads the actual human model and
changes only the in-memory manual `Version` metadata to
`manual-v4+human-verification`. No node, edge, coefficient, bias or input changes.
The version gates in that suite concern manual-only v4 pressure-bonus comparisons
against manual-v3 and preservation of the old manual-v3 graph. Those assertions
do not describe the combined evaluator; their block is not entered. The separate
manual-v3-only bonus block was already inapplicable to manual-v4.

All general checks remain: synthetic and historical position replay, legal move
generation, compiled/interpreted and scalar/vector agreement, rotation symmetry,
quiet-count invariance, 32 independent minimax cases, aspiration, cached history,
storage-horizon terminals, LMR reference fixtures, corner probe and timeout.
The existing replay fixture archive is required rather than silently skipped.
Reports use new timestamped files under `artifacts/ametista-human-search-verification`
and record both loaded hashes and the metadata-only override. No reports are
written into the frozen main model's results. Static inspection found no use of
the manual version string in search, inference compilation or numerical network
evaluation; it only gates these manual-specific fixture blocks and labels reports.

The sum is a manually chosen combination, not a learned residual correction.
Improved outcome-prediction MSE does not establish stronger play. Any activation
requires the separately declared offline gate, resource coordination and time
limit. Six exploratory games would not demonstrate causal improvement without
a paired baseline. Frozen main/contact/Opale artifacts remain unchanged.

The actual training report, zero-initialized learned model, verification reports
and archived runtime package are in
[human-data](../../arena/ametista/2026-11-07/human-data/README.md).
The initial six exploratory matches are tracked separately from the frozen
confirmations. A paired-control extension was declared after observing the
first two candidate losses; it is exploratory, not preregistered confirmation.
See [extension](../../arena/ametista/2026-11-07/human-control-extension.json).

To launch the archived experimental package from the repository root on a free port:

```powershell
dotnet arena/ametista/2026-11-07/human-data/runtime-package/Ametista.HumanSearch.dll --human-weights arena/ametista/2026-11-07/human-data/learned-v1/model.json --urls http://127.0.0.1:9019
```

The main Ametista startup remains the manual-v4/search-v14 service.
