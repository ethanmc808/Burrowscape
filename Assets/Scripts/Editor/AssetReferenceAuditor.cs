using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only utility: scans every prefab and scene in the project and reports
/// any mesh/material/texture dependencies that still point INTO the graveyard
/// folder (where old, non-ithappy assets were moved). Use this to confirm it's
/// safe to permanently delete the graveyard folder — a "0 references" result
/// means nothing in the project still needs it.
///
/// Menu: Tools > Burrowscape > Find References Into Graveyard Folder
/// </summary>
public static class AssetReferenceAuditor
{
    // Edit this to match the exact folder you moved your non-ithappy assets into.
    private static readonly string GraveyardPathPrefix = "Assets/3DAssetGraveyard";

    // Only these asset types are checked — scripts, ScriptableObjects, etc.
    // are ignored since they're not the "visual asset" swap we care about.
    private static readonly string[] RelevantExtensions =
    {
        ".fbx", ".obj", ".mat", ".png", ".jpg", ".jpeg", ".tga", ".psd"
    };

    [MenuItem("Tools/Burrowscape/Find References Into Graveyard Folder")]
    public static void FindGraveyardReferences()
    {
        var report = new StringBuilder();
        var perAssetOwners = new Dictionary<string, List<string>>(); // graveyard asset -> list of prefabs/scenes that still reference it

        int flaggedCount = 0;
        var detailSection = new StringBuilder();

        // --- Prefabs ---
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            var offenders = GetGraveyardDependencies(prefabPath);
            if (offenders.Count > 0)
            {
                flaggedCount++;
                detailSection.AppendLine($"[Prefab] {prefabPath}");
                foreach (string dep in offenders)
                {
                    detailSection.AppendLine($"    -> {dep}");
                    RecordOwner(perAssetOwners, dep, prefabPath);
                }
                detailSection.AppendLine();
            }
        }

        // --- Scenes ---
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            var offenders = GetGraveyardDependencies(scenePath);
            if (offenders.Count > 0)
            {
                flaggedCount++;
                detailSection.AppendLine($"[Scene] {scenePath}");
                foreach (string dep in offenders)
                {
                    detailSection.AppendLine($"    -> {dep}");
                    RecordOwner(perAssetOwners, dep, scenePath);
                }
                detailSection.AppendLine();
            }
        }

        // --- Summary: distinct graveyard assets still in use, ranked by reference count ---
        report.AppendLine("=== Graveyard Reference Report ===");
        report.AppendLine($"Generated: {System.DateTime.Now}");
        report.AppendLine($"Graveyard folder checked: {GraveyardPathPrefix}");
        report.AppendLine();

        if (perAssetOwners.Count == 0)
        {
            report.AppendLine("RESULT: 0 references found. Nothing in the project still uses");
            report.AppendLine("assets from the graveyard folder — it should be safe to delete.");
        }
        else
        {
            report.AppendLine($"RESULT: {perAssetOwners.Count} distinct graveyard asset(s) are still");
            report.AppendLine("referenced somewhere. Do NOT delete the graveyard folder yet —");
            report.AppendLine("fix the items below first, then re-run this check.");
        }

        report.AppendLine();
        report.AppendLine("--- Summary (ranked by usage count) ---");

        foreach (var kvp in perAssetOwners.OrderByDescending(kv => kv.Value.Count))
        {
            report.AppendLine($"{kvp.Value.Count,4}x  {kvp.Key}");
        }

        report.AppendLine();
        report.AppendLine("--- Full detail (per prefab/scene) ---");
        report.Append(detailSection);
        report.AppendLine($"Total flagged prefabs/scenes: {flaggedCount}");

        // Write to file and log a summary to the console.
        string outputPath = Path.Combine(Application.dataPath, "..", "GraveyardReferenceReport.txt");
        File.WriteAllText(outputPath, report.ToString());

        Debug.Log($"Graveyard reference audit complete. {perAssetOwners.Count} distinct asset(s) still " +
                   $"referenced across {flaggedCount} prefab(s)/scene(s).\nFull report written to: {outputPath}");
    }

    private static void RecordOwner(Dictionary<string, List<string>> map, string assetPath, string ownerPath)
    {
        if (!map.TryGetValue(assetPath, out var owners))
        {
            owners = new List<string>();
            map[assetPath] = owners;
        }
        owners.Add(ownerPath);
    }

    private static List<string> GetGraveyardDependencies(string assetPath)
    {
        string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);

        return dependencies
            .Where(dep => dep != assetPath)
            .Where(dep => RelevantExtensions.Contains(Path.GetExtension(dep).ToLowerInvariant()))
            .Where(dep => dep.StartsWith(GraveyardPathPrefix))
            .Distinct()
            .OrderBy(dep => dep)
            .ToList();
    }
}