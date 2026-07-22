using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

// EDITOR-ONLY. Must live in a folder named "Editor" anywhere under Assets (confirmed working at
// Assets/Scripts/Editor) so it isn't compiled into player builds.
//
// Copies per-part sprite PIVOT (and alignment/border) from one texture onto another. Built for the
// "traced new type art over an existing rigged sprite at the same canvas size" case, where every body
// part sits at the exact same position/size as the source, only recolored (plus a few added/removed
// parts like tail or eyes/mouth).
//
// Two ways to use it:
//   - Single Pair: manually drag in one Source/Target texture for a one-off fix.
//   - Batch By Type Folder: pick a Source Type and Target Type from the 20 elemental types, and it
//     matches files across Assets/Art/Characters/Bunnies/Base Art/<Type> by translating the type
//     token in the filename (e.g. Rabbit_Neutral_Type -> Rabbit_Fire_Type), then within each matched
//     file pair, matches individual body-part sprites by NAME (e.g. "Head", "Front Arm") and copies
//     pivot data for all matching pairs in one click.
//
// Does NOT copy skeleton/mesh/weights. This rig uses the PSD Importer's Character Mode, which stores
// the skeleton in a shared, hierarchical structure (sharedRigCharacterData/characterData) that isn't
// reachable through the public ISpriteBoneDataProvider/ISpriteMeshDataProvider scripting API — that API
// only writes each sprite's own (unused, in Character Mode) spriteBone/vertices entry, which the
// Skinning Editor doesn't read from for this rig type. Use the Skinning Editor's own Copy/Paste buttons
// for the skeleton (works once source/target have identical sprite names) — geometry/weights still need
// to be redone by hand per type.
//
// CAVEAT: this only copies numbers — it does not check that source and target are actually pixel-
// aligned. If a part sits in a different position than the original, the copied pivot will be wrong and
// need manual correction afterward. Parts whose name doesn't exist on both sides (or is duplicated
// within one file) are reported and skipped rather than guessed.
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

    // --- Batch fields ---
    private int sourceTypeIndex = 0; // Neutral
    private int targetTypeIndex = 1; // Fire
    private Vector2 resultsScroll;
    private readonly List<string> lastBatchResults = new List<string>();

    // --- Prefab sprite swap fields ---
    private GameObject targetPrefab;
    private int prefabTypeIndex = 1; // Fire
    private Vector2 prefabResultsScroll;
    private readonly List<string> prefabSwapResults = new List<string>();

    // The 13 body-part sprite names this rig actually has. A GameObject's own name (after stripping a
    // trailing "_1"/"_2" rotation-flip duplicate suffix) must match one of these exactly to be swapped —
    // this is what correctly leaves shared/generic parts alone (Eyes_Open, Mouth_Eating, Carrot, Z_Sleep,
    // the Rabbit_Neutral_Eyes_* expression overlays) since none of those match a bare part name.
    private static readonly HashSet<string> KnownPartNames = new HashSet<string>
    {
        "Eyes", "Front Ear", "Mouth", "Head", "Front Arm", "Front Thigh", "Front Foot",
        "Torso", "Back Arm", "Tail", "Back Ear", "Back Thigh", "Back Foot"
    };

    private static readonly Regex TrailingDuplicateSuffix = new Regex(@"_\d+$");

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
        EditorGUILayout.Space(15);
        DrawPrefabSwapSection();
    }

    private void DrawSinglePairSection()
    {
        EditorGUILayout.LabelField("Single Pair", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Copies each matching body-part sprite's pivot from Source onto Target. Useful for one-off " +
            "fixes or parts outside the standard type folders.",
            MessageType.Info);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source (already rigged)", sourceTexture, typeof(Texture2D), false);
        targetTexture = (Texture2D)EditorGUILayout.ObjectField("Target (needs pivot data)", targetTexture, typeof(Texture2D), false);

        EditorGUI.BeginDisabledGroup(sourceTexture == null || targetTexture == null);
        if (GUILayout.Button("Copy Pivot Data"))
        {
            string result = CopyPivotData(sourceTexture, targetTexture);
            Debug.Log(result);
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawBatchSection()
    {
        EditorGUILayout.LabelField("Batch By Type Folder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Matches files between {BaseArtFolder}/<Source Type> and {BaseArtFolder}/<Target Type> " +
            "by translating the type name in the filename, then matches each file's body-part sprites " +
            "by NAME and copies pivot data for every matched pair.",
            MessageType.Info);

        sourceTypeIndex = EditorGUILayout.Popup("Source Type", sourceTypeIndex, TypeNames);
        targetTypeIndex = EditorGUILayout.Popup("Target Type", targetTypeIndex, TypeNames);

        if (sourceTypeIndex == targetTypeIndex)
        {
            EditorGUILayout.HelpBox("Source and Target type can't be the same.", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(sourceTypeIndex == targetTypeIndex);
        if (GUILayout.Button("Copy All Matching Files In Folder"))
        {
            RunBatchCopy(TypeNames[sourceTypeIndex], TypeNames[targetTypeIndex]);
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

    private void RunBatchCopy(string sourceType, string targetType)
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

        Dictionary<string, string> targetLookup = BuildFilenameLookup(targetFolder);
        string[] sourceGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder });

        int copiedCount = 0;
        int skippedCount = 0;

        foreach (string guid in sourceGuids)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);

            // Filenames encode the type itself (e.g. Rabbit_Neutral_Type vs Rabbit_Fire_Type), so they
            // never match by literal equality across type folders. Translate the source filename into
            // its expected target-type equivalent before looking it up.
            string expectedTargetName = fileName.Replace(sourceType, targetType);

            if (!targetLookup.TryGetValue(expectedTargetName, out string targetPath))
            {
                lastBatchResults.Add($"SKIPPED (no match in {targetType} for '{expectedTargetName}'): {fileName}");
                skippedCount++;
                continue;
            }

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            Texture2D target = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);

            string result = CopyPivotData(source, target);
            lastBatchResults.Add(result);
            Debug.Log(result);
            if (!result.StartsWith("ERROR", StringComparison.Ordinal)) copiedCount++;
        }

        lastBatchResults.Insert(0,
            $"Done. Files processed: {copiedCount}, Skipped (no match): {skippedCount}.");

        Debug.Log(lastBatchResults[0]);
    }

    private void DrawPrefabSwapSection()
    {
        EditorGUILayout.LabelField("Swap Prefab Sprites By Type", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "For a prefab duplicated from Rabbit_Neutral_Idle (or another type): walks every " +
            "SpriteRenderer, matches its GameObject name (ignoring a trailing _1/_2 duplicate suffix) " +
            "against the chosen Type's body-part sprites in Base Art/<Type>/Rabbit_<Type>_Type.psb, and " +
            "swaps in that sprite. Also rebuilds each SpriteSkin's bone Transform list from the new " +
            "sprite's own bone order (fixes the 'one part looks deformed' bug caused by SpriteSkin " +
            "mapping bones by list position, not name). Parts with no exact name match (Eyes_Open, " +
            "Mouth_Eating, Carrot, Z_Sleep, the Rabbit_Neutral_Eyes_* overlays, etc.) are left untouched.",
            MessageType.Info);

        targetPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab To Update", targetPrefab, typeof(GameObject), false);
        prefabTypeIndex = EditorGUILayout.Popup("Sprite Source Type", prefabTypeIndex, TypeNames);

        EditorGUI.BeginDisabledGroup(targetPrefab == null);
        if (GUILayout.Button("Swap Sprites From Type Folder"))
        {
            RunPrefabSpriteSwap(targetPrefab, TypeNames[prefabTypeIndex]);
        }
        EditorGUI.EndDisabledGroup();

        if (prefabSwapResults.Count > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"Last run: {prefabSwapResults.Count} line(s)", EditorStyles.miniBoldLabel);
            prefabResultsScroll = EditorGUILayout.BeginScrollView(prefabResultsScroll, GUILayout.Height(180));
            foreach (string line in prefabSwapResults)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunPrefabSpriteSwap(GameObject prefabAsset, string type)
    {
        prefabSwapResults.Clear();

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrEmpty(prefabPath))
        {
            prefabSwapResults.Add("ERROR: Selected object is not a prefab asset.");
            return;
        }

        string psbPath = $"{BaseArtFolder}/{type}/Rabbit_{type}_Type.psb";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(psbPath) == null)
        {
            prefabSwapResults.Add($"ERROR: Could not find {psbPath}");
            return;
        }

        // Runtime Sprite objects, for assigning SpriteRenderer.sprite.
        Dictionary<string, Sprite> spritesByName = new Dictionary<string, Sprite>();
        foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(psbPath))
        {
            if (obj is Sprite sprite && !spritesByName.ContainsKey(sprite.name))
            {
                spritesByName[sprite.name] = sprite;
            }
        }

        // Bone name list per part, straight from the importer's own rig data (the source of truth for
        // bone order — see the "one part looks deformed" bug this is here to avoid).
        AssetImporter psbImporter = AssetImporter.GetAtPath(psbPath);
        ISpriteEditorDataProvider psbProvider = GetDataProvider(psbImporter);
        Dictionary<string, SpriteRect> rectsByName = BuildNameLookup(psbProvider.GetSpriteRects(), out _);
        ISpriteBoneDataProvider psbBoneProvider = psbProvider.GetDataProvider<ISpriteBoneDataProvider>();

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            int swapped = 0, skipped = 0, bonesFixed = 0;

            foreach (SpriteRenderer renderer in renderers)
            {
                string partName = TrailingDuplicateSuffix.Replace(renderer.gameObject.name, "");

                if (!KnownPartNames.Contains(partName) || !spritesByName.TryGetValue(partName, out Sprite newSprite))
                {
                    prefabSwapResults.Add($"SKIPPED: '{renderer.gameObject.name}' (no {type} match for part '{partName}')");
                    skipped++;
                    continue;
                }

                renderer.sprite = newSprite;
                swapped++;

                SpriteSkin spriteSkin = renderer.GetComponent<SpriteSkin>();
                if (spriteSkin != null && rectsByName.TryGetValue(partName, out SpriteRect rect))
                {
                    List<SpriteBone> bones = psbBoneProvider.GetBones(rect.spriteID);
                    if (bones != null && bones.Count > 0)
                    {
                        Transform[] boneTransforms = new Transform[bones.Count];
                        bool allFound = true;
                        for (int i = 0; i < bones.Count; i++)
                        {
                            Transform found = FindDeepChild(root.transform, bones[i].name);
                            boneTransforms[i] = found;
                            if (found == null) allFound = false;
                        }

                        if (allFound)
                        {
                            spriteSkin.SetBoneTransforms(boneTransforms);
                            spriteSkin.autoRebind = true;
                            bonesFixed++;
                        }
                        else
                        {
                            prefabSwapResults.Add($"WARNING: '{renderer.gameObject.name}' swapped, but couldn't find all bone Transforms by name in the prefab — check its SpriteSkin manually.");
                        }
                    }
                }

                prefabSwapResults.Add($"OK: '{renderer.gameObject.name}' -> {type} '{partName}'");
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            prefabSwapResults.Insert(0, $"Done. Swapped: {swapped}, Bone lists rebuilt: {bonesFixed}, Skipped: {skipped}.");
            Debug.Log(prefabSwapResults[0]);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // BFS over the whole prefab hierarchy rather than just the SpriteRenderer's own subtree — bones live
    // in a separate branch (the shared skeleton), not under the sprite-part GameObjects themselves.
    private static Transform FindDeepChild(Transform root, string name)
    {
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            if (current.name == name) return current;
            foreach (Transform child in current)
            {
                queue.Enqueue(child);
            }
        }
        return null;
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
    //
    // Matches sub-sprites by their SpriteRect NAME (e.g. "Head", "Front Arm"), not by array index or
    // by outer filename. A multi-part PSB's internal sprite order isn't guaranteed to match another
    // file's even when both have the same named parts (confirmed: Neutral/Plant/Shock order
    // "Back Arm, Tail" where Fire orders them "Tail, Back Arm"), so index-based copying would silently
    // assign one part's pivot to a different part once the order diverges.
    private static string CopyPivotData(Texture2D source, Texture2D target)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string targetPath = AssetDatabase.GetAssetPath(target);

        // Note: these are typically PSDImporter (2D PSD Importer package), not TextureImporter — the
        // .psb source files are imported through that package, and PSDImporter does not inherit from
        // TextureImporter. Use the base AssetImporter type instead; SaveAndReimport() lives there too.
        AssetImporter sourceImporter = AssetImporter.GetAtPath(sourcePath);
        AssetImporter targetImporter = AssetImporter.GetAtPath(targetPath);

        if (sourceImporter == null || targetImporter == null)
        {
            return $"ERROR: Could not find AssetImporter for '{source?.name}' or '{target?.name}'.";
        }

        ISpriteEditorDataProvider sourceProvider = GetDataProvider(sourceImporter);
        ISpriteEditorDataProvider targetProvider = GetDataProvider(targetImporter);

        SpriteRect[] sourceRects = sourceProvider.GetSpriteRects();
        SpriteRect[] targetRects = targetProvider.GetSpriteRects();

        if (sourceRects.Length == 0)
        {
            return $"ERROR: Source '{source.name}' has no sprite/pivot data to copy from.";
        }

        if (targetRects.Length == 0)
        {
            return $"ERROR: Target '{target.name}' has no sprite entries at all — check its Sprite Mode/import settings.";
        }

        Dictionary<string, SpriteRect> sourceByName = BuildNameLookup(sourceRects, out List<string> sourceDupes);
        Dictionary<string, int> targetIndexByName = BuildNameIndexLookup(targetRects, out List<string> targetDupes);

        int copiedCount = 0;
        List<string> unmatchedNames = new List<string>();

        foreach (KeyValuePair<string, int> kvp in targetIndexByName)
        {
            string partName = kvp.Key;
            int targetIndex = kvp.Value;

            if (!sourceByName.TryGetValue(partName, out SpriteRect sourceRect))
            {
                unmatchedNames.Add(string.IsNullOrEmpty(partName) ? "<unnamed>" : partName);
                continue;
            }

            SpriteRect targetRect = targetRects[targetIndex];
            targetRect.pivot = sourceRect.pivot;
            targetRect.alignment = sourceRect.alignment;
            targetRect.border = sourceRect.border;
            targetRects[targetIndex] = targetRect;

            copiedCount++;
        }

        targetProvider.SetSpriteRects(targetRects);
        targetProvider.Apply();

        targetImporter.SaveAndReimport();

        List<string> notes = new List<string>();
        if (sourceDupes.Count > 0) notes.Add($"source has duplicate part names, skipped: {string.Join(", ", sourceDupes)}");
        if (targetDupes.Count > 0) notes.Add($"target has duplicate part names, skipped: {string.Join(", ", targetDupes)}");
        if (unmatchedNames.Count > 0) notes.Add($"target parts with no source match: {string.Join(", ", unmatchedNames)}");
        string noteText = notes.Count > 0 ? $" ({string.Join("; ", notes)})" : string.Empty;

        return $"OK: '{source.name}' -> '{target.name}', {copiedCount} pivot(s) copied{noteText}";
    }

    // Names must be unique to be matched unambiguously; a name that appears more than once in a file
    // (e.g. a mis-named layer) is excluded from the lookup and reported back via duplicateNames instead
    // of guessing which occurrence is the "right" one.
    private static Dictionary<string, SpriteRect> BuildNameLookup(SpriteRect[] rects, out List<string> duplicateNames)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (SpriteRect rect in rects)
        {
            string name = rect.name ?? string.Empty;
            counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
        }

        duplicateNames = counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();

        Dictionary<string, SpriteRect> lookup = new Dictionary<string, SpriteRect>();
        foreach (SpriteRect rect in rects)
        {
            string name = rect.name ?? string.Empty;
            if (counts[name] == 1) lookup[name] = rect;
        }

        return lookup;
    }

    private static Dictionary<string, int> BuildNameIndexLookup(SpriteRect[] rects, out List<string> duplicateNames)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        for (int i = 0; i < rects.Length; i++)
        {
            string name = rects[i].name ?? string.Empty;
            counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
        }

        duplicateNames = counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();

        Dictionary<string, int> lookup = new Dictionary<string, int>();
        for (int i = 0; i < rects.Length; i++)
        {
            string name = rects[i].name ?? string.Empty;
            if (counts[name] == 1) lookup[name] = i;
        }

        return lookup;
    }

    private static ISpriteEditorDataProvider GetDataProvider(AssetImporter importer)
    {
        SpriteDataProviderFactories factory = new SpriteDataProviderFactories();
        factory.Init();

        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        return provider;
    }
}
