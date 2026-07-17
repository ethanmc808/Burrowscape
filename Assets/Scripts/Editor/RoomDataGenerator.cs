using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

// One-time bulk data-entry tool for the room merge/upgrade system (see RoomMergeUpgrade_DesignDoc.md
// at the project root). Every room prefab's filename already encodes its RoomTypeId, width, and Grade
// ("{Type}_{Width}x2x6_Grade{N}.prefab"), so this infers all three instead of requiring each prefab's
// RoomTypeId and each RoomDefinition asset to be hand-entered — a large, repetitive, typo-prone task
// once every Grade/width combination is counted (75+ prefabs, 65+ RoomDefinition assets as of the
// Grade 2/3 authoring pass).
//
// Safe to re-run: matches existing RoomDefinition assets by their `prefab` OBJECT REFERENCE, not by
// asset name (a few of the original assets were named independently of their prefab — e.g. the Water
// Room's definition is still named "WaterHouse_..." from an older naming pass), so running this again
// after adding more prefabs only fills in what's new/missing rather than creating duplicates.
public static class RoomDataGenerator
{
    private static readonly Regex FileNamePattern = new Regex(@"^(?<type>[A-Za-z]+)_(?<width>\d+)x2x6_Grade(?<grade>\d+)$", RegexOptions.Compiled);

    private const string RoomsFolder = "Assets/Prefabs/Rooms";
    private const string DefinitionsFolder = "Assets/Data/Room Definitions";

    [MenuItem("Burrowscape/Generate Room Type Ids and Definitions")]
    public static void Generate()
    {
        List<RoomDefinition> existingDefinitions = LoadAllDefinitions();

        int roomTypeIdsSet = 0;
        int footprintWidthsSet = 0;
        int definitionsCreated = 0;
        int definitionsUpdated = 0;
        List<string> skipped = new List<string>();

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder });

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            Match match = FileNamePattern.Match(fileName);
            if (!match.Success)
            {
                skipped.Add($"{fileName} (doesn't match the Type_WIDTHx2x6_GradeN filename pattern)");
                continue;
            }

            string typeId = match.Groups["type"].Value;
            int width = int.Parse(match.Groups["width"].Value);
            int grade = int.Parse(match.Groups["grade"].Value);

            // Lift never merges or upgrades (it only extends vertically) — no RoomTypeId, and it
            // already has its one buildable Grade-1 RoomDefinition, so nothing else to do for it.
            if (typeId == "LiftRoom") continue;

            RoomBase roomBase = prefab.GetComponent<RoomBase>();
            if (roomBase == null)
            {
                skipped.Add($"{fileName} (no RoomBase-derived component attached — add one first, then re-run)");
                continue;
            }

            bool prefabChanged = false;
            if (SetRoomTypeId(roomBase, typeId))
            {
                roomTypeIdsSet++;
                prefabChanged = true;
            }
            if (SetFootprintWidth(roomBase, width))
            {
                footprintWidthsSet++;
                prefabChanged = true;
            }
            if (prefabChanged)
                PrefabUtility.SavePrefabAsset(prefab);

            RoomDefinition existing = existingDefinitions.FirstOrDefault(d => d != null && d.prefab == prefab);
            if (existing != null)
            {
                if (UpdateDefinition(existing, prefab, typeId, width, grade))
                    definitionsUpdated++;
            }
            else
            {
                CreateDefinition(prefab, fileName, typeId, width, grade);
                definitionsCreated++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int registryCount = PopulateCatalogRegistryIfPresent();

        string summary = $"RoomDataGenerator: set {roomTypeIdsSet} RoomTypeId(s), corrected {footprintWidthsSet} FootprintWidth(s), created {definitionsCreated} new RoomDefinition asset(s), updated {definitionsUpdated} existing one(s)"
            + (registryCount > 0 ? $", added {registryCount} to the open scene's RoomCatalogRegistry (remember to save the scene)." : ", RoomCatalogRegistry not found in the open scene (skipped — add one and re-run, or drag the assets in by hand).");

        if (skipped.Count > 0)
            summary += $"\nSkipped {skipped.Count}:\n- {string.Join("\n- ", skipped)}";

        Debug.Log(summary);
    }

    private static List<RoomDefinition> LoadAllDefinitions()
    {
        string[] guids = AssetDatabase.FindAssets("t:RoomDefinition", new[] { DefinitionsFolder });
        return guids.Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
    }

    private static bool SetRoomTypeId(RoomBase roomBase, string typeId)
    {
        SerializedObject so = new SerializedObject(roomBase);
        SerializedProperty prop = so.FindProperty("roomTypeId");
        if (prop.stringValue == typeId) return false;

        prop.stringValue = typeId;
        so.ApplyModifiedProperties();
        return true;
    }

    // Derived from the same filename-parsed width used for the RoomDefinition's footprint — this is
    // the fix for the real gap that caused the merge/upgrade collider generation to go wrong: a prefab
    // duplicated from a different width (e.g. an 8-wide Garden made by copying the 4-wide one) doesn't
    // get this field updated just by resizing the mesh, since nothing about it is visually obvious in
    // the Inspector. LiftRoom overrides FootprintWidth's getter entirely (always 1, regardless of this
    // backing field), so this is a harmless no-op for it even though it's never actually called there
    // (see the LiftRoom skip earlier in Generate()).
    private static bool SetFootprintWidth(RoomBase roomBase, int width)
    {
        SerializedObject so = new SerializedObject(roomBase);
        SerializedProperty prop = so.FindProperty("footprintWidth");
        if (Mathf.Approximately(prop.floatValue, width)) return false;

        prop.floatValue = width;
        so.ApplyModifiedProperties();
        return true;
    }

    private static bool UpdateDefinition(RoomDefinition def, GameObject prefab, string typeId, int width, int grade)
    {
        Vector3Int footprint = new Vector3Int(width, 2, 6);
        bool changed = false;

        if (def.prefab != prefab) { def.prefab = prefab; changed = true; }
        if (def.footprint != footprint) { def.footprint = footprint; changed = true; }
        if (def.roomTypeId != typeId) { def.roomTypeId = typeId; changed = true; }
        if (def.grade != grade) { def.grade = grade; changed = true; }

        if (changed) EditorUtility.SetDirty(def);
        return changed;
    }

    // goldCost/displayName/icon are creative decisions, not mechanical ones — left for manual tuning.
    // displayName gets a readable placeholder so the asset isn't blank in the Inspector in the meantime.
    private static void CreateDefinition(GameObject prefab, string fileName, string typeId, int width, int grade)
    {
        RoomDefinition def = ScriptableObject.CreateInstance<RoomDefinition>();
        def.prefab = prefab;
        def.footprint = new Vector3Int(width, 2, 6);
        def.roomTypeId = typeId;
        def.grade = grade;
        def.displayName = ObjectNames.NicifyVariableName(typeId);

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DefinitionsFolder}/{fileName}.asset");
        AssetDatabase.CreateAsset(def, assetPath);
    }

    // Adds RoomUpgradeClickHandler to every room prefab except Lift (which never upgrades), and a
    // BoxCollider first if one doesn't already exist on the root GameObject — RoomUpgradeClickHandler's
    // [RequireComponent(typeof(Collider))] can't auto-satisfy itself since Collider is abstract, so this
    // has to happen explicitly, in this order, or AddComponent leaves the prefab without one.
    // Collider dimensions are copied from a same-width Garden prefab rather than computed from scratch —
    // Garden already has a working RoomClickHandler at every width today, confirming its BoxCollider is
    // correctly sized and actually on the root GameObject (not a child mesh piece), so it's a known-good
    // template rather than a guess at footprint/pivot geometry.
    [MenuItem("Burrowscape/Add Upgrade Click Handlers")]
    public static void AddUpgradeClickHandlers()
    {
        List<GameObject> allRoomPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(p => p != null)
            .ToList();

        Dictionary<int, BoxCollider> colliderTemplates = new Dictionary<int, BoxCollider>();

        // Seed from Garden first (known-good at every width — see comment above).
        foreach (GameObject prefab in allRoomPrefabs)
        {
            if (!prefab.name.StartsWith("Garden_")) continue;
            RoomBase roomBase = prefab.GetComponent<RoomBase>();
            BoxCollider existing = prefab.GetComponent<BoxCollider>();
            if (roomBase == null || existing == null) continue;

            colliderTemplates[Mathf.RoundToInt(roomBase.FootprintWidth)] = existing;
        }

        // Fallback: any other room with an existing root-level Collider, for any width Garden didn't cover.
        foreach (GameObject prefab in allRoomPrefabs)
        {
            RoomBase roomBase = prefab.GetComponent<RoomBase>();
            BoxCollider existing = prefab.GetComponent<BoxCollider>();
            if (roomBase == null || existing == null || prefab.GetComponent<LiftRoom>() != null) continue;

            int width = Mathf.RoundToInt(roomBase.FootprintWidth);
            if (!colliderTemplates.ContainsKey(width))
                colliderTemplates[width] = existing;
        }

        int collidersAdded = 0;
        int handlersAdded = 0;
        List<string> skipped = new List<string>();

        foreach (GameObject prefab in allRoomPrefabs)
        {
            RoomBase roomBase = prefab.GetComponent<RoomBase>();
            if (roomBase == null || prefab.GetComponent<LiftRoom>() != null) continue;

            bool changed = false;

            if (prefab.GetComponent<Collider>() == null)
            {
                int width = Mathf.RoundToInt(roomBase.FootprintWidth);
                if (!colliderTemplates.TryGetValue(width, out BoxCollider template))
                {
                    skipped.Add($"{prefab.name} (no Collider, and no same-width reference room to copy one from)");
                    continue;
                }

                BoxCollider newCollider = prefab.AddComponent<BoxCollider>();
                newCollider.size = template.size;
                newCollider.center = template.center;
                newCollider.isTrigger = template.isTrigger;
                collidersAdded++;
                changed = true;
            }

            if (prefab.GetComponent<RoomUpgradeClickHandler>() == null)
            {
                prefab.AddComponent<RoomUpgradeClickHandler>();
                handlersAdded++;
                changed = true;
            }

            if (changed)
                PrefabUtility.SavePrefabAsset(prefab);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"RoomDataGenerator: added {collidersAdded} BoxCollider(s) and {handlersAdded} RoomUpgradeClickHandler(s).";
        if (skipped.Count > 0)
            summary += $"\nSkipped {skipped.Count}:\n- {string.Join("\n- ", skipped)}";

        Debug.Log(summary);
    }

    // Best-effort: only touches the currently open scene, and only if a RoomCatalogRegistry is already
    // placed in it. Safe to skip if not — re-run this tool after adding one, or drag the assets in by
    // hand; either way nothing here is required for the generation above to have worked correctly.
    private static int PopulateCatalogRegistryIfPresent()
    {
        RoomCatalogRegistry registry = Object.FindAnyObjectByType<RoomCatalogRegistry>();
        if (registry == null) return 0;

        SerializedObject so = new SerializedObject(registry);
        SerializedProperty listProp = so.FindProperty("allDefinitions");

        HashSet<RoomDefinition> already = new HashSet<RoomDefinition>();
        for (int i = 0; i < listProp.arraySize; i++)
            already.Add(listProp.GetArrayElementAtIndex(i).objectReferenceValue as RoomDefinition);

        int added = 0;
        foreach (RoomDefinition def in LoadAllDefinitions())
        {
            if (def == null || already.Contains(def)) continue;

            listProp.arraySize++;
            listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = def;
            already.Add(def);
            added++;
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);
        }

        return added;
    }
}
