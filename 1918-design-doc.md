# Project 1918 (working title)

A Company of Heroes-style RTS built for two players: Joel and his son. Not a commercial product. Built primarily by coding agents under Joel's direction.

## Core decisions

| Decision | Choice |
|---|---|
| Audience | Two players (father + son) |
| Core mode | Comp-stomp co-op vs AI |
| Setting | Western Front, Spring 1918 |
| Structure | Pure skirmish, no campaign |
| Co-op roles | Any faction human-playable (as in CoH3); default co-op is BEF + AEF defending |
| Engine | Godot 4 (C# sim core, GDScript glue) |
| Architecture | Deterministic simulation library + thin renderer |
| Networking | Lockstep, 2 players, Steam or Tailscale transport |
| Match structure | CoH3-style open VP skirmish; maps are unscripted terrain + point layouts |
| Scale | CoH3-like company scale (~8-12 squads per player), 30-40 min matches |
| Art direction | Stylized low-poly 3D; animated soldiers, CoH-like watchability |
| Asset pipeline | Image-to-3D generation + agent-driven Blender cleanup (see Art pipeline) |
| Maps | Handcrafted data files first; procedural generation later, if ever |

## The fiction

Sector battles of 1918, the year the war moved again: the Spring Offensive and the Hundred Days as backdrop. Match setup is CoH3-style: pick a map, pick factions. Default co-op is the players as BEF and AEF against the German AI. Any faction is human-playable. There are no fixed attacker or defender roles; the front is fluid and both sides fight for ground.

## Match structure

CoH3's skirmish model. Territory is divided into sectors with capture points; holding territory raises manpower income and supply points generate shells. A small number of victory points drain the enemy's tickets while held. First side to zero tickets loses. Matches run 30 to 40 minutes.

Maps carry no scripting of any kind: a map is terrain, cover, buildings, and a point layout, exactly as in CoH3. Everything that happens in a match emerges from the two sides playing.

The preparation mechanics (trench digging, wire, MG siting, artillery registration) are in-match tools available at any time, not a phase. Digging in is a standing strategic option that trades tempo for defensive strength.

Units are requisitioned from off-map reserve and march in from the player's edge of the map. No base construction; the HQ sector is where reinforcement and rally happen.

## Signature mechanics

- **Craters become cover.** Barrages deform the map; shell holes are usable cover. The enemy's bombardment builds the terrain you fight from. This is the game's centerpiece mechanic.
- **Creeping barrage as a player tool.** Plot a barrage line that walks; infantry advances behind it. Mistiming shells your own men. In co-op, one player times the guns while the other moves troops.
- **Player-built fortifications.** Engineers dig trench networks in real time. Your line is something you made.
- **Gas.** Drifting area denial. Masked troops fight worse but survive.
- **Tanks as monsters.** Slow, terrifying, breakdown-prone. Need infantry screens.
- **Suppression and morale.** Central, not secondary. Green troops break; veterans hold.

## Core systems

- **Economy: Manpower + Shells.** Manpower drips over time, rate modified by territory held; buys and reinforces squads. Shells accumulate via supply points and power all artillery: barrages, creeping barrages, hurricane bombardments. Tanks cost both. No third resource.
- **Fog of war: true line of sight.** Terrain, smoke, and structures block vision. Recon via tethered observation balloons (deep vision, improve artillery accuracy, can be shot down) and consumable spotter flights. Artillery accuracy scales with observation.
- **Facing and arcs.** Squads and support weapons have facing; machine guns have limited traverse arcs. Frontal assaults into arcs die; flanking works. In the sim data model from day one.
- **Retreat and reinforcement.** A retreat order sends a broken squad back along the communication trench to the reserve line. Squads reinforce near field telephone positions or in friendly trenches. Preserving squads matters because of:
- **Veterancy.** Three ranks earned in combat, modest bonuses (accuracy, suppression resistance). AEF starts greenest and earns fastest; German stormtroopers start at rank one.
- **Garrisons and rubble.** Buildings are garrisonable; artillery converts them to rubble; rubble persists as cover. Together with craters, the map degrades into playable moonscape over a match.
- **Command abilities.** Small fixed kit per faction (smoke, gas shells, spotter flight, faction signature). A doctrine-choice layer is out of scope unless the game earns it later.
- **Defeat pacing.** Victory-point drain does the pacing; losing territory costs income and position but must not trigger inevitable collapse. Comebacks stay possible late; the director is tuned to this.
- **Company roster and memorial.** Every soldier has a generated name and hometown. The players' companies persist across matches: veterancy attaches to the man, not the squad (a squad's quality is the sum of its men); wounded soldiers sit out the next match; the dead are permanent entries on a memorial wall. Reinforcements arrive as new named men. This is memory attached to skirmish, not a campaign.
- **Flank sectors.** The neighboring sectors of the front are abstractly simulated. Players hear their barrages and see their flares; a collapsing neighbor sends pressure onto the flank, a holding one may release spare reserves. A small system that makes each match feel like part of an army-sized battle. The director accounts for it. Design principle: flank events are the game's variance engine, and every one must be telegraphed through audible or visible signals before it lands, so that every surprise is legible in hindsight. Vary when and where, never whether the players had a chance to read it. Reserve windfalls are rare enough to feel like providence.
- **Pause and mid-match save.** Deferred nice-to-have. Cheap under lockstep (a save is the input log), so it lands after first playable, not before.

## Factions

**BEF (player):** Veterans. Expensive resilient squads, best artillery (superior creeping barrages), Lewis guns, Mark V tanks. Holds ground and wins firefights.

**AEF (player):** Fresh and numerous. Large rifle squads, cheap replacements, high morale, counterattack bonuses. Green (suppresses easily until veteran), borrowed French kit (FT-17s, jam-prone Chauchats). Momentum and manpower.

**German Empire (playable):** Stormtroopers as the signature player toy: infiltration squads that move fast, ignore some suppression, and excel at close assault, but bleed men. Hurricane barrages (short, violent, on-demand) versus the Allies' sustained artillery. Flamethrowers, and rare A7V/captured tanks as monsters. When AI-controlled, attacks in director-controlled waves.

## AI design

- Tactical layer: squads that use cover, flank, retreat when broken.
- Strategic layer: a full skirmish AI, as in CoH3 comp-stomp. It manages its economy, captures territory, contests victory points, attacks, and defends, all emergently. This is the hardest single component in the project and the one that most determines whether the game is fun.
- Director layer: sits above the strategic AI and modulates tempo and aggression by reading match state (map control, player losses, momentum), keeping matches tense rather than steamrolled in either direction. It also drives flank-sector events.
- Adaptive loop: every match logged. Agents periodically review logs and patch AI weaknesses the players exploited. The opponent learns Joel-and-son doctrine over time.

## Art direction and asset pipeline

Stylized low-poly 3D. The camera sits at RTS height, which makes the documented flaws of AI-generated rigged characters (weight painting on shoulders, facial rigging) invisible. Individual soldiers are visual puppets: the renderer distributes animated soldier models around each squad's sim position and selects animations from sim state (moving, firing, suppressed, dead). All watchability lives in the presentation layer; the sim never knows individual soldiers exist.

Pipeline (agent-drivable end to end):
1. **Concept sheets** per faction via image generation, human-curated. These live in `/design/style-bible/` and anchor everything downstream.
2. **Image-to-3D** generation of characters and vehicles from the concept sheets (Meshy primary, driven via its REST API from `/tools`, so asset generation is scripted, not clicked). Image input, not text input, to keep the army stylistically coherent.
3. **Blender cleanup pass** via Blender MCP: topology cleanup, decimation to poly budget, UV fixes, export to glTF per naming spec. Terrain props and materials pulled from Poly Haven through the same loop.
4. **Godot import and animation retarget**: Meshy animation presets plus the Mixamo library for gaps. No hand-keyed animation; RTS distance doesn't need it.
5. **Audio**: ElevenLabs for squad voice barks (period accents, field-telephone filter) and effects. Public-domain 1918 gramophone recordings for music.

Why generation over purchased packs: WWI-specific kit (Brodie helmets, puttees, Chauchats, FT-17s, A7Vs) barely exists in commercial asset stores, and per-character costs of traditional pipelines are absurd for a two-player game.

## Scope

- ~15 unit types total; all three factions (BEF, AEF, German Empire) human-playable
- 3 to 5 maps
- No menus beyond a skirmish launcher, no tutorial, no difficulty settings (one AI, tuned to the two players)

## Build order

1. **Sim core.** Pure C# library. Units, cover, suppression, capture points. Fixed timestep, fixed-point math, seeded RNG, deterministic. Automated tests including determinism checks (state hashes).
2. **Lockstep networking.** Input exchange, tick buffering, per-tick hash comparison for desync detection. Built while the game is still placeholder shapes.
3. **First playable.** One map, capsules for units, capture points and ticket drain working, a rudimentary AI opponent that captures ground and attacks. Milestone: Joel and son win an ugly skirmish together.
4. **Barrages and the real AI.** Barrage system with crater deformation, trench digging, the full strategic skirmish AI, and the director on top.
5. **Factions.** BEF and AEF kits, then the German player kit and roster.
6. **Art pass.** Execute the asset pipeline above. Camera and squad-puppet rendering system first with one placeholder soldier model, then batch generation of the rosters.
7. **Adaptive AI loop.** Match logging, agent-driven AI patches.

## Testing philosophy

Agents run the game headless. AI-vs-AI self-play overnight for balance sweeps. Game logic verified by assertion, not manual playtesting. The humans only playtest for fun.
