# Diminishing Returns Production — Design Doc

## Problem

Every work room currently treats each assigned worker as an equal, independent producer. Losing 1 of 3 workers in a room loses exactly 1/3 of that room's output — a flat, linear relationship between headcount and production. This makes small staffing fluctuations (a bunny leaving to eat/drink/sleep, or genuinely being short-staffed) hit total output much harder than it should, especially in rooms with few slots.

## Reference: Fallout Shelter

FO Shelter scales room output by the *sum of assigned dwellers' SPECIAL stats*, not raw headcount, and stat points are capped (10, ~17 with outfits) — in practice this means the first couple of dwellers in a room matter enormously and each additional one matters progressively less. The exact curve isn't published; what's confirmed is the *shape* (steep first, flattening fast) and the practical effect (2 great dwellers > 4 mediocre ones > 6 poor ones, per unit of room-slot cost).

Burrowscape has no per-bunny stat feeding production today, so we're not porting the SPECIAL-sum mechanism itself — just the shape of the curve, applied directly to worker *slot rank* instead of stat totals.

## Agreed mechanic

A worker's contribution is `100% × 0.7^rank`, where rank is 0 for the first active worker in a room, 1 for the second, etc.:

| Rank (worker #) | Multiplier | Cumulative room total |
|---|---|---|
| 1st | 100% | 100% |
| 2nd | 70% | 170% |
| 3rd | 49% | 219% |
| 4th | 34.3% | 253.3% |
| 5th | 24% | 277.3% |
| 6th | 16.8% | 294.1% |

Cumulative total for N workers is a geometric series: `(1 - 0.7^N) / (1 - 0.7)`. No lookup table needed — rank 7, 8, etc. fall out of the same formula automatically, so raising a room's worker-slot count later needs no balance-table maintenance.

Applies to every production ("work") room: **Garden, Water Room (production side), Coal Room**. Does NOT apply to service/need rooms (Cafeteria eating, Water Room drinking, Bedroom sleeping, Living Room relaxing) — those aren't headcount-scaled production, they're per-visitor need restoration.

## Where "rank" comes from — two different code paths, same math

The three production rooms currently work in two structurally different ways, so the diminishing-returns hook lands in two different places:

### Garden / Water (per-bunny independent coroutines)

Each assigned bunny already runs its own coroutine that fires every `productionInterval` and adds `amountPerProduction × GradeMultiplier × bunny.ProductionMultiplier` to the resource stockpile — fully independent of what other bunnies in the room are doing.

Plan: add one more ordered list per room, e.g. `activeWorkerOrder: List<NPCBunny>`, tracking who's currently actively producing (not just holding a spot — a bunny away eating drops out of this list, same lifecycle as `activeProductionRoutines` already has). A bunny's rank is just its live index in that list at the moment its coroutine ticks: `Mathf.Pow(0.7, activeWorkerOrder.IndexOf(bunny))`, folded in as one more multiplicative factor alongside `GradeMultiplier` and `bunny.ProductionMultiplier`.

Because rank is recomputed fresh every tick (not cached), the moment a worker leaves, everyone behind them shifts down a rank on their very next tick — no separate "rebalance" step needed, and total room output is purely a function of *how many* are currently active, never *which* individuals hold which rank. New/returning workers (including a bunny back from an eating trip) always join at the back of the list, i.e. lowest current rank. Since total output only depends on count, this doesn't create any exploit around eating-trip timing.

### Coal / Power (already a room-level aggregate)

Power never touches CoalRoom's coroutines directly — `PowerManager` already just tracks an `activeProducerWorkerCounts: Dictionary<RoomBase, int>` per room and computes `SafeRate(amount, interval) × count × GradeMultiplier` in `Evaluate()`. This is actually the simpler integration point: swap the linear `× count` for the same geometric-series sum, `× (1 - 0.7^count) / (1 - 0.7)`. No changes needed to CoalRoom.cs itself.

### Shared utility

Both paths need the same two pure functions (per-rank multiplier, and the summed total for N), so they should live in one small static helper rather than being duplicated in GardenRoom/WaterRoom/PowerManager — e.g. `WorkerProductionScaling.SlotMultiplier(rate, rank)` / `WorkerProductionScaling.AggregateMultiplier(rate, count)`.

### Where the 0.7 rate itself lives

Proposed: a new `[SerializeField] protected float diminishingReturnsRate = 0.7f;` on **RoomBase**, alongside the existing per-room balance fields (`gradeMultiplier`, `workerEnergyDecayPerSecond`, etc.) — same Inspector-tunable-per-prefab pattern already used everywhere else in this codebase. Every prefab defaults to 0.7, satisfying "apply it globally for consistency" today, while leaving the door open to override a specific room type later without new plumbing. Open question below — you may want this to be a true single shared constant instead, if per-prefab drift risk is unwanted.

## Real technical risk: integer rounding could zero out marginal workers

Garden and Water both round to whole units before storing: `Mathf.RoundToInt(carrotsPerProduction * GradeMultiplier * bunny.ProductionMultiplier)`. With `carrotsPerProduction = 1`, a 3rd-ranked worker (49%) produces `0.49` pre-round → **rounds down to 0 carrots, every single tick.** Every worker past the 2nd would be contributing nothing but decaying their own needs for zero output — the opposite of the intended effect.

Power isn't affected (PowerManager's math is pure float, no rounding), but Garden's carrots and Water's water both are.

Two real fixes, not mutually exclusive:
1. **Fractional carry-over accumulator** — track a running float remainder (per bunny, or per room) and only add whole units to the stockpile once the accumulated fraction crosses 1.0, keeping the leftover for next tick. Exact over time, no lost production, no rebalancing of base amounts required. This is the technically correct fix and what I'd recommend.
2. **Raise `carrotsPerProduction`/`waterPerProduction`** so per-tick amounts stay comfortably above rounding-loss range at every rank up to the room's slot count — simpler, but reopens the whole rebalancing-the-numbers work from earlier in this session, and low ranks (5th/6th worker, ~24%/17%) would still round to 0 unless the base amount is pushed uncomfortably high.

Recommend (1) — happy to do (2) instead if you'd rather just push the numbers up.

## Resolved decisions (implemented)

1. **Rate storage**: `diminishingReturnsRate` is a per-prefab `[SerializeField]` on each of GardenRoom/WaterRoom/CoalRoom (not RoomBase — only work rooms need it), broadcast-editable across all three at once via the new `Burrowscape > Work Room Production Tuner` Editor window (mirrors `NPCBunnyNeedsTuner`'s pattern). All three default to 0.7.

2. **Multiplier stacking order confirmed irrelevant** (multiplication is commutative) — but this surfaced a bigger, explicitly-requested addition: a **Recommended Type production bonus**, mirroring `ForagingLocationDefinition.recommendedTypes` exactly. Each work room now has:
   - `recommendedTypes: List<BunnyType>` — per-room (Garden → Plant, Water → Water, Coal → Shock), genuinely different per room so NOT part of the broadcast section. Revised after initial implementation: rather than a manual per-prefab Inspector edit, the field is `[HideInInspector]` and edited exclusively through a per-room grid in the tuner (one row per discovered room, applied immediately on change, same live-edit pattern as `BunnyBaseStatsWindow`) — this removes the risk of a stray edit landing on the wrong prefab. A one-click "Apply Suggested Defaults" button sets Garden/Water/Coal to Plant/Water/Shock respectively.
   - `typeMatchProductionBonus: float` (default 0.25) — the flat bonus, IS part of the broadcast section, same reasoning as the diminishing-returns rate.
   
   Final per-tick multiplier chain: `baseAmountPerProduction × GradeMultiplier × bunny.ProductionMultiplier × slotMultiplier × (1 + typeMatchProductionBonus if matched else 1)`.

3. **Rounding fix**: fractional carry-over accumulator, confirmed. Each bunny's production coroutine keeps a local `float carryover`, rounded to 2 decimal places every tick (`Mathf.Round(x * 100f) / 100f`) to avoid float drift; only whole banked units are ever added to CarrotManager/WaterManager, the remainder always carries forward. UI/debug panels still only ever see whole numbers.

4. **Rank reassignment on leave**: simplest option confirmed. `activeWorkerOrder` is a plain ordered `List<NPCBunny>`; rank is `IndexOf(bunny)`, recomputed fresh on every tick/report (never cached). Removing a bunny (spot released, unassigned, left the base) automatically shifts everyone behind it down a rank for free — no special-case code needed. New/returning workers (including back-from-eating) always append at the back (lowest current rank), since total room output only depends on active count, not identity.

## Implementation notes: Coal Room / Power is structurally different

Garden and Water directly add to a passive stockpile (CarrotManager/WaterManager) per bunny per tick, so the per-bunny carry-over + slot-multiplier logic lives entirely inside each room's own coroutine.

Coal Room doesn't work that way — Power production is read by `PowerManager.Evaluate()` from a per-room number it holds, because Power is arbitrated in real time against consumption (floor shutoff, rationing pool, etc.), not just accumulated. Previously that number was a plain integer headcount (`NotifyProducerActive`/`NotifyProducerInactive`, ±1 per bunny). Since the type-match bonus is now per-bunny-identity-dependent (a Shock bunny counts for more than a Neutral one), a plain headcount can no longer represent a Coal Room's true output — so:

- `CoalRoom` now keeps its own `activeWorkerOrder` list (same shape as Garden/Water) and, on every worker start/stop, recomputes its **full weighted total from scratch** (`Σ slotMultiplier × typeBonus × bunny.ProductionMultiplier` over all active workers) via `ReportWeightToPowerManager()`.
- `PowerManager`'s `Dictionary<RoomBase, int> activeProducerWorkerCounts` + `NotifyProducerActive/Inactive/AllInactive` (±1 semantics) was replaced with `Dictionary<RoomBase, float> activeProducerWeights` + a single `SetProducerWeight(room, weight)` (confirmed via grep this trio had exactly one caller — CoalRoom — so the API surface was safe to change outright rather than needing a compatibility shim).
- `Evaluate()`'s arbitration math is otherwise unchanged: `SafeRate(amount, interval) × weight × GradeMultiplier`, just reading a pre-weighted float instead of multiplying a raw int count.
- Incidental improvement: Power production previously never applied `bunny.ProductionMultiplier` at all (an existing asymmetry with Garden/Water). Since CoalRoom now iterates its own active workers anyway to compute the weight, this was folded in for free — Power is now consistent with Garden/Water on that front.

## Files touched

- `Assets/Scripts/Room Scripts/WorkerProductionScaling.cs` (new) — shared `SlotMultiplier(rate, rank)` formula.
- `Assets/Scripts/Room Scripts/GardenRoom.cs`, `WaterRoom.cs` — diminishing returns + type bonus + carry-over.
- `Assets/Scripts/Room Scripts/CoalRoom.cs` — same, via the weighted-report model described above.
- `Assets/Scripts/Resource Management Scripts/PowerManager.cs` — count→weight refactor.
- `Assets/Scripts/Editor/WorkRoomProductionTuner.cs` (new) — broadcast tool for `diminishingReturnsRate` / `typeMatchProductionBonus`.
