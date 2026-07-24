using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// One-time cutover tool: swaps BunnyApprovalClickHandler/BunnyStatsClickHandler for the new unified
// BunnyInfoClickHandler on every NPC Bunny Default prefab. Needed now that BunnyApprovalUI/BunnyStatsUI
// have been retired from the scene in favor of BunnyInfoUI — the old click handlers still call
// BunnyApprovalUI.Instance/BunnyStatsUI.Instance, which are now permanently null (NullReferenceException
// on every bunny click) since nothing in the scene provides those singletons anymore. See
// BunnyInfoUI.cs's "CUTOVER STATUS" comment for the full story.
//
// Run this BEFORE deleting BunnyApprovalClickHandler.cs/BunnyStatsClickHandler.cs/BunnyApprovalUI.cs/
// BunnyStatsUI.cs — it needs those types to still exist so it can find and remove the old components.
//
// Safe to re-run: only adds BunnyInfoClickHandler if missing, only removes the old handlers if present.
public static class BunnyClickHandlerMigrationTool
{
    private const string PrefabFolder = "Assets/Prefabs/Rabbits/NPC Bunnies";

    [MenuItem("Burrowscape/Migrate Bunny Click Handlers")]
    public static void Migrate()
    {
        List<GameObject> prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(p => p != null && p.GetComponent<NPCBunny>() != null)
            .ToList();

        int updated = 0;
        List<string> results = new List<string>();

        foreach (GameObject prefabAsset in prefabs)
        {
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;

                BunnyApprovalClickHandler oldApproval = root.GetComponent<BunnyApprovalClickHandler>();
                if (oldApproval != null)
                {
                    Object.DestroyImmediate(oldApproval, true);
                    changed = true;
                }

                BunnyStatsClickHandler oldStats = root.GetComponent<BunnyStatsClickHandler>();
                if (oldStats != null)
                {
                    Object.DestroyImmediate(oldStats, true);
                    changed = true;
                }

                if (root.GetComponent<BunnyInfoClickHandler>() == null)
                {
                    root.AddComponent<BunnyInfoClickHandler>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    updated++;
                    results.Add($"OK: {prefabAsset.name}");
                }
                else
                {
                    results.Add($"SKIPPED (already up to date): {prefabAsset.name}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"BunnyClickHandlerMigrationTool: updated {updated} of {prefabs.Count} bunny prefab(s).\n{string.Join("\n", results)}");
    }
}
