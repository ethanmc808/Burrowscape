# Room Merging & Upgrading — Design Document

## Context

`RoomBase` already carries `FootprintWidth` and `Grade` per-instance (added during the build system work specifically so this system wouldn't need a retrofit — `RoomBase.cs:43-49`). Merged-size assets (8x2x6/12x2x6 Grade 1) exist for most room types, and Grade 2/3 prefabs are being authored now. Nothing in code touches merging or upgrading yet — this doc defines what needs to be built.

## Locked-in decisions

- **Merge trigger**: fully automatic. Whenever a newly-placed room ends up directly adjacent to an existing room of the same type and same Grade, they merge immediately — no player action, no confirmation.
- **Merge cost**: free. The placed room pays its own normal build cost; the resulting merge costs nothing extra.
- **Merge cap**: hardcoded at 12x2x6 (adjustable later via a serialized const), not derived from "largest asset that happens to exist."
- **Merge resolution when multiple pairings are possible** (e.g. `[8-wide][gap][4-wide]`, player fills the gap with a new 4-wide): prefer whichever pairing produces the single largest resulting room, since bigger rooms are strictly more space-efficient. `8+4=12` beats leaving `8` and `8` as two separate rooms.
- **Upgrade trigger**: click room -> context panel with an "Upgrade to Grade X" button showing cost, same interaction shape as the existing job-assignment click flow.
- **Upgrade cost**: authored per room-type-and-Grade, via the `goldCost` field already on each variant's `RoomDefinition` asset — a Hospital's Grade-2 entry and a Garden's Grade-2 entry are independent numbers, as is Grade 1->2 vs. Grade 2->3 for the same room type, since each is a separate `RoomDefinition` asset. No formula, no shared multiplier — just whatever the designer sets on that specific asset, editable directly in the Inspector.
- **Spot/path authoring**: every merged-size and every-Grade prefab is fully hand-authored in the Editor (own `RoomSpot`s, `RoomPath`s, entrances) — same as the Grade 1 prefabs today. No runtime spot-combination logic.
- **Grade capacity**: higher Grades are authored with more spots, so upgrading increases capacity.
- **Mid-action edge case**: defer the operation until the room reaches a quiescent state, rather than forcibly redirecting anyone. Cheap to do now that wandering is gone — idle bunnies just sit in a Living Room or hold their current spot, so the window where someone is mid-transit into a room is much smaller than it used to be.
- **EntranceRoom**: upgradable, never mergeable (width fixed at 4x2x6). **LiftRoom**: neither — it only stacks vertically.

## New concept: Room Type Identity

This is the one piece with no existing precedent, and it's the foundation everything else sits on.

**Problem:** merge eligibility needs to answer "is this neighboring room the *same type* as me, at the *same Grade*?" Grade is already on `RoomBase`. Type is not — today, type is only implicit in which concrete `MonoBehaviour` subclass a room uses (`GardenRoom`, `CafeteriaRoom`, `LivingRoom`, `BedroomRoom`, `WaterRoom`, `EntranceRoom`). That works for room types with unique behavior, but breaks down for purely decorative types like "Default Room," "Kitchen," "Storage Room" — if those all just use bare `RoomBase` with no subclass, `GetType()` can't tell a Kitchen apart from a Storage Room, and they'd wrongly become merge-eligible with each other.

**Proposal:** add an explicit `RoomTypeId` to `RoomBase`, authored per-prefab in the Inspector, mirroring exactly how `FootprintWidth`/`Grade` are already done (a `[SerializeField]` set once on the prefab, read at runtime, no catalog lookup required). A simple string (`"Garden"`, `"Kitchen"`, `"Storage Room"`, etc.) is enough — no need for an enum that has to be edited in code every time a new room type is added.

This also solves the Swap-target lookup: given a source room's `RoomTypeId` + a target `(width, grade)`, we need to find the matching prefab. Rather than inventing a new catalog structure, extend the existing `RoomDefinition` — every width/Grade variant of a room type (currently: only the 4-wide Grade-1 ones are wired up, per the comment in `RoomDefinition.cs:5-9`) becomes its own `RoomDefinition` asset, all sharing the same `RoomTypeId`. Only the base 4-wide Grade-1 entries get shown in `BuildMenuUI`'s buildable catalog (unchanged from today); the merged-size and higher-Grade entries exist purely as lookup targets for the Swap step. `RoomDefinition.goldCost` on a Grade-2/3 variant entry doubles as "the cost to upgrade into this variant" — no new cost field needed, and it naturally doesn't get consulted for merges since merge is free.

This means every existing room prefab (including the ones already placed in hand-authored scenes) needs a `RoomTypeId` retrofitted — a one-time Editor pass, not a code migration, since the field defaults sensibly and nothing reads it until this system ships.

## The shared operation: Evacuate -> Swap -> Resettle

Both merge and upgrade are the same operation at different parameters — "replace N existing rooms with 1 new room, preserving occupants":
- **Upgrade**: N=1, width unchanged, Grade increases.
- **Merge**: N=2 (or 3, see resolution algorithm below), Grade unchanged, width increases.

One coroutine-based implementation handles both, e.g. a new static/manager class (`RoomTransitionService` or similar) with `MergeRooms(List<RoomBase>, RoomDefinition target)` and `UpgradeRoom(RoomBase, RoomDefinition target)`, both delegating to a shared `SwapRooms(...)` coroutine. It needs to be a coroutine (not a one-shot method) because of the quiescence wait described below.

**1. Wait for quiescence.** Before touching anything, poll (once per frame or tick) until every bunny associated with the room(s) is in a "spot-committed" state — meaning its logical position is a claimed `RoomSpot` reference plus a `BunnyState` (Working/Relaxing/Sleeping), not a raw `Transform` inside the room that's about to be destroyed. Concretely, defer while any bunny has:
- `CurrentState == MovingToSpot` with a target inside the room (mid-walk, including mid-lift-trip via `pendingWanderRoom`/`pendingFinalRoom`), or
- `CurrentState == Eating` or `Drinking` with `cafeteriaBeingUsed`/`waterRoomBeingUsed` pointing at this room (their production/consumption coroutine is mid-flight and not worth interrupting).

Idle-pacing-without-a-claim inside the room (rare now that wandering's gone, but still possible for a beat between losing a job and claiming a relax spot) also blocks — its position is a raw wander-point Transform with no claim to cleanly detach.

This reuses the same shape as the existing `IsAssociatedWithRoom` check (`NPCBunny.cs:370-379`) but needs a second, narrower predicate — call it `IsTransientlyInRoom(room)` — since `IsAssociatedWithRoom` alone doesn't distinguish "settled, safe to evacuate" from "mid-flight, must wait." Note `IsAssociatedWithRoom` today also doesn't check `cafeteriaBeingUsed`/`waterRoomBeingUsed` at all — that's a real gap if the room being merged/upgraded is itself a `WaterRoom`/`CafeteriaRoom` with someone actively eating/drinking there; `IsTransientlyInRoom` needs to cover it even though `IsAssociatedWithRoom` doesn't.

**2. Evacuate.** Once quiescent, gather every bunny where `IsAssociatedWithRoom(room)` is true for any of the rooms being replaced (this reuses the existing check as-is — `NPCBunny.cs:370`). For each:
- If `assignedJobRoom == room` (Working): remember it needs re-assignment, call the existing `UnassignFromJob()` (`NPCBunny.cs:327`).
- If `claimedRelaxRoom == room` (Relaxing): release via the same pattern `claimedRelaxRoom.ReleaseSpot(...)` already uses elsewhere, clear the claim, set `CurrentState = Idle`. **No bespoke resettle needed** — `TryClaimRelaxSpot()` (`NPCBunny.cs:531`) already searches *every* registered Living Room for the nearest open spot on the very next Idle tick, and the new merged/upgraded room will be one of the candidates. This is the one case where Resettle is literally "do nothing, let the existing per-frame idle logic handle it."
- If `claimedSleepRoom == room` (Sleeping): release the spot the same way. Unlike Relaxing, Sleeping has no per-frame re-poll loop — entry only happens via explicit calls like `LeaveWorkForBedroom()`/`LeaveRelaxingForBedroom()` (`NPCBunny.cs:1230`, `:1269`). Resettle must explicitly redo that: find the nearest Bedroom with a spot (finds the new room) and re-enter Sleeping via the same claim-and-route logic those methods use.

**3. Swap.** Destroy the old room instance(s) (`Destroy(gameObject)` — `OnDisable` already handles `BaseLayoutManager.UnregisterRoom`). Instantiate the target `RoomDefinition.prefab` at the midpoint X of the source rooms' combined span (same Y/floor, same Z), register with `BaseLayoutManager`. For upgrade, position is identical to the source room (width doesn't change).

**4. Resettle.** For each remembered Working bunny, call `AssignToJob(newRoomAsIJobRoom)` — this re-triggers `RequestNewJobSpot()` and needs no other special-casing. Relaxing bunnies are already handled (did nothing in step 2, self-resolves). Sleeping bunnies get explicitly re-routed as described above.

## Merge trigger & resolution algorithm

Not a placement-validator concern (`RoomPlacementValidator` stays generic, no type-awareness added there) — this runs as an explicit post-placement check in `BuildModeController`, right after a room is successfully instantiated and registered. It must **not** run generically from `BaseLayoutManager.RegisterRoom`, since that also fires for every hand-placed room at scene load, where auto-merge should never trigger.

After placement, look at the immediate left and right neighbors on the same floor (via `BaseLayoutManager.GetAllRoomsOnFloor`, same ordering `RoomPlacementValidator` already uses for adjacency). A neighbor qualifies if `RoomTypeId` and `Grade` both match the new room. At most one neighbor per side is ever in play (anything beyond an immediate neighbor isn't contiguous).

Given the qualifying run `[left?][new][right?]`:
1. If `left + new + right` fits within the cap (<=12) and a variant of exactly that width exists -> merge all three into one room. (This is the `4+4+4=12` single-step case.)
2. Otherwise, evaluate the two possible pairings — `left+new` and `new+right` — and take whichever produces the **larger** valid (<= cap, has an authored variant) result. This is what makes `8+4=12` (leaving the far `4` alone) beat `4+4=8` (leaving the far `8` alone) in the classic edge case.
3. If only one side qualifies at all, merge that pairing if it fits under the cap; if it doesn't (e.g. an existing neighbor is already at the 12 cap), no merge happens — the new room just sits there as its own instance, eligible to merge on some future placement if the cap changes.

## Upgrade flow

Click room -> existing-style context panel (new UI, mirrors `RoomClickHandler`/`AssignmentUI`'s open-on-click shape) -> "Upgrade to Grade N" button shows the cost pulled from the target Grade's `RoomDefinition` variant's `goldCost`, which is set independently per room type and per Grade tier -> on confirm, spend Gold via `GoldManager`, then run `UpgradeRoom(room, targetVariant)` (the same Evacuate->Swap->Resettle coroutine, N=1). `EntranceRoom` participates in this despite `CanBeDeleted()` unconditionally blocking deletion — Swap must not route through `CanBeDeleted()` for either merge or upgrade, same as the original room-build design doc already noted.

## Open items / assumptions to confirm before implementation

- **Grade-1 prefab retrofit**: every existing room prefab (already in scenes and in `BuildMenuUI`'s catalog) needs a `RoomTypeId` added. One-time Editor pass, no code migration.
- **Capacity assumption**: assuming merged/higher-Grade prefabs are always authored with at least as many spots as the maximum simultaneous occupancy of what they're replacing (2 Grade-1 4-wide Gardens merging into one 8-wide should have >= their combined spot count). If a prefab is under-provisioned, Resettle could strand a bunny with nothing to reclaim — worth a sanity check once the Grade 2/3 assets are further along, not a code-level guard.
- **`IsTransientlyInRoom` is new code**, distinct from the existing `IsAssociatedWithRoom` — flagging so it's not mistaken for a duplicate.
- **RoomDefinition catalog growth**: every room type now needs up to 9 `RoomDefinition` assets (3 widths x 3 Grades) instead of 1, even though only 1 (the base) is player-buildable. This is more Editor authoring than before but requires zero new asset *types* — just more instances of a type that already exists.
