using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// EDITOR-ONLY. Converts just the materials in the current Project window SELECTION (a folder, or
// individual .mat assets) from a legacy Built-in-RP shader to URP Lit — unlike Window > Rendering >
// Render Pipeline Converter, which always rescans every material in the entire project (~10 min on this
// project's asset count), this only ever touches what's selected, so re-running it after importing one
// more asset pack is instant. Written because every newly-imported non-URP asset pack (see
// GPVFX_POTIONS PACK) ships materials that render magenta in this project until converted.
//
// Covers the common cases an asset-store pack's materials actually use: Opaque, AlphaTest/Cutout, and
// AlphaBlend/Transparent, each with an optional _EMISSION channel — this is NOT a full reimplementation of
// Unity's own internal MaterialUpgrader (which also handles specular workflows, detail maps, etc.), just
// enough to unblock straightforward asset-pack materials. If a converted material still looks wrong,
// double-check its Surface Type/property values in the Inspector by hand — this tool gets you most of the
// way, not guaranteed pixel-perfect for every shader this could technically encounter.
public static class SelectedMaterialsToURPConverter
{
    [MenuItem("Assets/Convert Selected Materials to URP")]
    private static void ConvertSelected()
    {
        List<Material> materials = CollectSelectedMaterials();
        if (materials.Count == 0)
        {
            EditorUtility.DisplayDialog("Convert Selected Materials to URP", "No materials found in the current selection (select a folder or one or more .mat assets).", "OK");
            return;
        }

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("SelectedMaterialsToURPConverter: couldn't find \"Universal Render Pipeline/Lit\" — is the URP package installed?");
            return;
        }

        int converted = 0, skipped = 0;
        foreach (Material mat in materials)
        {
            if (mat.shader != null && mat.shader.name.StartsWith("Universal Render Pipeline/"))
            {
                skipped++; // already URP, nothing to do
                continue;
            }

            ConvertOne(mat, urpLit);
            converted++;
        }

        AssetDatabase.SaveAssets();
        string summary = $"Converted {converted} material(s) to URP Lit ({skipped} already were).";
        Debug.Log($"[SelectedMaterialsToURPConverter] {summary}");
        EditorUtility.DisplayDialog("Convert Selected Materials to URP", summary, "OK");
    }

    [MenuItem("Assets/Convert Selected Materials to URP", true)]
    private static bool ValidateConvertSelected() => Selection.objects != null && Selection.objects.Length > 0;

    private static List<Material> CollectSelectedMaterials()
    {
        HashSet<Material> materials = new HashSet<Material>();

        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (mat != null) materials.Add(mat);
                }
            }
            else if (obj is Material mat)
            {
                materials.Add(mat);
            }
        }

        return materials.ToList();
    }

    // Legacy property names -> URP Lit equivalents, copied where present. Blend/alpha-clip setup is
    // inferred from whichever keyword the old shader had (_ALPHATEST_ON / _ALPHABLEND_ON), matching what
    // every asset-store Standard-shader-derived material actually sets.
    private static void ConvertOne(Material mat, Shader urpLit)
    {
        bool wasAlphaTest = mat.IsKeywordEnabled("_ALPHATEST_ON");
        bool wasAlphaBlend = mat.IsKeywordEnabled("_ALPHABLEND_ON") || mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
        bool hadEmission = mat.IsKeywordEnabled("_EMISSION") || mat.HasProperty("_EmissionColor");

        Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        Vector2 mainTexScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 mainTexOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
        Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
        Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
        Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;

        Undo.RecordObject(mat, "Convert Material to URP");
        mat.shader = urpLit;

        if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
        mat.SetTextureScale("_BaseMap", mainTexScale);
        mat.SetTextureOffset("_BaseMap", mainTexOffset);
        mat.SetColor("_BaseColor", color);

        if (wasAlphaBlend)
        {
            // Transparent surface — Surface Type = Transparent, standard alpha blend, no depth write.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (wasAlphaTest)
        {
            // Opaque surface with alpha-clipped cutout — Surface Type stays Opaque, Alpha Clipping on.
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", cutoff);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_ZWrite", 1);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_ZWrite", 1);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        if (hadEmission)
        {
            mat.SetColor("_EmissionColor", emissionColor);
            if (emissionMap != null) mat.SetTexture("_EmissionMap", emissionMap);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(mat);
    }
}
