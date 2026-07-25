using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: force-assigns a chosen material onto every Renderer that's
/// currently gray (null material, or Unity's built-in default placeholder
/// material) under a target scope. Bypasses FBX material remap entirely —
/// use this when the normal Materials/Remap workflow isn't surfacing the
/// broken slot.
///
/// Menu: Tools > Burrowscape > Gray Material Fixer
/// </summary>
public class GrayMaterialFixerWindow : EditorWindow
{
    private Material replacementMaterial;
    private GameObject scopeRoot;
    private bool forceReplaceAll = false;

    [MenuItem("Tools/Burrowscape/Gray Material Fixer")]
    public static void ShowWindow()
    {
        GetWindow<GrayMaterialFixerWindow>("Gray Material Fixer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Force-fix gray/missing materials", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        replacementMaterial = (Material)EditorGUILayout.ObjectField(
            "Replacement Material", replacementMaterial, typeof(Material), false);

        scopeRoot = (GameObject)EditorGUILayout.ObjectField(
            "Scope (optional)", scopeRoot, typeof(GameObject), true);

        EditorGUILayout.HelpBox(
            "Leave 'Scope' empty to scan the ENTIRE open scene. " +
            "Set it to a parent object (e.g. your gallery's group folder) " +
            "to only fix objects under that one.",
            MessageType.Info);

        forceReplaceAll = EditorGUILayout.Toggle("Force Replace ALL Materials", forceReplaceAll);

        EditorGUILayout.HelpBox(
            forceReplaceAll
                ? "WARNING: this will replace EVERY material slot in scope with the " +
                  "replacement material, even ones that already look correct."
                : "Only replaces slots that are null or using Unity's default placeholder material.",
            forceReplaceAll ? MessageType.Warning : MessageType.None);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(replacementMaterial == null))
        {
            if (GUILayout.Button("Fix Gray Materials", GUILayout.Height(30)))
            {
                FixGrayMaterials();
            }
        }
    }

    private void FixGrayMaterials()
    {
        Renderer[] renderers = scopeRoot != null
            ? scopeRoot.GetComponentsInChildren<Renderer>(true)
            : Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        int fixedSlotCount = 0;
        int fixedRendererCount = 0;

        foreach (Renderer renderer in renderers)
        {
            Material[] mats = renderer.sharedMaterials;
            bool changedThisRenderer = false;

            for (int i = 0; i < mats.Length; i++)
            {
                bool isGray = mats[i] == null || IsDefaultMaterial(mats[i]);

                if (forceReplaceAll || isGray)
                {
                    mats[i] = replacementMaterial;
                    fixedSlotCount++;
                    changedThisRenderer = true;
                }
            }

            if (changedThisRenderer)
            {
                Undo.RecordObject(renderer, "Fix Gray Materials");
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer);
                fixedRendererCount++;
            }
        }

        Debug.Log($"Gray Material Fixer: replaced {fixedSlotCount} material slot(s) " +
                   $"across {fixedRendererCount} renderer(s).");
    }

    private static bool IsDefaultMaterial(Material mat)
    {
        // Unity's built-in placeholder materials for unlinked FBX slots typically
        // carry one of these names.
        return mat.name.Contains("Default-Material") || mat.name.Contains("Default-Diffuse");
    }
}