# Foraging System — Design Document

## Context

Foraging sends an already-resident bunny out of the base to a chosen location to find loot, currency,
and — the actual original motivation for this whole doc — **XP**. `NPCBunny.AddExperience`/`LevelUp`
(see `BunnyTypeSystem_DesignDoc.md`'s "Leveling scaffold" section) have existed as unwired scaffolding
since the stat/type system was built; this doc is what finally gives them a real source and curve.

Modeled loosely on Fallout Shelter's "Explore the Wasteland," researched directly against it, but
deliberately different in a few ways:

| | Fallout Shelter | Burrowscape Foraging |
|---|---|---|
| Primary risk/reward dial | How long you leave them out (the only dial) | Which **location** you send them to (locations differ in loot table + enemy difficulty); duration is a secondary factor via ticks |
| Combat | Real, dwellers can die | A deliberately minimal placeholder resolver (see below) — no permadeath |
| Death | Revivable for caps, or permanent in Survival mode | Never happens from Foraging — HP floors at 1 and forces a return instead |
| Equipment | Outfit + weapon (combat-only) | Healing potions (auto-use) + one accessory slot (utility-flavored: capacity/luck/speed, not combat) |
| Currency vs. carry cap | Caps don't count toward the 100-item cap | Gold doesn't count toward the carry cap; Carrots do (see Loot economy) |

Population accounting needs no new work — `ResidentCategory.Foraging` and
`PopulationManager.MoveResident` already exist and are already unused by anything. Dispatch calls
`MoveResident(InBase, Foraging)`; return calls `MoveResident(Foraging, InBase)`. Total population never
changes, exactly like every other category move in that system.

## Locked-in decisions

- **Trip visuals**: a foraging bunny walks out through the existing `EntranceGate` to the same offscreen
  staging position `WildBunnySpawner` already uses for wild arrivals, and parks there — **alive and still
  simulated, not despawned/destroyed** (avoids an entire class of respawn/re-registration bugs, e.g.
  `DwellerRoster`). `CurrentState` becomes a new `BunnyState.Foraging` value for the duration. On return,
  the bunny walks back in through the Gate — the wild-spawn entrance, mirrored in reverse. No new scene
  or "wasteland" visualization needed.
- **No death from Foraging, ever** (this pass). Auto-return is forced by any of three conditions (see
  Auto-return conditions) — HP is clamped at a floor of 1 rather than allowed to reach 0. A future
  "hard mode" toggle that allows real death is explicitly deferred, not designed here.
- **Dispatch is per-trip, not a persistent loadout.** Potions and the accessory slot are chosen fresh each
  time a bunny is sent out (Bunny -> Location -> Equip Items -> Confirm), not equipped once and left on.
- **Two dispatch entry points, one flow**: a "Send Foraging" button on a bunny's own info panel (skips
  straight to Location, since the bunny is already chosen), and a dedicated "Foraging" button that opens
  a bunny-picker first. Both converge on the same Location -> Equip Items -> Confirm steps.
- **Location unlocks are population-gated and sticky**, reusing `BunnyTypeUnlockTracker`'s pattern
  exactly (once population has ever crossed a threshold, the location stays available even if population
  later drops). Deliberately staggered against the Type-unlock thresholds (25/50/75/100/150/200) so the
  player is always unlocking *something* rather than both systems gating at the same population numbers.
- **The placeholder combat/encounter resolver is explicitly disposable.** It exists only so Foraging has
  stakes before the real combat system is designed, and is meant to be replaced wholesale, not built on
  top of, once that system exists.

## Locations

New `ForagingLocationDefinition` ScriptableObject, one asset per location — mirrors
`BunnyTypeDefinition`/`RoomDefinition`'s one-asset-per-entry pattern.

| Field | Purpose |
|---|---|
| `displayName`, `icon` | Identity, shown in the dispatch UI |
| `populationThreshold` | Sticky unlock gate, see below |
| `difficultyTier` | `Weak` / `Average` / `Tough` / `Brutal` — drives both enemy encounters and gate-check bands (same 4-tier vocabulary reused for both, so the player only learns one difficulty language) |
| `recommendedTypes` | `List<BunnyType>` — **a list, not a single type**, kept flexible for the future even though early examples only use one entry each |
| `lootTable` | Unified list of entries (see Loot economy) — Carrots, Gold, and items all live in the same per-location list |

**Unlock thresholds** (population-gated, sticky, staggered against Bunny Type's 25/50/75/100/150/200 so
the two systems don't both gate at the same numbers):

| Tier | Population |
|---|---|
| 1 | 0 (start) |
| 2 | 30 |
| 3 | 60 |
| 4 | 90 |
| 5 | 120 |
| 6 | 150 |

Named locations mentioned as concrete examples so far — **not all 6 tiers need content authored yet**,
same as the Type roster's Group 1-only-has-art approach:
- **Beach** — starter tier, recommends Water, easier difficulty, Carrots findable
- **Forest** — starter tier, Carrots sit under its Common band
- **Dungeon** / **Volcano** — harder/later tiers, tougher enemies, Carrots absent from their loot tables entirely (not just rare — genuinely not on the list)

## Held items

- **Healing potions** — consumable, limited carry count, auto-used automatically once HP gets low (mirrors
  Stimpak). A bunny that runs out of potions and then reaches 1 HP triggers an auto-return (see below).
- **Accessory slot** — one slot, utility-flavored rather than combat-flavored. Examples given so far (not
  a final list): Backpack (+carry capacity), Binoculars (+rare-loot chance), Lightning Shoes (+return
  speed).
- **Shared base potions** — a future Laboratory room (not yet designed — new room type, own future design
  pass) produces a base-wide potion stockpile bunnies can also draw from. Deferred; Foraging v1 only
  needs carried potions to function.

## Traits (Foraging-specific)

- **Adventurous** — Energy decays more slowly while foraging.
- **Homebody** — Energy decays faster while foraging.
- **Treasure Finder** — increases rare (Uncommon/Rare) loot find chance. Stacks additively with the Luck
  stat's own contribution rather than competing with it — Luck and Treasure Finder are two separate
  inputs to the same roll, not two traits fighting over the same knob.

**Resolved**: `NPCBunny.energyDecayPerSecondForaging` already exists as a tunable field (authored ahead
of time, before this system existed), and is already wired into `TraitEffectType.EnergyDecayMultiplier`'s
effect (`ApplyTraitEffects`, `NPCBunny.cs:900`). So Adventurous/Homebody don't need a new
`TraitEffectType` case at all — they're just new trait *entries* reusing the existing
`EnergyDecayMultiplier` case, exactly like Energetic/Lazy. The only remaining code work is adding
`BunnyState.Foraging` to the enum and uncommenting `GetEnergyDecayRate()`'s already-present (currently
commented-out) case for it.

## Other needs while foraging

**Only Energy is affected**, for this pass. Hunger/Thirst/Mood are entirely frozen for the whole trip
(active phase and return countdown both) — the same freeze pattern already used for a bunny awaiting
gate approval (`HasEnteredBase` guard in `NPCBunny.Update()`). Deliberately left as an easy option to
revisit later (a location could plausibly drain Hunger/Thirst or grant Mood on a great haul), but out of
scope now — this system already has enough moving pieces without them.

## Stats' payoff

- **Luck** → rare-loot roll (additively stacks with Treasure Finder, see above)
- **Speed** → explore-tick frequency (see Tick rate, below)
- **Attack/Defense** → the placeholder encounter resolver's "effective power"
- **HP** → the first system to actually spend it

## Tick rate

One shared "explore tick" timer per foraging bunny. Each tick rolls both a loot-find check and an
encounter check together.

- **Speed** shortens the interval between ticks — more ticks per minute out in the field, meaning both
  more loot opportunities *and* more danger exposure per unit time. A genuinely double-edged stat, not a
  flat "safer" one.
- **Recommended-type match** also increases tick rate, and **stacks with Speed's effect** (and with any
  future tick-rate modifiers) rather than overriding it.
- **Energy decay is explicitly decoupled from tick rate.** It ticks down on its own fixed per-second rate
  (same pattern as the existing Hunger/Thirst/Energy decay in `NPCBunny.Update()`), modifiable by
  traits/items (Adventurous/Homebody) but never by how fast the explore-tick timer is running. A fast
  bunny doesn't get tired faster or slower just from being fast.
- **Both the tick-rate bonus and the XP bonus from a type match must be tunable values** (not hardcoded
  constants) so they can be balanced later without a code change.

## Return triggers

A foraging trip's active (loot/encounter/gate-check/XP-earning) phase ends the instant **any** of these
four becomes true:

1. **Carry limit reached** (base capacity, modifiable by carry-boosting items/traits — e.g. Backpack,
   or a possible future "Pack Rat"-style trait; same overlap question as Adventurous/Homebody above,
   flagged in Open items)
2. **HP reaches 1** and the bunny is out of healing items to auto-use
3. **Energy reaches 0**
4. **Manual recall** — the player forces an early return via the dedicated Foraging screen (see Manual
   recall, below)

No condition here can result in death — HP is floored at 1, never allowed to reach 0, and the bunny is
pulled home instead. Whatever loot/Gold/XP accrued before the trigger is kept regardless of which of the
four fired — recall is not a penalty, just an early exit through the same door.

## Return delay

Once any trigger above fires, the bunny doesn't reappear instantly — it enters a **return countdown**
before walking back in through the Gate, matching Fallout Shelter's "recall still takes time" behavior:

```
ReturnDuration = ElapsedTripTime * 0.75
```

further reduced by the bunny's Speed stat and any traits/items (e.g. Lightning Shoes) — exact scaling
not yet numbered, left tunable like everything else here. Example: a Shock-type (Base Speed 130) wearing
Lightning Shoes returns much faster than a Plant-type with no accessories, even given the same elapsed
trip time.

**The return countdown is safe** — once it starts, no more loot/encounter/gate-check ticks and Energy
stops decaying entirely. It's purely a timer counting down to the walk-in animation, not a continuation
of the trip's danger. This applies uniformly to all four triggers, including manual recall — recalling a
bunny doesn't instantly erase the wait, but it does immediately end all further risk.

## Manual recall

A "Recall" control per currently-out bunny, living on the dedicated Foraging screen (dispatch entry-point
B) rather than `BunnyInfoUI` — a foraging bunny is parked offscreen for the whole trip and can't be
clicked on in the world the way a resident bunny can. This screen doubles as a live status list of every
bunny currently out (worth showing HP/Energy/carry-fullness so the player can make an informed call), not
just a one-time dispatch picker.

## Loot economy

**One unified per-location list.** Carrots, Gold, and items (potions, accessories, future material types)
all live in the same list, banded into three rarity tiers — **Common, Uncommon, Rare** — that determine
how often each entry is rolled. Traits/items (Treasure Finder, Binoculars) bias the roll toward
Uncommon/Rare.

- **Carrots behave like any other carry-limited item** in the list — present in some locations' Common
  band (Forest), absent entirely from others (Volcano). They count toward the carry limit like a physical
  item would, which is also the in-fiction reason Gold doesn't (Carrots are bulky cargo; Gold is
  weightless coinage).
- **Gold is a separate mechanic entirely**, not a rarity-banded list entry. It has its own independent
  per-tick find-roll, and when it triggers, the **amount** granted scales with the location's difficulty
  tier (harder/later locations pay out more Gold per find), further modified by traits/items. Gold never
  counts toward the carry limit.

## Placeholder encounter resolver

**Explicitly disposable** — a stand-in so Foraging has stakes before the real combat system exists, not
a foundation to build real combat on top of later. Proposed shape: each encounter-tick roll compares the
bunny's "effective power" (a blend of Attack/Defense/Luck — exact blend not yet decided, left as an
implementation detail since this whole resolver gets thrown out later) against an enemy difficulty number
tied to the location's `difficultyTier` (Weak/Average/Tough/Brutal). A loss applies HP damage (magnitude
also not yet decided); a carried potion auto-uses if HP is low. HP is clamped at a floor of 1 the moment
it would go lower, immediately satisfying auto-return condition 2 above rather than ever reaching 0.

## Gate-checks

A stat-gated skill-check encounter type (separate from combat), styled after Fallout Shelter's rare
"quest requires X stat" encounters. Reuses the same Weak/Average/Tough/Brutal vocabulary as enemy
difficulty rather than inventing a second one.

```
PassChance = clamp01((ActualStat - Floor) / (Ceiling - Floor))
```

Each difficulty tier authors a `(Floor, Ceiling)` pair per relevant stat. Below `Floor` = guaranteed
fail, above `Ceiling` = guaranteed pass, linear in between. Deliberately compares against the bunny's
**actual resolved stat value**, not a theoretical per-type min/max — a check tier doesn't need to be
aware of which BunnyType it's checking, since IV/EV/Base/Nature spread already varies wildly by type at
the same level.

**Failing a gate-check has no downside** — it's a pure miss, no XP, nothing else. Distinct from
encounters, which remain the actual danger/HP-loss source.

## HP recovery

The first system to actually spend a bunny's HP, so recovery needs designing too:

- **Primary**: passive regen while Sleeping, mirroring the existing Energy-regen-via-Bedroom pattern
  (`Bedroom.GradeMultiplier`-scaled). Gives Bedrooms a second reason to matter and needs a new HP-regen
  field/mechanic on `NPCBunny` (nothing currently regenerates HP, since nothing has ever spent it before
  this system).
- **Future**: a base-wide shared-potion stockpile from the not-yet-designed Laboratory room (see Held
  items).
- **Future**: a Plant-type passive that auto-heals HP over time — slots into the existing
  `BunnyPassiveDefinition`/`BunnyPassiveResolver`/`unlockLevel` system whenever authored, no new plumbing
  needed for that part.

## XP system

Four earning sources, all Foraging-specific for now (no other activity grants XP yet):

| Source | Detail |
|---|---|
| Per explore-tick | Flat XP every tick regardless of outcome — since tick rate scales with Speed (and type-match), faster bunnies level faster |
| Per item found | All finds grant XP; Uncommon > Common, Rare > Uncommon |
| Per enemy defeated | Scaled by the enemy's difficulty tier (Weak/Average/Tough/Brutal) — exact per-tier amounts left tunable, not yet numbered |
| Per gate-check passed | Only on success (see Gate-checks) — scaled by check tier the same way enemy-defeat XP is |

**Recommended-type bonus**: if the bunny's `Type` is in the location's `recommendedTypes` list, the
**entire trip's total XP** (all four sources combined) gets a flat **+30%** multiplier, in addition to
the tick-rate bonus described above. Both the tick-rate bonus and this 30% figure must be exposed as
tunable values, not hardcoded, for later balance passes.

**Noted but not double-counted**: Speed already compounds across per-tick XP (more ticks), per-item XP
(more loot rolls from more ticks), and per-enemy XP (more encounter rolls from more ticks) simultaneously.
A recommended-type match compounds a fourth time on top via tick rate, then a fifth time via the flat 30%
multiplier. This is very likely the intended fantasy (Speed + type match = the leveling build), but worth
re-checking against actual playtest numbers once tunable values exist.

### Level curve

Pokémon's **Fast** growth group, single shared curve for every bunny regardless of type (no per-type Fast/
Medium/Slow groups):

```
CumulativeXPForLevel(L) = 0.8 * L^3
```

| Level | Cumulative XP |
|---|---|
| 5 | 100 |
| 10 | 800 |
| 20 | 6,400 |
| 30 | 21,600 |
| 40 | 51,200 |
| 50 | 100,000 |

Feeds directly into the existing (currently unwired) `NPCBunny.experience`/`experienceToNextLevel`
fields and `AddExperience`/`LevelUp` methods — see `BunnyTypeSystem_DesignDoc.md`'s "Leveling scaffold"
section for the pre-existing scaffolding this plugs into. Level cap stays 50, matching the existing
`WildBunnySpawner.RollSpawnLevel` clamp.

## Dispatch flow

1. Entry point A: "Send Foraging" button on `BunnyInfoUI` (or wherever it lands) for an already-resident,
   non-awaiting-approval bunny — skips straight to step 3.
2. Entry point B: dedicated "Foraging" button opens a bunny-picker (list of current dwellers).
3. Player picks a Location.
4. Player equips potions (up to a carry cap) and optionally one accessory, fresh for this trip.
5. Confirm — `PopulationManager.MoveResident(InBase, Foraging)`, bunny walks out through the
   `EntranceGate` to the offscreen staging position, `CurrentState = BunnyState.Foraging`.
6. Trip runs (ticks, loot/encounter/gate-check rolls, XP accrual, Energy decay) until one of the four
   return triggers fires (see Return triggers).
7. Return countdown runs (see Return delay) — no further ticks or Energy decay during this phase.
8. Bunny walks back in through the Gate, `MoveResident(Foraging, InBase)`, `CurrentState` returns to
   Idle, loot/Gold added to the base, any earned `LevelUp` calls resolve.

## Open items / assumptions to confirm before implementation

- **Effective-power formula** for the placeholder encounter resolver (which Attack/Defense/Luck blend,
  and the HP-damage magnitude on a loss) — deliberately left undecided since the whole resolver is
  disposable once real combat exists.
- **Per-tier XP amounts** for enemy defeats and gate-checks are unnumbered — tunable values to be set
  once enemies/checks are actually authored.
- **Carry-capacity trait/item overlap** — a hypothetical "Pack Rat"-style trait and the Backpack
  accessory both target carry capacity; decide whether they stack or compete.
- **Return-time Speed/item/trait scaling** — the 75%-of-elapsed-time baseline is confirmed, but exactly
  how much Speed/Lightning-Shoes-style items reduce it below that baseline is not yet numbered.
- **Full 6-tier location roster** — only Beach/Forest/Dungeon/Volcano are named as examples; the
  remaining unlock tiers are reserved slots, not yet authored, same as Bunny Type Groups 2-7.
- **Laboratory room** — completely undesigned (production rate, staffing, room grade) beyond "it makes
  the shared potion stockpile." Own future design pass.
- **Gold's exact find-chance and per-tier amount formula** — not numerically specified yet, just "scales
  with difficulty tier, modified by traits/items."
- **Accessory/item acquisition economy** — currently only foraging finds; whether Gold eventually buys
  accessories from a shop (once one exists) is unresolved.
- **HP-regen-while-Sleeping mechanic** needs an actual field/formula on `NPCBunny` — nothing regenerates
  HP today since nothing has spent it before this system.
