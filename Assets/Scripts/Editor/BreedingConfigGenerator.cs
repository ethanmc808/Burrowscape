using UnityEngine;
using UnityEditor;

// Creates the single BreedingConfig asset at Assets/Resources/BreedingConfig.asset (the exact path
// BreedingConfig.Instance loads via Resources.Load) with the default values already authored in the
// script. Same reasoning as WorkWanderConfigGenerator: avoids hand-authoring ScriptableObject YAML with a
// guessed script GUID. Safe to re-run — does nothing if the asset already exists, so re-running after
// tuning values in the Inspector won't stomp those edits.
public static class BreedingConfigGenerator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = "Assets/Resources/BreedingConfig.asset";

    [MenuItem("Burrowscape/Generate Breeding Config")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<BreedingConfig>(AssetPath) != null)
        {
            Debug.Log("BreedingConfigGenerator: BreedingConfig already exists at " + AssetPath + " — no changes made.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        BreedingConfig config = ScriptableObject.CreateInstance<BreedingConfig>();
        AssetDatabase.CreateAsset(config, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("BreedingConfigGenerator: created BreedingConfig at " + AssetPath + " with default values.");
    }
}
