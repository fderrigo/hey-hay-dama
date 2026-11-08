# Opale: learning exclusively through play

## Outcome and later strategy changes

The initial random network did not outperform the original Opale network on the
matched varied-opening probe (5/48 wins versus 15/48). Winning-move memory did
win all 16 periodic standard-start games: 8 against Kimberlite, 8 against Agata,
both colours. All 16 were entirely covered by learned memories. These are repeated
deterministic continuations, not 16 independent demonstrations of generalization.

After the user requested preserving Opale's original strength, `Evolution.cs`
was added. Original Opale has manually constructed weights, **not pretrained
weights**. Evolution mutates output weights and selects candidates solely using
registered outcomes on paired games. It never uses original model evaluations as
training labels. The larger-suite variant uses 52 games, separate validation,
and an untouched final opening seed. Initial selection drift and shared-registry
lock contention prevented a demonstrated final improvement of the pure network.

After the user prioritized winning by any available approach, `--win` was added:
it uses Opale's own winning-move memory, with depth-6/250ms search when a position
is not memorized. Search is an inference procedure, not a source of supervised
training labels. Its new confirmation attempt may be blocked by the shared
registry; the proven 16/16 result above is from the earlier memory evaluations.

```powershell
dotnet run --project experiments/Dama.Learn -c Release -- --win src/Dama.Web/Models/opale-v1.json artifacts/new-confirmation FUTURE-UTC-DEADLINE
```

The following documents the initial TD-learning phase; its saved files are
retained for audit, not represented as superior to the original network.

This runner starts a new randomly initialized network. It does not load a dataset,
copy an opponent's evaluation, import pretrained parameters, use material rewards,
or train on handcrafted position labels. Frozen Kimberlite and Agata are
opponents only. The rules engine supplies legal moves and terminal outcomes.

The user corrected the challenge duration to **30 minutes**, starting at
2026-11-06 16:51:44 UTC, with an absolute deadline of **17:21:44 UTC**. Setup and
compilation count against that interval.

## Algorithm

- 128 binary occupancy inputs, 64 ReLU units, linear skip, tanh value output.
  Initial weights are pseudorandom; biases start at zero.
- Afterstates are encoded from the learner's perspective. The input contains
  piece identities and squares, without assigned piece values or tactical features.
- The final reward is +1 / 0 / -1 for a referee win / draw / loss. TD(lambda=0.8),
  discount 0.995, and SGD (0.002) update all network parameters after each training
  game, using afterstates from both sides with their corresponding outcome sign.
  Replay contains only completed games from this run; three prior
  episodes are sampled in addition to both perspectives of the latest game. Interrupted games
  provide neither a reward nor training targets.
- Legal-move selection evaluates candidate afterstates with the current network.
  Actual terminal conditions take precedence. There is no game-tree search.
- The learner also remembers its own state/action pairs from actual wins, indexed
  by opponent, colour, board, turn and quiet-king count. A legal remembered move
  takes precedence during exploitation. This is an allowed episodic memory, not
  evidence that the network alone has learned the same policy.
- Epsilon-greedy exploration decreases during the run. Every fifth training game
  has four genuinely played random opening plies; all opening moves are recorded.

## Registration and evidence

Every match calls `CompetitionStore.Start` before its first move and is disposed.
Only referee outcomes call `Complete`; deadlines and safety limits call `Interrupt`.
The shared registry uses source `benchmark`, learner ID `opale`, and opponent IDs
`kimberlite` and `agata`. No production model is overwritten.

An initial misinterpretation ran a separate learner named Selce against all three
existing models. The user corrected the identity: the learner is Opale and must
not challenge itself. That run was stopped, its outstanding match marked
interrupted, and its artifacts retained separately in `artifacts/learning-20261106`.
**No weights, memory or replay from that mistaken run are used by the Opale run.**
The corrected run began at 17:00:12 UTC and retains the original 17:21:44 deadline.

Each match has a JSONL transcript with initial state, every move and resulting
state, and the final outcome. `games.jsonl` links transcripts to shared match IDs.
`progress.jsonl` and `report.json` contain phase-separated W/D/L and win rates.
`manifest.json` records assumptions, seed, deadline and shared directory.

`initial-weights.json` preserves the random baseline. `weights.json` and
`memory.json` are the latest trained artifacts. `evaluated-weights.json` and
`evaluated-memory.json` preserve the frozen policy used by the final evaluation;
training may resume with remaining time after that evaluation.

The frozen evaluation reports standard-start memory-assisted play, standard-start
neural-only play, and new random openings separately. Fixed-opponent standard-start
repeats are **not independent trials or evidence of general strength**. Opponents
are deterministic here (Agata temperature 0). New openings are not guaranteed to
exclude positions encountered earlier in training.

Run from the repository root with a fresh output directory and a future absolute
deadline. Reusing an output directory is unsupported:

```powershell
dotnet run --project experiments/Dama.Learn -c Release -- --deadline 2026-11-06T17:21:44Z --output artifacts/opale-learning-20261106
```

Read-only verification reconstructs the **recorded** moves with the referee and
checks the resulting boards, natural outcomes, rewards and shared records. It
starts no games and performs no weight updates:

```powershell
dotnet run --project experiments/Dama.Learn -c Release -- --audit artifacts/opale-learning-20261106
```
