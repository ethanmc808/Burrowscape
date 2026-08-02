# Working-State Wander Points — Design Doc

**Status: implemented, not yet playtested.** `WorkWanderConfig`/`WorkWanderConfigGenerator`,
`RoomBase.GetWorkWanderChain`, and the full wander loop on `NPCBunny` (`BeginWorkWander`/
`TickWorkWander`/`PickNextWorkWanderTarget`/`StepWorkWanderMovement`) are in place. Still needed before
this does anything visible: run `Burrowscape > Generate Work Wander Config`, and author at least one
spot's `WanderLocation_01/02/...` children in a room prefab to test against.

## Animator Wiring — DECIDED

`CurrentState` deliberately never leaves `Working` during a wander hop (see Runtime Behavior), but
`NPC_Rabbit_Neutral_Controller.controller`'s `Working` state had exactly one outgoing transition
(`IsWorking==false -> Idle`) and nothing reacting to movement — so without a change, a wandering bunny
would slide across the room still playing its static Working pose.

Read the actual transition graph before deciding how to fix it. `Working` needed one new edge
regardless of approach; the real choice was reusing the existing `IsMoving` parameter/`Walking` state
vs. a dedicated new bool. Reusing `IsMoving` won: the return trip (`Walking -> Working`) already exists
with exactly the right condition (`IsMoving==false AND IsWorking==true`), so nothing else needs
touching. A dedicated bool would have needed that *existing* Walking->Working edge modified too (adding
a third condition), since otherwise `IsMoving` staying permanently false the whole time would make it
fire immediately and bounce straight back out of Walking — riskier, since that edge is shared with every
real job-spot arrival in the game, not just this feature.

This also makes mid-hop interrupts (eating/drinking/sleeping firing while a bunny is between wander
points) provably glitch-free: the animator is already sitting in `Walking` when the interrupt fires,
`IsWorking` flips false but `Walking`'s transitions don't reference `IsWorking` at all, so it just
continues the walk cycle straight into the real interrupt-travel with zero transition. An interrupt
firing while dwelling (not mid-hop) has the same one-frame Working->Idle->Walking flash the controller
already had for any ordinary work interrupt before this feature existed — not a regression.

Implementation: `UpdateAnimator()`'s `IsMoving` line now also checks `workWanderPath != null`;
`NPC_Rabbit_Neutral_Controller.controller` got one new `AnimatorStateTransition` (Working -> Walking,
`IsMoving==true`), added to `Working`'s transition list, with no existing transitions modified.

## Per-Room Working Animation — REWORKED (AnimatorOverrideController abandoned)

First attempt used a per-bunny `AnimatorOverrideController` to swap the Working state's clip at
runtime per room. This broke leg bone playback (frozen/wrong-position legs, everything else animating
fine) specifically and only while a bunny sat in the Working state — confirmed via elimination: ruled
out curve-type mismatches, missing curves, and SpriteSkin's stale-mesh caching (forcing
`alwaysUpdate` on all SpriteSkins made no difference); isolated to Working because it was the ONLY
state whose clip was ever reassigned at runtime — every other state (Idle/Walking/Eating/etc.) was
never touched and never broke. Conclusion: runtime clip reassignment via `AnimatorOverrideController`
is not safe for this rig, regardless of which clip or whether the reassignment was even a genuine
value change.

**Replacement: dedicated Animator states, no runtime clip swapping at all.** `RoomBase.workingAnimationKind`
(a `WorkAnimationKind` enum: `Idle` = 0, `Gardening` = 1) replaces the old free-form `AnimationClip`
field. `NPCBunny` sets a plain `WorkAnimIndex` int parameter every frame (`animator.SetInteger(...)`) —
never a controller/clip reassignment, the same safe mechanism every other animator parameter here
already uses. The controller gained a second dedicated state, `Working_Gardening` (baked Motion =
`Rabbit_Neutral_Gardening.anim`), mirroring `Working_Idle`'s own transition shape (arrival from
Idle/Walking gated on `WorkAnimIndex==<kind>` in addition to the existing `IsWorking`/`IsMoving`
conditions; exits back to Idle on `IsWorking==false`; the wander-walk edge to Walking on `IsMoving==true`).

Adding a new working animation later: one more `WorkAnimationKind` enum value + one more
`Working_<Name>` state/transition set in the controller (same shape as Gardening's) — no other code
changes. `WorkRoomAnimationTuner` was updated to broadcast the enum (an `EnumPopup` instead of an
`AnimationClip` picker) per `RoomTypeId` group, same bulk-apply UX as before.

**Migration note:** the field rename (`workingAnimationClip` -> `workingAnimationKind`) orphans any
value already set via the old Tuner — Garden Room needs `Working Animation Kind` set to `Gardening`
again via the Tuner after this change. The now-removed `NPCBunny.defaultWorkingClip` field will simply
vanish from the 5 bunny prefabs' Inspectors — no cleanup needed there.

Status: implemented, not yet playtested against the original frozen-legs report.

## Goal

Bunnies assigned to a job spot (e.g. `PottingSpot_01`) should visibly roam between a handful of
nearby points while remaining in `Working` state, instead of standing frozen at one Transform.
Interruptions (hunger/thirst/sleep triggers, room shutdown, etc.) must still route the bunny out
through the correct authored path — even if the bunny wanders onto a raised platform reachable only
by stairs.

First rooms targeted: Guard Room, Power Room (Coal Room), Water Room. Extendable to any job room
later without new plumbing.

## Core Principle: Capacity vs. Position Stay Separate

`RoomSpot.TryClaim()` continues to be the ONLY thing that governs room job capacity. Nothing about
wander points changes claiming, releasing, or `HasAvailableSpot()` — a bunny claims exactly one
`RoomSpot` for the full duration of the job, same as today. Wander points are purely a cosmetic layer
on top of an already-claimed spot. Production logic never needs to know where the bunny's Transform
currently is — only that `CurrentState == Working`.

## Naming & Hierarchy

Per spot, author a small cluster of child wander points using the existing `SpotNamePrefixAttribute`
convention. Naming order IS walking order — these form a **strictly linear chain**, not an
unordered set:

```
PottingSpot_01
PottingSpot_01_WanderLocation_01
PottingSpot_01_WanderLocation_02
PottingSpot_01_WanderLocation_03
```

The full chain for a spot is `[Spot, WanderLocation_01, WanderLocation_02, WanderLocation_03, ...]`,
with the spot itself as the anchor at index 0. Each *consecutive* pair in that chain must be reachable
by a clear straight line — nothing is assumed about non-consecutive pairs (e.g. the spot itself and
`WanderLocation_03` may NOT have a clear line between them; that's fine, since travel between them
always walks the intermediate points, never a direct line — see Runtime Behavior below).

No branching for now — a single linear chain per spot, kept simple deliberately. If a work area needs
a more complex layout later (a loop, a branch around a big prop), that's a future extension, not
something this pass needs to support.

Each spot owns its own private wander set (Option A) — never shared across spots or bunnies. This
guarantees two working bunnies can never wander toward the same point, since there's no collision
avoidance between NPC bunnies at this scale.

Recommended: 2–3 wander locations per spot is enough to read as "moving around" without needing a lot
of extra platform space or authoring overhead across 70+ room prefabs.

## Runtime Behavior (conceptual — not code)

While `CurrentState == Working`:

- The bunny dwells at its current chain position (initially the spot itself) for a randomized
  interval (see tuning below).
- After dwelling, it picks a random OTHER point anywhere in its spot's chain (excluding wherever it
  currently is) as the next destination.
- It walks there by feeding the **intermediate chain points between current and target, in order**,
  into the same `MoveAlongPath`/waypoint-queue mechanism used everywhere else in this codebase (e.g.
  `GetRouteToSpot`'s multi-hop stairs paths) — never a direct line. E.g. currently at the spot (index
  0), target is `WanderLocation_03` (index 3): the path fed in is
  `[WanderLocation_01, WanderLocation_02, WanderLocation_03]`, walked in that order.
- Because the existing waypoint-queue system (`MoveAlongPath`/`AdvanceToNextWaypoint`/
  `HandleMovingToSpot`) already walks through intermediate waypoints with **no dwell** and only fires
  the arrival callback once the queue is empty, "dwell only at the final destination" falls out for
  free — no new timing logic needed beyond starting the dwell timer on that same arrival callback.
- `CurrentState` stays `Working` the entire time (production ticks continue uninterrupted) — only the
  Transform's target changes, similar in spirit to how `MovingToSpot` already works, just scoped to a
  tiny local loop instead of a cross-room path.
- The bunny's `currentWanderPoint` field (already present, already used by `GetRouteToSpot`/
  `ReturnToPreviousActivity` for eat/drink/sleep interruptions) gets updated to whichever wander
  location the bunny is currently at/heading to.

### Wander interval tuning — DECIDED

Randomized within a range, not fixed, so multiple bunnies in the same room don't visually sync their
movement. Initial values, tunable universally (single global source, not per-room/per-prefab):

- `workWanderMinIntervalSeconds = 3f`
- `workWanderMaxIntervalSeconds = 6f`

Confirmed working in a live playtest (Water Room) — stairs/path routing and the randomized-interval
pacing between 3 points both behaved correctly, including on interrupt. Follow-up tuning from that same
playtest: wander-hop movement reads better slower than normal travel speed. Added
`workWanderSpeedMultiplier = 0.3f` (also global/tunable) — scales both the actual movement speed AND
the Animator's playback speed for the Walking clip while mid-hop (mirrors the existing wanderSpeedMultiplier
pattern used by the Living-Room-fallback pacing state, so feet don't slide at the slower pace).

Lives in a new small config asset following the exact `CombatBalanceConfig` pattern (Resources-loaded
singleton `Instance`, generator-seeded asset, not hand-authored) — NOT added to `CombatBalanceConfig`
itself, since wander pacing applies to non-combat job rooms (Water Room, Coal Room, etc.) and doesn't
belong under a "combat numbers" header.

## Interruption Handling (the stairs problem) — CORRECTED

This is the part that needed verification against the actual routing code before implementation,
since the original draft's reasoning was subtly wrong about *why* it would work.

**The mechanism that actually matters:** `BaseLayoutManager.GetRouteToSpot` (`BaseLayoutManager.cs:398`)
branches on whether `startSpot` (the bunny's `currentSpot`) is null:

```csharp
if (startSpot != null)
    fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance)); // authored chain, incl. stairs
else
    // naive 2-point line: wander point -> exit entrance, NO stairs waypoints at all
```

The authored stairs chain (`StairsWaypoint_02 -> StairsWaypoint_01 -> Entrance`) is ONLY used when
`currentSpot` is non-null. `currentWanderPoint` is not what selects the safe path — it only affects
the naive fallback branch's starting point.

**The bug this would have caused:** the codebase's existing wander-point precedent
(`NPCBunny.OnArrivedAtWanderPoint`, used for Living-Room-fallback pacing) clears `currentSpot = null`
on every arrival at a wander point. If the new Working-wander loop copied that shape, any
hunger/thirst/sleep interrupt firing mid-wander would silently skip the authored stairs chain and
route the bunny in a straight line through geometry it can't actually reach — invisible on flat rooms,
broken on any room with a raised platform.

**The fix:** the new Working-wander code must be a distinct method, separate from
`OnArrivedAtWanderPoint`, and must **never clear `currentSpot`**. Keep `currentSpot = claimedWorkSpot`
for the entire duration of `Working` — only ever update `currentWanderPoint` to track the bunny's live
position. `GetRouteToSpot` will then always take the `startSpot != null` branch on interrupt, using the
authored `GetPathFromSpotToEntrance` chain regardless of which wander location the bunny was standing
at, while `MoveAlongPath` still starts the walk from the bunny's real current Transform position (not
from `PottingSpot_01` itself) — so the visual result is a bunny walking from wherever it actually is
straight into the authored path, no teleport, no line through walls.

This is exactly why the **Design Constraint below** (wander locations reachable via straight line, no
stairs/gaps/walls crossed) still matters — not because of `currentWanderPoint` plumbing, but because
the first waypoint of the room's authored spot-to-entrance path must be reachable via straight line
from every wander location in that spot's cluster.

## Design Constraint to Enforce During Prefab Authoring

Consecutive points in a spot's chain (spot → `WanderLocation_01` → `WanderLocation_02` → ...) must
each be reachable via a straight/simple local line from the one before/after them — chain order should
literally trace a walkable line around any obstacle, e.g. a locker or table sitting between the spot
and a far wander point gets routed around by placing points on both sides of it in sequence. In other
words: wander points are for roaming a small mostly-flat cluster of floor, not for wandering across a
room's full layout. If a spot's "work area" spans both the main floor and a raised platform, that's two
separate concerns — the wander cluster should live entirely on one side, and the stairs should only
ever appear in the room's authored entrance/exit path, never inside the local wander loop itself.

Additionally, the chain's first point (the spot itself) must be reachable via straight line from the
first waypoint of that spot's authored exit path — this is what keeps interrupt routing (below) visibly
clean regardless of where in the chain the bunny actually was standing.

## Verdict: Build Now or Defer?

Not too complicated — build now. It reuses `currentWanderPoint` and `GetPathFromSpotToEntrance`, both
already battle-tested by the eat/drink/sleep interruption flows, with one deliberate deviation from the
existing wander-point precedent (`currentSpot` must NOT be cleared) called out above. The main cost
isn't code complexity, it's prefab authoring time (placing 2–3 wander points + correct stairs waypoints
per spot, across every room that has elevated platforms). Nothing about finishing current prefabs
blocks this feature from being added retroactively room-by-room.
