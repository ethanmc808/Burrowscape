# Room Visual Systems — Design Doc

Supersedes `Docs/DoorwaySystem_Design.md` (kept for history; this doc folds in everything from that one plus the theming work discussed after it). Covers four additive features built on the same room prefab architecture:

1. **Dynamic wall filler** — a doorway gap that seals shut when there's no neighboring room, and opens when one is placed.
2. **Decorative door frames** — a per-room custom-art frame that appears exactly when the filler disappears (inverse toggle).
3. **Room wall/ceiling/floor theming** — each room *type* (Garden, Bedroom, etc.) gets its own consistent wall/ceiling/floor color, with the wall itself split into a lighter "upper" tone and a darker "lower" tone.
4. **`BackWall` split** — dividing the single back-wall piece into `BackWall_Upper`/`BackWall_Lower` (same combined size) so the two-tone look wraps all the way around the room, not just the sides.

None of these require severing any prefab's link to `DefaultRoom` — that link is exactly what keeps the editing cost small, and severing it would multiply all future maintenance (including this work) by ~7x. All four features use the same core trick: **resolve pieces at runtime by GameObject name**, never by serialized Inspector reference, so the shared structural prefabs stay clean and no per-prefab wiring is needed beyond adding the objects themselves.

## 1. Current architecture (reference)

- **`RoomBase.cs`** (`Assets/Scripts/Room Scripts/RoomBase.cs`) — `FootprintWidth` (virtual, default 4), `Grade`, `RoomTypeId`, `GridX => Mathf.RoundToInt(transform.position.x)`, `LeftEntrance`/`RightEntrance`/middle waypoints. Registers/unregisters with `BaseLayoutManager` in `OnEnable`/`OnDisable`. No wall/door/theme concept exists today.
- **`BaseLayoutManager.cs`** — singleton, `roomsByFloor: Dictionary<int, List<RoomBase>>`. `GetOrderedRooms(floor)` returns rooms sorted by `GridX`. Convention: **higher world-X = visually left**. `RegisterRoomOnFloor`/`UnregisterRoomOnFloor` are the two chokepoints every room placement/removal path already funnels through (including the already-implemented merge/upgrade system in `RoomTransitionService.SwapRooms`).
- **`RoomPlacementValidator.cs`** — `GetInterval(room, out min, out max)` = `position.x ± FootprintWidth/2` (`internal`, already shared with `RoomMergeResolver`). Two rooms are "touching" when `Mathf.Abs(aMin - bMax) < Epsilon` or `Mathf.Abs(aMax - bMin) < Epsilon` (`Epsilon = 0.01f`) — the adjacency test feature 1 reuses.
- **Wall geometry, confirmed by a full trace of the prefab catalog**: all wall pieces are flat siblings directly under the room root, named `LeftWall_Upper`, `LeftWall_Lower_Big`, `LeftWall_Lower_Small`, `RightWall_Upper`, `RightWall_Lower_Big`, `RightWall_Lower_Small`, `BackWall` — identical convention across every width and grade, including `LiftRoom`.
- **Only 10 prefab files in the entire catalog own real wall/ceiling/floor geometry.** Every other room-type prefab (Garden, Cafeteria, Kitchen, etc. — all widths, all grades, 66 files total) nests the matching-width **Grade‑1** `DefaultRoom` prefab as a full nested instance and inherits its structure; none of them contain their own wall GameObjects. Grade doesn't affect wall visuals anywhere in the catalog — a "Grade 3 Kitchen" uses the exact same wall mesh as a "Grade 1 Kitchen" of the same width. The 10 files:
  1. `DefaultRoom_4x2x6_Grade1.prefab`
  2. `DefaultRoom_8x2x6_Grade1.prefab`
  3. `DefaultRoom_12x2x6_Grade1.prefab`
  4. `LiftRoom_1x2x6_Grade1.prefab`
  5–10. `DefaultRoom_{4,8,12}x2x6_Grade{2,3}.prefab` — six self-contained files nothing else nests, but which `RoomCatalogRegistry`'s superset catalog lists as independently-reachable merge/upgrade targets. Covering them is cheap insurance even though I couldn't fully confirm they're player-visible.
- **`BackWall` split (§5) is already done** for all 3 Grade‑1 default rooms (4/8/12-wide) — the user completed it by hand before implementation started, deliberately matching the split to the side walls' own Y-values rather than just bisecting the original single piece, which also fixed a pre-existing overlap between the old `BackWall` and the side walls' `Lower_Big` pieces. Current values (Grade 1, 4-wide, left side — right side mirrored at `x = +1.95`; scale.x scales with room width for the 8/12-wide files):
  - `LeftWall_Upper`/`RightWall_Upper`: pos `(∓1.95, 0.45, 4)`, scale `(0.1, 0.9, 6)` → spans y `[0, 0.9]`
  - `LeftWall_Lower_Big`/`RightWall_Lower_Big`: pos `(∓1.95, -0.45, 4.5)`, scale `(0.1, 0.9, 4.8)` → spans y `[-0.9, 0]`, z `[2.1, 6.9]`
  - `LeftWall_Lower_Small`/`RightWall_Lower_Small`: pos `(∓1.95, -0.45, 1.25)`, scale `(0.1, 0.9, -0.5)` → spans z `[1.0, 1.5]`
  - Gap to fill (wall filler, §2): z `[1.5, 2.1]` → center z = `1.8`, scale.z = `0.6`
  - `BackWall_Upper`: pos `(0, 0.45, 6.95)`, scale `(4, 0.9, 0.1)` → spans y `[0, 0.9]`, z `[6.9, 7.0]`
  - `BackWall_Lower`: pos `(0, -0.45, 6.95)`, scale `(4, 0.9, 0.1)` → spans y `[-0.9, 0]`, z `[6.9, 7.0]`
  - Upper/Lower seam is now a clean y = `0` across every wall on these 3 files (side walls and back wall alike), and `Lower_Big`/`BackWall_Upper`/`BackWall_Lower` all meet flush at z = `6.9` with no gap or overlap.
  - Still outstanding: the same `BackWall` split on `LiftRoom_1x2x6_Grade1.prefab` and the 6 orphaned Grade‑2/3 `DefaultRoom` files (see §7) — those still have the original single `BackWall` piece and old, unrounded side-wall values. Their gap/seam numbers must be computed from each file's own current values, not copied from this sample.

## 2. Feature A — Dynamic wall filler

Add `LeftWall_Filler`/`RightWall_Filler` to each of the 10 structural prefabs: a `Cube` mesh matching its side's `Lower_Big`/`Lower_Small` siblings in Y-position, Y-scale, and collider setup, with Z position/scale computed to exactly plug the gap between them. A closed doorway reads as one unbroken wall band.

### Adjacency recompute — centralized, not per-call-site

Rather than adding a doorway-refresh call to `BuildModeController`, `RoomTransitionService`, and a brand-new hook in `DeleteModeController` separately, hook directly into the two existing chokepoints in `BaseLayoutManager`: `RegisterRoomOnFloor` and `UnregisterRoomOnFloor`. **No changes needed to `BuildModeController.cs`, `RoomTransitionService.cs`, or `DeleteModeController.cs` at all** — a deliberately narrow blast radius given how many pathing/click bugs have already come from touching adjacent systems in this project.

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

Call `RequestDoorwayRefresh(floorIndex)` at the end of both `RegisterRoomOnFloor` and `UnregisterRoomOnFloor`. This single mechanism covers runtime placement, merge/upgrade swaps, deletion, and scene-load/hand-placed rooms for free — see the original doc's §4 walkthrough for why each case falls out of it automatically (unchanged from before, not repeated here).

Toggling is instant (`SetActive`), no slide/fade animation — confirmed decision.

### Entrance Room exception

**Correction to the original design doc**, which assumed the entrance's outward-facing edge (left, per the "higher X = visually left" convention — see `BaseLayoutManager.EntranceLeftEdgeX`) should stay permanently *sealed* since nothing is ever registered there. Found via playtest to be backwards: that side always leads to the gate/surface, so it must always read as a real, open doorway (filler hidden, frame shown), not a sealed wall. Fixed with a targeted override rather than teaching `BaseLayoutManager` a `room is EntranceRoom` special case — `SetLeftDoorwayOpen`/`SetRightDoorwayOpen` on `RoomBase` are now `virtual`, and `EntranceRoom` overrides just the left one to force `true` regardless of what the generic adjacency check computes:

```csharp
// EntranceRoom.cs
public override void SetLeftDoorwayOpen(bool open)
{
    base.SetLeftDoorwayOpen(true);
}
```

Mirrors the existing `EntranceRoom.CanBeDeleted()` override pattern (special-case behavior lives on the specific room type, not scattered into a manager). The right side (where rooms actually get built into the base) is untouched and behaves like any other room's edge.

## 3. Feature B — Decorative door frames

Unlike the wall filler, the frame is owned per **individual** room prefab, not the 10 shared structural files, since each room type should eventually look different. That means it touches all ~76 individual room-type prefabs (every `{RoomType}_{width}x2x6_Grade{n}.prefab`, plus `LiftRoom_1x2x6_Grade1.prefab`) — each needs its own `LeftDoorFrame`/`RightDoorFrame` GameObject directly under its own root, not under the nested structural instance.

- **Positioning anchor**: each prefab's existing `LeftEntrance`/`RightEntrance` marker Transform (already sitting right at the doorway, and freshly trustworthy since `RoomReferenceFixer.cs` fixed cross-prefab reference corruption on these exact fields yesterday).
- **Half-frame model**: each room independently contributes one full frame object anchored to its own entrance marker. Two adjacent rooms' entrance markers already coincide at the shared boundary (required for pathing to line up at all), so both rooms' frames naturally align there — no cross-file coordination needed, exactly mirroring how `LeftWall_*`/`RightWall_*` pieces from two different rooms already sit coincident at a shared boundary.
- **Placeholder geometry**: each `LeftDoorFrame`/`RightDoorFrame` is a single parent `GameObject` (initially inactive) containing three simple `Cube` children forming a plain border (two side posts + a lintel), reusing the adjacent wall's material for now. Toggling the parent's `SetActive` cascades to all three children. Real per-room-type art later just replaces what's inside that same named parent — the runtime lookup and toggle logic never change.
- **Rollout mechanics**: given the placeholder is generic and derived the same way (anchored to each prefab's own entrance marker) across all 76 files, this is a good fit for a small bulk Editor tool — the same strategy already validated for yesterday's merge/upgrade retrofit (`RoomDataGenerator.cs`, `RoomReferenceFixer.cs`). A new tool (e.g. `Burrowscape/Add Door Frame Placeholders`) would iterate every room prefab, read its entrance marker, and inject the placeholder relative to it — skipping any prefab whose frame parent no longer contains exactly the 3 placeholder-named children (i.e. already hand-customized), so re-running it later doesn't clobber real art.

Confirmed decisions: placeholder frame everywhere now (real art later, swapped without touching logic), full ~76-file rollout in this pass, half-frame-per-side model.

## 4. Feature C — Room wall/ceiling/floor theming

Each room *type* gets a consistent color (all 9 Gardens green, all 9 Bedrooms blue, etc. — same color across every width/grade of that type), with the wall split into a lighter **upper** tone and darker **lower** tone. This is **not** achieved by unpacking `DefaultRoom` or by hand-overriding materials on ~72 individual nested-instance files — both of those were considered and rejected:

- **Unpacking/severing the `DefaultRoom` link** would mean every future structural change (including the wall filler and `BackWall` split in this very doc) has to be manually re-applied to ~76 files forever instead of 10. Rejected.
- **Per-instance material overrides** (Unity's standard nested-prefab-override mechanism) would work without severing anything, but since each width/grade combination of a room type is a *separate* nested `PrefabInstance`, full coverage still means touching ~9 files per room type by hand. Workable but tedious and easy to miss a tier.

### Chosen approach: a central `RoomThemeCatalog`, applied at runtime by name

```csharp
[CreateAssetMenu(menuName = "Burrowscape/Room Theme Catalog")]
public class RoomThemeCatalog : ScriptableObject
{
    [SerializeField] private List<RoomTheme> themes = new List<RoomTheme>();

    public RoomTheme GetTheme(string roomTypeId) =>
        themes.Find(t => t.roomTypeId == roomTypeId);
}

[System.Serializable]
public class RoomTheme
{
    public string roomTypeId;       // must match RoomBase.RoomTypeId, e.g. "Garden"
    public Material upperWallMaterial;
    public Material lowerWallMaterial;
    public Material ceilingMaterial;
    public Material floorMaterial;
}
```

One asset (`RoomThemeCatalog.asset`, placed in a `Resources` folder so it can be loaded by name at runtime without any per-prefab reference) holds every room type's colors in one place. Adding or changing a room type's palette is one entry in one asset — **zero prefab file edits**, for any number of room types.

`RoomBase` looks itself up by its own `RoomTypeId` once in `Awake` and applies materials to whichever of its own renderers it finds, using a simple, fully general name rule (no per-piece special-casing, and it automatically covers the new filler and split `BackWall` pieces too since they follow the same naming convention):

```csharp
private void ApplyRoomTheme()
{
    RoomTheme theme = RoomThemeCatalog.Load()?.GetTheme(roomTypeId);
    if (theme == null) return; // ungraded/un-themed room types just keep the prefab's baked-in default material

    foreach (var renderer in GetComponentsInChildren<Renderer>(true))
    {
        string n = renderer.gameObject.name;
        if (n.Contains("Wall") && n.Contains("Upper"))       renderer.sharedMaterial = theme.upperWallMaterial;
        else if (n.Contains("Wall"))                          renderer.sharedMaterial = theme.lowerWallMaterial; // Lower_Big, Lower_Small, Filler
        else if (n.Contains("Ceiling"))                       renderer.sharedMaterial = theme.ceilingMaterial;
        else if (n.Contains("Floor"))                         renderer.sharedMaterial = theme.floorMaterial; // LiftRoom's own "Floor_Front"/"Floor _Back" included
    }
}
```

Because this matches by substring rather than an exact list of piece names, `LeftWall_Filler`/`RightWall_Filler` automatically pick up the lower-band color the moment they're added (no extra theming work), and `BackWall_Upper`/`BackWall_Lower` (see §5) automatically pick up upper/lower the same way `LeftWall_Upper`/`Lower` do. `LeftDoorFrame`/`RightDoorFrame` don't contain `"Wall"` and aren't named `Ceiling`/`Floor`, so they're correctly left untouched by this pass — they keep whatever material their own custom art uses, exactly as intended in §3. Floor/Ceiling were originally an exact-name match, but `LiftRoom_1x2x6_Grade1.prefab` is the one prefab in the whole catalog that splits its floor into two pieces (`Floor_Front`/`Floor _Back`, to leave a gap for the elevator car) rather than one plain `Floor` — found via playtest, fixed by switching both to substring matching, same as the wall pieces.

`sharedMaterial` (not `material`) is used deliberately — it assigns the same Material *asset* to every instance of that room type rather than cloning a new Material per room, keeping batching intact and letting you tweak "Garden Green (Light)" once and have every Garden update.

### What you'll need to author

Real `Material` assets — at minimum one light/dark pair per themed room type, plus ceiling/floor materials (can reuse the lower-wall material for ceiling/floor if you don't want yet another shade to manage). This is genuine content work, not something I can generate meaningfully on your behalf beyond picking a placeholder color.

## 5. Feature D — `BackWall` split

**Already done for the 3 Grade‑1 default rooms** (4/8/12-wide) — completed by hand ahead of implementation. Rather than just bisecting the original single piece, the split was matched to the side walls' own Y-values, which incidentally fixed a pre-existing overlap between the old `BackWall` and the side walls' `Lower_Big` pieces (see §1 for current exact values). Still needed on the 6 orphaned Grade‑2/3 `DefaultRoom` files.

**Intentionally skipped on `LiftRoom_1x2x6_Grade1.prefab`** — the elevator car permanently occludes the back wall in that room, so there's no player-visible payoff for splitting it. `LiftRoom` still gets the wall filler (§2) and still gets a theme entry if desired, just not this particular split; its `BackWall` stays a single piece, which will fall into the generic "lower" bucket of the theming rule below (contains `"Wall"`, not `"Upper"`) rather than showing a two-tone back wall. That's fine — it's simply invisible either way.

- **No script depends on `BackWall`'s name or piece count** — nothing in `RoomBase` or elsewhere references it, so splitting it breaks nothing at the code level.
- **No functional gap is introduced** — unlike the side walls' intentional doorway gap, the two `BackWall` pieces touch with zero space between them (confirmed flush at z = `6.9` on the completed files).
- **Seam height should match the side walls' Upper/Lower seam** rather than an arbitrary 50/50 split, so the color line stays visually continuous around the corner where the back wall meets the side walls — confirmed on the completed files (both now split at a clean y = `0`).
- **Naming makes it fall out of the theming rule below for free** — `BackWall_Upper` contains `"Wall"` and `"Upper"`, `BackWall_Lower` contains `"Wall"` without `"Upper"`, so `ApplyRoomTheme()` colors them correctly with no extra code.
- Ceiling matching the upper wall tone is just setting the same `Material` asset in both the `ceilingMaterial` and `upperWallMaterial` slots of a given `RoomTheme` entry — no code change needed.

## 6. Consolidated runtime design on `RoomBase`

```csharp
private GameObject leftWallFiller;
private GameObject rightWallFiller;
private GameObject leftDoorFrame;
private GameObject rightDoorFrame;

private void Awake()
{
    ResolveDoorwayPieces();
    ApplyRoomTheme();
}

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

(`ApplyRoomTheme()` as shown in §4.) All four features share the same foundation — resolve-by-name at `Awake`, no serialized references, no changes to `BuildModeController`/`RoomTransitionService`/`DeleteModeController`. `LiftRoom` inherits everything for free as a `RoomBase` subclass; its own prefab just needs the same pieces added at its narrower scale. Both `?.` calls and the `theme == null` early-return mean any of these four features can be partially rolled out (e.g. a prefab with a filler but no frame yet, or a room type with no theme entry yet) without breaking anything else.

## 7. Prefab / asset editing plan

**10 structural files** — add `LeftWall_Filler`/`RightWall_Filler`, and split `BackWall` into `BackWall_Upper`/`BackWall_Lower` where not already done (all transform values computed per-file from that file's own existing sibling values, not copied across files):

1. `Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade1.prefab` — **`BackWall` split ✅ done**, filler still needed
2. `Assets/Prefabs/Rooms/Grade 1 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade1.prefab` — **`BackWall` split ✅ done**, filler still needed
3. `Assets/Prefabs/Rooms/Grade 1 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade1.prefab` — **`BackWall` split ✅ done**, filler still needed
4. `Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/LiftRoom_1x2x6_Grade1.prefab` — **`BackWall` split intentionally skipped** (occluded by the elevator car), filler still needed
5. `Assets/Prefabs/Rooms/Grade 2 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade2.prefab` — split + filler both still needed
6. `Assets/Prefabs/Rooms/Grade 3 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade3.prefab` — split + filler both still needed
7. `Assets/Prefabs/Rooms/Grade 2 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade2.prefab` — split + filler both still needed
8. `Assets/Prefabs/Rooms/Grade 3 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade3.prefab` — split + filler both still needed
9. `Assets/Prefabs/Rooms/Grade 2 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade2.prefab` — split + filler both still needed
10. `Assets/Prefabs/Rooms/Grade 3 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade3.prefab` — split + filler both still needed

**~76 individual room-type files** — add a placeholder `LeftDoorFrame`/`RightDoorFrame`, via a new bulk Editor tool rather than by hand (see §3).

**1 new asset** — `RoomThemeCatalog.asset` in a `Resources` folder, populated with one `RoomTheme` entry per room type you want colored (un-populated types simply keep the structural prefab's baked-in default material, so this can grow incrementally).

**N new Material assets** — light/dark wall pair (+ ceiling/floor, optional reuse) per themed room type. Real content work, not automatable.

No changes needed to `BuildModeController.cs`, `RoomTransitionService.cs`, or `DeleteModeController.cs` for any of the four features.

## 8. What you need to do in Unity

**Before implementation:**
- You have uncommitted changes to `NPCBunny.cs` and `RoomTransitionService.cs` (unrelated to this feature per `git status`, likely left over from yesterday's merge/upgrade bug fixes). Worth committing or stashing those first so this feature's diff stays clean.
- Optional: confirm whether a "Default Room" (as opposed to Garden/Cafeteria/etc.) is ever actually player-reachable via `BuildMenuUI`/upgrade config — if provably never reachable, files 5–10 in §7 can be skipped. Not required.
- Decide on your initial palette (which room types get themed first, light/dark shades) so the `RoomThemeCatalog` asset and materials can be authored alongside the code.

**After implementation (playtest checklist):**
- Let Unity reimport the edited/generated prefabs; check the 10 structural files for seams at the new filler and `BackWall` split, especially at the corner where the color line should stay continuous.
- In Build Mode, place a chain of rooms and confirm doorways open/close correctly, and the placeholder frame appears/disappears in sync (opposite the filler) on both sides.
- Check a placed pair of adjacent rooms up close for frame/frame overlap where both sides' placeholder frames coincide at the shared boundary.
- Delete a middle room from a chain and confirm former neighbors reseal (filler back, frame gone) correctly.
- Confirm hand-placed starting rooms (entrance room, any starting layout) come up with correct doorway state and correct theme color on Play, not just runtime-built ones.
- Confirm bunny pathing through open doorways still works with no regressions, including walking near/through the new frame posts.
- Spot-check a themed room type (e.g. Garden) at multiple widths/grades to confirm the color is consistent everywhere, and that the upper/lower split lines up visually at the back-wall corners.
- Spot-check a `LiftRoom` segment at the edge of a floor (seals correctly) and with neighbors on both sides (opens correctly).

## 9. Open items

- ~~Whether the 6 orphaned Grade‑2/3 `DefaultRoom` prefabs are actually player-reachable is unconfirmed (see §7, item 5–10) — recommend editing them anyway since it's cheap insurance.~~ **Resolved 2026-07-18**: confirmed never buildable by the player. Left unsplit (no `BackWall_Upper`/`Lower`) and without a door frame — no further work needed on them.

## 10. Status

All four features (wall filler, door frames, room theming, `BackWall` split) implemented, playtested, and confirmed working as of 2026-07-18, including the Entrance Room and Lift edge cases fixed along the way (§2's Entrance Room exception, and the `Floor`/`Ceiling` substring-matching fix for `LiftRoom`'s split floor). No open items remain.
- `BackWall_Upper`/`BackWall_Lower`'s exact seam height should be computed per structural file from that file's own side-wall seam, not assumed identical to the 4-wide Grade‑1 sample.
- `RoomThemeCatalog`'s load mechanism (`Resources.Load`) requires the asset to live in a folder literally named `Resources` somewhere under `Assets` — a one-time Editor-side placement detail to get right when the asset is first created.
