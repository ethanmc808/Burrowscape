using UnityEngine;
using UnityEditor;

// Creates the single WorkWanderConfig asset at Assets/Resources/WorkWanderConfig.asset (the exact path
// WorkWanderConfig.Instance loads via Resources.Load) with the default values already authored in the
// script. Same reasoning as CombatBalanceConfigGenerator: avoids hand-authoring ScriptableObject YAML
// with a guessed script GUID. Safe to re-run — does nothing if the asset already exists, so re-running
// after tuning values in the Inspector won't stomp those edits.
public static class WorkWanderConfigGenerator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = "Assets/Resources/WorkWanderConfig.asset";

    [MenuItem("Burrowscape/Generate Work Wander Config")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<WorkWanderConfig>(AssetPath) != null)
        {
            Debug.Log("WorkWanderConfigGenerator: WorkWanderConfig already exists at " + AssetPath + " — no changes made.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        WorkWanderConfig config = ScriptableObject.CreateInstance<WorkWanderConfig>();
        AssetDatabase.CreateAsset(config, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("WorkWanderConfigGenerator: created WorkWanderConfig at " + AssetPath + " with default values.");
    }
}
