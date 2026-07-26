using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// EDITOR-ONLY. Broadcasts NPCBunny's need decay/gain/threshold fields to every bunny type prefab at
// once, so tuning e.g. energyDecayPerSecondWorking doesn't mean opening 5 separate
// NPC_Bunny_{Type}_Default prefabs and typing the same number 5 times. Auto-discovers every prefab under
// BunnyPrefabRoot that actually has an NPCBunny component (rather than hardcoding the 5 current type
// names), so a future 6th type is picked up with no tool change needed — same reasoning as
// RoomSpotPathAutoPopulator's SpotNamePrefix-driven field matching.
//
// Unlike BunnyBaseStatsWindow (a grid editor for values that are SUPPOSED to differ per type), this is a
// broadcast tool: one set of target values, applied identically to all prefabs. Each field also shows its
// CURRENT value across every discovered prefab (bolded if they've already drifted apart) so drift is
// visible before you overwrite it, not after.
public class NPCBunnyNeedsTuner : EditorWindow
{
    private const string BunnyPrefabRoot = "Assets/Prefabs/Rabbits/NPC Bunnies";

    // (serialized field name on NPCBunny, display label) — update this list if NPCBunny's own need fields
    // change. Grouped to mirror NPCBunny.cs's own [Header] groupings.
    private static readonly (string field, string label)[] HungerFields =
    {
        ("hungerDecayPerSecond", "Decay / sec"),
        ("hungerThresholdLow", "Threshold: Low"),
        ("hungerThresholdFull", "Threshold: Full"),
        ("hungerThresholdCritical", "Threshold: Critical"),
        ("hungerGainPerCarrot", "Gain per Carrot"),
    };

    private static readonly (string field, string label)[] ThirstFields =
    {
        ("thirstDecayPerSecond", "Decay / sec"),
        ("thirstThresholdLow", "Threshold: Low"),
        ("thirstThresholdFull", "Threshold: Full"),
        ("thirstThresholdCritical", "Threshold: Critical"),
        ("thirstGainPerDrink", "Gain per Drink"),
    };

    private static readonly (string field, string label)[] EnergyFields =
    {
        ("energyDecayPerSecond", "Decay / sec (Passive)"),
        ("energyDecayPerSecondWorking", "Decay / sec (Working)"),
        ("energyDecayPerSecondQuesting", "Decay / sec (Questing)"),
        ("energyDecayPerSecondForaging", "Decay / sec (Foraging)"),
        ("energyThresholdLow", "Threshold: Low"),
        ("energyGainPerSecondSleeping", "Gain / sec (Sleeping)"),
    };

    private static readonly (string field, string label)[] MoodFields =
    {
        ("moodDecayPerSecondWorking", "Decay / sec (Working)"),
        ("moodGainPerSecondRelaxing", "Gain / sec (Relaxing)"),
        ("moodGainPerSecondEatingOrDrinking", "Gain / sec (Eating/Drinking)"),
        ("moodGainPerSecondSleeping", "Gain / sec (Sleeping)"),
        ("moodDecayPerSecondIdlePacing", "Decay / sec (Idle Pacing)"),
    };

    private static readonly (string field, string label)[][] AllGroups =
    {
        HungerFields, ThirstFields, EnergyFields, MoodFields
    };

    // Scratch input only — NOT bound to any single prefab's SerializedObject until Apply is pressed.
    private readonly Dictionary<string, float> targetValues = new Dictionary<string, float>();
    private List<GameObject> discoveredPrefabs = new List<GameObject>();
    private Vector2 scroll;

    [MenuItem("Burrowscape/NPC Bunny Needs Tuner")]
    private static void ShowWindow()
    {
        NPCBunnyNeedsTuner window = GetWindow<NPCBunnyNeedsTuner>("Bunny Needs Tuner");
        window.minSize = new Vector2(620, 300);
        window.RefreshPrefabList();
        window.LoadFromFirstPrefab();
    }

    private void OnEnable()
    {
        RefreshPrefabList();
    }

    private void RefreshPrefabList()
    {
        discoveredPrefabs.Clear();
        if (!AssetDatabase.IsValidFolder(BunnyPrefabRoot)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { BunnyPrefabRoot });
        discoveredPrefabs = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && p.GetComponent<NPCBunny>() != null)
            .OrderBy(p => p.name)
            .ToList();
    }

    // Convenience starting point only — pre-fills every target field from the first discovered prefab so
    // the user only has to retype the ONE field they actually want to change, not every field from
    // scratch. Harmless if the 5 types have already drifted apart (the per-row "current" readout below
    // still shows the real per-prefab values regardless of what this loads).
    private void LoadFromFirstPrefab()
    {
        targetValues.Clear();
        if (discoveredPrefabs.Count == 0) return;

        SerializedObject so = new SerializedObject(discoveredPrefabs[0].GetComponent<NPCBunny>());
        foreach ((string field, string _) in AllGroups.SelectMany(g => g))
        {
            SerializedProperty prop = so.FindProperty(field);
            targetValues[field] = prop != null ? prop.floatValue : 0f;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh Prefab List", EditorStyles.toolbarButton, GUILayout.Width(140)))
            RefreshPrefabList();
        if (GUILayout.Button("Load Current (from first prefab)", EditorStyles.toolbarButton, GUILayout.Width(200)))
            LoadFromFirstPrefab();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (discoveredPrefabs.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No prefab with an NPCBunny component found under \"{BunnyPrefabRoot}\".",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            $"Editing values below and clicking Apply writes them to all {discoveredPrefabs.Count} bunny " +
            "prefab(s) found. Each row's \"current\" readout shows what every prefab actually has right " +
            "now — bolded if they've already drifted apart from each other.",
            MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawFieldGroup("Hunger", HungerFields);
        DrawFieldGroup("Thirst", ThirstFields);
        DrawFieldGroup("Energy", EnergyFields);
        DrawFieldGroup("Mood", MoodFields);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Target prefabs ({discoveredPrefabs.Count}):", EditorStyles.boldLabel);
        foreach (GameObject prefab in discoveredPrefabs)
            EditorGUILayout.LabelField("  " + prefab.name, EditorStyles.miniLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button($"Apply to All {discoveredPrefabs.Count} Prefab(s)", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog(
                "Apply Bunny Needs",
                $"Write these values to all {discoveredPrefabs.Count} bunny prefab(s)? This overwrites their current values.",
                "Apply", "Cancel"))
            {
                ApplyToAll();
            }
        }
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

            List<float> perPrefabValues = discoveredPrefabs.Select(p => GetCurrentValue(p, field)).ToList();
            bool allMatch = perPrefabValues.Count == 0 || perPrefabValues.All(x => Mathf.Approximately(x, perPrefabValues[0]));

            string summary;
            if (perPrefabValues.Count == 0)
                summary = "current: —";
            else if (allMatch)
                summary = $"current: {perPrefabValues[0]} (all match)";
            else
                summary = "current: " + string.Join(" / ", discoveredPrefabs.Select((p, i) => $"{GetShortTypeName(p)}={perPrefabValues[i]}"));

            EditorGUILayout.LabelField(summary, allMatch ? EditorStyles.miniLabel : EditorStyles.boldLabel);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space();
    }

    private static float GetCurrentValue(GameObject prefab, string field)
    {
        NPCBunny bunny = prefab.GetComponent<NPCBunny>();
        if (bunny == null) return 0f;
        SerializedProperty prop = new SerializedObject(bunny).FindProperty(field);
        return prop != null ? prop.floatValue : 0f;
    }

    // "NPC_Bunny_Fire_Default" -> "Fire", purely for the compact per-prefab readout above.
    private static string GetShortTypeName(GameObject prefab)
    {
        string n = prefab.name;
        const string prefix = "NPC_Bunny_";
        const string suffix = "_Default";
        if (n.StartsWith(prefix)) n = n.Substring(prefix.Length);
        if (n.EndsWith(suffix)) n = n.Substring(0, n.Length - suffix.Length);
        return n;
    }

    private void ApplyToAll()
    {
        int changedCount = 0;

        foreach (GameObject prefab in discoveredPrefabs)
        {
            NPCBunny bunny = prefab.GetComponent<NPCBunny>();
            if (bunny == null) continue;

            SerializedObject so = new SerializedObject(bunny);
            bool anyChanged = false;

            foreach ((string field, string _) in AllGroups.SelectMany(g => g))
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
                EditorUtility.SetDirty(bunny);
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        string summary = $"Applied need values to {changedCount} of {discoveredPrefabs.Count} bunny prefab(s).";
        Debug.Log($"[NPCBunnyNeedsTuner] {summary}");
        EditorUtility.DisplayDialog("Apply Bunny Needs", summary, "OK");
    }
}
