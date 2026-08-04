using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// Fixes a systemic authoring gap found 2026-08-04: RoomClickHandler was only ever added to prefabs for
// room types that implement IJobRoom (Garden, GuardRoom, HospitalRoom, WaterRoom, CoalRoom, EntranceRoom).
// Cafeteria/Bedroom/StorageRoom/LivingRoom never got one at all, since nobody has a job to assign there —
// but RoomClickHandler is also what opens GuardDeployUI/PatientUI (both no-op unless actually relevant,
// see RoomClickHandler's own comment), so those room types ended up with literally no way to click into
// them at all, including no way to manually send a defender when slimes spawned inside one. Same
// LoadPrefabContents/SaveAsPrefabAsset idiom as RoomReferenceFixer — generic over "any RoomBase prefab
// missing the component" rather than a hardcoded room-type list, so it also covers any future room type
// that ends up without a job. Safe to re-run (already-wired prefabs are untouched).
public static class RoomClickHandlerAdder
{
    private const string RoomsFolder = "Assets/Prefabs/Rooms";

    [MenuItem("Burrowscape/Add Missing Room Click Handlers")]
    public static void AddMissingClickHandlers()
    {
        List<string> prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();

        int prefabsFixed = 0;
        List<string> fixedNames = new List<string>();

        foreach (string path in prefabPaths)
        {
            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RoomBase roomBase = contentRoot.GetComponent<RoomBase>();
                if (roomBase == null) continue;
                if (contentRoot.GetComponent<RoomClickHandler>() != null) continue;
                if (contentRoot.GetComponent<Collider>() == null)
                {
                    Debug.LogWarning($"RoomClickHandlerAdder: {path} has no Collider on its root — skipped (RoomClickHandler requires one).");
                    continue;
                }

                contentRoot.AddComponent<RoomClickHandler>();
                PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                prefabsFixed++;
                fixedNames.Add(contentRoot.name);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        string summary = prefabsFixed > 0
            ? $"RoomClickHandlerAdder: added RoomClickHandler to {prefabsFixed} prefab(s):\n- {string.Join("\n- ", fixedNames)}"
            : "RoomClickHandlerAdder: every RoomBase prefab already has a RoomClickHandler — nothing to do.";

        Debug.Log(summary);
        EditorUtility.DisplayDialog("Add Missing Room Click Handlers", summary, "OK");
    }
}
