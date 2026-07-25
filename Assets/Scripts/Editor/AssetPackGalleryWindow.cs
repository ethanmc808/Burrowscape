using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor tool: pick a folder (e.g. an imported ithappy pack), and this generates
/// a brand new scene with every 3D model in that folder laid out in a grid,
/// fully textured/materialed, grouped into per-type parent GameObjects (e.g.
/// all "Generator" variants under one "Generator" parent). Use the Solo/Show
/// buttons in this window to isolate one type at a time in the Hierarchy.
///
/// Menu: Tools > Burrowscape > Asset Pack Gallery Viewer
/// </summary>
public class AssetPackGalleryWindow : EditorWindow
{
    private enum SearchMode { PrefabsOnly, ModelsOnly, Both }

    private DefaultAsset targetFolder;
    private float spacing = 4f;
    private bool includeLabels = true;
    private SearchMode searchMode = SearchMode.Both;

    // Strips trailing variant markers like "_01", "_2", "(3)" so "Generator_01"
    // and "Generator_02" collapse to the same group name "Generator".
    private static readonly Regex TrailingVariantPattern =
        new Regex(@"[\s_\-]*\(?\d+\)?$", RegexOptions.Compiled);

    // Populated after a gallery is generated; used to draw the Solo/Show buttons.
    private readonly List<GameObject> generatedGroups = new List<GameObject>();
    private Vector2 groupListScroll;

    [MenuItem("Tools/Burrowscape/Asset Pack Gallery Viewer")]
    public static void ShowWindow()
    {
        GetWindow<AssetPackGalleryWindow>("Asset Pack Gallery");
    }

    private void OnGUI()
    {
        GUILayout.Label("Generate a gallery scene from a folder of 3D assets", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Pack Folder", targetFolder, typeof(DefaultAsset), false);

        spacing = EditorGUILayout.FloatField("Grid Spacing", spacing);
        includeLabels = EditorGUILayout.Toggle("Add Name Labels", includeLabels);
        searchMode = (SearchMode)EditorGUILayout.EnumPopup("Search For", searchMode);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This creates a NEW scene (you'll be prompted to save your current one first). " +
            "It does not modify or save over any existing scene automatically.",
            MessageType.Info);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(targetFolder == null))
        {
            if (GUILayout.Button("Generate Gallery Scene", GUILayout.Height(30)))
            {
                GenerateGallery();
            }
        }

        DrawGroupControls();
    }

    private void DrawGroupControls()
    {
        // Clean out any null entries (e.g. after a scene change/reload).
        generatedGroups.RemoveAll(g => g == null);

        if (generatedGroups.Count == 0)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Groups ({generatedGroups.Count})", EditorStyles.boldLabel);

        if (GUILayout.Button("Show All"))
            ShowAllGroups();

        groupListScroll = EditorGUILayout.BeginScrollView(groupListScroll, GUILayout.Height(250));

        foreach (var group in generatedGroups)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(group.name);

            if (GUILayout.Button("Solo", GUILayout.Width(50)))
                SoloGroup(group);

            bool isActive = group.activeSelf;
            bool newActive = EditorGUILayout.Toggle(isActive, GUILayout.Width(20));
            if (newActive != isActive)
                group.SetActive(newActive);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void ShowAllGroups()
    {
        foreach (var group in generatedGroups)
            if (group != null)
                group.SetActive(true);
    }

    private void SoloGroup(GameObject target)
    {
        foreach (var group in generatedGroups)
            if (group != null)
                group.SetActive(group == target);
    }

    private void GenerateGallery()
    {
        string folderPath = AssetDatabase.GetAssetPath(targetFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("Selected object is not a valid project folder.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        generatedGroups.Clear();

        string[] searchFilters = searchMode switch
        {
            SearchMode.PrefabsOnly => new[] { "t:Prefab" },
            SearchMode.ModelsOnly => new[] { "t:Model" },
            _ => new[] { "t:Prefab", "t:Model" }
        };

        var uniquePaths = new HashSet<string>();
        foreach (string filter in searchFilters)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folderPath }))
                uniquePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        var sortedPaths = new List<string>(uniquePaths);
        sortedPaths.Sort();

        if (sortedPaths.Count == 0)
        {
            Debug.LogWarning($"No matching assets found in {folderPath} for search mode {searchMode}.");
            return;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(sortedPaths.Count));
        var root = new GameObject($"Gallery_{Path.GetFileName(folderPath)}");
        var groupParents = new Dictionary<string, GameObject>();
        int placedCount = 0;

        for (int i = 0; i < sortedPaths.Count; i++)
        {
            string assetPath = sortedPaths[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                continue;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string groupName = GetGroupName(fileName);

            if (!groupParents.TryGetValue(groupName, out GameObject groupParent))
            {
                groupParent = new GameObject(groupName);
                groupParent.transform.SetParent(root.transform);
                groupParent.transform.localPosition = Vector3.zero;
                groupParents[groupName] = groupParent;
                generatedGroups.Add(groupParent);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(groupParent.transform);
            instance.name = fileName;

            int row = placedCount / columns;
            int col = placedCount % columns;
            instance.transform.position = new Vector3(col * spacing, 0f, row * spacing);

            if (includeLabels)
                AddLabel(instance, fileName);

            placedCount++;
        }

        generatedGroups.Sort((a, b) => string.Compare(a.name, b.name));

        PositionCameraAndLight(columns, Mathf.CeilToInt((float)placedCount / columns), spacing);

        Debug.Log($"Gallery generated: {placedCount} asset(s) from {folderPath}, " +
                   $"organized into {groupParents.Count} group(s).");
    }

    private static string GetGroupName(string assetFileName)
    {
        string name = TrailingVariantPattern.Replace(assetFileName, "").Trim(' ', '_', '-');
        return string.IsNullOrEmpty(name) ? assetFileName : name;
    }

    private static void AddLabel(GameObject target, string labelText)
    {
        var labelGO = new GameObject($"Label_{labelText}");
        labelGO.transform.SetParent(target.transform);
        labelGO.transform.localPosition = new Vector3(0f, 2.5f, 0f);

        var textMesh = labelGO.AddComponent<TextMesh>();
        textMesh.text = labelText;
        textMesh.characterSize = 0.15f;
        textMesh.fontSize = 48;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = Color.white;
    }

    private static void PositionCameraAndLight(int columns, int rows, float spacing)
    {
        float gridWidth = columns * spacing;
        float gridDepth = rows * spacing;
        float distance = Mathf.Max(gridWidth, gridDepth);

        var camGO = new GameObject("Gallery Camera");
        camGO.AddComponent<Camera>();
        camGO.transform.position = new Vector3(gridWidth * 0.5f, distance * 0.9f, -distance * 0.6f);
        camGO.transform.LookAt(new Vector3(gridWidth * 0.5f, 0f, gridDepth * 0.5f));

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            var lightGO = new GameObject("Gallery Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Selection.activeGameObject = camGO;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}