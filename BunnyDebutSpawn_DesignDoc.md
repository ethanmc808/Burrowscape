# Bunny Debut Spawn & Weighted Selection — Design Doc

## Purpose

Two related changes to `WildBunnySpawner`'s type-selection logic, both landed
2026-08-13 alongside the population curve retiming in
`BunnyTypePopulationCurve_DesignDoc.md`:

1. **Guaranteed debut spawn** — whichever bunny type most recently crossed its
   population threshold is guaranteed to be the very next wild arrival, for every
   type, not just Fire.
2. **Weighted random selection** — the ordinary (non-debut) spawn pick is no longer
   equal-weight; Neutral is weighted to appear noticeably more often than every other
   unlocked type.

## Guaranteed Debut Spawn

### The old approach, and why it broke

Before this pass, only Fire had any kind of "debut guarantee," and it was achieved by
hand-placing Fire as the literal 7th entry in `WildBunnySpawner.startingBunnyOrder` (an
explicit, designer-authored opening sequence spawned once in `Start()` for a brand-new
game). `SpawnStartingBunnies()` spawns that list unconditionally — it bypasses
`GetAvailableTypes()`'s unlock/prefab filtering entirely, since it's treated as an
explicit sequence rather than a random draw. That only worked because Fire's old
population threshold was `0`: Fire would spawn as the 7th starting bunny regardless of
whether it was "actually" unlocked yet, and since its threshold was 0, it always was.
Once the population curve retiming moved Fire's threshold to 10, this approach would
have spawned Fire at population 7 — three population short of being genuinely unlocked,
an inconsistency `BunnyTypeUnlockTracker.IsUnlocked` and any UI reading it wouldn't
agree with.

Beyond that one hardcoded slot, **no other type ever had a debut guarantee** —
`SpawnWildBunny()`'s ongoing picks were pure equal-weight random among
`GetAvailableTypes()`, so a freshly-unlocked type could go many spawn cycles before its
first actual appearance, worse as the unlocked-type pool grows later in a playthrough.

### The new mechanic

`WildBunnySpawner.GetPendingDebutType(availableTypes)`: filters the already-unlocked,
has-a-prefab `availableTypes` list down to types NOT yet present in `typesEverSpawned`
(the existing per-session "has this type ever actually spawned" set, previously used
only to gate the `NewBunnyType` notification), and returns whichever pending type has
the lowest `populationThreshold`. `SpawnWildBunny()` calls this first and only falls
back to the normal weighted pick (see below) when nothing is pending:

```csharp
BunnyTypeDefinition chosenType = GetPendingDebutType(availableTypes) ?? PickWeightedRandomType(availableTypes);
```

This covers both `AutoSpawnLoop` (the ongoing timed spawner) and the manual
"Spawn Wild Bunny" context-menu trigger, since both call `SpawnWildBunny()`.

No new tracking state was needed — "unlocked but never spawned" is already fully
derivable from `GetAvailableTypes() minus typesEverSpawned` at call time.

**Multiple thresholds crossed between spawns** (e.g. a population jump from breeding or
a mass Hatchery hatch): only the lowest-threshold pending type debuts on the very next
spawn. The next-lowest becomes the new pending debut and wins the spawn after that, and
so on — one guaranteed debut per spawn cycle, cascading in threshold order rather than
bursting all at once. Simple, reuses existing state, no special-casing needed.

### `startingBunnyOrder` simplification

Since debuts are now fully generic, `startingBunnyOrder` no longer needs to carry any
non-base-4 "reveal" entry at all — it's meant to be exactly the starting roster
(Neutral/Water/Plant/Shock, population 0). The scene's list was trimmed from 7 entries
(Neutral ×3, Plant, Shock, Water, Fire) down to 6 (dropping the trailing Fire entry).
Fire's debut now happens automatically the moment population crosses 10, during the
`AutoSpawnLoop` that starts right after the opening 6 finish spawning — no code change
needed to make that happen, since `AutoSpawnLoop`'s existing population-ramped wait
already gives it the same "arrives as its own event, not clustered with the opening
rush" pacing the old held-back-wait special case used to provide by hand.

`SpawnStartingBunnies()`'s old "hold back non-base-type entries with a real wait"
branch was removed as dead code — every remaining entry is a base type, so the branch
could never trigger again.

### Known accepted interaction (not a new issue)

`SeedAlreadyRevealedTypes()` (called on save load) marks every currently-unlocked type
as already present in `typesEverSpawned`. If a type was unlocked-but-never-actually-
spawned before a save (bad luck under the old random system, or a debut that was
pending right at save time), loading won't retroactively grant it a guaranteed debut —
it just re-enters the normal weighted pool going forward. This mirrors the exact same
accepted gap the code already documented for the notification-suppression side of this
seeding; not introduced by this pass, just worth knowing about.

## Weighted Random Selection

The non-debut fallback pick (`PickWeightedRandomType`) is no longer equal-weight.
Neutral always carries weight 4; every other currently-unlocked-and-available type
carries weight 1 each. The ratio is computed live off `availableTypes` each call, so it
self-adjusts as more types unlock with no hardcoded weight table to maintain:

| State | Weights | Neutral's share |
|---|---|---|
| Starting roster (Neutral, Water, Shock, Plant) | 4, 1, 1, 1 | 4/7 |
| + Fire unlocked | 4, 1, 1, 1, 1 | 4/8 |
| + Insect unlocked | 4, 1, 1, 1, 1, 1 | 4/9 |
| ... | ... | 4/(4 + N other unlocked types) |

This applies through every type up to and including Draco. Implementation is a
standard weighted-pick-by-cumulative-sum: sum the weights, roll
`Random.Range(0, totalWeight)`, walk the list accumulating weight until the roll lands
inside an entry's range.

Debut priority always overrides this — the guaranteed-next-arrival rule wins for the
one spawn right after a threshold crossing; weighting only governs the ordinary pick
once nothing is pending.

## Related Docs

- `BunnyTypePopulationCurve_DesignDoc.md` — the retimed population thresholds that
  exposed the old Fire-at-position-7 bug and motivated this mechanic.
- `BunnyTypeNiches_DesignDoc.md` — per-type niche/room design; unrelated to spawn
  selection mechanics but shares the same underlying `BunnyTypeDefinition` data.
