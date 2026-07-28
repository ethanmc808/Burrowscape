# Work Room XP & Level-Up Feedback — Design Doc

**Status: implemented 2026-07-27, Cheer animation + SFX confirmed working in-Editor 2026-07-27.** All
script changes described below are in place (`AudioManager`, `RoomBase`'s shared XP coroutine,
`GardenRoom`/`WaterRoom`/`CoalRoom` wiring, `NPCBunny`'s `XPGainMultiplier`/`OnLevelUp`/
`PlayLevelUpFeedback`, the Smart/Dumb trait seeds, the Tuner's XP section, and `ForagingManager`'s
`OnLevelUp` subscription). Ethan authored the jump clip and a level-up SFX, wired `NPC_Rabbit_Neutral_
Controller.controller` (the shared controller all 5 gameplay bunny prefabs use), and confirmed by
playtest: a bunny leveling up mid-Working plays the Cheer, then falls straight back into its Working
pose with zero interruption to the actual job/production/XP logic — the "never touches CurrentState"
design worked exactly as intended.

**Animator wiring gotcha hit during this pass** (worth remembering — see the session's diagnostic
trail): the trigger parameter name silently became `IsCheering 0` (Unity auto-appends a numeric
suffix when a new parameter's name collides with an existing/leftover one — here, from converting an
earlier same-named Bool attempt to a Trigger without fully removing the old entry first). Neither
`animator.SetTrigger("IsCheering")` in code nor the transition's own condition could match that name,
so the whole thing silently no-op'd — no error, SFX still fired (unrelated code path), just no visual.
Confirmed root cause only after directly reading the `.controller` YAML asset itself rather than
guessing from a description of the Editor UI — `m_Name: IsCheering 0` was right there in the parameter
list. Two secondary issues were also caught the same way and are worth remembering as general Animator
patterns for this project's hub-based state machine (Idle is the default/hub state; spokes like
Working/Eating transition back to it, not to each other directly): the *entry* transition (`Any State
-> Cheering`) needs Has Exit Time OFF (fire immediately on trigger), while the *exit* transition
(`Cheering -> Idle`) needs it ON with Exit Time near 1 (not 0 — Exit Time is a fraction of the clip's
length, so 0 would cut it short) so it waits for the clip to actually finish before leaving.

**Still not done**:
- Run `Burrowscape > Generate Bunny Trait Seed Data` to actually create/update Smart/Dumb in whichever
  `BunnyTraitCatalog` is in the open scene (code only touches the generator's seed list, not the scene
  component itself — matches this project's usual "code first, Editor pass separately" workflow).
- `AudioManager` self-creates via `EnsureInstance()` if nothing's placed in the scene, so no manual
  setup is strictly required, but a manually-placed instance lets `masterVolume` be Inspector-tuned.
- Balance values (`baseXPPerSecond`/`typeMatchXPBonus`/`xpGradeMultiplier` per-prefab, Smart/Dumb's
  1.25x/0.75x) are the placeholders discussed below — expect a retuning pass once fully playtested,
  same caveat as every other numeric system in this project.
- The Work Room XP *rate itself* (bunnies actually earning XP from a full work shift, tick-decoupling
  across Garden/Water/Coal's different production intervals) hasn't been playtested yet — only the
  level-up feedback (animation/SFX/return-to-work) has been confirmed so far.

## Goal

Give bunnies a second source of XP — working a job in Garden/Water/Coal Room — alongside the
already-working Foraging XP source, using a formula that stays consistent across work room types
despite wildly different production tick rates/amounts. Pair it with a level-up "Cheer" animation +
sound effect so leveling is felt in the moment, without ever gating the level-up itself behind a
player action (explicitly NOT the Fallout Shelter model, where the player must click to redeem a
level-up).

## What already exists (confirmed working this session)

- `NPCBunny.AddExperience(float)` / `LevelUp(int)` ([NPCBunny.cs:1072](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:1072),
  [NPCBunny.cs:993](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:993)) — fully implemented and live, not scaffolding.
  `AddExperience` adds to a running cumulative `experience` total then loops `LevelUp` for as many
  levels as the new total justifies (handles one grant crossing several levels at once), capped at
  level 50. `LevelUp` re-resolves `Stats`/`ActivePassives` through the same IV/EV/Nature pipeline as
  spawn and carries `currentHP` forward by the max-HP delta rather than snapping to full.
- `BunnyLevelCurve.CumulativeXPForLevel(level)` = `round(0.8 × level³)` — Pokemon's "Fast" growth
  group, one shared curve for every bunny type.
- Today's only real caller is `ForagingManager.cs:544` (`bunny.AddExperience(finalXP)` on trip
  return). Nothing calls it from a work room yet.
- `RoomBase`/`IJobRoom` architecture: `GardenRoom`, `WaterRoom`, `CoalRoom` each implement `IJobRoom`,
  each run their own per-worker production `Coroutine` keyed in an `activeProductionRoutines` dict,
  and each already compute a production multiplier from slot-rank (diminishing returns), a per-room
  `recommendedTypes` type-match bonus, and `bunny.ProductionMultiplier` (trait-driven).
- `RoomBase.gradeMultiplier` ([RoomBase.cs:154](Assets/Scripts/Room%20Scripts/RoomBase.cs:154)) — a per-prefab,
  Editor-authored multiplier (not auto-derived from Grade) already driving production. **Fixed this
  session**: audited every Grade 1/2/3 Garden/WaterRoom/CoalRoom prefab — Grade 1 was already
  consistently `1` across the board; Grade 2 and Grade 3 were each only correctly authored on one
  Coal Room prefab out of nine, with the other eight still sitting at the Grade-1 default. All 9
  Grade 2 prefabs are now `1.5`, all 9 Grade 3 prefabs are now `2`.
- `WorkRoomProductionTuner` ([Editor/WorkRoomProductionTuner.cs](Assets/Scripts/Editor/WorkRoomProductionTuner.cs)) —
  existing Editor window that broadcasts `diminishingReturnsRate`/`typeMatchProductionBonus` to every
  work room prefab at once, plus a per-room grid for editing each room's `recommendedTypes` list. This
  is the "edit universally" pattern the new XP tuning knobs below should follow.
- `TraitEffectType` enum ([BunnyTraitDefinition.cs](Assets/Scripts/NPC%20Bunny%20Scripts/BunnyTraitDefinition.cs)) —
  one multiplier per trait (`EnergyDecayMultiplier`, `ProductionMultiplier`, etc.), applied once in
  `NPCBunny.ApplyTraitEffects()`.
- Animation: every `NPCBunny` requires an `Animator` ([NPCBunny.cs:69](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:69)),
  driven every frame by `UpdateAnimator()` ([NPCBunny.cs:2724](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:2724)), which
  sets bool params (`IsMoving`/`IsWorking`/`IsEating`/`IsDrinking`/`IsSleeping`) straight off
  `CurrentState`. No trigger-based one-shot animations exist yet. Two Animator Controller assets are
  in play across the 5 bunny types: one shared by Fire/Water/Plant/Shock (guid `1a8fd7cd...`) and a
  separate one for Neutral (guid `6771f901...`).
- **No audio playback exists anywhere in the codebase** — no `AudioSource`/`PlayOneShot`/`AudioClip`
  usage found in any script. The level-up SFX is genuinely new infrastructure, not a wire-up of
  something already there.

---

## Part 1 — Work Room XP

### The problem: production numbers don't share a common rate

Per-room production tuning is deliberately different per room type, e.g.:

| Room | Production Amount | Interval |
|---|---|---|
| Power (Coal) Room | 1.5 | 2s |
| Garden | 2 | 40s |
| Water Room | 3 | 15s |

Tying XP directly to production amount/interval would make a Power Room worker earn XP at roughly
**20x** the rate of a Garden worker, purely as a side effect of numbers that were tuned for resource
balance, not XP pacing. XP needs its own rate, independent of any room's production tick.

### Formula

```
XP/sec (raw, pre-trait) = baseXPPerSecond × xpGradeMultiplier × (typeMatch ? 1 + typeMatchXPBonus : 1)
```

**Correction (2026-07-27, post-implementation):** `bunny.XPGainMultiplier` (Smart/Dumb) is **not**
part of this room-side formula at all — it's applied once, centrally, inside
`NPCBunny.AddExperience(amount)` itself (`experience += amount * XPGainMultiplier`), so it scales XP
from *every* source automatically — Foraging, Work Rooms, and anything added later — without each
caller needing to remember to apply it. The original version of this doc (and the first pass of code)
scoped it to only the work-room formula above, matching a literal reading of Part 1's original
formula; Ethan corrected this once he saw the Smart/Dumb trait descriptions only mentioned Work Rooms
— the intent was always "affects all XP gain," not "affects Work Room XP specifically." Every XP
source (`ForagingManager.DepositTripResults`, `RoomBase.GrantWorkXPRoutine`) now passes its own raw,
pre-multiplier amount straight into `AddExperience`, which applies the trait once as the final step.

- `baseXPPerSecond` — one shared placeholder value across every work room type, broadcast the same
  way `diminishingReturnsRate` already is.
- `xpGradeMultiplier` — **a separate field from production's `gradeMultiplier`**, per Ethan's call:
  XP and production are allowed to diverge in pacing later even though they start at the same 1/1.5/2
  shape.
- Type-match bonus — same shape as production's `typeMatchProductionBonus`, but its own separate
  `xpRecommendedTypes` list per room (also per Ethan's call to keep XP fields fully independent of
  production fields).
- `bunny.XPGainMultiplier` — new trait-driven multiplier, mirrors `ProductionMultiplier` in shape but
  NOT in scope — see the correction above, it applies inside `AddExperience` to ALL XP, not just this
  formula. Smart (1.25x) and Dumb (0.75x) are new trait content — neither existed in
  `BunnyTraitCatalog` before this pass.

### Pacing math

Reaching level 5 from a fresh level-1 spawn (0 XP) costs `CumulativeXPForLevel(5) = round(0.8×5³) = 100 XP`.

At the original placeholder of **20 XP/sec**, that's reachable in **5 seconds** unbuffed, or **1.6
seconds** with Grade 3 + type match + Smart stacked (62.5 XP/sec) — a level-1 bunny would blow past
level 5 almost the instant it starts its very first shift. Compared against Foraging's own passive
tick income (`xpPerTick=2` every `baseTickInterval=8s` ≈ 0.25 XP/sec baseline, before item-find
bonuses — [ForagingManager.cs:80,90](Assets/Scripts/Foraging/ForagingManager.cs:80)), 20 XP/sec is roughly
two orders of magnitude too fast.

**Confirmed by Ethan: target base rate ≈ 0.25 XP/sec.** At that rate:

| Scenario | Rate | Time to level 5 | Time to level 50 (100,000 XP) |
|---|---|---|---|
| Grade 1, no match | 0.25/sec | 6.7 min | ~111 hrs (4.6 days) |
| Grade 1, type match | 0.3125/sec | 5.3 min | ~89 hrs |
| Grade 3, type match, Smart (3.125x) | 0.78125/sec | 2.1 min | ~35.6 hrs (1.5 days) |

Single-digit minutes to reach level 5 feels like real but not tedious early progress; a multi-day
grind for max level under best-case buffs is normal pacing for a management sim's endgame.

### Architecture

- New fields on `GardenRoom`/`WaterRoom`/`CoalRoom` (mirrors the existing production-scaling fields
  exactly): `baseXPPerSecond` (broadcast), `typeMatchXPBonus` (broadcast), `xpGradeMultiplier`
  (per-prefab, authored like `gradeMultiplier`), `xpRecommendedTypes` (`HideInInspector`, per-room
  grid, its own list separate from `recommendedTypes`).
- **XP is granted on its own fixed timer, fully decoupled from each room's production tick.** A new
  shared coroutine lives on `RoomBase` (the common parent of all three job rooms) — protected
  `StartWorkXPRoutine(NPCBunny)` / `StopWorkXPRoutine(NPCBunny)`, backed by its own
  `activeXPRoutines` dictionary, ticking on a short fixed interval (placeholder: 1 second) regardless
  of whether that room's `productionInterval` is 2s or 40s. This is the actual fix for the tick-rate
  mismatch — XP no longer looks at `productionInterval`/`PowerProductionInterval` at all.
- Each room calls `StartWorkXPRoutine`/`StopWorkXPRoutine` at exactly the same points it already
  starts/stops its production coroutine (`NotifyBunnyReadyToWork`, `StopProductionRoutine`,
  `OnRoomShutdown`) — so XP accrual shares the same "is this bunny actively working right now"
  lifecycle as production, without sharing its clock.
- New `NPCBunny.XPGainMultiplier` float property, populated in `ApplyTraitEffects()` the same way
  `ProductionMultiplier` is (defaults to 1, multiplies in place per matching trait) — but applied to
  the incoming `amount` inside `AddExperience` itself, not read by this room-side formula at all.
- New `TraitEffectType.XPGainMultiplier` enum case; **Smart** (1.25x) and **Dumb** (0.75x) authored as
  new `BunnyTraitDefinition` entries in `BunnyTraitCatalog` — net-new data, nothing to migrate.
- `WorkRoomProductionTuner` gets a second broadcast section (`baseXPPerSecond`/`typeMatchXPBonus`)
  plus its own per-room grid for `xpRecommendedTypes`, mirroring the existing production grid exactly.
  `xpGradeMultiplier` gets authored per-prefab the same way `gradeMultiplier` already is.

---

## Part 2 — Level-Up Feedback (Cheer animation + SFX)

### Constraints (from Ethan)

1. **No Fallout Shelter–style manual level-up.** Leveling stays fully automatic exactly as it works
   today — `AddExperience`/`LevelUp` are never gated behind a player click. The animation/SFX is a
   pure reactive side effect of a level-up that already happened, never a prerequisite for it.
2. Must not interfere with pathfinding.
3. A bunny that levels up and cheers while Working must resume Working afterward.

### Key decision: the cheer never touches `CurrentState`

Both constraints 2 and 3 collapse into the same answer if the cheer is built as a **pure Animator
Controller effect that never changes `BunnyState`/`CurrentState`.**

Today, `UpdateAnimator()` drives locomotion bools straight off `CurrentState`
([NPCBunny.cs:2728-2732](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:2728)), and every job room's
production/XP coroutine self-terminates the moment `bunny.CurrentState != BunnyState.Working` (e.g.
[GardenRoom.cs:163](Assets/Scripts/Room%20Scripts/GardenRoom.cs:163)). If leveling introduced a new
`BunnyState.Cheering` (or reused an existing non-Working state) even momentarily, every one of those
coroutines would read it as "this bunny stopped working" and unregister it from
`activeWorkerOrder`/`activeProductionRoutines`/the new XP routine — silently dropping a bunny out of
its job over a cosmetic flourish. That's exactly the class of pathing/state-machine fragility this
project has been careful about before.

So: **`CurrentState` is never touched by leveling, at all.** Instead:

- New `levelUpTriggerParam = "LevelUpCheer"` string field, declared the same way as the existing bool
  params ([NPCBunny.cs:123-127](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:123)).
- The Animator Controller(s) (both the shared Fire/Water/Plant/Shock controller and the separate
  Neutral controller — two physical assets need this added) gain one new state for the jump clip,
  wired as `Any State → Cheer` (condition: the trigger), with **Has Exit Time** checked and an
  unconditional transition back out once the clip finishes (transition duration ~0, no conditions).
  Because the exit transition doesn't check anything, it lands wherever the existing
  `IsWorking`/`IsEating`/`IsSleeping` bools already say the bunny should be — i.e. exactly what
  `UpdateAnimator()` would already be driving that frame if the cheer had never happened.
- Net effect: a bunny that levels up mid-Working plays the jump for its short duration, then falls
  right back into its Working pose — with zero code path ever reading or writing `CurrentState`.
  Nothing about production, job assignment, or movement needs to know the cheer happened.

### Authoring note for the jump clip itself

Keep the hop as a bone/local-space animation, not one that drives the GameObject's root Transform.
`MoveAlongPath` already drives root `Transform.position` every frame during actual movement — a
root-motion jump risks visually fighting that in the (rare but possible, see below) case a level-up
lands mid-transit. Any vertical motion should be baked into the rig's existing bone keyframes, the
same category as the Eating/Drinking/Working clips already are, so world-space position is untouched
regardless of what state the bunny is in when it fires.

### When to actually play it — three buckets, confirmed by Ethan

`LevelUp` can fire while a bunny is in any state, not only Working — `AddExperience` is called from
Foraging's trip-return and (after this system) periodically while Working, and nothing today
prevents a *future* passive-XP source (an accessory or passive that grants trickle XP over time)
from landing a grant mid-transit or while still out on a trip. **Neither of those sources exists
yet** — nothing currently calls `AddExperience` while `CurrentState` is a transit state or
`Foraging` — but Ethan wants the general rule built now so it's already correct whenever such a
source shows up, rather than retrofitting this later. Three buckets:

1. **Transit states** (`MovingToSpot`, `PassingGate`, `DepartingThroughGate`, `WaitingForLift`,
   `RidingLift`, `DisembarkingLift`, `Despawning`) — suppressed entirely, no visual, no SFX, no log.
   Confirmed by Ethan: simplicity over correctness here, since nothing grants XP mid-transit today
   anyway (a bunny doesn't earn XP by riding a lift).
2. **`BunnyState.Foraging`** (parked off-screen at the staging point for the whole trip) — visual
   and SFX both suppressed (no visible bunny, no source for the sound to seem to come from), but
   **a log entry is still recorded** — see below. Also confirmed unreachable by any code today (the
   only real `AddExperience` call, in `DepositTripResults`, runs strictly after the bunny is already
   back to `Idle` post-gate — see `RunDispatchRoutine`'s `WaitUntil` chain,
   [ForagingManager.cs:225-238](Assets/Scripts/Foraging/ForagingManager.cs:225)), but built now for the same
   forward-looking reason as bucket 1.
3. **Every other "settled" state** (`Working`, `Idle`, `Relaxing`, `Eating`, `Drinking`, `Sleeping`)
   — visual + SFX play normally, and can overlap freely across bunnies. Confirmed by Ethan: no
   throttle of any kind in base, even if several bunnies level up in the same frame — the
   Foraging-suppression above is what keeps that scenario from mattering for trips resolving in a
   batch, and outside of Foraging, stacking is fine.

### Foraging's bold log entry

`ForagingTripState` already has exactly this mechanism:
[ForagingManager.cs:47](Assets/Scripts/Foraging/ForagingManager.cs:47)'s `AddLogEntry(string)` inserts at the
front of `recentLog` (max 10, newest first), rendered by `ForagingTripDetailUI` as a single
`TextMeshProUGUI.text` joined by newlines
([ForagingTripDetailUI.cs:176](Assets/Scripts/UI%20Elements/ForagingTripDetailUI.cs:176)). TMP has rich text
enabled by default, so "making it bold" is trivial — wrap the string in `<b>...</b>` when it's added,
e.g. `trip.AddLogEntry($"<b>Level Up! Reached Level {newLevel}</b>")`. No new UI work needed.

The wrinkle: `NPCBunny` has no reference to `ForagingTripState` (tracked externally by
`ForagingManager`, per the existing "mirrors GardenRoom's externally-tracked coroutines" pattern —
see [[project_burrowscape_foraging_system]]-shaped precedent). So `NPCBunny` can't write to the trip
log itself. Cleanest fix, keeping the dependency direction the same as everywhere else in this
codebase (Foraging depends on `NPCBunny`, never the reverse): `NPCBunny` exposes a public
`event Action<int> OnLevelUp` (fires with the new `Level`, once per `AddExperience` call that leveled
up at all — see below), and `ForagingManager` subscribes to it for the lifetime of a trip
(`RunDispatchRoutine`'s dispatch/cleanup are the natural subscribe/unsubscribe points, mirroring how
it already manages `activeTrips`). The handler simply checks `bunny.CurrentState ==
BunnyState.Foraging` before logging — harmless no-op otherwise (e.g. the current always-post-return
call site). `NPCBunny` itself never needs to know Foraging exists; it just always fires the event.

### Multi-level-in-one-grant handling

`AddExperience`'s while loop ([NPCBunny.cs:1076](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:1076)) can
call `LevelUp` more than once in a single call — a big XP grant crossing several level thresholds at
once is explicitly by design (see the existing comment on that method). The cheer/SFX/event must fire
**once per `AddExperience` call that leveled the bunny up at all**, not once per individual `LevelUp`
invocation — otherwise a big grant would fire 2-3+ times in the same frame, which would look/sound
like a stutter rather than a single satisfying cheer. So the hook belongs in `AddExperience`, not
inside `LevelUp` itself:

```
public event System.Action<int> OnLevelUp;

public void AddExperience(float amount)
{
    int levelBefore = Level;
    experience += amount;

    while (Level < 50 && experience >= BunnyLevelCurve.CumulativeXPForLevel(Level + 1))
        LevelUp(Level + 1);

    if (Level > levelBefore)
    {
        PlayLevelUpFeedback();
        OnLevelUp?.Invoke(Level);
    }
}

private void PlayLevelUpFeedback()
{
    bool isTransit = CurrentState == BunnyState.MovingToSpot || CurrentState == BunnyState.PassingGate
        || CurrentState == BunnyState.DepartingThroughGate || CurrentState == BunnyState.WaitingForLift
        || CurrentState == BunnyState.RidingLift || CurrentState == BunnyState.DisembarkingLift
        || CurrentState == BunnyState.Despawning;
    if (isTransit || CurrentState == BunnyState.Foraging) return; // Foraging logs instead, via OnLevelUp

    if (animator != null) animator.SetTrigger(levelUpTriggerParam);
    if (audioSource != null && levelUpClip != null) audioSource.PlayOneShot(levelUpClip);
}
```

(Illustrative only — not yet written.)

### Sound effect — confirmed: build a centralized `AudioManager` now

Since no audio playback exists in the codebase at all yet, and this is the first of what will
obviously be many sound effects (Ethan's own example: a UI click sound on every button, which would
be miserable to retrofit one button at a time later), **confirmed: build a small `AudioManager`
singleton now**, matching the shape of existing singletons in this codebase (`CarrotManager`,
`WaterManager`, `NotificationToast`) rather than adding a persistent `AudioSource` component to every
one of 200+ bunny prefabs (or, worse, to every UI button).

Two entry points, since level-up cheers and UI clicks are different shapes of sound:
- `AudioManager.Instance.PlaySFXAtPosition(AudioClip clip, Vector3 position)` — for world-anchored
  sounds like the level-up cheer, built on Unity's built-in `AudioSource.PlayClipAtPoint`. Free
  overlapping playback per call, no pooling/throttle logic required, which matches bucket 3 above
  (unlimited overlap in base).
- `AudioManager.Instance.PlaySFX2D(AudioClip clip)` — for UI/non-positional sounds (button clicks,
  toasts, menu open/close) that have no world position to anchor to. Backed by one or a few
  `AudioSource`s living on the manager itself with `spatialBlend = 0`, `PlayOneShot`-ing onto whichever
  is free (or just always onto one — 2D UI clicks rarely need to overlap themselves the way world SFX
  does).

Both routes give a single choke point for a future global SFX volume slider / mute toggle, without
ever having to touch `NPCBunny`, a work room, or any individual UI button again when that setting
gets added — the entire point of building this now instead of the first time a second sound effect is
needed.

---

## Decisions (confirmed by Ethan)

1. Work-room base rate target ≈ **0.25 XP/sec** at Grade 1, unmatched.
2. XP tuning uses **separate dedicated fields** (`xpGradeMultiplier`, `xpRecommendedTypes`) rather
   than reusing production's `gradeMultiplier`/`recommendedTypes` — allowed to diverge later.
3. `gradeMultiplier` data-authoring gap fixed this session (all Grade 2 work rooms → 1.5, all Grade 3
   → 2, 16 prefabs total) — done, not pending.
4. No manual/click-gated leveling, ever — animation and SFX are purely reactive.
5. Cheer clip = a new "bunny jumps into the air" animation Ethan will author.
6. Mid-transit (lift ride, etc.): suppress animation AND SFX together, no special-casing — simplicity
   over correctness, since nothing grants XP mid-transit today regardless.
7. `BunnyState.Foraging`: suppress animation AND SFX (no visible/audible source), but always record a
   bold `<b>...</b>` log entry in the trip's `recentLog` via a new `NPCBunny.OnLevelUp` event.
8. No throttle on simultaneous cheers/SFX in base, ever — full overlap allowed across bunnies. The
   Foraging suppression is what keeps a batch of resolving trips from being the noisy case.
9. Centralized `AudioManager` singleton **confirmed** for the SFX (see "Sound effect" above) — general
   -purpose infra (`PlaySFXAtPosition` + `PlaySFX2D`), not level-up-specific, explicitly so future UI
   click sounds etc. don't need retrofitting one button at a time.

## Non-goals

- No UI toast/popup for leveling — animation + SFX (+ the Foraging log entry) only, per Ethan's ask.
- No changes to the leveling formula/curve itself (`BunnyLevelCurve` untouched).
- No changes to Foraging's existing `AddExperience` call site itself — it already works correctly;
  only the new `OnLevelUp` subscription is added around it.
