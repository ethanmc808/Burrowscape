using UnityEngine;
using UnityEditor;

// One place to tune both halves of the rarity system — see Rarity_DesignDoc.md. "Roll Weights" edits the
// scene's ForagingManager (the universal weights every location's loot roll shares). "Item Rarities"
// edits every item asset's own fixed `rarity` field (Fruit/Trinket/Accessory/Material) plus the
// ForagingKindRarityConfig asset backing Carrot/Potion/Crystal Carrot. Deliberately reads/writes through
// SerializedObject/SerializedProperty so edits go through Unity's normal dirty/undo pipeline.
public class RarityManagerWindow : EditorWindow
{
    private Vector2 scroll;
    private bool showFruits = true;
    private bool showTrinkets = true;
    private bool showAccessories = true;
    private bool showMaterials = true;

    [MenuItem("Burrowscape/Rarity Manager")]
    public static void Open()
    {
        GetWindow<RarityManagerWindow>("Rarity Manager");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawRollWeights();
        EditorGUILayout.Space(12);
        DrawKindRarities();
        EditorGUILayout.Space(12);
        DrawItemRarities();

        EditorGUILayout.EndScrollView();
    }

    // ---------- Roll Weights ----------

    private void DrawRollWeights()
    {
        EditorGUILayout.LabelField("Roll Weights (universal — applies to every location)", EditorStyles.boldLabel);

        ForagingManager manager = FindFirstObjectByType<ForagingManager>();
        if (manager == null)
        {
            EditorGUILayout.HelpBox("No ForagingManager found in the open scene — open the scene that contains it to edit roll weights.", MessageType.Info);
            return;
        }

        SerializedObject so = new SerializedObject(manager);
        SerializedProperty common = so.FindProperty("baseCommonWeight");
        SerializedProperty uncommon = so.FindProperty("baseUncommonWeight");
        SerializedProperty rare = so.FindProperty("baseRareWeight");
        SerializedProperty superRare = so.FindProperty("baseSuperRareWeight");
        SerializedProperty mythical = so.FindProperty("baseMythicalWeight");

        float total = common.floatValue + uncommon.floatValue + rare.floatValue + superRare.floatValue + mythical.floatValue;

        EditorGUI.BeginChangeCheck();
        DrawWeightField(common, "Common", total);
        DrawWeightField(uncommon, "Uncommon", total);
        DrawWeightField(rare, "Rare", total);
        DrawWeightField(superRare, "Super Rare", total);
        DrawWeightField(mythical, "Mythical", total);
        if (EditorGUI.EndChangeCheck())
            so.ApplyModifiedProperties();
    }

    private static void DrawWeightField(SerializedProperty prop, string label, float total)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(prop, new GUIContent(label));
        float pct = total > 0f ? prop.floatValue / total * 100f : 0f;
        EditorGUILayout.LabelField($"{pct:0.0}%", GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();
    }

    // ---------- Kind Rarities (Carrot/Potion/Crystal Carrot) ----------

    private void DrawKindRarities()
    {
        EditorGUILayout.LabelField("Fixed Rarities — Carrot / Potion / Crystal Carrot", EditorStyles.boldLabel);

        string[] guids = AssetDatabase.FindAssets("t:ForagingKindRarityConfig");
        if (guids.Length == 0)
        {
            EditorGUILayout.HelpBox("No ForagingKindRarityConfig asset found — run Burrowscape/Generate Foraging Data first.", MessageType.Info);
            return;
        }

        ForagingKindRarityConfig config = AssetDatabase.LoadAssetAtPath<ForagingKindRarityConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        SerializedObject so = new SerializedObject(config);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("carrotRarity"), new GUIContent("Carrot"));
        EditorGUILayout.PropertyField(so.FindProperty("potionRarity"), new GUIContent("Potion"));
        EditorGUILayout.PropertyField(so.FindProperty("crystalCarrotRarity"), new GUIContent("Crystal Carrot"));
        if (EditorGUI.EndChangeCheck())
            so.ApplyModifiedProperties();
    }

    // ---------- Item Rarities ----------

    private void DrawItemRarities()
    {
        EditorGUILayout.LabelField("Item Rarities", EditorStyles.boldLabel);

        showFruits = DrawAssetTypeSection<ForagingFruitDefinition>("Fruits", showFruits);
        showTrinkets = DrawAssetTypeSection<ForagingTrinketDefinition>("Trinkets", showTrinkets);
        showAccessories = DrawAssetTypeSection<ForagingAccessoryDefinition>("Accessories", showAccessories);
        showMaterials = DrawAssetTypeSection<ForagingMaterialDefinition>("Materials (Cloth/Metal/Herb tiers)", showMaterials);
    }

    private static bool DrawAssetTypeSection<T>(string label, bool expanded) where T : ScriptableObject
    {
        expanded = EditorGUILayout.Foldout(expanded, label, true);
        if (!expanded) return expanded;

        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        System.Array.Sort(guids, (a, b) => string.Compare(AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

        EditorGUI.indentLevel++;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) continue;

            SerializedObject so = new SerializedObject(asset);
            SerializedProperty rarityProp = so.FindProperty("rarity");
            if (rarityProp == null) continue;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(rarityProp, new GUIContent(asset.name));
            if (EditorGUI.EndChangeCheck())
                so.ApplyModifiedProperties();
        }
        EditorGUI.indentLevel--;

        return expanded;
    }
}
