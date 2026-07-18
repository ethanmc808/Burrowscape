# Dynamic Doorway System — Design Doc

Goal: side walls between rooms should only open into a doorway when an actual neighboring room is placed there. Every room keeps its current 3-piece side wall (`{Side}Wall_Upper`, `{Side}Wall_Lower_Big`, `{Side}Wall_Lower_Small`) with the doorway gap between the two Lower pieces; we add a 4th "filler" piece per side, exactly sized to plug that gap, and toggle it on/off based on live adjacency.

A second, complementary piece: a decorative door **frame** per side, nested per individual room (not the shared structural prefabs), that appears exactly when the filler disappears — i.e. the doorway gets dressed with a frame precisely when it's a real, walkable opening. See §5.

## 1. Current architecture (for reference)

- **`RoomBase.cs`** (`Assets/Scripts/Room Scripts/RoomBase.cs`) — `FootprintWidth` (virtual, default 4), `Grade`, `RoomTypeId`, `GridX => Mathf.RoundToInt(transform.position.x)`, `LeftEntrance`/`RightEntrance`/middle waypoints. Registers/unregisters with `BaseLayoutManager` in `OnEnable`/`OnDisable`. No wall/door concept exists today.
- **`BaseLayoutManager.cs`** — singleton, `roomsByFloor: Dictionary<int, List<RoomBase>>`. `GetOrderedRooms(floor)` returns rooms sorted by `GridX`. Convention: **higher world-X = visually left**. `RegisterRoomOnFloor`/`UnregisterRoomOnFloor` are the two chokepoints every room placement/removal path already funnels through.
- **`RoomPlacementValidator.cs`** — `GetInterval(room, out min, out max)` = `position.x ± FootprintWidth/2` (`internal`, already shared with `RoomMergeResolver`). Two rooms are "touching" when `Mathf.Abs(aMin - bMax) < Epsilon` or `Mathf.Abs(aMax - bMin) < Epsilon` (`Epsilon = 0.01f`). This is the adjacency test we'll reuse — never cached today, always recomputed from live transform positions.
- **Placement/removal call sites**: `BuildModeController.TryConfirmPlacement` (Instantiate + `RoomMergeResolver`), `RoomTransitionService`'s swap method (Destroy old + Instantiate new, for merge/upgrade), `DeleteModeController` (destroys a room, currently touches no adjacency logic at all). All three ultimately go through `RoomBase.OnEnable`/`OnDisable` → `BaseLayoutManager`'s register/unregister methods.
- **Wall geometry**, confirmed by direct trace of the actual prefab catalog:
  - All wall pieces are flat siblings directly under the room root (no "Walls" parent), named `LeftWall_Upper`, `LeftWall_Lower_Big`, `LeftWall_Lower_Small`, `RightWall_Upper`, `RightWall_Lower_Big`, `RightWall_Lower_Small`, `BackWall` — identical convention across every width and grade, including `LiftRoom`.
  - **Only 10 prefab files in the entire catalog actually own real wall geometry.** Every other room-type prefab (Garden, Cafeteria, Kitchen, etc. — all widths, all grades, 66 files total) nests the matching-width **Grade‑1** `DefaultRoom` prefab as a full nested instance and inherits its walls; none of them contain their own wall GameObjects. Grade doesn't affect wall visuals anywhere in the catalog today — a "Grade 3 Kitchen" uses the exact same wall mesh as a "Grade 1 Kitchen" of the same width. The 10 files are:
    1. `DefaultRoom_4x2x6_Grade1.prefab`
    2. `DefaultRoom_8x2x6_Grade1.prefab`
    3. `DefaultRoom_12x2x6_Grade1.prefab`
    4. `LiftRoom_1x2x6_Grade1.prefab`
    5–10. `DefaultRoom_{4,8,12}x2x6_Grade{2,3}.prefab` — six self-contained files that nothing else nests, but which `RoomCatalogRegistry` (a superset catalog "including the ones BuildMenuUI never shows directly") lists as real, independently-reachable `RoomDefinition`s for merge/upgrade lookups. I couldn't fully confirm whether any live upgrade path actually results in a player-visible "Default Room," but the cost of covering them is 6 extra file edits, which is cheap insurance either way.
  - Sample gap geometry (Grade 1, 4-wide, left side — right side mirrored at `x = +1.95`):
    - `LeftWall_Lower_Big`: pos `(-1.95, -0.4567, 4.4583)`, scale `(0.1, 0.9028312, 4.6840405)` → spans z `[2.116, 6.800]`
    - `LeftWall_Lower_Small`: pos `(-1.95, -0.4567, 1.25)`, scale `(0.1, 0.9028312, -0.5)` → spans z `[1.0, 1.5]`
    - Gap to fill: z `[1.5, 2.116]` → center z ≈ `1.808`, scale.z ≈ `0.616` (consistent with `LeftEntrance` sitting at local z ≈ 1.833)
    - Every other structural file has its own slightly different numbers for the same two Lower pieces — the filler's transform must be computed from each file's own sibling values, not copy-pasted from this sample.

## 2. Decisions locked in (from your answers)

- Wall geometry is shared across grades intentionally — editing the Grade‑1 structural prefabs is sufficient for the 66 dependent variants, but the 6 orphaned Grade‑2/3 `DefaultRoom` files still get the same treatment since they hold independent geometry.
- Roll out to the entire catalog (all room types × all widths × all grades) in this pass.
- `LiftRoom` gets the same doorway toggle as any other room (no special-casing for "always open").
- Toggling is instant (`SetActive`), no slide/fade animation.
- Door frames: start with a generic placeholder frame everywhere (real per-room-type art swapped in later), rolled out to the entire catalog (all ~76 individual room prefabs) in this pass, with each room independently contributing its own frame piece at the shared boundary (mirroring how wall pieces already work) rather than one frame being "owned" by only one of the two rooms.

## 3. New wall pieces

Add `LeftWall_Filler` and `RightWall_Filler` to each of the 10 structural prefabs: a `Cube` mesh matching its side's `Lower_Big`/`Lower_Small` siblings in Y-position, Y-scale, material, and collider setup, with X unchanged (same wall plane) and Z position/scale computed to exactly plug the gap between them (see formula above, computed per-file). Because they share Y and material with the existing Lower pieces, a closed doorway reads as a single unbroken wall band, exactly as described.

## 4. Runtime design

### Why name-based lookup, not serialized Inspector fields

`LeftEntrance`/`RightEntrance` on `RoomBase` are serialized Transform references, and because 66 of the ~76 room prefabs nest their geometry from a different file, someone had to hand-wire those references into every single one of those 66 files already. Repeating that pattern for two new fields would mean manually dragging references into ~76 prefabs by hand.

Instead, `RoomBase` resolves its filler pieces (and, per §5, its door frame pieces) at runtime by name, once, in `Awake`:

```csharp
private GameObject leftWallFiller;
private GameObject rightWallFiller;
private GameObject leftDoorFrame;
private GameObject rightDoorFrame;

private void ResolveDoorwayPieces()
{
    foreach (var t in GetComponentsInChildren<Transform>(true))
    {
        switch (t.name)
        {
            case "LeftWall_Filler": leftWallFiller = t.gameObject; break;
            case "RightWall_Filler": rightWallFiller = t.gameObject; break;
            case "LeftDoorFrame": leftDoorFrame = t.gameObject; break;
            case "RightDoorFrame": rightDoorFrame = t.gameObject; break;
        }
    }
}
```

`GetComponentsInChildren(true)` walks the whole hierarchy regardless of nesting depth, and includes inactive objects (needed once a filler has been toggled off, or before a frame has ever been shown). This means **the only Editor work required is adding the filler GameObjects to the 10 structural prefabs, and the frame GameObjects to each individual room prefab** — every filler-dependent variant picks up its filler automatically via the shared structure, no per-prefab wiring needed for that half. Frames still need to be added once per individual room file since they're intentionally not shared (see §5), but neither piece needs a serialized reference anywhere.

This also sidesteps a bug class you just fixed yesterday: the cross-prefab reference corruption where 36+ wide/Grade-2/3 room prefabs had their serialized `leftEntrance`/`rightEntrance`/spot fields silently pointing at the 4-wide Grade‑1 prefab's own internal children (from copy-pasting without retargeting), fixed with `RoomReferenceFixer.cs`. Because both lookups are resolved live off each instantiated object's own hierarchy rather than a serialized fileID reference baked into a prefab asset, there's no reference to duplicate-and-forget-to-retarget in the first place — it can't reproduce that failure mode.

### Public API on `RoomBase`

```csharp
public void SetLeftDoorwayOpen(bool open)
{
    leftWallFiller?.SetActive(!open);
    leftDoorFrame?.SetActive(open);
}

public void SetRightDoorwayOpen(bool open)
{
    rightWallFiller?.SetActive(!open);
    rightDoorFrame?.SetActive(open);
}
```

One boolean per side now drives both pieces in opposite directions — filler hides and frame shows when a doorway opens, and vice versa when it seals. Both `?.` calls are no-ops for a prefab that hasn't had one piece or the other added yet, so the two features can land and be rolled out independently without breaking each other.

`LiftRoom` inherits these for free since it's a `RoomBase` subclass; its own prefab just needs the same pieces added at its (narrower) scale.

### Adjacency recompute — centralized, not per-call-site

Rather than adding a doorway-refresh call to `BuildModeController`, `RoomTransitionService`, and a brand-new hook in `DeleteModeController` separately, hook directly into the two existing chokepoints in `BaseLayoutManager` that every placement/deletion/merge/scene-load path already funnels through: `RegisterRoomOnFloor` and `UnregisterRoomOnFloor`. This means **no changes are needed to `BuildModeController.cs`, `RoomTransitionService.cs`, or `DeleteModeController.cs` at all** — a deliberately narrow blast radius given how many pathing/click bugs have already come from touching adjacent systems in this project.

```csharp
// BaseLayoutManager.cs
private readonly HashSet<int> dirtyDoorwayFloors = new HashSet<int>();
private Coroutine doorwayRefreshRoutine;

private void RequestDoorwayRefresh(int floorIndex)
{
    dirtyDoorwayFloors.Add(floorIndex);
    if (doorwayRefreshRoutine == null)
        doorwayRefreshRoutine = StartCoroutine(DoorwayRefreshRoutine());
}

private IEnumerator DoorwayRefreshRoutine()
{
    yield return null; // let this frame's registrations/unregistrations settle
    foreach (var floor in dirtyDoorwayFloors)
        RefreshDoorwaysForFloor(floor);
    dirtyDoorwayFloors.Clear();
    doorwayRefreshRoutine = null;
}

private void RefreshDoorwaysForFloor(int floorIndex)
{
    var rooms = GetOrderedRooms(floorIndex); // already sorted by GridX
    foreach (var room in rooms)
    {
        RoomPlacementValidator.GetInterval(room, out float min, out float max);
        bool hasLeftNeighbor = false;  // higher-X side
        bool hasRightNeighbor = false; // lower-X side
        foreach (var other in rooms)
        {
            if (other == room) continue;
            RoomPlacementValidator.GetInterval(other, out float oMin, out float oMax);
            if (Mathf.Abs(oMin - max) < RoomPlacementValidator.Epsilon) hasLeftNeighbor = true;
            if (Mathf.Abs(oMax - min) < RoomPlacementValidator.Epsilon) hasRightNeighbor = true;
        }
        room.SetLeftDoorwayOpen(hasLeftNeighbor);
        room.SetRightDoorwayOpen(hasRightNeighbor);
    }
}
```

Call `RequestDoorwayRefresh(floorIndex)` at the end of both `RegisterRoomOnFloor` and `UnregisterRoomOnFloor`.

This single mechanism covers every scenario for free:
- **Runtime placement** (`BuildModeController`) — new room's `OnEnable` registers → refresh queued.
- **Merge/upgrade swap** (`RoomTransitionService.SwapRooms`, already implemented and playtest-confirmed) — old room(s)' `OnDisable` unregisters, new room's `OnEnable` registers → refresh queued. This works unchanged whether it's a simple upgrade (1 room → 1 room) or a real merge (2+ rooms → 1 wider room, including the recursive re-merge chain `SwapRooms` already runs): the replaced rooms simply vanish from the floor list and the new one's edges get evaluated like any other room, no special-casing needed.
- **Deletion** (`DeleteModeController`) — destroyed room's `OnDisable` unregisters, former neighbor(s) get re-evaluated on the next pass → they reseal automatically, with zero new code in `DeleteModeController` itself.
- **Scene-load / hand-placed rooms** (e.g. the entrance room and any starting layout) — each one's `OnEnable` fires during scene load same as any other registration, so the very first coalesced refresh pass after load computes correct initial state without any special-cased startup logic.
- ~~**Entrance room's outward-facing edge** — never has a neighbor by construction (nothing can be built beyond it), so it naturally stays sealed forever without needing to special-case "this is the entrance." The other side behaves like any other room's edge.~~ **Corrected in `Docs/RoomVisualSystems_Design.md` §2** — this was backwards. That side always leads to the gate/surface and must stay permanently *open* (filler hidden, frame shown), not sealed. Fixed via an `EntranceRoom.SetLeftDoorwayOpen` override.

## 5. Door frames

Unlike the wall filler, the frame is owned per individual room prefab, not the 10 shared structural files — because you want each room type free to have a visually different frame later. That means it touches a much larger set of files: all ~76 individual room-type prefabs (every `{RoomType}_{width}x2x6_Grade{n}.prefab`, plus `LiftRoom_1x2x6_Grade1.prefab`), since each one needs its own `LeftDoorFrame`/`RightDoorFrame` GameObject placed directly under its own root — not under the nested structural instance.

### Positioning anchor

Every room prefab already has `LeftEntrance`/`RightEntrance` marker Transforms sitting right at the doorway (confirmed local z ≈ 1.833 for the 4-wide/Grade‑1 case, i.e. essentially the same location as the wall gap's center). These markers were exactly what `RoomReferenceFixer.cs` fixed yesterday, so they should now be trustworthy per-prefab anchors. The frame's position is derived from each prefab's own `LeftEntrance`/`RightEntrance` transform rather than hardcoded per width/grade.

### Half-frame model

Two adjacent rooms' side walls already sit coincident at the shared boundary (Room A's `RightWall_*` pieces and Room B's `LeftWall_*` pieces occupy essentially the same physical X plane when the rooms are touching) — each room independently contributes a full piece, and they simply end up in the same place. The door frame follows the identical pattern: each room authors one complete frame object for its own side, anchored to its own entrance marker. Since two adjacent rooms' entrance markers coincide at the shared boundary by construction (required for pathing to line up at all today), both rooms' frames naturally align there with no cross-file coordination needed — nothing has to know about its neighbor's frame.

### Placeholder geometry

To get the whole mechanism visibly working now, each `LeftDoorFrame`/`RightDoorFrame` is a single parent `GameObject` (initially inactive) containing three simple `Cube`-mesh children forming a plain border — two vertical side posts flanking the opening plus one horizontal lintel across the top — reusing the same material as the adjacent `Lower_Big`/`Lower_Small` wall pieces so it reads as one coherent set until custom art replaces it. Toggling the single parent's `SetActive` cascades to all three children, matching the filler's single-object toggle model exactly. Later, replacing the placeholder with real per-room-type art just means swapping what's inside that same named parent object — the runtime lookup and toggle logic never need to change.

### Rollout mechanics

Since the placeholder is generic and derived the same way (anchor to each prefab's own entrance marker) across all 76 files, this is a good fit for a small bulk Editor tool — the same strategy already validated yesterday for the merge/upgrade retrofit (`RoomDataGenerator.cs`, `RoomReferenceFixer.cs`, both under `Assets/Scripts/Editor/`) rather than 76 manual edits. A new tool (e.g. `Burrowscape/Add Door Frame Placeholders`) would iterate every room prefab, read its `LeftEntrance`/`RightEntrance` marker, and inject the placeholder frame relative to it. Re-running it later should skip any prefab whose frame has already been hand-customized (e.g. detect by checking if the parent still only contains exactly the 3 placeholder-named children) so it doesn't clobber real art once you start replacing pieces per room type.

## 6. Prefab editing plan

Edit these 10 files, adding `LeftWall_Filler`/`RightWall_Filler` to each (transform values computed per-file from that file's own `Lower_Big`/`Lower_Small` siblings, not copied across files):

1. `Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade1.prefab`
2. `Assets/Prefabs/Rooms/Grade 1 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade1.prefab`
3. `Assets/Prefabs/Rooms/Grade 1 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade1.prefab`
4. `Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/LiftRoom_1x2x6_Grade1.prefab`
5. `Assets/Prefabs/Rooms/Grade 2 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade2.prefab`
6. `Assets/Prefabs/Rooms/Grade 3 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade3.prefab`
7. `Assets/Prefabs/Rooms/Grade 2 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade2.prefab`
8. `Assets/Prefabs/Rooms/Grade 3 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade3.prefab`
9. `Assets/Prefabs/Rooms/Grade 2 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade2.prefab`
10. `Assets/Prefabs/Rooms/Grade 3 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade3.prefab`

Since the gap geometry is derivable from values already stored in each prefab's own YAML, this can be done as direct file edits (computing each filler's transform from its file's own `Lower_Big`/`Lower_Small` values) rather than requiring manual eyeballing/snapping in the Unity Editor GUI.

## 7. What you need to do in Unity

**Before implementation:**
- You have uncommitted changes to `NPCBunny.cs` and `RoomTransitionService.cs` (unrelated to this feature per `git status`, most likely left over from yesterday's merge/upgrade bug fixes). Worth committing or stashing those first so this feature's diff stays clean and reviewable on its own.
- Optional: quickly check `BuildMenuUI`'s serialized room list and any upgrade-path config to confirm whether a "Default Room" (as opposed to Garden/Cafeteria/etc.) is ever actually reachable by a player. If it's provably never reachable, files 5–10 in §6 can be skipped. Not required — just saves a little editing time if you already know the answer.
- The wall filler only needs 10 files edited directly, no bulk tooling required. The door frame placeholder, at 76 files, is exactly the kind of bulk-retrofit problem `RoomDataGenerator.cs`/`RoomReferenceFixer.cs` already exist for — plan on a new Editor tool for that part rather than hand-editing each file (see §5).

**After implementation (playtest checklist):**
- Let Unity reimport the edited/generated prefabs; visually check each width/grade tier's filler piece for seams, z-fighting, or material mismatch against its neighbors.
- In Build Mode, place a chain of rooms (matching your `E-G-C` example) and confirm each doorway opens exactly when a real neighbor appears, on both sides, **and** that the placeholder frame appears at the same moment the filler disappears (and vice versa when deleting).
- Check a placed pair of adjacent rooms up close for frame/frame overlap or z-fighting where both sides' placeholder frames coincide at the shared boundary.
- Delete a middle room from a 3-room chain and confirm both former neighbors reseal (filler back, frame gone) correctly.
- Confirm whatever rooms are hand-placed in the scene at Editor-authoring time (entrance room, any starting layout) come up with correct doorway state on Play, not just runtime-built ones.
- Confirm bunny pathing through open doorways still works with no regressions, including walking directly through/past the new frame posts (flagging this explicitly since pathing has been fragile before, and the frame is a new physical object near the walkway that didn't exist before).
- Spot-check a `LiftRoom` segment placed at the edge of a floor (nothing beside it) to confirm it seals correctly (filler visible, frame hidden), and one with rooms on both sides to confirm it opens correctly.

## 8. Open item

Whether the 6 orphaned Grade‑2/3 `DefaultRoom` prefabs are actually player-reachable is unconfirmed — they're listed in `RoomCatalogRegistry`'s superset catalog and referenced in `Home_Base.unity`, which is consistent with either "reachable via upgrade" or simply "registry lists every asset that exists, whether reachable or not." Recommend editing them anyway (cheap) rather than spending more time tracing upgrade config to prove a negative.
