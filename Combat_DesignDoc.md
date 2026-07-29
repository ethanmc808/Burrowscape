# Combat System — Design Doc

**Status: core math/resolution layer implemented 2026-07-28, not yet playtested or AI-wired.** Every
formula/system this doc specifies concretely — type chart, crit/hit/damage math, status effects, the
attack travel/fizzle pipeline, flanking-slot bookkeeping, enemy stat/level resolution — now has real code
behind it (see "Implementation checklist" at the bottom for the full file list). **What's deliberately
NOT built yet**: any AI/behavior-tree logic that decides when a bunny or enemy actually attacks, walks to
a flank position, or targets something; the invasion spawn/trigger system (when/where enemies appear);
Guard Room deploy logic; and NPCBunny "fainting" (HP can reach 0 and fires `OnDefeated`, but nothing
reacts to it — no state machine integration). This is a deliberate scope boundary, not an oversight: it
either requires design decisions this doc doesn't have yet (exact melee distance, invasion triggering,
faint/recovery behavior) or touches NPCBunny's large, already-stable state machine in ways that need
in-Editor testing to verify safely, which wasn't available while writing this code. See each section
below for exactly what exists vs. what's still a stub.

This doc covers **base-invasion combat** (the first, simpler combat track). Squad-based quest combat
(3-bunny squads, full type-coverage strategy layer) and the Guard Room's manual-deploy UX are related
but scoped separately — see their own sections near the end and the build-order note at the very bottom.

## Narrative context

The Evil Fox King has driven the bunnies underground; the player builds an army to eventually retake
Bunnytopia and defeat him (final boss). **Base invasions are deliberately NOT Fox King forces** — that
roster (fox/canine grunts, lieutenants) is reserved for the later quest system. Base invasions use
mundane pest/critter types instead (snails, moles, worms, rats, snakes, slimes, etc.) — things that would
plausibly tunnel into a burrow. Difficulty scaling for invasions leans on breadth/toughness of pest
variety rather than a straight narrative escalation; the boss-ward escalation belongs to the quest track.

## Type system

20 types: Neutral, Fire, Water, Plant, Shock, Insect, Melee, Stone, Mind, Toxic, Ice, Sound, Air, Earth,
Pixie, Light, Metal, Ghost, Dark, Draco (`BunnyType` enum, `NPCBunny.cs`). Every bunny/enemy is
single-typed — no dual-typing, so type effectiveness is always a single lookup, never stacked.

**Deliberate deviation from real type-chart games: no true 0-damage immunities.** The worst matchup is
1/4 damage, not zero — no attack in auto-combat is ever fully wasted, just weaker. Modifiers: 2x
(super-effective), 0.5x (not very effective), 0.25x (resisted-max), 1x (neutral, unlisted default).

Full chart, hand-authored by Ethan (source: `Burrowscape Type Chart.xlsx`), implemented as a sparse
lookup in `TypeChart.cs` (`TypeChart.GetMultiplier(attacker, defender)`):

| Atk ↓ / Def → | Neu | Fire | Wat | Pla | Sho | Ice | Min | Tox | Sou | Ins | Mel | Sto | Ear | Air | Met | Pix | Lig | Gho | Dar | Dra |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Neutral** | | | | | | | | | | | | - | | | - | | | x | | |
| **Fire** | | - | - | + | | + | | | | + | | - | | | + | | - | | | - |
| **Water** | | + | - | - | | | | | | | | + | + | | | | | | | - |
| **Plant** | | - | + | - | | | | - | | - | | + | + | - | - | | + | | | - |
| **Shock** | | | + | - | - | | | | | | | | x | + | + | | - | | | - |
| **Ice** | | - | + | + | | - | | | | | | | + | + | - | | | - | | + |
| **Mind** | | | | | | | - | + | | - | + | | | | + | | | | x | |
| **Toxic** | | | + | + | | | | - | | + | | - | - | + | x | + | | - | | - |
| **Sound** | | | | - | | + | + | | - | | | - | - | + | - | | | | | |
| **Insect** | | - | | + | | - | + | - | + | | - | | + | - | - | + | | - | | |
| **Melee** | + | | | | | + | - | - | | | | + | | - | - | - | | x | + | |
| **Stone** | | + | | | | + | | | | + | - | | - | + | - | | | | | |
| **Earth** | | + | | - | + | | | + | | - | | + | | x | + | | | | | |
| **Air** | | | | + | - | | | | | + | + | - | | | - | | | | | |
| **Metal** | | - | - | | - | + | | | | | - | + | | | - | + | | | | |
| **Pixie** | | | | - | | | | - | | | + | | | | - | | - | | + | + |
| **Light** | | | | x | | | | + | | + | | - | - | | - | | | + | + | |
| **Ghost** | x | - | | | + | | + | | + | - | | | | | | | x | + | - | |
| **Dark** | | | | | | | + | | | - | - | | | | | - | + | + | - | |
| **Draco** | | | | | | - | | | | | | | | | - | - | | | | + |

(`+` = 2x, `-` = 0.5x, `x` = 0.25x, blank = 1x/neutral)

## Stats & Luck

Bunny stats resolve via the existing IV/EV/Nature system (`BunnyStatCalculator`, see
`BunnyTypeSystem_DesignDoc.md`). Combat introduces Luck's first real gameplay effect:

**Crit chance** = `0.0625` base (1/16) `+ (resolved Luck stat × 0.0005)`, clamped to `[0, 0.50]`.
Implemented in `CombatMath.GetCritChance(luckStat)`. Uses the fully resolved Luck stat post IV/EV/
Nature, not a type's base Luck. All three numbers are named consts — easy to retune.

**Crit damage multiplier**: 2x, applied on top of the normal damage formula when a crit lands (separate
from the chance formula above).

## Attacks: one signature move per type

Every `BunnyType` has exactly **one** named attack — no movesets, no ability to choose an attack
mid-fight. `BunnyTypeDefinition.attackName` holds the name (empty/unauthored until designed — same
"null until content exists" convention already used for `.icon`/`.prefab`). Toxic's is authored:
**"Sludge Hurl."** The other 19 are not yet named.

**Base Power scales with level in 5 tiers of 10 levels each**, universal across all types (not stored
per-type — a pure function of level):

| Levels | Base Power |
|---|---|
| 1-9 | 20 |
| 10-19 | 40 |
| 20-29 | 60 |
| 30-39 | 80 |
| 40+ | 100 |

Implemented as `CombatMath.GetBasePower(level)`. **Enemies use this exact same function now** — since
enemies level too (see "Enemy leveling by population" below), a level-50 Slime's Sludge Hurl hits at
100 BP just like a level-50 bunny's would. `EnemyDefinition` has no `basePower` field (removed — it's
resolved from whatever level the spawned instance actually has, at runtime, not stored per-definition).

**Melee vs. ranged is a per-type data flag, not a fixed roster.** Most types use actual projectiles;
Neutral and Melee are melee-style (more types may be added to this list later, as-yet undesignated). The
flag lives alongside `attackName` on `BunnyTypeDefinition`, defaulting to ranged/false, assignable
whenever that type's attack actually gets designed — not decided for all 20 types upfront. **Melee is not
a separate code path** — it's the same spawned-hitbox pipeline as a projectile, just short-range and
stationary: the particle effect plays essentially in place at the target rather than travelling any real
distance (the attacker is already standing adjacent, per the flanking rule below). See "Attack visuals"
below for how melee's particle differs from a projectile's.

## Damage formula

Derived from the real Pokémon damage formula (Gen VI+, researched from Bulbapedia) and adapted
variable-by-variable to what actually applies here:

```
Damage = (⌊⌊(⌊2×Level÷5⌋+2)×Power×A÷D⌋÷50⌋+2) × Critical × random × STAB × Type
```

(Pokémon's Targets/PB/Weather/GlaiveRush/Burn/other/ZMove/TeraShield terms are all dropped — no
equivalent systems exist; Burn is instead its own status effect, see below.)

- **Level** — attacker's level (bunnies level normally; enemies now level too, see "Enemy leveling"
  below — this replaces an earlier draft where enemies were flat/non-leveling).
- **Power** — the attacker's Base Power for its level, `CombatMath.GetBasePower(level)` — same function
  for bunnies and enemies alike, since both now level.
- **A / D** — attacker's resolved Attack stat / defender's resolved Defense stat.
- **Critical** — 2x if the crit-chance roll (above) succeeds, else 1x.
- **random** — integer **80–100** divided by 100 (wider variance than Pokémon's 85–100, deliberate).
- **STAB** — literal, always-on **1.5x**, since every attack is currently same-type-by-construction (no
  off-type moves exist). Kept in anticipation of a possible future "bunny learns a secondary attack of a
  different type" mechanic; if this makes bunnies overtuned before that exists, the fix is to raise enemy
  HP rather than remove STAB.
- **Type** — `TypeChart.GetMultiplier(attacker.type, defender.type)`, single lookup, 2x/0.5x/0.25x/1x.

## Hit/evasion (separate from the damage formula)

Speed gains a second job beyond whatever else it does: **higher Speed increases your own chance to
land a hit and increases the chance an opponent's attack on you misses; lower Speed does the reverse.**
A to-hit roll gates whether an attack lands *at all*, resolved at the moment the attack's hitbox actually
reaches the defender (see "Attack travel & hit resolution" below) — only a landed hit proceeds into the
crit/random/type/STAB damage math above.

Proposed formula (shape agreed, exact constants not yet locked by Ethan):

```
HitChance = 0.90 + (AttackerSpeed − DefenderSpeed) × 0.0005, clamped to [0.50, 0.95]
```

Same "base + stat × small constant, clamped" shape as the crit-chance formula, for consistency.

## Attack travel & hit resolution

Combat is **auto-battle with visible, non-hitscan attacks** (unlike Fallout Shelter's instant hits) —
the point is to actually show off each type's attack animation/VFX. Core principle: **"attack fired" and
"damage applied" are decoupled.** An attack spawns a hitbox that travels from attacker to defender over
some duration; damage/hit-reaction/floating numbers only resolve on actual arrival, never at cast time. A
near-instant attack (melee, or a fast projectile like lightning) just uses a very short/zero travel time
through the exact same path — no separate "instant attack" code.

Resolution order once a hitbox is spawned:
1. **Homing/auto-target**: the hitbox always tracks its target's live position and will visually connect
   if the target still exists when travel time elapses — a moving target is never physically missed by
   the travelling hitbox. This is a pure targeting/tracking guarantee, separate from (2).
2. **Target died mid-flight → fizzle harmlessly.** If the target is gone by arrival (e.g. killed by a
   faster ally's hit a moment earlier), the attack just disappears — no damage, optionally still plays
   its impact VFX in place. No retargeting to another enemy-group member.
3. **Hit/miss roll** (the Speed-based formula above) — only now, at actual arrival.
4. **If hit**: crit roll → damage formula → status-effect chance roll (below).

### Attack visuals: particle effects layered on the existing Attack animation

**Clarification: "attacks are particle effects" does NOT replace character animation.** There's already
a generic "Attack" Animator state built into the bunny rig controllers (confirmed present in
`NPC_Rabbit_Neutral_Controller.controller`/`Rabbit_Neutral_Controller.controller`, just not yet wired to
any code trigger since no combat system calls it). That existing attack animation still plays on the
attacker as normal. The **type-specific magical effect itself** — the fireball, the sludge glob, the
lightning bolt — is a Particle System, not hand-drawn sprite animation, layered on top of / timed to that
existing animation (e.g. via an animation event), rather than replacing it. This is purely a decision
about how the *effect* is rendered; the *character* still visibly performs its built-in Attack animation.

Structure for ranged attacks: a single "AttackInstance" prefab whose root transform carries a `Collider`
(kept for possible future use — e.g. an AOE-splash query via `Physics.OverlapSphere` at arrival — but not
what gameplay trusts, see below) plus one or more child `ParticleSystem`s (a "core" particle for the
projectile body, optionally a trailing particle system for a streak effect). The whole root moves
together — VFX and hitbox always occupy the same place.

**Hit-arrival is deterministic (distance/time-based), not physics-collision-based.** A fast-moving small
trigger `Collider` risks tunneling past its target between physics steps and never firing
`OnTriggerEnter` — a real risk for this game's faster attacks, and this project has hit collision/click
fragility issues before (see `project_burrowscape_click_raycast_fragility` memory). Since homing already
guarantees an attack visually connects if the target's alive, the *actual* "resolve the hit now" trigger
is the root transform reaching within a small distance of the target's live position (or its travel time
elapsing) — reliable regardless of speed. The Collider is present on the prefab but isn't gameplay-
authoritative.

**Melee attacks are short-range, stationary particle effects — not a travelling hitbox at all.** Rather
than spawning near the attacker and homing across distance like a projectile, a melee attack's particle
effect plays essentially in place at/around the target, since the attacker is already standing adjacent
(per the flanking rule above) — there's no meaningful distance to travel. Still the same underlying
AttackInstance concept (root + Collider + ParticleSystem), just with travel effectively skipped rather
than merely shortened.

**Data hookup**: `BunnyTypeDefinition` will need an `attackVFXPrefab` field alongside `attackName`/the
melee flag — null until that type's VFX is authored, same "null until content exists" convention as
`.icon`/`.prefab` elsewhere. Not yet added to the script (still design-only).

## Status effects

5 statuses, each tied to one type's signature attack, ~20% chance-on-hit per attack (placeholder
percentage, not finalized):

| Type | Status | Effect | Duration |
|---|---|---|---|
| Fire | Burn | DOT 1/16 max HP per tick, **-25% actual Attack** | 4s |
| Toxic | Poison | DOT 1/32 max HP per tick, **-25% actual Defense** | 8s |
| Ice | Chill | **-35% actual Speed stat** | 6s |
| Shock | Paralyze | **-25% movement speed AND -25% attack speed** (time per attack-animation cycle) | 6s |
| Mind | Sleep | Full incapacitation — cannot move or act | 3s |

Paralyze and Chill are deliberately different mechanisms: Paralyze slows movement/animation timing
directly without touching the Speed *stat* (so it doesn't cascade into the hit/evasion formula); Chill
hits the Speed stat itself, which *does* cascade into hit/evasion (a Chilled target is easier to hit and
more likely to miss). DOT fractions mirror modern Pokémon's approach (fraction of max HP per tick) at
Ethan's request, though the actual Burn/Poison numbers (1/16 for 4s vs. Pokémon's 1/16-for-the-whole-
battle, and 1/32 for 8s vs. Pokémon's 1/8) are Burrowscape-specific tuning, not a direct port.

## Enemies

`EnemyDefinition` (ScriptableObject, `Assets/Scripts/Combat Scripts/EnemyDefinition.cs`) is a new
catalog type mirroring `BunnyTypeDefinition`'s one-asset-per-entry pattern, but a flat stat-stick — no
IV/EV/Nature/traits, since pests aren't individual characters. Fields: `displayName`, `type` (BunnyType),
`prefab`, `baseHP/Attack/Defense/Speed/Luck`, `attackSource` (direct reference to the matching
`BunnyTypeDefinition`, reusing its attack name/animation/VFX instead of bespoke enemy art — "reuse
animations/particle effects as much as possible" was an explicit ask). **No `basePower` field** — enemies
level like bunnies (below), so their attack's Base Power comes from `CombatMath.GetBasePower(level)` at
runtime using whatever level the spawned instance has, exactly like a bunny.

**Note**: `EnemyDefinition` as currently coded does NOT yet have a level field — it was written before
"enemy leveling by population" (below) was decided. It'll need updating to add a level range (or the
generator will need to compute a level and pass it through some other path) before that's wired up.

### Slime — first enemy, seeded via `Assets/Scripts/Editor/EnemyDataGenerator.cs`

Baseline/tutorial-weak roster floor. Toxic type (deliberately, to exercise the type chart — Toxic
resists Plant, a starter type). Pure stat-stick, no special ability/gimmick (a "splits on death" or
on-hit-debuff gimmick was discussed and explicitly deferred for the first enemy, not forgotten). Reuses
Toxic's "Sludge Hurl" attack identity — described as a slow lobbed "goop glob," though the actual
projectile prefab/animation doesn't exist yet, only the data linkage.

| Stat | Value |
|---|---|
| HP | 40 |
| Attack | 25 |
| Defense | 15 |
| Speed | 15 |
| Luck | 10 |

(No Base Power row — computed from whatever level the spawned instance has, via
`CombatMath.GetBasePower(level)`, same as a bunny.)

Future scaling idea floated, not committed: Small/Medium/Large Slime variants reusing the same rig at
different scale/tier, as cheap invasion-roster breadth without new art.

### Enemy leveling by population

Enemies now level like bunnies (supersedes the flat/non-leveling assumption baked into
`EnemyDefinition`/`EnemyDataGenerator` above). Level scales with the player's population:

- Population 10 → enemy level range 3–5
- Population 200 → enemy level range 45–50
- Interpolated linearly in between; exact shape not yet coded.

Population 200 is the same ceiling as the Group 7/Draco bunny-type unlock, so max invasion difficulty
and full type-roster unlock land together narratively. All four numbers (start min/max, cap min/max)
should be Inspector-tunable the same way `WildBunnySpawner`'s pacing fields are.

## Positioning & spots

- **Ranged attacks**: can fire from a distance without moving, up to a universal max range of **12
  Unity units** (chosen to span the largest possible room footprint, 12x2x6). Not per-type for now.
- **Melee attacks** (Neutral/Melee types, on both bunny and enemy sides): must physically stand at a set
  distance to the *left or right* of the target — never front/behind. The exact standing-distance number
  isn't decided yet.
- **Working bunnies interrupted by an invasion**: ranged bunnies fight from their current work spot, no
  movement. Melee/Neutral working bunnies DO break off their work spot and walk to a flank position —
  a deliberate exception to this project's general pathing-stability caution (see
  `feedback_pathing_caution_cosmetic` memory), made consciously for a real gameplay need, not a casual
  change.
- **Flanking capacity**: capped at **2 attackers per target** (left + right slot). A 3rd+ melee bunny in
  the room idles until a slot frees (target dies, or an engaging attacker faints/disengages). The same
  cap is assumed to apply symmetrically when melee-type enemies flank a bunny (not explicitly confirmed,
  a natural extension of the stated rule — worth a quick confirm before/while implementing).
- **CombatSpots**: 6 per room — Guard Room bunnies' default "post" position while idle/awaiting an
  enemy. Matches Guard Room's max roster of 6. Authored via the existing Spot/Path auto-populator tool
  (`[SpotNamePrefix]`-tagged prefab, no tool changes needed), living inside the room prefab like
  `RoomSpot`/`RoomPath` (see `feedback_roompath_prefab_constraint` memory).
- **EnemySpots**: 3 per room, same auto-populator convention. Ranged enemies just hold at their
  EnemySpot and attack from there. Melee enemies path to their EnemySpot first (an initial staging/entry
  position), then approach the closest bunny and flank whichever side is geographically nearer — the
  same mechanic as bunny-initiated melee, just enemy-initiated.

**Open/unresolved**: exact melee standing-distance value; whether the 2-flanker cap is truly symmetric
for enemies-flanking-bunnies; what happens if a bunny mid-walk toward a flank slot finds it taken by
another bunny first (race condition, not addressed).

## Guard Room

**Scaffolding only — deploy/combat behavior is a separate, not-yet-designed system.** `GuardRoom.cs`
(`Assets/Scripts/Room Scripts/GuardRoom.cs`) exists as a room shell mirroring `Bedroom`/`LivingRoom`'s
shape: a `combatSpots` list (`RequestSpot`/`ReleaseSpot`/`HasAvailableSpot`, same claim/release pattern
as every other room's `RoomSpot` list) and registration with `BaseManager` (`RegisterGuardRoom`/
`UnregisterGuardRoom`), so a future "nearest Guard Room" deploy-picker lookup has a list ready to query.
`RoomBase` also gained an `enemySpots` list (3 per room, every room — not just Guard Rooms, since any
room can be invaded), same `[SpotNamePrefix("EnemySpot")]` auto-populator convention. No deploy logic,
no combat AI, no roster-capacity enforcement beyond spot count exists yet — that's all still design work.

Not present in Fallout Shelter — Ethan's own addition. Guard bunnies auto-fight whatever invades their
own room (passive baseline defense) using the same CombatSpots/positioning rules as any other room. The
player can also **manually deploy** a Guard Room's roster to reinforce a *different* room during an
active invasion, deliberately avoiding the harder pathing question of an enemy group relocating mid-
transit (redeployment is an infrequent, deliberate player action, not continuous auto-chase).

- **Only one Guard Room "unit" may be deployed into any given room at a time** — avoids contention over
  the 6 CombatSpots and simplifies "whose spots are these" bookkeeping.
- **Deploy UX**: an automatic "Deploy" prompt appears the moment an enemy group spawns. With a single
  Guard Room, one click sends them with no picker. With 2+ Guard Rooms, the same click should expand a
  small inline list sorted nearest-to-the-invaded-room first (progressive disclosure, not a separate
  menu screen) — likely reusable via `BaseManager.cs`'s existing nearest-room-by-`Vector3.Distance`
  pattern. **Not yet confirmed**: whether the *un-modified* single-click Deploy always means "nearest
  Guard Room" or something else (e.g. most-idle-bunnies).
- Possible future direction, not committed: a combat-specific type-match/trait bonus for Guard Room
  roster choice, mirroring how `recommendedTypes` works for production rooms today — motivated by
  wanting Guard Room roster selection to carry real weight given how differentiated bunnies already are
  (IV/EV/Nature/traits).

## Out of scope for this doc (later systems)

- **Quest combat**: 3-bunny squads, full type-coverage strategy (a squad can't cover all 20 types, so
  composition is a real tradeoff). This is where Fox King forces actually appear. Requires base-invasion
  combat to exist first, since quests need combat resolution to mean anything.
  Build order: **combat → quests → Fox King boss fight.**
- Quest dispatch likely reuses the Foraging system's dispatch/trip-lifecycle bones, but foraging dispatch
  is solo (one bunny) where a combat quest needs party dispatch (a squad) — same "who's away and
  unavailable" bookkeeping as `CanDepartForForaging`, multiplied across several bunnies. Worth designing
  for up front when quests get scoped.

## Implementation checklist

**Done (code), needs in-Editor follow-up:**
- [x] `CombatBalanceConfig.cs` — every "easily editable for balance" number (crit, random variance, STAB,
      hit/evasion, all 5 statuses' chances/durations/DOT-fractions/debuffs, positioning ranges, enemy
      level-ramp knobs) as an Inspector-editable ScriptableObject, loaded via `Resources` (same pattern as
      `RoomThemeCatalog.Load`). **Needs**: run `Burrowscape > Generate Combat Balance Config` in-Editor to
      actually create `Assets/Resources/CombatBalanceConfig.asset` — nothing reads real values until then
      (falls back to code defaults with a warning).
- [x] `CombatMath.cs` — `GetCritChance`, `GetBasePower` (unchanged, structural), and new `GetHitChance`
      (the Speed-based accuracy formula) + `RollDamageVariance`, all reading from `CombatBalanceConfig`.
- [x] `TypeChart.cs` — unchanged from earlier this session, still the sparse hardcoded lookup.
- [x] `ICombatant.cs` — shared interface implemented by `NPCBunny` (additive — no existing state-machine
      code touched) and the new `EnemyInstance.cs`.
- [x] `EnemyInstance.cs` — runtime counterpart to `EnemyDefinition`. Rolls its level once at spawn via
      `CombatBalanceConfig.RollEnemyLevel()` (mirrors `WildBunnySpawner`'s population-ramp shape on
      separate knobs), resolves stats via `BunnyStatCalculator`'s new raw-stat overload (IV/EV fixed at 0
      — no individual variance, pure stat-stick). **Needs**: nothing spawns this yet — no invasion-trigger
      system exists. Whatever does eventually should `Instantiate(EnemyDefinition.prefab)` +
      `GetComponent<EnemyInstance>().Initialize(def)`.
- [x] `BunnyStatCalculator` (`BunnyStats.cs`) — refactored (behavior-preserving) to expose a raw-base-stat
      overload so `EnemyInstance` reuses the exact same level-scaling formula instead of duplicating it.
- [x] `StatusEffectType.cs` / `StatusEffectController.cs` — all 5 statuses, application/tick/expiry, stat
      modifiers (`ModifyAttack`/`ModifyDefense`/`ModifySpeed`) and movement/attack-speed multipliers for
      Paralyze. **Judgment call**: only ONE status active at a time (a new one replaces the old) — the
      design doc didn't specify stacking rules; this is the simpler, classic interpretation, not confirmed
      by Ethan. **Needs**: this component must actually be added to bunny/enemy prefabs in-Editor to do
      anything (it's inert without one).
- [x] `CombatResolver.cs` — the full damage formula (level/power/A/D core, crit, random, STAB, type),
      gated by the hit/evasion roll, applying status-effect modifiers and rolling status application.
      Called by `AttackInstance` on arrival, never at cast time.
- [x] `AttackInstance.cs` — homing movement, deterministic distance-based arrival (NOT physics
      `OnTriggerEnter`, per the fragility concern in "Attack visuals" above), fizzle-on-dead-target,
      stationary/instant resolution for melee. **Needs**: the actual prefab (Collider + ParticleSystem
      hierarchy) doesn't exist — this script has nothing to attach to yet, and nothing calls `Launch()`.
- [x] `FlankSlots.cs` — 2-slot (left/right) claim/release bookkeeping, symmetric for bunny-on-enemy and
      enemy-on-bunny. **Needs**: nothing calls `TryClaimSlot` yet — the AI that decides "approach and
      flank this target" doesn't exist.
- [x] `BunnyTypeDefinition.isMelee`/`.attackVFXPrefab` — added; Neutral/Melee assets set `isMelee: true`
      (both by hand-editing the existing 2 assets and in `BunnyDataGenerator` for future fresh installs).
- [x] Guard Room room-shell scaffolding (`GuardRoom.cs` + `RoomBase.enemySpots` + `BaseManager`
      registration) — from earlier this session, unchanged, still not Editor-wired.

**Still fully open (no code, needs more design first):**
- [ ] Invasion spawn/trigger system — when/where/how enemy groups actually appear in a room. Nothing in
      this doc specifies this; `EnemyInstance`/`EnemyDefinition` assume something else will call them.
- [ ] AI/behavior-tree layer — the actual decision-making that makes a bunny or enemy walk to a flank
      spot, choose a target, fire an `AttackInstance`, react to `CombatHitResult`. This is the biggest
      remaining piece and the one most likely to need real NPCBunny state-machine surgery.
- [ ] Bunny "fainting" — `ICombatant.OnDefeated` fires correctly when a bunny's HP hits 0, but nothing
      listens. No design exists yet for what happens next (removed from room? recovers after time?
      population impact?).
- [ ] Author CombatSpots (6/room) and EnemySpots (3/room) via the Spot/Path auto-populator — the
      `[SpotNamePrefix]` field exists (`RoomBase.enemySpots`, `GuardRoom.combatSpots`), but no actual
      prefab has spot children placed yet.
- [ ] Guard Room deploy logic (single-unit-per-room lock, nearest/picker UX) — still just the room shell.
- [ ] Exact melee standing distance (`CombatBalanceConfig.meleeStandingDistance` exists as a flagged
      placeholder, not a confirmed number).
- [ ] Run `EnemyDataGenerator` in-Editor to create the actual Slime asset (script-ready since last
      session, just never executed).
