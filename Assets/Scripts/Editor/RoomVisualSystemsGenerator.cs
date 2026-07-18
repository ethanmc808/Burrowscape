using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// Bulk Editor tooling for Docs/RoomVisualSystems_Design.md — same LoadPrefabContents/SaveAsPrefabAsset
// strategy as RoomReferenceFixer.cs/RoomDataGenerator.cs, for the same reason: doing this by hand across
// 10-76 prefabs is the actual bottleneck, not the underlying logic. Every method here is safe to re-run
// (each checks for its own already-added marker object before doing anything).
public static class RoomVisualSystemsGenerator
{
    private const string RoomsFolder = "Assets/Prefabs/Rooms";

    // The 10 prefabs that actually own real wall/ceiling/floor geometry — everything else nests one of
    // these. See Docs/RoomVisualSystems_Design.md §1.
    private static readonly string[] StructuralPrefabPaths =
    {
        "Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/LiftRoom_1x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 2 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade2.prefab",
        "Assets/Prefabs/Rooms/Grade 3 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade3.prefab",
        "Assets/Prefabs/Rooms/Grade 2 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade2.prefab",
        "Assets/Prefabs/Rooms/Grade 3 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade3.prefab",
        "Assets/Prefabs/Rooms/Grade 2 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade2.prefab",
        "Assets/Prefabs/Rooms/Grade 3 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade3.prefab",
    };

    // Already split by hand before this tool existed — see project memory / design doc §1.
    private static readonly HashSet<string> AlreadySplitBackWall = new HashSet<string>
    {
        "Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade1.prefab",
    };

    // Intentionally never split — the elevator car permanently occludes the Lift's back wall. Don't
    // "fix" this later; see Docs/RoomVisualSystems_Design.md §5.
    private const string LiftRoomPath = "Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/LiftRoom_1x2x6_Grade1.prefab";

    // The 3 Grade-1 DefaultRoom files are nested by all 66 dependent room-type prefabs (see §1) — unlike
    // the wall filler, where that propagation is exactly the point, a door frame added directly to one of
    // these 3 files would automatically propagate into every Garden/Bedroom/Kitchen/etc. instance that
    // nests it, silently giving all of them the SAME shared frame object instead of their own independently
    // editable one. That defeats Feature B's entire reason for living on individual prefabs rather than the
    // shared structure, so these 3 are deliberately never given a door frame by this tool.
    private static readonly HashSet<string> ExcludeFromDoorFrames = new HashSet<string>
    {
        "Assets/Prefabs/Rooms/Grade 1 Rooms/1 Room Wide/DefaultRoom_4x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/2 Rooms Wide/DefaultRoom_8x2x6_Grade1.prefab",
        "Assets/Prefabs/Rooms/Grade 1 Rooms/3 Rooms Wide/DefaultRoom_12x2x6_Grade1.prefab",
    };

    // ---------- 1. WALL FILLER (Feature A) ----------

    [MenuItem("Burrowscape/Room Visuals/1 - Add Wall Filler Pieces")]
    public static void AddWallFillerPieces()
    {
        int prefabsChanged = 0;
        List<string> skipped = new List<string>();

        foreach (string path in StructuralPrefabPaths)
        {
            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changedLeft = AddFillerForSide(contentRoot, "Left", path, skipped);
                bool changedRight = AddFillerForSide(contentRoot, "Right", path, skipped);

                if (changedLeft || changedRight)
                {
                    PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                    prefabsChanged++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"RoomVisualSystemsGenerator: added wall filler pieces to {prefabsChanged} prefab(s).";
        if (skipped.Count > 0)
            summary += $"\n\n{skipped.Count} skipped:\n- {string.Join("\n- ", skipped)}";
        Debug.Log(summary);
        EditorUtility.DisplayDialog("Add Wall Filler Pieces", summary, "OK");
    }

    // Computes the doorway gap directly from this file's own Lower_Big/Lower_Small siblings (never a
    // hardcoded sample value — every structural file has its own slightly different numbers) and plugs
    // it with a piece matching their Y-position/scale, X-position, and material.
    private static bool AddFillerForSide(GameObject root, string side, string path, List<string> skipped)
    {
        string fillerName = $"{side}Wall_Filler";
        if (FindChild(root, fillerName) != null) return false; // already present — safe to re-run

        Transform bigT = FindChild(root, $"{side}Wall_Lower_Big");
        Transform smallT = FindChild(root, $"{side}Wall_Lower_Small");
        if (bigT == null || smallT == null)
        {
            skipped.Add($"{path}: missing {side}Wall_Lower_Big/Small, can't compute the gap");
            return false;
        }

        GetZSpan(bigT, out float bigMin, out float bigMax);
        GetZSpan(smallT, out float smallMin, out float smallMax);

        float nearMax, farMin;
        if (smallMin < bigMin) { nearMax = smallMax; farMin = bigMin; }
        else { nearMax = bigMax; farMin = smallMin; }

        float gapCenter = (nearMax + farMin) / 2f;
        float gapLength = farMin - nearMax;

        if (gapLength <= 0f)
        {
            skipped.Add($"{path}: {side}Wall_Lower_Big/Small don't leave a positive gap (got {gapLength:F4}) — geometry may already differ from what this tool expects");
            return false;
        }

        GameObject filler = GameObject.CreatePrimitive(PrimitiveType.Cube);
        filler.name = fillerName;
        filler.transform.SetParent(root.transform, false);
        filler.transform.localPosition = new Vector3(bigT.localPosition.x, bigT.localPosition.y, gapCenter);
        filler.transform.localScale = new Vector3(bigT.localScale.x, bigT.localScale.y, gapLength);
        filler.transform.localRotation = Quaternion.identity;

        Renderer bigRenderer = bigT.GetComponent<Renderer>();
        Renderer fillerRenderer = filler.GetComponent<Renderer>();
        if (bigRenderer != null && fillerRenderer != null)
            fillerRenderer.sharedMaterial = bigRenderer.sharedMaterial;

        return true;
    }

    private static void GetZSpan(Transform t, out float min, out float max)
    {
        float half = Mathf.Abs(t.localScale.z) / 2f;
        min = t.localPosition.z - half;
        max = t.localPosition.z + half;
    }

    // ---------- 2. BACKWALL SPLIT ON REMAINING FILES (Feature D) ----------

    [MenuItem("Burrowscape/Room Visuals/2 - Split Remaining Back Walls")]
    public static void SplitRemainingBackWalls()
    {
        List<string> allPrefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();

        int splitCount = 0;
        List<string> skipped = new List<string>();

        foreach (string path in allPrefabPaths)
        {
            if (AlreadySplitBackWall.Contains(path)) continue;
            if (path == LiftRoomPath) continue; // intentionally never split

            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform backWall = FindChild(contentRoot, "BackWall");
                // No local BackWall means this prefab either already has BackWall_Upper/Lower, or nests
                // its wall geometry from another prefab (in which case that prefab is handled separately
                // when this same method reaches it in this loop) — either way, nothing to do here.
                if (backWall == null) continue;

                Transform upperRef = FindChild(contentRoot, "LeftWall_Upper");
                Transform lowerRef = FindChild(contentRoot, "LeftWall_Lower_Small");
                if (upperRef == null || lowerRef == null)
                {
                    skipped.Add($"{path}: has its own BackWall but no LeftWall_Upper/Lower_Small to derive a matching seam height from");
                    continue;
                }

                // Seam height taken from THIS file's own existing side walls (never hardcoded), so the
                // back wall's color line stays continuous with the side walls' at the corner.
                float seamY = ((upperRef.localPosition.y - Mathf.Abs(upperRef.localScale.y) / 2f)
                    + (lowerRef.localPosition.y + Mathf.Abs(lowerRef.localScale.y) / 2f)) / 2f;

                float originalTop = backWall.localPosition.y + Mathf.Abs(backWall.localScale.y) / 2f;
                float originalBottom = backWall.localPosition.y - Mathf.Abs(backWall.localScale.y) / 2f;

                if (seamY <= originalBottom || seamY >= originalTop)
                {
                    skipped.Add($"{path}: derived seam Y ({seamY:F4}) falls outside BackWall's own span [{originalBottom:F4}, {originalTop:F4}] — skipped rather than guess");
                    continue;
                }

                // X/Z position and scale are deliberately left untouched on both halves — this is a pure
                // vertical bisection that exactly reproduces the original piece's combined footprint, not
                // an attempt to also replicate the extra overlap-fix the user separately did by hand on
                // the 3 Grade-1 files (see design doc §1) — safer for files nobody has visually verified.
                float origX = backWall.localPosition.x;
                float origZ = backWall.localPosition.z;
                float origScaleX = backWall.localScale.x;
                float origScaleZ = backWall.localScale.z;

                GameObject backWallGO = backWall.gameObject;
                backWallGO.name = "BackWall_Upper";
                backWall.localPosition = new Vector3(origX, (seamY + originalTop) / 2f, origZ);
                backWall.localScale = new Vector3(origScaleX, originalTop - seamY, origScaleZ);

                GameObject lowerGO = Object.Instantiate(backWallGO, contentRoot.transform);
                lowerGO.name = "BackWall_Lower";
                lowerGO.transform.localPosition = new Vector3(origX, (originalBottom + seamY) / 2f, origZ);
                lowerGO.transform.localScale = new Vector3(origScaleX, seamY - originalBottom, origScaleZ);
                lowerGO.transform.localRotation = Quaternion.identity;

                PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                splitCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"RoomVisualSystemsGenerator: split BackWall on {splitCount} prefab(s).";
        if (skipped.Count > 0)
            summary += $"\n\n{skipped.Count} skipped:\n- {string.Join("\n- ", skipped)}";
        Debug.Log(summary);
        EditorUtility.DisplayDialog("Split Remaining Back Walls", summary, "OK");
    }

    // ---------- 3. DOOR FRAME PLACEHOLDERS (Feature B) ----------

    private const float FrameHeight = 0.9f;
    private const float FrameHalfGap = 0.35f;
    private const float PostThicknessX = 0.12f;
    private const float PostThicknessZ = 0.08f;
    private const float LintelThicknessY = 0.1f;

    [MenuItem("Burrowscape/Room Visuals/3 - Add Door Frame Placeholders")]
    public static void AddDoorFramePlaceholders()
    {
        List<string> allPrefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();

        int prefabsChanged = 0;
        int framesAdded = 0;
        List<string> skipped = new List<string>();

        foreach (string path in allPrefabPaths)
        {
            if (ExcludeFromDoorFrames.Contains(path)) continue;

            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changedLeft = AddFrameForSide(contentRoot, "Left", path, skipped, ref framesAdded);
                bool changedRight = AddFrameForSide(contentRoot, "Right", path, skipped, ref framesAdded);

                if (changedLeft || changedRight)
                {
                    PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                    prefabsChanged++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"RoomVisualSystemsGenerator: added {framesAdded} door frame placeholder(s) across {prefabsChanged} prefab(s). (Skipped the 3 shared Grade-1 DefaultRoom structural files on purpose — see comment on ExcludeFromDoorFrames.)";
        if (skipped.Count > 0)
            summary += $"\n\n{skipped.Count} skipped:\n- {string.Join("\n- ", skipped)}";
        Debug.Log(summary);
        EditorUtility.DisplayDialog("Add Door Frame Placeholders", summary, "OK");
    }

    // Anchored to this prefab's own LeftEntrance/RightEntrance marker — reachable via GetComponentsInChildren
    // regardless of whether it's a direct child or inherited from a nested DefaultRoom instance, and
    // guaranteed to coincide with a touching neighbor's own entrance marker (see design doc §3's
    // half-frame model). Never touches a prefab that already has this frame — including one that's been
    // hand-customized with real art, since the marker object's mere presence is enough to skip.
    private static bool AddFrameForSide(GameObject root, string side, string path, List<string> skipped, ref int framesAdded)
    {
        string frameName = $"{side}DoorFrame";
        string entranceName = $"{side}Entrance";

        if (FindChild(root, frameName) != null) return false;

        Transform entrance = FindChild(root, entranceName);
        if (entrance == null)
        {
            skipped.Add($"{path}: no {entranceName} to anchor a {frameName} to");
            return false;
        }

        // LeftEntrance/RightEntrance sit near floor height (where a bunny actually walks), not the
        // doorway gap's vertical center — offset upward so the placeholder centers in the opening.
        Vector3 localAnchor = root.transform.InverseTransformPoint(entrance.position) + new Vector3(0f, FrameHeight / 2f, 0f);

        Material wallMaterial = FindChild(root, $"{side}Wall_Upper")?.GetComponent<Renderer>()?.sharedMaterial;

        GameObject frame = new GameObject(frameName);
        frame.transform.SetParent(root.transform, false);
        frame.transform.localPosition = localAnchor;
        frame.transform.localRotation = Quaternion.identity;

        CreateFramePost(frame.transform, "Post_Near", new Vector3(0f, 0f, -FrameHalfGap), wallMaterial);
        CreateFramePost(frame.transform, "Post_Far", new Vector3(0f, 0f, FrameHalfGap), wallMaterial);
        CreateFrameLintel(frame.transform, wallMaterial);

        // Hidden until BaseLayoutManager detects a real neighbor on this side (see RoomBase.SetLeftDoorwayOpen/
        // SetRightDoorwayOpen) — set AFTER adding children so the placeholder's own pieces don't need to
        // each be independently activated later; SetActive(false) on the parent already covers all of them.
        frame.SetActive(false);

        framesAdded++;
        return true;
    }

    private static void CreateFramePost(Transform parent, string name, Vector3 localPos, Material material)
    {
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = name;
        post.transform.SetParent(parent, false);
        post.transform.localPosition = localPos;
        post.transform.localScale = new Vector3(PostThicknessX, FrameHeight, PostThicknessZ);
        if (material != null) post.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void CreateFrameLintel(Transform parent, Material material)
    {
        GameObject lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "Lintel";
        lintel.transform.SetParent(parent, false);
        lintel.transform.localPosition = new Vector3(0f, FrameHeight / 2f, 0f);
        lintel.transform.localScale = new Vector3(PostThicknessX, LintelThicknessY, FrameHalfGap * 2f + PostThicknessZ);
        if (material != null) lintel.GetComponent<Renderer>().sharedMaterial = material;
    }

    // ---------- 4. ROOM THEME CATALOG ASSET (Feature C) ----------

    [MenuItem("Burrowscape/Room Visuals/4 - Create Room Theme Catalog Asset")]
    public static void CreateRoomThemeCatalogAsset()
    {
        const string folder = "Assets/Resources";
        const string assetPath = folder + "/RoomThemeCatalog.asset";

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        if (AssetDatabase.LoadAssetAtPath<RoomThemeCatalog>(assetPath) != null)
        {
            Debug.Log("RoomVisualSystemsGenerator: RoomThemeCatalog.asset already exists, nothing to do.");
            return;
        }

        RoomThemeCatalog catalog = ScriptableObject.CreateInstance<RoomThemeCatalog>();
        AssetDatabase.CreateAsset(catalog, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"RoomVisualSystemsGenerator: created {assetPath}. Add RoomTheme entries in its Inspector to start theming room types.");
    }

    // ---------- SHARED ----------

    private static Transform FindChild(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
