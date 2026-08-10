# Bunny Type Base-Utility Niches — Design Doc

## Goal

Every one of the 20 bunny types (see `BunnyTypeSystem_DesignDoc.md` for the type/stat/level/trait/passive
system itself) should give the player a real reason to want it in the base, beyond raw combat stats.
This doesn't have to mean "recommended worker in room X" every time — it can be a hard requirement
(Sound is *required* for the Radio Station to function at all, not just recommended), a population-count
effect with no room at all (Earth lets the player dig deeper underground), a brand-new resource/currency,
or an entirely new system. The throughline across this pass: prefer active, engaging mechanics over flat
passive bonuses where possible, and reuse existing systems/patterns wherever a niche can be built cheaply
on top of something that already exists.

This doc is the master record of that design pass. **Update (2026-08-10): Neutral's "Adaptable" is the
first entry actually implemented** (see its section below for the file list) — everything else described
below is still design-only, not a shipped feature. Follow-up docs will flesh out
each system one at a time, implementation-ready, before it's actually built (see "Next steps" at the
bottom).

## Status legend

- **Live in code** — already implemented and working today, independent of this pass.
- **Locked** — fully designed and agreed in this pass; ready for its own implementation-ready doc.
- **Decided, needs a mechanic** — the room/system assignment is settled, but the actual bonus/effect
  hasn't been designed yet.
- **Named, undesigned** — only a name/concept exists (from the original ask), nothing else decided.
- **Mulling** — a concrete proposal exists and was discussed in detail, but the user hasn't committed to
  it yet.

## Master table

| Group (unlock pop.) | Type | Niche | Status |
|---|---|---|---|
| 1 (0) | Neutral | "Adaptable" — only Neutral bunnies can re-roll one of their own traits (long per-bunny cooldown); a Passive, dormant until unlocked via Ghost's Ancient Knowledge; has its own dedicated `BunnyInfoUI` element | **Implemented** (dormant — see below) |
| 1 (0) | Water | Water Room — production bonus (`recommendedTypes`) | **Live in code** |
| 1 (0) | Fire | Kitchen — bonus TBD | Decided, needs a mechanic |
| 1 (0) | Plant | Garden Room — production bonus (`recommendedTypes`); also the only type with a real Passive today (Regrowth) | **Live in code** |
| 1 (0) | Shock | Coal Room — production bonus (`recommendedTypes`) | **Live in code** |
| 2 (25) | Insect | Storage Room — clears "Clutter" that otherwise eats into the room's own storage capacity | Mulling |
| 2 (25) | Melee | Guard Room — bigger guard buff when posted | Decided, needs a mechanic |
| 2 (25) | Stone | Entrance Room — bonus TBD | Decided, needs a mechanic |
| 3 (50) | Mind | Library (new room) | Named, undesigned |
| 3 (50) | Toxic | Laboratory (new room) | Named, undesigned |
| 3 (50) | Ice | Cold Room (new room) — big carrot-capacity boost, heavy power draw, Ice workers cut the draw | Mulling |
| 4 (75) | Sound | Radio Station (new room) — **required**, not just recommended | Named, undesigned |
| 4 (75) | Air | Discovery Expedition (reveals new Foraging locations) + Lookout Duty (early-warning post at the Entrance) | **Locked** (mechanic details still open, see below) |
| 4 (75) | Earth | Digs the base deeper underground once enough Earth types are in the colony (population-count effect, no room) | Named, undesigned |
| 5 (100) | Pixie | Hospital Room — heal-rate bonus (`recommendedTypes`) | **Live in code** |
| 5 (100) | Light | Hatchery Room — hatch-speed bonus | **Live in code**, needs a small edit (see below) |
| 6 (150) | Metal | Crafting Room (future room, not yet designed) | Named, undesigned |
| 6 (150) | Ghost | Shrine Room (Ghost-only) generates Ancient Knowledge → unlocks type Passives; separately, hard-mode-only Revive | Mulling (most fleshed out) |
| 6 (150) | Dark | Population count gates/paces the entire Wish System, base-wide | **Locked** |
| 7 (200) | Draco | 3x (tunable) gold from Foraging + enemy-defeats; unlocks the Vault Room (raises the gold cap) | **Locked** |

## Per-type detail

### Neutral — "Adaptable" self-only trait re-roll (Locked)

No room tie-in at all, deliberately — Neutral having no elemental identity is *why* it's the one type
that can reshape identity. Only a Neutral bunny can re-roll one of its own current traits, on a long
per-bunny cooldown.

**Update (2026-08-10): this is a Passive, gated behind Ghost.** Rather than an innate ability every
Neutral bunny has from the moment it spawns, trait re-roll is implemented as an entry in Neutral's
`BunnyTypeDefinition.passives` list — meaning it doesn't exist in the game at all until a Ghost bunny
spends Ancient Knowledge to discover it (see the Ghost section below). Mechanically this needed no new
design of its own: it's simply one concrete, named example of the "Ghost unlocks Passives for every type"
system already planned, rather than a special case. Practical effect: **Neutral bunnies are just a solid,
mid-stat generic bunny (400 total, see the stat buff below) with no special trick at all until the player
has reached Group 6 (population 150) and specifically chosen to spend Knowledge unlocking Neutral's
passive** rather than one of the other 19 types'. Worth being deliberate about that pacing — every other
type's Passive is a bonus layered on top of a niche they already have from the moment they're unlocked
(Plant has Garden Room *and*, once discovered, Regrowth); Neutral has no other niche at all, so this is its
*only* reason to be wanted, and it's dormant for a potentially very long stretch of early/mid-game. The
stat buff means it's never dead weight in the meantime, just unremarkable. Flagging in case you'd rather
guarantee Neutral's passive is cheap/early in Ghost's unlock order rather than leaving it fully player-
prioritized alongside the other 19.

Once discovered, the ability itself still works as designed:
- The player picks **which** of the bunny's current traits to discard (not random) — this is what makes
  it feel like "getting rid of a bad trait" rather than a coin-flip that might strip a good one instead.
- The replacement trait rolls **randomly** from `BunnyTraitCatalog`'s shared pool, reusing the exact
  incompatibility-check loop `RollTraits` already has (reject a candidate that conflicts with the bunny's
  remaining trait, re-roll). Excludes the trait just discarded, so a spent cooldown can't roll back into
  the same thing.
- Base stats bumped from 70/70/70/70/20 (300 total, lowest in the whole roster) to a flat 80/80/80/80/80
  (400 total, mid-pack) specifically so a Neutral bunny is worth optimizing via this ability in the first
  place — see `BunnyTypeSystem_DesignDoc.md`'s stat table. Confirmed via grep that nothing in the codebase
  treats Neutral's stats as a baseline/reference constant, so this was a safe, isolated data change.
- Bonus payoff, not designed together but composes for free: once Ghost's Ancient Knowledge starts
  unlocking the 16 currently-dormant trait names into the shared pool over time, Neutral's re-roll gets
  more interesting for free (bigger pool, better odds of landing something worth having).

**Update (2026-08-10): named "Adaptable," and it needs its own `BunnyInfoUI` element.** Naming it makes
this the passive's actual identifier everywhere it's referenced (this section, the master table, and the
Ghost section's cross-reference to it as the first named example of a Ghost-unlocked Passive).

It also can't just ride the existing generic Passives display. `BunnyInfoUI.cs` currently shows every
Passive as a flat name only — `PopulateList(passiveListContainer, bunny.ActivePassives, p =>
p.displayName)`, the same mechanism Traits use. That's fine for a background stat multiplier like Plant's
Regrowth (nothing to click), but Adaptable is a player-triggered ability with a cooldown and a "pick which
current trait to discard" step, so it needs real interactive UI — Adaptable would still also appear as a
plain name in the shared list like any other passive, but that's not where the player actually *uses* it.

`BunnyInfoUI.cs` already has two precedents for exactly this shape of addition — a self-contained,
conditionally-visible control block inside the same panel, rather than a new separate panel:
- **Approval Controls** (`approveButton`/`rejectButton`, shown only while `bunny.IsAwaitingApproval`) — the
  precedent for "a block that only appears under a specific bunny condition."
- **Foraging Fruit Feeding** (`feedFruitButton` → a detail panel with `fruitDetailIcon`/
  `fruitDetailNameText`/`fruitDetailStatText` → `eatFruitButton`/`fruitDetailBackButton`) — the precedent
  for "a multi-step trigger → select → confirm flow" inside the panel, which Adaptable also needs (trigger
  → pick a trait to discard → confirm).

Proposed shape for the new block, modeled on those two: visible only when the inspected bunny is Neutral
**and** has Adaptable in `ActivePassives` (i.e., Ghost has already unlocked it); contents are a cooldown
readout (ready vs. time remaining), a "Reroll Trait" button (disabled while on cooldown), and a trait-picker
step that reuses the bunny's current trait data (the same data `traitListContainer` already displays) but
made selectable, since the player chooses which current trait goes.

**Update (2026-08-10): implemented.** All of the above is now real code:
- `BunnyTypeDefinition.cs` — new `PassiveEffectType.TraitReroll` case, and two new `BunnyPassiveDefinition`
  fields: `abilityCooldownSeconds` (cooldown length) and `discovered` (the Ghost-unlock gate, defaults
  `true` so every passive authored before this field existed, i.e. Plant's Regrowth, keeps working
  unchanged with no edit needed — only Adaptable's own entry explicitly sets it `false`).
- `BunnyStats.cs` (`BunnyPassiveResolver.ResolvePassives`) and `NPCBunny.GetPassiveHPRegenMultiplier` both
  now also check `discovered`, so an undiscovered passive can never appear as active anywhere, including
  the plain-text Passives list.
- `Neutral.asset` — the actual "Adaptable" entry, `discovered: false`. **Dormant in-game today**: nothing
  exists yet to flip that flag (Ghost's Shrine Room/Ancient Knowledge is still design-only), so the only
  way to turn it on right now is hand-editing the Inspector checkbox. That's expected, not a bug — same
  "ahead of its dependency" scaffolding this codebase already does elsewhere (e.g. `AddExperience`/
  `LevelUp` before an XP curve existed).
- `BunnyTraitCatalog.cs` — new `RollReplacementTrait(discarded, keptTraits)`, mirroring `RollTraits`'
  incompatibility-check loop exactly, scoped to "replace one entry" instead of "roll a fresh set."
- `NPCBunny.cs` — `CanRerollTrait()`/`HasAdaptablePassive` (live-read against `typeDefinition.passives`,
  same idiom as `GetPassiveHPRegenMultiplier`, not baked into `ActivePassives` at spawn) and
  `TryRerollTrait(traitToDiscard)`, which validates, rolls the replacement, and starts the cooldown. A
  failed attempt (bad precondition, or no valid replacement left in the pool) is a complete no-op — no
  cooldown spent, no trait touched.
- `BunnyInfoUI.cs` + new `TraitRerollRowUI.cs` — the dedicated block: an `adaptableRoot` shown whenever
  `HasAdaptablePassive` is true (visible even on cooldown, showing a countdown; only the button itself
  disables), a "Reroll Trait" button opening a `traitPickerRoot` list (one row per current trait, click to
  discard-and-reroll that one, no separate confirm step — simpler than Fruit Feeding's list→detail flow
  since there's no extra per-row info worth a detail panel here).
- **Not done, and can't be from code**: the actual Unity-side wiring — creating the button/text/row-prefab
  UI elements in the `BunnyInfoUI` prefab and dragging them into the new Inspector fields
  (`adaptableRoot`/`rerollTraitButton`/`adaptableCooldownText`/`traitPickerRoot`/`traitPickerListContainer`/
  `traitPickerRowPrefab`), and the `Neutral.asset` seed row in `Editor/BunnyDataGenerator.cs` was also
  updated to 80/80/80/80/80 for consistency (harmless — the generator never overwrites an already-tuned
  asset, so this only matters if `Neutral.asset` is ever deleted and regenerated).

### Fire — Kitchen (Decided, needs a mechanic)

Moved off Hatchery (see Light below) onto Kitchen. Kitchen exists today only as art/prefab/`RoomDefinition`
— no dedicated script, no `IJobRoom`, nothing (confirmed via its prefab's component list: `RoomBase`,
`RoomLightFlicker`, `RoomClickHandler`, `RoomUpgradeClickHandler` only). The actual Fire bonus there
(cooking-flavored, presumably) hasn't been designed yet.

### Light — Hatchery (Live in code, needs a small edit)

`HatcheryRoom.HatchSpeedMultiplier` currently reads `tender.Type == BunnyType.Fire || tender.Type ==
BunnyType.Light` for the hatch-speed bonus. Now that Fire has moved to Kitchen, this should drop to
`BunnyType.Light` alone.

### Insect — Storage Room "Clutter" (Mulling)

Storage Room currently has **zero behavior** — no script, no `IJobRoom`, and `contributesToCarrotStorage`
is `false` on every prefab. Proposed mechanic, chosen specifically to avoid a naive "Insect count = flat
capacity bonus" design (which would silently lose banked carrots the moment Insects step out to eat):
- Clutter accrues automatically over time, capped so it can only ever eat a fraction (e.g. 60-70%) of the
  room's own storage-capacity contribution — never all of it, guaranteeing a floor so an unattended room
  degrades rather than collapses.
- Real `IJobRoom` cleaning/organizing spots; assigned workers reduce clutter per tick using the same
  diminishing-returns-for-multiple-workers math Garden/Water/Coal already use.
- Insect gets `recommendedTypes = { Insect }`, reusing the existing `typeMatchProductionBonus` field
  verbatim — no new bonus mechanism, just another room plugged into the one that exists.
- **Open decision**: does this room's capacity govern carrots (add it as a 4th `CarrotManager` contributor
  alongside Cafeteria/Kitchen/Cold Room — reuses all existing plumbing, simplest) or a new
  loot/crafting-materials resource (bigger lift, no manager exists yet)? Leaning carrots.

### Melee — Guard Room (Decided, needs a mechanic)

Guard Room currently has no `recommendedTypes`/bonus system at all. Melee should get a bigger guard buff
(`GuardBuffController`) when posted — offense-flavored, distinct from Stone's old slot there.

### Stone — Entrance Room (Decided, needs a mechanic)

Moved off Guard Room (to make room for Melee) onto Entrance Room. Entrance Room also has no
`recommendedTypes`/bonus system today — the actual Stone bonus there hasn't been designed yet.

### Mind — Library (Named, undesigned)

User's own original example (`mind = library`). No room script, no mechanic discussed beyond the name.

### Toxic — Laboratory (Named, undesigned)

User's own original example (`toxic = laboratory`). No room script, no mechanic discussed beyond the name.

### Ice — Cold Room (Mulling)

New room. Maps cleanly onto existing `RoomBase` fields:
- Big carrot-storage-capacity boost via the existing `contributesToCarrotStorage` /
  `carrotStorageCapacityAmount` fields — the same mechanism Cafeteria already uses (a code comment there
  already anticipated "Kitchen and Cold Storage can opt in later").
- Heavy power draw via `consumesPower` / high `powerConsumptionAmount` — same system Coal Room already
  feeds.
- Ice-type workers present reduce the room's effective power draw. This would be the **first** type-match
  hook in the game that cuts a cost instead of boosting output (every existing one — Garden/Water/Coal/
  Hospital/Hatchery — only ever boosts something), so it needs new code, not a reuse of
  `typeMatchProductionBonus`.
- Naming not settled (Cold Room vs. "Cold Storage," the name a `RoomBase.cs` comment already anticipated).

### Sound — Radio Station (Named, undesigned)

User's own original example — **required**, not just recommended, for the room to function at all (same
family as Ghost/Shrine below). No mechanic beyond that framing exists yet.

### Air — Discovery Expedition + Lookout Duty (Locked shape, some numbers still open)

Two-part kit. The Air Circulation Room / base-wide "Smog" idea from earlier in this pass is **scrapped**.

**Discovery Expedition** — a new Foraging trip type, reusing the existing dispatch/wait engine
(`ForagingManager`) rather than new infrastructure:
- **Air-only hard gate** — only an Air-type bunny can be dispatched. Justified narratively (see below) as
  "other bunnies are too afraid to wander without knowing where they're going."
- **Per-location, not per-lifetime** — dispatching reveals the *next* eligible not-yet-discovered
  location, not "all future locations forever." This is the deliberate fix for "the player just needs 1
  Air bunny ever" — you need an *available* Air bunny again for every subsequent location.
- `ForagingLocationDefinition.populationThreshold` still gates eligibility exactly as it does today (via
  `ForagingLocationUnlockTracker`); the Expedition is the actual trigger that reveals a location once
  eligible, not a replacement for that system.
- Duration scales with a new "Discovery Effort" cost tied to the location's existing
  `ForagingDifficultyTier` (Weak/Tough/Brutal) — Volcano/Dungeon should cost dramatically more effort than
  Beach/Forest. Multiple Air bunnies sent together cut the time down via the **same** diminishing-returns
  curve already implemented for Work Room stacking (`WorkerProductionScaling`: `100% × 0.7^rank` per
  worker, cumulative `(1 - 0.7^N) / (1 - 0.7)`). Solo is technically possible but impractically slow for
  late-game locations — multiple Air bunnies bring it down to reasonable. This is what gives the player a
  real reason to own more than 1 Air bunny, without a hard minimum-count lock.
- Deterministic — no combat/encounter roll. That flavor is already spoken for by Dark's Wish System tie-in
  and part of Ghost's Commune.
- Open: does the player choose which eligible location to target, or is it always "next in line"? Does
  duration scale with anything besides tier?

**Lookout Duty** — a second small job on Entrance Room:
- A couple of `lookoutSpots` (a `RoomSpot`-style claim list, same shape as Entrance Room's existing
  `guardSpots`), hard-gated to Air only in `RequestSpot` (same pattern Ghost/Shrine uses — the gate is a
  type check, not physical inaccessibility).
- Reuses the **already-existing** `RoomBase.GetWorkWanderChain` patrol system (currently used by Guard
  Room's WatchSpots), just with waypoints placed in open air outside/above the entrance instead of on a
  floor. Bunny movement is plain `transform.position` lerping between authored Transforms, not
  NavMesh-based, so airborne waypoints cost nothing extra — no ledge geometry, no lift/floor connectivity
  needed at all.
- Needs one new **Flying** animation state (plus a little hover/bob) played during that wander instead of
  the walk cycle. The bunny stays fully "in base" the whole time (not a new Foraging/Questing-style
  population state) — just visually parked/patrolling in the air near the entrance.
- Actual gameplay payoff still needs speccing (reviving the earlier "early invasion warning" framing —
  more/higher-level Lookouts posted = more advance warning before a wave hits, presumably the same
  diminishing-returns shape for consistency with the rest of Air's kit).
- **Unifying narrative** for both halves of Air's kit: Air bunnies "read the wind" — sensing what's ahead
  (safe to explore) and what's approaching (danger) via the same innate sense other types simply don't
  have. This is also why it's a hard type-gate rather than a bonus: everyone else is blind to it, not just
  worse at it.

### Earth — digs deeper (Named, undesigned)

User's own original example from the very start of this pass. Population-count effect, no room. No
specific trigger/threshold numbers discussed yet.

### Metal — Crafting Room (Named, undesigned)

Future room, not yet designed at all.

### Ghost — Shrine Room + Ancient Knowledge, and hard-mode Revive (Mulling, most fleshed out)

Two separate niches, deliberately not merged:

**Hard-mode Revive** — a Ghost can revive a dead teammate, on a long per-bunny cooldown. Scoped entirely
to a future hard-mode/permadeath context that doesn't exist yet — bunnies never actually die today
(`BunnyState.Fainted` always heals back to 1 HP once a battle ends).

**Shrine Room** (name not settled — "Church" was also floated) — the normal-game niche, needed since Revive
only matters in hard mode:
- New room, **Ghost-only hard gate** — only Ghost-type bunnies can claim a work spot there at all, same
  "required, not recommended" family as Sound/Radio Station. No lift/floor-connectivity implications here
  (unlike Air's Lookout, this is a normal room), the gate is purely a `RequestSpot` type check.
- Generates a brand-new resource, **Ancient Knowledge**, via its own manager (same shape as `GoldManager`).
  Deliberately kept separate from the also-unbuilt Technology/tech-tree system already stubbed in code
  (`RoomUnlockCondition.TechUnlock` — "a clear extension point for a future tech-tree system," currently
  always locked).
- Ancient Knowledge is spent to unlock entries in `BunnyTypeDefinition.passives` — a list that already
  exists in code but is almost entirely empty today (only Plant's Regrowth has a real effect). Ghost
  becomes the mechanism that fleshes out every other type's Passives over time, one entry at a time, at
  **escalating cost per unlock** — cheap and impactful early, tapering off, so it can't compound into
  infinite value from stacking Ghosts forever. Optional secondary brake: diminishing returns on multiple
  Ghosts communing simultaneously, same curve family as Work Rooms. Neutral's "Adaptable" trait re-roll
  (see above) is the first concrete, named example of a Passive this system unlocks — every other type's
  eventual Passive works the same way, just with effects still to be designed.
- Optional flavor, not load-bearing: Commune could occasionally surface a lore fragment (backstory on the
  world/Fox King) as a bonus roll.
- **Open**: trickle production (steady per-second while staffed, simplest, matches every other Work Room)
  vs. a periodic lump-sum "event" (more memorable, more bespoke) was raised but never explicitly decided.

### Dark — paces the Wish System (Locked)

Reframed from "evil type" to more of a night/star type (the existing crescent-moon tail already supports
this). Ties into the separately-authored Wish System design doc (a "reason to check in" mechanic — see
that doc for the full success-formula/spawn/outcome design):
- **Zero Dark bunnies = zero wishes, full stop.** Same "required, not recommended" family as Sound/Radio
  and Ghost/Shrine.
- Dark population count raises the base-wide wish spawn rate, with diminishing returns and a hard ceiling
  (reuses the Work Room diminishing-returns curve shape) — this also happens to answer two of the Wish
  System doc's own open questions (Section 7 Q2: yes, something besides Luck paces frequency, uniform and
  separate, easier to balance independently; Q8: yes, there's a global pacing cap).
- Luck's existing job is untouched — it's already baked into the per-bunny wish success% formula. Dark
  governs a completely separate lever: whether wishes exist at all, and how often, base-wide.
- No work room, no active ability — purely a population-driven gate. Explicitly accepted as fine *because*
  it's the required backbone of the game's stated core "reason to check in" loop, not a supplementary
  side-system bonus (an earlier "Dark passively reduces invasion frequency" idea was rejected for being
  exactly that kind of low-stakes passive).
- Deliberately kept as a single, standalone niche — no additional active ability layered on top, since
  Dark unlocks late (Group 6, population 150) and is meant to read as a fun bonus rather than a key system,
  in contrast with Ghost's more central/tangible role.

### Draco — Gold multiplier + Vault Room (Locked)

- **Passive**: gold from Foraging trips and from defeating enemies in base-invasion combat is multiplied
  (tunable, starting around 3x). Requires new baseline content first — enemies currently drop **no** gold
  at all on defeat, so every existing pest (snail, mole, worm, rat, snake, slime, etc.) needs a base
  gold-on-kill value authored before Draco's multiplier means anything.
- **Vault Room**: unlocks once the player has ever owned at least one Draco (a new sticky "has-owned" flag
  — none of the existing `UnlockConditionType` cases, `PopulationAtLeast`/`PermanentFlag`/`TechUnlock`,
  check "owns a bunny of type X," only population thresholds). Gets Grade 1/2/3 tiers like every other
  room, for consistency.
- Vault Room's sole function is contributing to a brand-new `GoldManager.goldStorageMax` — gold has never
  been capped before this. The default cap independent of ever building a Vault is a generous **99,999**,
  deliberately meant to almost never bind in normal play (a "flex/display ceiling" rather than a real
  limiter — gold is the core spend-everywhere currency, so wasting it past a cap the way excess Carrots
  get wasted today would feel bad). Vault Room raises the ceiling further, per Grade. Uses the same
  defensive-clamp safety `CarrotManager` already has (never lets the stored amount silently go
  negative/corrupt on a cap decrease).
- Visually, the Vault Room should show a growing pile of gold as the stockpile grows — a late-game "look
  how rich you are" reward room. Presentation-only requirement layered on top of the functional cap.

## Next steps

Nothing in this doc has touched the repo beyond this write-up and the Neutral stat correction in
`BunnyTypeSystem_DesignDoc.md`. The plan going forward is to author a focused, implementation-ready design
doc **one system at a time**, and build each before starting the next, rather than attempting all of this
at once. Roughly in order of how settled each already is (most-locked first, since those need the least
further design conversation before implementation can start):

1. Draco — Vault Room + gold cap + enemy-defeat gold baseline
2. Dark — Wish System spawn-frequency tie-in (depends on the separately-authored Wish System doc landing
   its own open questions first)
3. Air — Discovery Expedition + Lookout Duty
4. Ghost — Shrine Room + Ancient Knowledge + Passives unlock (trickle-vs-event and naming still need a
   decision first)
5. Neutral — trait re-roll
6. Ice — Cold Room
7. Insect — Storage Room Clutter (resource-target decision still needed first)
8. Melee/Stone — Guard Room and Entrance Room bonuses (mechanics still undesigned)
9. Fire/Light — Kitchen bonus + the one-line Hatchery edit
10. Mind/Toxic/Sound/Earth/Metal — Library, Laboratory, Radio Station, Earth's dig-deeper effect, Crafting
    Room (all still just named concepts, need a full design pass each)
