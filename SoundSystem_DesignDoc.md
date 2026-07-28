# Sound System — Design Doc

**Status: implemented 2026-07-27, not yet Editor-wired or playtested.** All script changes described
below are in place (`AudioManager`'s music/UI/gate/build-destroy-upgrade/new-type/proximity-loop
additions; hooks in `EntranceGate`, `BuildModeController`, `DeleteModeController`,
`RoomTransitionService`, `WildBunnySpawner`, `CoalRoom`/`GardenRoom`/`WaterRoom`'s Work Room ambient,
and `NPCBunny`'s `SyncProximityAmbient` for Eating/Drinking/Sleeping). Every clip is still an empty
Inspector field — nothing will actually be audible until Ethan drags in real `AudioClip` assets and,
for the proximity system, tunes `baseProximityRadius`/`referenceOrthographicSize` against the actual
camera setup in-Editor. Not yet compiled/playtested in the Unity Editor itself.

**One implementation-time correction worth flagging**: the doc originally assumed proximity distance
would be computed on the XZ plane. Re-reading `TestCamera.cs`, the orthographic camera actually pans in
X/Z (middle-mouse drag) and moves along Y for floor switching (Q/E) — since it's orthographic and looks
down Z, only X and Y actually affect what's visibly on screen. The implementation computes proximity
distance on the **XY plane** instead (ignoring Z, which is just view depth), which is what "audible
when visibly close" actually requires.

**Key-collision fix**: `WaterRoom` is simultaneously a Work Room (production ambient) and the
drinking-spot room (drinking ambient) — the same instance, two unrelated sounds. Proximity loop keys
are `(room, category)` tuples (e.g. `(this, "Work")` vs. `(waterRoom, "Drinking")`), not bare room
references, so the two never collide onto the same AudioSource/clip.

## Goal

Add sound to the game beyond the one existing SFX (level-up Cheer, see
[WorkRoomXP_DesignDoc.md](WorkRoomXP_DesignDoc.md)): background music, UI feedback, and a set of
world/gameplay cues. Split cues into **global** (always audible, position doesn't matter) and
**proximity** (only audible near the camera, scaled by zoom) so a full base doesn't turn into a wall
of overlapping noise.

**Global cues** (play everywhere, once, no distance falloff):
- Background music — loops from scene start, playlist-ready even though only one track exists today
- UI button clicks — distinct open vs. close cue per menu
- Gate opening / closing
- Building a room
- Destroying a room
- A new bunny type appearing (first-time reveal, not every spawn)

**Proximity cues** (audible only near the camera, volume scaled by distance and zoom):
- Work Room ambient noise (while a room has at least one active worker)
- Rabbits eating carrots
- Rabbits drinking water
- Rabbits sleeping

## Existing infrastructure

`Assets/Scripts/Audio Scripts/AudioManager.cs` is already the project's one audio singleton
(`AudioManager.EnsureInstance()`, self-creating, mirrors `PowerManager`/`GoldManager`). It currently
exposes two entry points, both already proven in production at
[NPCBunny.cs:1151](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:1151) (the level-up cheer):

- `PlaySFXAtPosition(clip, position, volumeScale)` — one-shot via `AudioSource.PlayClipAtPoint`, free
  overlap, no pooling.
- `PlaySFX2D(clip, volumeScale)` — one-shot on a single shared 2D `AudioSource` living on the manager.

Everything below is planned as **additions to this same script** (per your ask to tie as much as
possible into `AudioManager`), not a new manager. No dedicated camera/proximity system exists yet in
the codebase — this doc introduces that concept fresh, following the `Vector3.Distance` idiom already
used in `BaseManager.cs` (lines 48/84/124/158) and the `Camera.main`-caching convention in
`BunnyDepthSorter.cs:31`.

## AudioManager additions

### 1. Music playlist

- New `[SerializeField] private List<AudioClip> musicPlaylist` + `[SerializeField] private bool
  shuffleMusic`.
- Dedicated `musicSource` (separate `AudioSource`, `spatialBlend = 0`, own volume slider,
  `loop = false` — looping is handled by the manager advancing to the next track, so a 1-track
  playlist still "loops" via wraparound).
- `Awake()` starts the first track; a coroutine (or `Update` check on `!musicSource.isPlaying`)
  advances to the next track (sequential or shuffled) when the current one ends.
- `[Range(0,1)] private float musicVolume` separate from the existing SFX `masterVolume`, since music
  and SFX are usually balanced independently and the user will likely want to tune them separately
  later.

### 2. UI click SFX

- `PlayUIOpen(AudioClip clip = null)` / `PlayUIClose(AudioClip clip = null)` — thin wrappers around
  `PlaySFX2D`, but with their own default-clip fields (`[SerializeField] private AudioClip
  defaultMenuOpenClip / defaultMenuCloseClip`) so every panel doesn't need its own clip reference wired
  individually. A panel can still pass a specific clip if it wants a unique sound; passing `null` falls
  back to the shared default.
- `PlayButtonClick()` — same pattern, one shared default click clip, for plain (non-menu) buttons.
- Hook points (per the survey): each panel's `Open...()`/`Close()` pair —
  [RoomUpgradeUI.cs:49](Assets/Scripts/UI%20Elements/RoomUpgradeUI.cs:49) /
  [:59](Assets/Scripts/UI%20Elements/RoomUpgradeUI.cs:59),
  [AssignmentUI.cs:46](Assets/Scripts/UI%20Elements/AssignmentUI.cs:46) /
  [:55](Assets/Scripts/UI%20Elements/AssignmentUI.cs:55), and the same shape in `BunnyInfoUI`,
  `ForagingScreenUI`, `BuildMenuUI`, `BunnyApprovalUI`. Both are already guarded by `if
  (!panelRoot.activeSelf) return;` style checks, so wiring the call inside each method won't double-fire.
  Individual in-panel action buttons (e.g. `RoomUpgradeUI.cs:92` `OnUpgradeClicked`) get the plain
  `PlayButtonClick()` instead of open/close.

### 3. Gate SFX

- `PlaySFXAtPosition` is already positional but gate should be **global** per your list, so use
  `PlaySFX2D` instead (or a new `PlayGateSound(AudioClip clip)` wrapper for clarity) called from
  [EntranceGate.cs](Assets/Scripts/Gate%20&%20Queue%20Scripts/EntranceGate.cs)'s `TransitionRoutine`:
  a cue when the transition starts moving (line ~95, `CurrentState = toState`), or separately at
  "reached Open" (line ~127) and "reached Closed" (line ~140) if opening and closing should sound
  different (they were called out as wanting different sounds).

### 4. Build / destroy room SFX

- Global, via `PlaySFX2D`.
- Build hook: [BuildModeController.cs:171](Assets/Scripts/Room%20Scripts/BuildModeController.cs:171),
  right after `Instantiate(selectedDefinition.prefab, ...)`.
- Destroy hook: [DeleteModeController.cs:90](Assets/Scripts/Room%20Scripts/DeleteModeController.cs:90),
  right after `Destroy(room.gameObject)`.
- `RoomTransitionService.cs`'s merge/upgrade path also does its own Destroy+Instantiate (`SwapRooms`
  coroutine, lines ~85/90) but plays a **distinct** `roomUpgradeClip` instead of the build/destroy
  cues — see Decisions below.

### 5. New bunny type reveal SFX

- Global, via `PlaySFX2D`.
- Hook: [WildBunnySpawner.cs:257-259](Assets/Scripts/NPC%20Bunny%20Scripts/WildBunnySpawner.cs:257),
  paired with the existing `NewBunnyTypeNotification.Instance?.Show(chosenType)` call — fires only on
  genuine first-time reveal (`typesEverSpawned.Add(...)` returns true), not on every wild spawn.

### 6. Proximity SFX system

New concept, added to `AudioManager` as the shared authority so individual scripts don't each
reimplement distance math.

**Listener point.** Camera is orthographic (`TestCamera.cs`, scroll wheel drives
`cam.orthographicSize`). `AudioManager` caches `Camera.main` once (same pattern as
`BunnyDepthSorter.cs:31`) and re-reads `orthographicSize` each check.

**Audible radius scales with zoom.** Base radius (Inspector field, e.g. `baseProximityRadius`) is
multiplied by `orthographicSize / referenceOrthographicSize` (also an Inspector field, defaulting to
whatever the camera's default zoom is), so zooming out enlarges the audible radius and zooming in
shrinks it — the goal being "sounds audible roughly whenever the source is visibly close on-screen,"
not a fixed world-space radius that feels wrong at every zoom level.

**Smooth fade, not hard cutoff.** A fade band at the outer edge of the radius (e.g. outer 20% of the
radius) linearly ramps volume 1→0, rather than a boolean snap. Distance is computed on the XZ plane
(ignoring the camera's height/Q-E floor panning) against each source's position.

**Per-source looping proximity voice** — `Assets/Scripts/Audio Scripts/AudioManager.cs` additions:

```csharp
public class ProximityLoopHandle { /* wraps an AudioSource the caller owns via a returned handle */ }

public ProximityLoopHandle RegisterProximityLoop(AudioClip clip, Transform followTarget, float volumeScale = 1f);
public void UnregisterProximityLoop(ProximityLoopHandle handle);
```

- `RegisterProximityLoop` creates a dedicated looping `AudioSource` parented to (or continuously
  following) `followTarget`, starts silent, and gets added to a list the manager re-evaluates every
  frame (or every N frames — see Question 3) for distance-based volume.
- Returns a handle so the caller (e.g. a Work Room) can `Unregister` it when the loop should stop (room
  goes idle, room destroyed).
- **One instance per room, not per bunny** — per your answer, concurrent identical sounds are capped at
  one per source room. For Work Room ambient this is natural (one loop per room, registered on
  first-worker, unregistered on last-worker-leaves). For eating/drinking/sleeping, "per room" means the
  WaterRoom/Bedroom/cafeteria spot cluster, not per-bunny — the loop is registered against the room
  (or a representative anchor point in it) the first time any bunny enters that state there, and stays
  running as long as at least one bunny is still in that state there; a simple per-room active-count is
  enough (increment on enter, decrement on leave/state-change, unregister at 0). See Question 2 for a
  clarifying edge case.

**Proximity hook points (from the survey):**
- Work Room ambient: `CoalRoom.cs:84` `NotifyBunnyReadyToWork` (register/increment) and the leaving
  path around lines 75-81 (decrement/unregister at zero); same shape needed in `WaterRoom.cs`,
  `GardenRoom.cs` (Coal/Water/Garden all implement `IJobRoom`). `EntranceRoom` is excluded — it's not a
  "work" room in the ambient-noise sense.
- Eating: `NPCBunny.cs` `OnArrivedAtEatingSpot()` (called from `HandleMovingToSpot()` around line 1572)
  to register/increment; the corresponding "left Eating state" transition to decrement.
- Drinking: `OnArrivedAtDrinkingSpot()` (line ~1574), same increment/decrement shape.
- Sleeping: `OnArrivedAtSleepingSpot()` (line ~1578), same shape.
- Exact "left the state" point needs a quick look at `NPCBunny.cs`'s state-exit code (wherever
  `CurrentState` next changes away from Eating/Drinking/Sleeping) once implementation starts — the
  survey found the entry points but not yet the specific exit lines.

## Decisions (resolved)

1. **Merge/upgrade rooms** get their own **distinct** SFX, separate from build and destroy — a third
   global clip field (`roomUpgradeClip`), played from `RoomTransitionService.cs`'s `SwapRooms` in place
   of (not in addition to) the normal build/destroy cues.
2. **Per-room proximity grouping** — one loop per room *instance*, anchored at that room's transform, as
   assumed. Confirmed fine for multiple same-type rooms (e.g. two WaterRooms) to each have their own
   audible loop.
3. **Update cadence** — per-frame distance re-checks for all registered proximity sources, as assumed.
   Revisit only if it becomes a measured problem.
4. **Clip assignment** — placeholder Inspector fields for all clips; audio files supplied later.
5. **Gate open vs. close** — two separate clip fields, `gateOpenClip` / `gateCloseClip`. Needed because
   the gate can stay open for a stretch while multiple bunnies pass through, so open/close are
   independent events, not a single reversible cue.

Ready to implement: the `AudioManager` additions above, the per-system hook calls, and the
proximity-loop registration in `CoalRoom`/`WaterRoom`/`GardenRoom`/`NPCBunny`.
