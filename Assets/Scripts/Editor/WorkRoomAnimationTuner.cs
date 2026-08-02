using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// EDITOR-ONLY. Bulk-assigns RoomBase.workingAnimationKind (see WorkAnimationKind's own header comment
// on RoomBase.cs) across every room prefab of a given TYPE at once. Grouped by RoomTypeId (e.g.
// "Garden", "Water", "Coal", "GuardRoom") rather than a hardcoded component-type list like
// WorkRoomProductionTuner — RoomTypeId is what actually identifies a room's type across its Grade
// 1/2/3 and 4x2x6/8x2x6/12x2x6 size variants (same field the merge/upgrade system keys off), so a type
// with e.g. 9 size/grade prefabs only needs the kind set once here instead of once per prefab.
// Deliberately scans EVERY RoomBase prefab, not just the three current IJobRoom types, since Guard Room
// (and any future job room) also reads WorkingAnimationKind.
public class WorkRoomAnimationTuner : EditorWindow
{
    private const string RoomPrefabRoot = "Assets/Prefabs/Rooms";

    private class RoomEntry
    {
        public GameObject prefab;
        public RoomBase component;
        public SerializedObject serializedObject;
    }

    private class TypeGroup
    {
        public string roomTypeId;
        public List<RoomEntry> rooms = new List<RoomEntry>();
        public WorkAnimationKind targetKind; // staged value for this group, not yet applied until the button is pressed
    }

    private List<TypeGroup> groups = new List<TypeGroup>();
    private Vector2 scroll;

    [MenuItem("Burrowscape/Work Room Animation Tuner")]
    private static void ShowWindow()
    {
        WorkRoomAnimationTuner window = GetWindow<WorkRoomAnimationTuner>("Work Room Animation Tuner");
        window.minSize = new Vector2(620, 360);
        window.RefreshGroups();
    }

    private void OnEnable()
    {
        RefreshGroups();
    }

    private void RefreshGroups()
    {
        // Preserve any staged-but-not-yet-applied picks across a refresh, keyed by roomTypeId, so
        // clicking Refresh doesn't discard what you were about to apply.
        Dictionary<string, WorkAnimationKind> staged = groups.ToDictionary(g => g.roomTypeId, g => g.targetKind);

        groups.Clear();
        if (!AssetDatabase.IsValidFolder(RoomPrefabRoot)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { RoomPrefabRoot });
        Dictionary<string, TypeGroup> byType = new Dictionary<string, TypeGroup>();

        foreach (string guid in guids)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;

            RoomBase component = prefab.GetComponent<RoomBase>();
            if (component == null) continue;

            string roomTypeId = string.IsNullOrEmpty(component.RoomTypeId) ? "(no RoomTypeId set)" : component.RoomTypeId;

            if (!byType.TryGetValue(roomTypeId, out TypeGroup group))
            {
                group = new TypeGroup { roomTypeId = roomTypeId };
                if (staged.TryGetValue(roomTypeId, out WorkAnimationKind stagedKind))
                    group.targetKind = stagedKind;
                byType[roomTypeId] = group;
            }

            group.rooms.Add(new RoomEntry
            {
                prefab = prefab,
                component = component,
                serializedObject = new SerializedObject(component)
            });
        }

        groups = byType.Values.OrderBy(g => g.roomTypeId).ToList();
        foreach (TypeGroup g in groups)
            g.rooms = g.rooms.OrderBy(r => r.prefab.name).ToList();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
            RefreshGroups();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (groups.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No prefab with a RoomBase component found under \"{RoomPrefabRoot}\".",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "Rooms are grouped by RoomTypeId (Garden, Water, Coal, GuardRoom, etc.) across every Grade/size " +
            "variant. Pick a kind and click Apply to push it to every prefab in that group in one go — Idle " +
            "is the default and needs no authoring. Adding a brand-new kind (beyond Idle/Gardening) needs a " +
            "new WorkAnimationKind enum value plus a matching Working_<Name> state in the Animator Controller " +
            "first, same as Gardening was added.",
            MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (TypeGroup group in groups)
            DrawGroup(group);

        EditorGUILayout.EndScrollView();
    }

    private void DrawGroup(TypeGroup group)
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        List<WorkAnimationKind> currentKinds = group.rooms.Select(r => GetCurrentKind(r.serializedObject)).ToList();
        bool allMatch = currentKinds.Count == 0 || currentKinds.All(k => k == currentKinds[0]);

        EditorGUILayout.LabelField($"{group.roomTypeId}  ({group.rooms.Count} prefab{(group.rooms.Count == 1 ? "" : "s")})", EditorStyles.boldLabel);

        string summary = allMatch
            ? $"current: {(currentKinds.Count > 0 ? currentKinds[0].ToString() : "—")} (all match)"
            : "current: " + string.Join(" / ", group.rooms.Select((r, i) => $"{r.prefab.name}={currentKinds[i]}"));
        EditorGUILayout.LabelField(summary, allMatch ? EditorStyles.miniLabel : EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        group.targetKind = (WorkAnimationKind)EditorGUILayout.EnumPopup("Working Animation Kind", group.targetKind);
        if (GUILayout.Button($"Apply to All {group.rooms.Count}", GUILayout.Width(140)))
            ApplyToGroup(group);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private static WorkAnimationKind GetCurrentKind(SerializedObject so)
    {
        so.Update();
        SerializedProperty prop = so.FindProperty("workingAnimationKind");
        return prop != null ? (WorkAnimationKind)prop.enumValueIndex : WorkAnimationKind.Idle;
    }

    private void ApplyToGroup(TypeGroup group)
    {
        int changedCount = 0;

        foreach (RoomEntry entry in group.rooms)
        {
            SerializedObject so = entry.serializedObject;
            so.Update();

            SerializedProperty prop = so.FindProperty("workingAnimationKind");
            if (prop == null) continue;

            if ((WorkAnimationKind)prop.enumValueIndex != group.targetKind)
            {
                prop.enumValueIndex = (int)group.targetKind;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.component);
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        string summary = $"Applied \"{group.targetKind}\" to {changedCount} of {group.rooms.Count} \"{group.roomTypeId}\" prefab(s).";
        Debug.Log($"[WorkRoomAnimationTuner] {summary}");
        EditorUtility.DisplayDialog("Apply Working Animation", summary, "OK");
    }
}
