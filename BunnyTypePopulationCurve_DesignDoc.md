# Bunny Type Population Curve & Room Pairings — Design Doc

## Purpose

Retimes bunny type population unlocks from the old scheme (clusters of 3 every 25
population) to a curve that unlocks faster early (steady stream of new types/rooms in
the opening stretch) and levels out later (steady 10-pop rhythm through the midgame),
capped by a deliberately harder-won finale type. Each newly-unlocked type is now also
paired with a themed room, giving every type a real niche rather than being purely a
stat/combat distinction.

This doc also formalizes a new **room pairing** concept: a room can optionally have one
associated bunny type, and no two rooms share the same type (each type's niche is
unique — no overlaps).

**Implemented 2026-08-13, retuned same day.** The population curve below is live on
every `BunnyTypeDefinition` asset (`populationThreshold`/`group`, no code changes
needed — `BunnyTypeUnlockTracker`/`RoomUnlockCondition` already read thresholds
generically). Light was moved from the fast-early cluster (originally 30) to 100 —
Light reads as more of a "late game" type thematically, and since Hatchery is a
day-one room independent of the pairing (see "Room Unlock Timing" below), moving Light
later costs nothing mechanically; Hatchery just runs at its base rate until Light's
hatch-speed bonus becomes available later. Everything from Stone through Metal shifted
down 5 population to absorb the gap Light left behind, which happened to land Light
exactly on the steady 10-rhythm's next open slot (90+10=100) — no awkward gap anywhere
in the curve. The room-pairing *catalog* below (which type pairs with which room, and
whether it's Bonus or Exclusive) is confirmed design; the actual per-room
bonus/exclusive-staffing **mechanic** is still a separate future implementation pass —
see `BunnyTypeNiches_DesignDoc.md` for full per-type mechanic detail and status, and
"Room Pairing Mechanics" below for what's actually built so far (just the
`recommendedTypes` field itself, consolidated onto `RoomBase`).

## Population Unlock Curve

All values below are `populationThreshold` fields on existing `BunnyTypeDefinition` and
`RoomDefinition` ScriptableObject assets — **no code changes required** for the curve
itself. Both `BunnyTypeUnlockTracker` and `RoomUnlockCondition` already read thresholds
generically.

| Pop | Type | Room | Pairing | Notes |
|---|---|---|---|---|
| 0 | Neutral, Water, Plant, Shock | — | — | Starting roster |
| 10 | Fire | Kitchen | Bonus | **Kitchen's own unlock gate is 40, not 10** — see "Room Unlock Timing" below, Kitchen is the deliberate exception to "room unlocks alongside its type" |
| 15 | Insect | Storage Room | Bonus | |
| 20 | Pixie | Hospital | Bonus | Healing — reduces bunnies fainting |
| 25 | Toxic | Laboratory | Bonus | Healing/support, pairs with Hospital wave |
| 30 | Stone | Entrance Room | Bonus | Entrance Room already exists as a day-one room; this only adds a Stone-specific bonus, not a new unlock gate on the room itself |
| 35 | Mind | Library | Bonus | Technology research |
| 40 | Air | — (no room) | Ability | Scouts new foraging locations outside the base; early warning for invasions. Kitchen's own room gate rides this threshold (see "Room Unlock Timing") |
| 50 | Melee | Guard Room | Bonus | Base defense |
| 60 | Earth | — (no room) | Ability | Unlocks building further down (expands base depth) |
| 70 | Sound | Radio Room | Bonus | Contact with other bases; increases wild bunny spawn rate; may unlock certain visitors. Supersedes `BunnyTypeNiches_DesignDoc.md`'s older "Radio Station, hard-required" framing — confirmed Bonus, not Exclusive; any type can staff it, Sound just gets the bonus |
| 80 | Ice | Cold Room | Bonus | Greatly increases carrot max capacity |
| 90 | Metal | Crafting Room | Bonus | Accessories |
| 100 | Light | Hatchery | Bonus | Faster population growth. Hatchery is already unlocked from game start (day-one room, see below) — Light's hatch-speed bonus is what arrives at 100, not the room itself |
| 110 | Ghost | Shrine Room | **Exclusive** | Only Ghost bunnies can staff the Shrine. Produces Ancient Knowledge Points over time, spent to unlock bunny passives one at a time (all types benefit, not just Ghost) |
| 120 | Dark | — (no room) | Ability | Unlocks the Wish system (entirely optional to engage with) |
| 150 | Draco | Vault Room | Bonus | Deliberate capstone gap (+30 vs. the steady +10 rhythm) — intentional harder-won finale. Vault visually fills with gold as the player accumulates it; gold cap is already 99,999 by default, so this is primarily an endgame visual/emotional payoff, not a mechanical one. Vault Room unlocks via the same `PopulationAtLeast=150` condition as Draco's own type unlock — no bespoke "ever-owned-a-Draco" flag, keeping every paired room's unlock mechanism identical |

**Gap pattern:** 5-pop steps from Fire through Air (fast early unlocks — Fire 10,
Insect 15, Pixie 20, Toxic 25, Stone 30, Mind 35, Air 40), widening to a steady 10-pop
rhythm from Melee through Dark (Melee 50, Earth 60, Sound 70, Ice 80, Metal 90, Light
100, Ghost 110, Dark 120), then a 30-pop capstone gap to Draco (confirmed intentional,
not a typo).

## Room Unlock Timing

Separate from the *bonus* the paired type gets while working a room, each existing
paired `RoomDefinition` also carries its own `RoomUnlockCondition` (`PopulationAtLeast`,
same generic mechanism every room uses) gating when the room itself becomes buildable
at all. **Confirmed 2026-08-13**, applied only to the buildable `4x2x6 Grade1` variant
of each room (matching the existing convention — Grade 2/3 are reached via the
upgrade system, not gated separately, and the 8x2x6/12x2x6 variants are reserved for
the future merge system):

| Room | Unlock pop. | Matches its type's threshold? |
|---|---|---|
| Storage Room | 15 | Yes (Insect) |
| Hospital | 20 | Yes (Pixie) |
| Laboratory | 25 | Yes (Toxic) |
| Guard Room | 50 | Yes (Melee) |
| **Kitchen** | **40** | **No — deliberate exception, see below** |
| Entrance Room | — (day-one) | No — predates the pairing, already exists from game start (see table note) |
| Hatchery | — (day-one) | No — predates the pairing; breeding is a core early-game mechanic and stays available from the start, Light just adds a hatch-speed bonus once it unlocks at 100 |

**Kitchen is the one deliberate exception to "room unlocks alongside its type."** Fire
itself still unlocks at population 10 (the type is usable in combat/assignable to other
rooms from then on), but the Kitchen room specifically is gated to population 40 —
riding on Air's threshold, since Air doesn't have a room of its own to pair with. When
Air's own threshold moved (50→40) during the same-day retune, Kitchen's gate moved
with it rather than staying pinned to the old number — the intent was always "borrow
whatever slot Air lands on," not the literal value 50. Reasoning for the exception
itself: Kitchen requires Power and a significant amount of Water to run, and the early
game is meant to lean on plain Carrots for a while rather than asking the player to
stand up Power+Water infrastructure immediately. Every other new paired room (Library,
Radio Room, Cold Room, Crafting Room, Shrine Room, Vault Room) is expected to unlock
alongside its type by default when it's eventually built, unless a similar
early-game-pacing reason comes up during that room's own design pass.

## Room Pairing Mechanics

Two distinct pairing types exist and should stay clearly distinguished in
implementation — they are different mechanics, not different strengths of the same
mechanic:

- **Bonus pairing** (most rooms above): any bunny type can be assigned to work the
  room. The paired type gets some bonus specific to that room while assigned there.
  Non-paired types get normal (unboosted) functionality from the room.
- **Exclusive pairing** (Shrine Room only, so far): only the paired type (Ghost) can
  staff the room at all. Other types cannot be assigned there.

**Status as of 2026-08-13**: the *bonus-pairing* mechanism already existed in code
before this doc, just duplicated per-room (`recommendedTypes` + a room-specific bonus
field like `typeMatchProductionBonus`, on `WaterRoom`/`GardenRoom`/`CoalRoom`/
`HospitalRoom`/`LaboratoryRoom`). That's now consolidated: `recommendedTypes` itself
lives on `RoomBase` (any future room script inherits it for free, no further
consolidation needed), while each room's own bonus-amount field stays local since
what it boosts differs per room (production rate, heal rate, brew speed, ...). The
5 existing rooms above are the only ones with a real bonus *effect* wired up today —
none of the 8 new Bonus pairings in the table (Kitchen/Fire, Storage Room/Insect,
Entrance Room/Stone, Library/Mind, Guard Room/Melee, Radio Room/Sound, Cold Room/Ice,
Crafting Room/Metal, Vault Room/Draco) have their bonus effect coded yet, and none of
those 6 rooms (Storage Room aside, which exists but has zero behavior) have a
`RoomDefinition` asset or prefab at all yet (Library, Radio Room, Cold Room, Crafting
Room, Shrine Room, Vault Room). The Exclusive-staffing restriction (Shrine/Ghost) also
doesn't exist yet — it's a different mechanic from the bonus pairing (a `RequestSpot`
type-check gate, not a bonus multiplier) and needs its own implementation pass. See
`BunnyTypeNiches_DesignDoc.md` for the fully detailed per-type status and "Next steps"
ordering.

## Design Rule: No Overlapping Pairings

Each room may have at most one paired type, and each type may have at most one paired
room. Not every type needs a room — some express their niche purely through an ability
(Air, Earth, Dark) rather than a physical space. This is an accepted, permanent state
for those types, not a gap to fill later.

## Open Items (Not Yet Confirmed)

- **Pre-existing rooms** (Cafeteria, Living Room, Water Room, Coal/Power Room, Garden
  Room, Bedroom): Water Room↔Water, Garden Room↔Plant, and Coal Room↔Shock are already
  **confirmed and live in code** (`recommendedTypes` type-match bonus) — not open items
  after all, contrary to earlier drafts of this doc. Cafeteria, Living Room, and
  Bedroom still have no pairing decision.
- **Neutral type** — confirmed roomless by design (see `BunnyTypeNiches_DesignDoc.md`):
  its whole identity is "no niche except what Ghost's Ancient Knowledge eventually
  unlocks for it" (Adaptable, its trait-reroll passive). Not an open item.
- **Merchant + universal blueprint room** — new idea, still loose. A recurring
  merchant (not one-time) periodically offers expensive purchasable items, gated
  behind having enough gold (e.g., a 250k+ item realistically requires a Vault Room to
  ever afford). One concept discussed: a blueprint for a new room type that ALL bunnies
  can use (no type pairing at all — sits outside the pairing rule by design). Function
  of the room itself is still undecided (candidate directions discussed: Observatory
  tied to the Nature/zodiac system, Trophy Hall for morale, Bank for passive gold
  trickle, Shrine-adjacent crisis mitigation). Needs its own design pass once the
  concept firms up, including merchant rotation frequency and whether cheaper items
  are mixed into the rotation.

## Related Docs

- `BunnyTypeNiches_DesignDoc.md` — the fuller per-type design pass this doc's room
  pairings are drawn from (niches, mechanic proposals, implementation status,
  "Next steps" ordering for building each pairing's actual effect).
- `BunnyDebutSpawn_DesignDoc.md` — separate subsystem, guarantees a newly-unlocked
  type's first wild spawn happens on the very next wild arrival rather than being left
  to random chance. Implemented 2026-08-13 alongside this doc's threshold retiming
  (the old thresholds made a hardcoded "Fire spawns 7th" approach coincidentally work;
  the new ones would have broken it).
