using UnityEngine;
using UnityEditor;

// Creates the single CombatBalanceConfig asset at Assets/Resources/CombatBalanceConfig.asset (the exact
// path CombatBalanceConfig.Instance loads via Resources.Load) with the default values already authored
// in the script. Same reasoning as every other generator in this project: avoids hand-authoring
// ScriptableObject YAML with a guessed script GUID. Safe to re-run — does nothing if the asset already
// exists, so re-running after tuning values in the Inspector won't stomp those edits.
public static class CombatBalanceConfigGenerator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = "Assets/Resources/CombatBalanceConfig.asset";

    [MenuItem("Burrowscape/Generate Combat Balance Config")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<CombatBalanceConfig>(AssetPath) != null)
        {
            Debug.Log("CombatBalanceConfigGenerator: CombatBalanceConfig already exists at " + AssetPath + " — no changes made.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        CombatBalanceConfig config = ScriptableObject.CreateInstance<CombatBalanceConfig>();
        AssetDatabase.CreateAsset(config, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("CombatBalanceConfigGenerator: created CombatBalanceConfig at " + AssetPath + " with default values.");
    }
}
