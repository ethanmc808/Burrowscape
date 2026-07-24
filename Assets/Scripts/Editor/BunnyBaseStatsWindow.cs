using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// EDITOR-ONLY. Grid editor for every BunnyTypeDefinition's base stats (HP/Attack/Defense/Speed/Luck) on
// one screen, so retuning the placeholder values from BunnyBaseStats.xlsx doesn't mean clicking through
// 20 separate assets one at a time. See BunnyTypeSystem_DesignDoc.md at the project root.
//
// Reads/writes the BunnyTypeDefinition assets directly (Assets/Data/Bunny Types) via SerializedObject,
// so edits here get normal Undo support and show up identically to editing the asset's Inspector by
// hand — this window is purely a faster way to see/edit all of them together, not a separate data store.
// Run Burrowscape > Generate Bunny Type Definitions first if the list below is empty.
public class BunnyBaseStatsWindow : EditorWindow
{
    private const string DefinitionsFolder = "Assets/Data/Bunny Types";

    private List<BunnyTypeDefinition> definitions = new List<BunnyTypeDefinition>();
    private List<SerializedObject> serializedDefs = new List<SerializedObject>();
    private Vector2 scroll;
    private bool dirtySinceLastSave;

    [MenuItem("Burrowscape/Bunny Base Stats Editor")]
    private static void ShowWindow()
    {
        BunnyBaseStatsWindow window = GetWindow<BunnyBaseStatsWindow>("Bunny Base Stats");
        window.minSize = new Vector2(760, 300);
        window.LoadDefinitions();
    }

    private void OnEnable()
    {
        LoadDefinitions();
    }

    private void LoadDefinitions()
    {
        definitions.Clear();
        serializedDefs.Clear();

        if (!AssetDatabase.IsValidFolder(DefinitionsFolder)) return;

        string[] guids = AssetDatabase.FindAssets("t:BunnyTypeDefinition", new[] { DefinitionsFolder });
        definitions = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<BunnyTypeDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .OrderBy(d => d.group)
            .ThenBy(d => (int)d.type)
            .ToList();

        foreach (BunnyTypeDefinition def in definitions)
            serializedDefs.Add(new SerializedObject(def));

        dirtySinceLastSave = false;
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (definitions.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No BunnyTypeDefinition assets found in {DefinitionsFolder}. " +
                "Run Burrowscape > Generate Bunny Type Definitions first.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            "These are pre-multiplier base stats. At spawn, HP is x2 and Luck is x0.5 relative to what's " +
            "shown here (Attack/Defense/Speed are unmodified) — see BunnyStatCalculator in the design doc.",
            MessageType.None);

        DrawHeaderRow();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        int currentGroup = -1;
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].group != currentGroup)
            {
                currentGroup = definitions[i].group;
                EditorGUILayout.LabelField($"Group {currentGroup} (unlocks at {definitions[i].populationThreshold} population)", EditorStyles.miniBoldLabel);
            }
            DrawRow(definitions[i], serializedDefs[i]);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            LoadDefinitions();

        GUI.enabled = dirtySinceLastSave;
        if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            AssetDatabase.SaveAssets();
            dirtySinceLastSave = false;
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(dirtySinceLastSave ? "Unsaved changes (auto-dirtied — Save All writes them to disk)" : "All saved", EditorStyles.miniLabel, GUILayout.Width(320));

        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeaderRow()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(90));
        GUILayout.Label("HP", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Attack", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Defense", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Speed", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Luck", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Total", EditorStyles.boldLabel, GUILayout.Width(60));
        GUILayout.Label("Prefab", EditorStyles.boldLabel, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRow(BunnyTypeDefinition def, SerializedObject so)
    {
        so.Update();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label(def.type.ToString(), GUILayout.Width(90));

        EditorGUI.BeginChangeCheck();
        DrawIntField(so, "baseHP", 60);
        DrawIntField(so, "baseAttack", 60);
        DrawIntField(so, "baseDefense", 60);
        DrawIntField(so, "baseSpeed", 60);
        DrawIntField(so, "baseLuck", 60);
        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(def);
            dirtySinceLastSave = true;
        }

        GUILayout.Label(def.BaseTotal.ToString(), GUILayout.Width(60));

        GUI.enabled = false;
        EditorGUILayout.ObjectField(def.prefab, typeof(GameObject), false, GUILayout.Width(60));
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private static void DrawIntField(SerializedObject so, string propertyName, float width)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        prop.intValue = EditorGUILayout.IntField(prop.intValue, GUILayout.Width(width));
    }
}
