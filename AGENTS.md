# AGENTS.md — working rules for coding agents

Read [1918-design-doc.md](1918-design-doc.md) for intent. This file is about *not breaking things*, several of which broke once already and cost a day each.

## Architecture boundaries

- `src/Sim/` is a **pure .NET class library**. It must never reference Godot, System.Random, floats, or wall clocks. It builds and tests with no engine installed — keep it that way.
- `src/Lockstep/` may use sockets and BCL only; no Godot references.
- `game/` is the only place Godot APIs may appear. The renderer **reads** sim state and issues **Commands**; gameplay truth lives exclusively inside `World`. The sim never knows individual soldiers exist — squads are single entities and the renderer puppets visuals around them.
- Transient output (shot events etc.) flows through `World.Events`, is consumed per tick, and must never influence state.

## Determinism checklist (the core promise)

Two peers run the same seed and input log and must agree bit-for-bit, verified by `World.StateHash()`.

- Numbers in the sim are `Fixed` (Q32.32) only. No `float`, `double`, or `Math.*`. `Int128` is allowed for intermediate arithmetic. Beware raw-space squaring: `radius.Raw * radius.Raw` overflows long past ~1.4 m radii — use `Fixed` multiplication instead.
- Randomness comes **only** from `World.Rng` (PCG32). Never `System.Random`, never time, never GUIDs inside the sim.
- Iterate collections in index order; never rely on `Dictionary` enumeration order without sorting first.
- Added any state field? Add it to `World.StateHash()` **and** bump `Hasher.Salt`. Events stay excluded from hashing.
- Tick pipeline order (commands → movement → dig → artillery → gas → combat → suppression → capture → victory → director) is part of the contract. Changing order changes hashes everywhere.
- Networked matches: both peers construct **identical worlds from `MapDef.Spawns`**. Never spawn units asymmetrically on one peer; the joiner's world gets rebuilt during seed adoption and anything spawned outside the map silently vanishes.
- Player commands are merged via `CanonicalMerge` so both peers apply identical sequences regardless of who sent first. New command types must be reflected in its total ordering.

## Testing

```bash
dotnet test                               # 63 tests, all must pass
godot --headless --path game -- --smoke     # sim end-to-end: real map, two AIs, no window
godot --headless --path game -- --selfplay  # full game scene + frame loop, AI vs AI,
                                            # asserts views track units and the verdict banner shows
godot --headless --path game -- --inputtest # synthesizes real mouse events: click-select a
                                            # squad, right-click an order, verify it lands in the
                                            # sim and the squad marches
```

All three Godot modes print `... OK` / `... FAIL` and exit 0/1 — check exit codes, not just output. Test matches run with fast ticket drain and an 8x tick-rate fast-forward so they finish in seconds.

- The determinism tests are the product: same seed + same inputs ⇒ identical hash sequences; one extra input ⇒ divergence exactly after it lands. If you broke one of these, nothing else you did matters.
- Balance work happens through AI-vs-AI self-play (`tests/Sim.Tests/HeadlessMatchTests.cs` is the pattern) — assertion-driven, not manual playtesting. Humans playtest for fun only.
- When adding a feature, add the test that would have caught its bug. Every silent-failure bug we've had (assembly name, radius overflow, Chance threshold) now has a tombstone test.

## Environment facts (this Mac — do not relearn them the hard way)

- **One .NET toolchain by policy**: system SDK at `/usr/local/share/dotnet` (10.x). All csproj target `net10.0`. Do not install side-by-side user-local dotnets; competing installations caused a full day of "Failed to load project assembly" mystery.
- Engine/SDK versions move as a set: Godot 4.7.2 ⇔ `Godot.NET.Sdk/4.7.2` ⇔ `config/features=PackedStringArray("4.7", "C#")`.
- **`dotnet/project/assembly_name` in `game/project.godot` must equal the `AssemblyName` produced by `game/Game.csproj` ("Game")**. A mismatch produces the silent "Cannot instantiate C# script because the associated class could not be found" — no exception, no stack trace, just failure. This cost the most debugging time of any bug in the project.
- `game/Game.sln` must exist **inside the Godot project directory**. `Heroes1918.sln` at the repo root exists for IDE convenience; Godot never sees it.
- `godot` on PATH is `/opt/homebrew/bin/godot`, a wrapper that exports `DOTNET_ROOT=/usr/local/share/dotnet` then execs `/Applications/Godot_mono.app/Contents/MacOS/Godot` directly. Symlinking the binary breaks bundled-assembly resolution; skipping `DOTNET_ROOT` makes managed runtime resolution fall back to wrong paths.
- Shell trap: `timeout 60 cmd | tail -3` reports **tail's** exit status, masking timeouts. Capture exit codes separately when a command can hang.
- First Godot run after changing targets: `dotnet build game` writes into `.godot/mono/temp/bin/Debug`, which is where the engine loads from — no separate editor build step needed for CLI verification.

## Godot C# API traps encountered here

- `Side` collides with `Godot.Side` → game scripts use `using Side = Sim.Side;`
- `FileAccess` collides with `System.IO.FileAccess` → qualify as `Godot.FileAccess`
- It's `Control.SizeFlags` (not `SizeFlag`); theme overrides are methods, so they cannot appear in object initializers
- `OS.GetCmdlineUserArgs()` returns args after `--` (used by `--smoke` mode)

## Tuning

All combat/economy constants live in `SimConfig`; unit stats in `UnitTypes`. Change freely — the determinism suite keeps you honest, and balance sweeps will be how the game gets tuned once step 4 lands.

## Commits

Imperative mood, subject line describes the build-order milestone or the fix; body lists subsystems touched and any environment lesson worth remembering (see prior commits for tone).
