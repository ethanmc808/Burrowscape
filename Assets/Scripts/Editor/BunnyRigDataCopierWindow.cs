using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

// EDITOR-ONLY. Must live in a folder named "Editor" anywhere under Assets (confirmed working at
// Assets/Scripts/Editor) so it isn't compiled into player builds.
//
// Copies a sprite's 2D Animation skinning data (pivot, and optionally bones/mesh/weights) from one
// texture onto another. Built for the "traced new type art over an existing rigged sprite at the same
// canvas size" case, where joint positions and mesh shape are still valid for the new art.
//
// Two ways to use it:
//   - Single Pair: manually drag in one Source/Target texture for a one-off fix.
//   - Batch By Type Folder: pick a Source Type and Target Type from the 20 elemental types, and it
//     matches every texture in Assets/Art/Characters/Bunnies/Base Art/<Type> by filename and copies
//     rig data for all matching pairs in one click.
//
// CAVEAT: this only copies numbers — it does not check that source and target are actually pixel-
// aligned. If a new part sits in a different position than the original, the copied bone/mesh data
// will be wrong and need manual correction in the Skinning Editor afterward.
public class BunnyRigDataCopierWindow : EditorWindow
{
    private static readonly string[] TypeNames =
    {
        "Neutral", "Fire", "Water", "Plant", "Shock", "Ice", "Mind", "Toxic", "Sound", "Insect",
        "Melee", "Stone", "Earth", "Air", "Metal", "Pixie", "Light", "Ghost", "Dark", "Draco"
    };

    private const string BaseArtFolder = "Assets/Art/Characters/Bunnies/Base Art";

    // --- Single pair fields ---
    private Texture2D sourceTexture;
    private Texture2D targetTexture;
    private bool pivotOnly;

    // --- Batch fields ---
    private int sourceTypeIndex = 0; // Neutral
    private int targetTypeIndex = 1; // Fire
    private string pivotOnlyKeywords = "Eye,Mouth";
    private Vector2 resultsScroll;
    private readonly List<string> lastBatchResults = new List<string>();

    [MenuItem("Burrowscape/Copy Rig Data Between Sprites")]
    private static void ShowWindow()
    {
        GetWindow<BunnyRigDataCopierWindow>("Copy Rig Data");
    }

    private void OnGUI()
    {
        DrawSinglePairSection();
        EditorGUILayout.Space(15);
        DrawBatchSection();
    }

    private void DrawSinglePairSection()
    {
        EditorGUILayout.LabelField("Single Pair", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Copies pivot (and, unless Pivot Only is checked, bones/mesh/weights) from Source onto " +
            "Target. Useful for one-off fixes or parts outside the standard type folders.",
            MessageType.Info);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source (already rigged)", sourceTexture, typeof(Texture2D), false);
        targetTexture = (Texture2D)EditorGUILayout.ObjectField("Target (needs rig data)", targetTexture, typeof(Texture2D), false);

        pivotOnly = EditorGUILayout.Toggle(
            new GUIContent("Pivot Only", "Use for parts like eyes/mouth that differ in size and don't need bone/mesh deformation."),
            pivotOnly);

        EditorGUI.BeginDisabledGroup(sourceTexture == null || targetTexture == null);
        if (GUILayout.Button("Copy Rig Data"))
        {
            string result = CopySpriteData(sourceTexture, targetTexture, pivotOnly);
            Debug.Log(result);
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawBatchSection()
    {
        EditorGUILayout.LabelField("Batch By Type Folder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Matches textures by filename between {BaseArtFolder}/<Source Type> and " +
            $"{BaseArtFolder}/<Target Type>, and copies rig data for every matching pair. Filenames " +
            "containing any of the Pivot Only Keywords (comma-separated, case-insensitive) get a " +
            "pivot-only copy; everything else gets the full copy.",
            MessageType.Info);

        sourceTypeIndex = EditorGUILayout.Popup("Source Type", sourceTypeIndex, TypeNames);
        targetTypeIndex = EditorGUILayout.Popup("Target Type", targetTypeIndex, TypeNames);
        pivotOnlyKeywords = EditorGUILayout.TextField(
            new GUIContent("Pivot Only Keywords", "Comma-separated. Any filename containing one of these (case-insensitive) is copied Pivot Only instead of the full rig."),
            pivotOnlyKeywords);

        if (sourceTypeIndex == targetTypeIndex)
        {
            EditorGUILayout.HelpBox("Source and Target type can't be the same.", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(sourceTypeIndex == targetTypeIndex);
        if (GUILayout.Button("Copy All Matching Files In Folder"))
        {
            RunBatchCopy(TypeNames[sourceTypeIndex], TypeNames[targetTypeIndex], pivotOnlyKeywords);
        }
        EditorGUI.EndDisabledGroup();

        if (lastBatchResults.Count > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"Last run: {lastBatchResults.Count} line(s)", EditorStyles.miniBoldLabel);
            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll, GUILayout.Height(180));
            foreach (string line in lastBatchResults)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunBatchCopy(string sourceType, string targetType, string pivotOnlyKeywordsCsv)
    {
        lastBatchResults.Clear();

        string sourceFolder = $"{BaseArtFolder}/{sourceType}";
        string targetFolder = $"{BaseArtFolder}/{targetType}";

        if (!AssetDatabase.IsValidFolder(sourceFolder))
        {
            lastBatchResults.Add($"ERROR: Source folder not found: {sourceFolder}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(targetFolder))
        {
            lastBatchResults.Add($"ERROR: Target folder not found: {targetFolder}");
            return;
        }

        string[] keywords = pivotOnlyKeywordsCsv
            .Split(',')
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToArray();

        Dictionary<string, string> targetLookup = BuildFilenameLookup(targetFolder);
        string[] sourceGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder });

        int fullCopyCount = 0;
        int pivotOnlyCount = 0;
        int skippedCount = 0;

        foreach (string guid in sourceGuids)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);

            if (!targetLookup.TryGetValue(fileName, out string targetPath))
            {
                lastBatchResults.Add($"SKIPPED (no match in {targetType}): {fileName}");
                skippedCount++;
                continue;
            }

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            Texture2D target = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);

            bool isPivotOnly = keywords.Any(k => fileName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

            string result = CopySpriteData(source, target, isPivotOnly);
            lastBatchResults.Add(result);

            if (isPivotOnly) pivotOnlyCount++;
            else fullCopyCount++;
        }

        lastBatchResults.Insert(0,
            $"Done. Full copy: {fullCopyCount}, Pivot only: {pivotOnlyCount}, Skipped (no match): {skippedCount}.");

        Debug.Log(lastBatchResults[0]);
    }

    private static Dictionary<string, string> BuildFilenameLookup(string folder)
    {
        Dictionary<string, string> lookup = new Dictionary<string, string>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            if (lookup.ContainsKey(fileName))
            {
                Debug.LogWarning($"Duplicate filename '{fileName}' found in {folder} — only the first match will be used. Rename one to avoid ambiguity.");
                continue;
            }

            lookup[fileName] = path;
        }

        return lookup;
    }

    // Returns a one-line summary instead of logging directly, so both the single-pair button and the
    // batch runner can report results their own way (Console vs. the in-window results list).
    private static string CopySpriteData(Texture2D source, Texture2D target, bool pivotOnly)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string targetPath = AssetDatabase.GetAssetPath(target);

        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        TextureImporter targetImporter = AssetImporter.GetAtPath(targetPath) as TextureImporter;

        if (sourceImporter == null || targetImporter == null)
        {
            return $"ERROR: Could not find TextureImporter for '{source?.name}' or '{target?.name}'.";
        }

        ISpriteEditorDataProvider sourceProvider = GetDataProvider(sourceImporter);
        ISpriteEditorDataProvider targetProvider = GetDataProvider(targetImporter);

        SpriteRect[] sourceRects = sourceProvider.GetSpriteRects();
        SpriteRect[] targetRects = targetProvider.GetSpriteRects();

        if (sourceRects.Length == 0)
        {
            return $"ERROR: Source '{source.name}' has no sprite/rig data to copy from.";
        }

        if (targetRects.Length == 0)
        {
            return $"ERROR: Target '{target.name}' has no sprite entries at all — check its Sprite Mode/import settings.";
        }

        string mismatchNote = targetRects.Length != sourceRects.Length
            ? $" (WARNING: source had {sourceRects.Length} sprite(s), target had {targetRects.Length} — matched by index)"
            : string.Empty;

        ISpriteBoneDataProvider sourceBoneProvider = sourceProvider.GetDataProvider<ISpriteBoneDataProvider>();
        ISpriteBoneDataProvider targetBoneProvider = targetProvider.GetDataProvider<ISpriteBoneDataProvider>();
        ISpriteMeshDataProvider sourceMeshProvider = sourceProvider.GetDataProvider<ISpriteMeshDataProvider>();
        ISpriteMeshDataProvider targetMeshProvider = targetProvider.GetDataProvider<ISpriteMeshDataProvider>();

        int count = Mathf.Min(sourceRects.Length, targetRects.Length);

        for (int i = 0; i < count; i++)
        {
            SpriteRect sourceRect = sourceRects[i];
            SpriteRect targetRect = targetRects[i];

            targetRect.pivot = sourceRect.pivot;
            targetRect.border = sourceRect.border;
            targetRects[i] = targetRect;

            if (pivotOnly) continue;

            List<SpriteBone> bones = sourceBoneProvider.GetBones(sourceRect.spriteID);
            targetBoneProvider.SetBones(targetRect.spriteID, bones);

            Vertex2DMetaData[] vertices = sourceMeshProvider.GetVertices(sourceRect.spriteID);
            targetMeshProvider.SetVertices(targetRect.spriteID, vertices);

            int[] indices = sourceMeshProvider.GetIndices(sourceRect.spriteID);
            targetMeshProvider.SetIndices(targetRect.spriteID, indices);
        }

        targetProvider.SetSpriteRects(targetRects);
        targetProvider.Apply();

        targetImporter.SaveAndReimport();

        string mode = pivotOnly ? "pivot only" : "full";
        return $"OK ({mode}): '{source.name}' -> '{target.name}'{mismatchNote}";
    }

    private static ISpriteEditorDataProvider GetDataProvider(TextureImporter importer)
    {
        SpriteDataProviderFactories factory = new SpriteDataProviderFactories();
        factory.Init();

        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        return provider;
    }
}
