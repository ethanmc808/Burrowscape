using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Fixes a systemic authoring bug found 2026-07-17: every 8-wide/12-wide (and some Grade 2/3) room
// prefab's entrance/spot/path Transform references were left pointing at the room type's original
// 4x2x6 Grade 1 prefab's own internal children, instead of that prefab's own local children — almost
// certainly from duplicating the 4-wide prefab file without retargeting internal references. Confirmed
// via direct guid comparison across every room prefab (see RoomMergeUpgrade_DesignDoc.md's history /
// project memory). Symptom: a merged/upgraded room's spots read as permanently "occupied" for the rest
// of the play session, since the broken reference actually points at a single shared, persistent
// asset-level object rather than anything cloned fresh per-instance.
//
// Generic by design (reflection over field TYPE, not field NAME) so it doesn't need per-room-type
// knowledge of what a spot list is called (farmingSpots, relaxingSpots, guardSpots, etc.) — it walks
// every RoomBase-derived component's Transform/RoomSpot/List<Transform>/List<RoomSpot>/List<RoomPath>
// fields, and for any reference that resolves OUTSIDE the current prefab's own hierarchy, looks for a
// same-named local child to redirect to instead. Safe to run across every room prefab (correctly-wired
// ones are untouched — every reference already resolves locally, so nothing gets rewritten) and safe to
// re-run.
public static class RoomReferenceFixer
{
    private const string RoomsFolder = "Assets/Prefabs/Rooms";

    [MenuItem("Burrowscape/Fix Cross-Prefab Room References")]
    public static void FixReferences()
    {
        List<string> prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();

        int prefabsFixed = 0;
        int fieldsFixed = 0;
        List<string> unresolved = new List<string>();

        foreach (string path in prefabPaths)
        {
            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                RoomBase roomBase = contentRoot.GetComponent<RoomBase>();
                if (roomBase == null) continue;

                Dictionary<string, Transform> localTransforms = contentRoot
                    .GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First());

                int changedInThisPrefab = 0;

                foreach (Component comp in contentRoot.GetComponents<Component>())
                {
                    if (!(comp is RoomBase)) continue;

                    // GetFields with DeclaredOnly, walked manually up the inheritance chain — otherwise
                    // private [SerializeField] fields declared on RoomBase itself (leftEntrance etc.)
                    // are invisible when reflecting on a derived type like GardenRoom.
                    System.Type type = comp.GetType();
                    while (type != null && type != typeof(MonoBehaviour))
                    {
                        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        foreach (FieldInfo field in fields)
                        {
                            if (!IsSerialized(field)) continue;
                            changedInThisPrefab += FixField(field, comp, contentRoot, localTransforms, unresolved, path);
                        }
                        type = type.BaseType;
                    }
                }

                if (changedInThisPrefab > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                    prefabsFixed++;
                    fieldsFixed += changedInThisPrefab;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        string summary = $"RoomReferenceFixer: fixed {fieldsFixed} reference(s) across {prefabsFixed} prefab(s).";
        if (unresolved.Count > 0)
            summary += $"\n\n{unresolved.Count} reference(s) couldn't be auto-fixed (no same-named local child found):\n- {string.Join("\n- ", unresolved)}";

        Debug.Log(summary);
        EditorUtility.DisplayDialog("Fix Cross-Prefab Room References", summary, "OK");
    }

    private static bool IsSerialized(FieldInfo field)
    {
        if (field.GetCustomAttribute<System.NonSerializedAttribute>() != null) return false;
        if (field.IsPublic) return true;
        return field.GetCustomAttribute<SerializeField>() != null;
    }

    // Returns how many individual references were changed on this field.
    private static int FixField(FieldInfo field, object owner, GameObject root, Dictionary<string, Transform> localTransforms, List<string> unresolved, string prefabPath)
    {
        int changed = 0;

        if (field.FieldType == typeof(Transform))
        {
            Transform current = (Transform)field.GetValue(owner);
            Transform fixedT = TryFixTransform(current, root, localTransforms, unresolved, prefabPath);
            if (fixedT != current) { field.SetValue(owner, fixedT); changed++; }
        }
        else if (field.FieldType == typeof(RoomSpot))
        {
            RoomSpot current = (RoomSpot)field.GetValue(owner);
            RoomSpot fixedS = TryFixRoomSpot(current, root, localTransforms, unresolved, prefabPath);
            if (fixedS != current) { field.SetValue(owner, fixedS); changed++; }
        }
        else if (field.FieldType == typeof(List<Transform>))
        {
            List<Transform> list = (List<Transform>)field.GetValue(owner);
            if (list == null) return 0;
            for (int i = 0; i < list.Count; i++)
            {
                Transform fixedT = TryFixTransform(list[i], root, localTransforms, unresolved, prefabPath);
                if (fixedT != list[i]) { list[i] = fixedT; changed++; }
            }
        }
        else if (field.FieldType == typeof(List<RoomSpot>))
        {
            List<RoomSpot> list = (List<RoomSpot>)field.GetValue(owner);
            if (list == null) return 0;
            for (int i = 0; i < list.Count; i++)
            {
                RoomSpot fixedS = TryFixRoomSpot(list[i], root, localTransforms, unresolved, prefabPath);
                if (fixedS != list[i]) { list[i] = fixedS; changed++; }
            }
        }
        else if (field.FieldType == typeof(List<RoomPath>))
        {
            List<RoomPath> list = (List<RoomPath>)field.GetValue(owner);
            if (list == null) return 0;
            foreach (RoomPath p in list)
            {
                Transform fixedEntrance = TryFixTransform(p.entrance, root, localTransforms, unresolved, prefabPath);
                if (fixedEntrance != p.entrance) { p.entrance = fixedEntrance; changed++; }

                RoomSpot fixedSpot = TryFixRoomSpot(p.targetSpot, root, localTransforms, unresolved, prefabPath);
                if (fixedSpot != p.targetSpot) { p.targetSpot = fixedSpot; changed++; }

                if (p.waypoints != null)
                {
                    for (int i = 0; i < p.waypoints.Count; i++)
                    {
                        Transform fixedWp = TryFixTransform(p.waypoints[i], root, localTransforms, unresolved, prefabPath);
                        if (fixedWp != p.waypoints[i]) { p.waypoints[i] = fixedWp; changed++; }
                    }
                }
            }
        }

        return changed;
    }

    private static Transform TryFixTransform(Transform current, GameObject root, Dictionary<string, Transform> localTransforms, List<string> unresolved, string prefabPath)
    {
        if (current == null) return null;
        if (current == root.transform || current.IsChildOf(root.transform)) return current;

        if (localTransforms.TryGetValue(current.name, out Transform replacement))
            return replacement;

        unresolved.Add($"{prefabPath}: no local '{current.name}' to replace a foreign Transform reference");
        return current;
    }

    private static RoomSpot TryFixRoomSpot(RoomSpot current, GameObject root, Dictionary<string, Transform> localTransforms, List<string> unresolved, string prefabPath)
    {
        if (current == null) return null;
        if (current.transform == root.transform || current.transform.IsChildOf(root.transform)) return current;

        if (localTransforms.TryGetValue(current.name, out Transform replacementTransform))
        {
            RoomSpot replacementSpot = replacementTransform.GetComponent<RoomSpot>();
            if (replacementSpot != null) return replacementSpot;
        }

        unresolved.Add($"{prefabPath}: no local '{current.name}' to replace a foreign RoomSpot reference");
        return current;
    }
}
