# Project 1918

A Company of Heroes-style real-time strategy game built for exactly two players: Joel and his son. Western Front, Spring 1918. Co-op against an AI opponent by default; every faction human-playable; matches decided by victory-point ticket drain while craters slowly turn the map into a moonscape.

Not a commercial product — a father-and-son project built primarily by coding agents under Joel's direction. The full design lives in [1918-design-doc.md](1918-design-doc.md).

## Status

Following the design doc's build order:

- [x] **1. Sim core** — deterministic Q32.32 fixed-point simulation: squads, cover, suppression and pinning, veterancy, capture points, ticket drain, true line of sight
- [x] **2. Lockstep networking** — versioned binary protocol over reliable-ordered transport; input-delay send windows; per-tick state-hash desync detection; TCP transport (works over LAN/Tailscale); joiner-side peer-seed adoption
- [x] **3. First playable** — Godot shell, capsule squads, box selection and attack-move orders, one handcrafted JSON map, rudimentary AI opponent, solo / host / join
- [~] **4. Barrages & the real AI** *(in progress)* — shipped: walking barrages, craters-as-cover, buildings-blast-to-rubble, trench digging, engineers, AI-called barrages · remaining: gas, flank-sector director, deeper strategic AI
- [ ] **5. Factions** — BEF, AEF, German Empire kits
- [ ] **6. Art pass** — image-to-3D asset pipeline, animated squad puppets
- [ ] **7. Adaptive AI loop** — match logging, agent-driven AI patches

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) — `dotnet --version` should print 10.x
- [Godot 4.7.x **.NET edition**](https://godotengine.org/download/macos/) — must be the mono/.NET build, not standard Godot

On this Mac, `godot` in PATH is a wrapper script (`/opt/homebrew/bin/godot`) that exports `DOTNET_ROOT` and execs the app binary directly. A bare symlink is not enough: Godot resolves its bundled assemblies relative to the real executable path.

## Build & test

```bash
dotnet build Heroes1918.sln   # sim, netcode, game, tests
dotnet test                   # 63 tests: sim behavior + netcode determinism
```

Headless verification (all exit non-zero on failure):

```bash
godot --headless --path game -- --smoke     # simulation end-to-end: real map, two AIs
godot --headless --path game -- --selfplay  # full game scene & frame loop, AI vs AI
godot --headless --path game -- --inputtest # synthetic mouse events drive selection & orders
```

## Play

Open `game/` in the Godot editor and press F5, or straight from a terminal:

```bash
godot --path game
```

| Menu item | What it does |
|---|---|
| **Solo vs AI** | You command the Allies against the Central Powers AI |
| **Host co-op** | You are Allies; wait for your partner (default port 19180) |
| **Join co-op** | Enter the host's IP or Tailscale name; you are Central |

Controls: left-click / drag-box to select, right-click to attack-move, WASD or arrows to pan, mouse wheel to zoom, `R` restarts after the match ends. Hold victory points to drain enemy tickets to zero.

## Layout

```
src/Sim/        pure C# simulation library — zero engine dependencies
src/Lockstep/   deterministic networking: protocol, session, transports
game/           Godot project: rendering, input, HUD, map JSON
tests/          xUnit suites for sim behavior and netcode determinism
```

Contributing (humans and agents alike): read [AGENTS.md](AGENTS.md) before touching anything under `src/Sim` — the entire game rests on bitwise determinism, and it is easier to preserve than to restore.
