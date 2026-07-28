using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// EDITOR-ONLY. Single home for every work room's (GardenRoom/WaterRoom/CoalRoom) worker-production-scaling
// data — see Docs/DiminishingReturnsProduction_Design.md. Two different edit shapes in one window:
//   - diminishingReturnsRate / typeMatchProductionBonus: BROADCAST — meant to be one shared constant
//     across every work room, so they're staged as scratch values and pushed to all rooms via Apply.
//   - recommendedTypes: a per-room GRID — genuinely different per room (Garden=Plant, Water=Water,
//     Coal=Shock), edited inline per row and applied immediately, same live-edit pattern as
//     BunnyBaseStatsWindow. HideInInspector on the field itself keeps this the only place it's editable,
//     so a stray edit can't land on the wrong prefab.
public class WorkRoomProductionTuner : EditorWindow
{
    private const string RoomPrefabRoot = "Assets/Prefabs/Rooms";

    // (serialized field name shared across GardenRoom/WaterRoom/CoalRoom, display label)
    private static readonly (string field, string label)[] ScalingFields =
    {
        ("diminishingReturnsRate", "Diminishing Returns Rate"),
        ("typeMatchProductionBonus", "Type-Match Production Bonus"),
        // Work Room XP (WorkRoomXP_DesignDoc.md) — declared on RoomBase (all three job rooms inherit
        // it), but DrawFieldGroup/ApplyScalingToAll below already resolve fields by name via
        // SerializedObject, which finds inherited serialized fields the same as ones declared directly
        // on GardenRoom/WaterRoom/CoalRoom, so no other code needs to change for these two.
        ("baseXPPerSecond", "Base XP/sec (Work)"),
        ("typeMatchXPBonus", "Type-Match XP Bonus"),
    };

    // Suggested recommendedTypes default per room-type label — just a one-click convenience for the
    // "Apply Suggested Defaults" button below, not enforced anywhere else.
    private static readonly Dictionary<string, BunnyType> SuggestedDefaults = new Dictionary<string, BunnyType>
    {
        { "Garden", BunnyType.Plant },
        { "Water", BunnyType.Water },
        { "Coal", BunnyType.Shock },
    };

    private class WorkRoomEntry
    {
        public GameObject prefab;
        public MonoBehaviour component; // GardenRoom, WaterRoom, or CoalRoom — whichever this prefab has
        public string roomTypeLabel;
        public SerializedObject serializedObject;
    }

    private readonly Dictionary<string, float> targetValues = new Dictionary<string, float>();
    private List<WorkRoomEntry> discoveredRooms = new List<WorkRoomEntry>();
    private Vector2 scroll;

    [MenuItem("Burrowscape/Work Room Production Tuner")]
    private static void ShowWindow()
    {
        WorkRoomProductionTuner window = GetWindow<WorkRoomProductionTuner>("Work Room Production Tuner");
        window.minSize = new Vector2(680, 400);
        window.RefreshRoomList();
        window.LoadFromFirstRoom();
    }

    private void OnEnable()
    {
        RefreshRoomList();
    }

    private void RefreshRoomList()
    {
        discoveredRooms.Clear();
        if (!AssetDatabase.IsValidFolder(RoomPrefabRoot)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { RoomPrefabRoot });
        foreach (string guid in guids)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;

            MonoBehaviour component = prefab.GetComponent<GardenRoom>();
            string label = "Garden";
            if (component == null) { component = prefab.GetComponent<WaterRoom>(); label = "Water"; }
            if (component == null) { component = prefab.GetComponent<CoalRoom>(); label = "Coal"; }
            if (component == null) continue;

            discoveredRooms.Add(new WorkRoomEntry
            {
                prefab = prefab,
                component = component,
                roomTypeLabel = label,
                serializedObject = new SerializedObject(component)
            });
        }

        discoveredRooms = discoveredRooms.OrderBy(r => r.prefab.name).ToList();
    }

    // Convenience starting point only — pre-fills target fields from the first discovered room so the
    // user only has to retype the ONE field they actually want to change.
    private void LoadFromFirstRoom()
    {
        targetValues.Clear();
        if (discoveredRooms.Count == 0) return;

        SerializedObject so = discoveredRooms[0].serializedObject;
        foreach ((string field, string _) in ScalingFields)
        {
            SerializedProperty prop = so.FindProperty(field);
            targetValues[field] = prop != null ? prop.floatValue : 0f;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh Room List", EditorStyles.toolbarButton, GUILayout.Width(140)))
            RefreshRoomList();
        if (GUILayout.Button("Load Current (from first room)", EditorStyles.toolbarButton, GUILayout.Width(200)))
            LoadFromFirstRoom();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (discoveredRooms.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No prefab with a GardenRoom/WaterRoom/CoalRoom component found under \"{RoomPrefabRoot}\".",
                MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            $"Editing values below and clicking Apply writes them to all {discoveredRooms.Count} work room " +
            "prefab(s) found (Garden/Water/Coal). Each row's \"current\" readout shows what every prefab " +
            "actually has right now — bolded if they've already drifted apart.",
            MessageType.Info);

        DrawFieldGroup("Worker Production Scaling (broadcast to all)", ScalingFields);

        EditorGUILayout.Space();
        if (GUILayout.Button($"Apply Rate/Bonus to All {discoveredRooms.Count} Prefab(s)", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog(
                "Apply Work Room Production Scaling",
                $"Write these values to all {discoveredRooms.Count} work room prefab(s)? This overwrites their current values.",
                "Apply", "Cancel"))
            {
                ApplyScalingToAll();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        DrawRecommendedTypesGrid();

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        DrawXPRecommendedTypesGrid();

        EditorGUILayout.EndScrollView();
    }

    private void DrawFieldGroup(string header, (string field, string label)[] fields)
    {
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        foreach ((string field, string label) in fields)
        {
            EditorGUILayout.BeginHorizontal();

            float current = targetValues.TryGetValue(field, out float v) ? v : 0f;
            targetValues[field] = EditorGUILayout.FloatField(label, current, GUILayout.Width(280));

            List<float> perRoomValues = discoveredRooms.Select(r => GetCurrentFloat(r.serializedObject, field)).ToList();
            bool allMatch = perRoomValues.Count == 0 || perRoomValues.All(x => Mathf.Approximately(x, perRoomValues[0]));

            string summary;
            if (perRoomValues.Count == 0)
                summary = "current: —";
            else if (allMatch)
                summary = $"current: {perRoomValues[0]} (all match)";
            else
                summary = "current: " + string.Join(" / ", discoveredRooms.Select((r, i) => $"{r.roomTypeLabel}={perRoomValues[i]}"));

            EditorGUILayout.LabelField(summary, allMatch ? EditorStyles.miniLabel : EditorStyles.boldLabel);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.indentLevel--;
    }

    private static float GetCurrentFloat(SerializedObject so, string field)
    {
        so.Update();
        SerializedProperty prop = so.FindProperty(field);
        return prop != null ? prop.floatValue : 0f;
    }

    private void ApplyScalingToAll()
    {
        int changedCount = 0;

        foreach (WorkRoomEntry entry in discoveredRooms)
        {
            if (entry.component == null) continue;

            SerializedObject so = entry.serializedObject;
            so.Update();
            bool anyChanged = false;

            foreach ((string field, string _) in ScalingFields)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null || !targetValues.TryGetValue(field, out float target)) continue;

                if (!Mathf.Approximately(prop.floatValue, target))
                {
                    prop.floatValue = target;
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.component);
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        string summary = $"Applied worker production scaling to {changedCount} of {discoveredRooms.Count} work room prefab(s).";
        Debug.Log($"[WorkRoomProductionTuner] {summary}");
        EditorUtility.DisplayDialog("Apply Work Room Production Scaling", summary, "OK");
    }

    // Per-room grid, NOT broadcast — each row is its own SerializedObject, applied immediately on change
    // (same live-edit pattern as BunnyBaseStatsWindow.DrawRow), since recommendedTypes is meant to differ
    // per room.
    private void DrawRecommendedTypesGrid()
    {
        EditorGUILayout.LabelField("Recommended Types (per room — applied immediately, not broadcast)", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Suggested Defaults (Garden=Plant, Water=Water, Coal=Shock)", GUILayout.Height(24)))
        {
            ApplySuggestedRecommendedTypeDefaults();
        }

        EditorGUILayout.Space();

        foreach (WorkRoomEntry entry in discoveredRooms)
        {
            if (entry.component == null) continue;

            SerializedObject so = entry.serializedObject;
            so.Update();

            SerializedProperty prop = so.FindProperty("recommendedTypes");
            if (prop == null) continue;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prop, new GUIContent($"[{entry.roomTypeLabel}] {entry.prefab.name}"), true);
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.component);
                AssetDatabase.SaveAssets();
            }
        }
    }

    private void ApplySuggestedRecommendedTypeDefaults()
    {
        int changedCount = 0;

        foreach (WorkRoomEntry entry in discoveredRooms)
        {
            if (entry.component == null) continue;
            if (!SuggestedDefaults.TryGetValue(entry.roomTypeLabel, out BunnyType defaultType)) continue;

            SerializedObject so = entry.serializedObject;
            so.Update();

            SerializedProperty prop = so.FindProperty("recommendedTypes");
            if (prop == null) continue;

            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).enumValueIndex = (int)defaultType;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(entry.component);
            changedCount++;
        }

        AssetDatabase.SaveAssets();
        string summary = $"Set suggested Recommended Types on {changedCount} work room prefab(s).";
        Debug.Log($"[WorkRoomProductionTuner] {summary}");
        EditorUtility.DisplayDialog("Apply Suggested Defaults", summary, "OK");
    }

    // Own per-room grid, deliberately separate from recommendedTypes' grid above (Ethan's call — XP
    // type-match is a fully independent field from production's, see WorkRoomXP_DesignDoc.md), even
    // though the two typically start out matching the same suggested default per room type.
    private void DrawXPRecommendedTypesGrid()
    {
        EditorGUILayout.LabelField("XP Recommended Types (per room — applied immediately, not broadcast)", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Suggested Defaults (Garden=Plant, Water=Water, Coal=Shock)", GUILayout.Height(24)))
        {
            ApplySuggestedXPRecommendedTypeDefaults();
        }

        EditorGUILayout.Space();

        foreach (WorkRoomEntry entry in discoveredRooms)
        {
            if (entry.component == null) continue;

            SerializedObject so = entry.serializedObject;
            so.Update();

            SerializedProperty prop = so.FindProperty("xpRecommendedTypes");
            if (prop == null) continue;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prop, new GUIContent($"[{entry.roomTypeLabel}] {entry.prefab.name}"), true);
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.component);
                AssetDatabase.SaveAssets();
            }
        }
    }

    private void ApplySuggestedXPRecommendedTypeDefaults()
    {
        int changedCount = 0;

        foreach (WorkRoomEntry entry in discoveredRooms)
        {
            if (entry.component == null) continue;
            if (!SuggestedDefaults.TryGetValue(entry.roomTypeLabel, out BunnyType defaultType)) continue;

            SerializedObject so = entry.serializedObject;
            so.Update();

            SerializedProperty prop = so.FindProperty("xpRecommendedTypes");
            if (prop == null) continue;

            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).enumValueIndex = (int)defaultType;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(entry.component);
            changedCount++;
        }

        AssetDatabase.SaveAssets();
        string summary = $"Set suggested XP Recommended Types on {changedCount} work room prefab(s).";
        Debug.Log($"[WorkRoomProductionTuner] {summary}");
        EditorUtility.DisplayDialog("Apply Suggested Defaults", summary, "OK");
    }
}
