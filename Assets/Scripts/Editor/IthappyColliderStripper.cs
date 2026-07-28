using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// Every ithappy asset pack ships each mesh prefab with a live Collider (usually MeshCollider) from
// import. These props are purely decorative room dressing in Burrowscape — 2D bunnies never physically
// collide with 3D geometry — so those colliders serve no gameplay purpose, and instead sit in front of
// a room's own click-target BoxCollider along the camera ray. Since OnMouseDown only fires on the
// single NEAREST collider with no fallthrough, clicking on/near a piece of decorative furniture hits
// its inert MeshCollider first and the click silently goes nowhere, instead of reaching
// RoomUpgradeClickHandler/RoomClickHandler on the room root. See project memory on click/raycast
// fragility for the same nearest-collider-wins failure mode elsewhere in this system.
//
// Fixed at the asset source (every ithappy prefab) rather than per-placement, so every room that already
// uses these props gets fixed at once and every future placement is safe with no extra step. Same
// LoadPrefabContents/SaveAsPrefabAsset bulk-edit pattern as RoomReferenceFixer/RoomDataGenerator.
public static class IthappyColliderStripper
{
    private const string IthappyFolder = "Assets/ithappy";

    [MenuItem("Burrowscape/Strip Colliders From ithappy Assets")]
    public static void StripColliders()
    {
        List<string> prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { IthappyFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();

        int prefabsFixed = 0;
        int collidersRemoved = 0;

        foreach (string path in prefabPaths)
        {
            GameObject contentRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Collider[] colliders = contentRoot.GetComponentsInChildren<Collider>(true);
                if (colliders.Length == 0) continue;

                foreach (Collider c in colliders)
                    Object.DestroyImmediate(c, true);

                PrefabUtility.SaveAsPrefabAsset(contentRoot, path);
                prefabsFixed++;
                collidersRemoved += colliders.Length;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentRoot);
            }
        }

        string summary = $"Stripped {collidersRemoved} collider(s) across {prefabsFixed} ithappy prefab(s).";
        Debug.Log($"[IthappyColliderStripper] {summary}");
        EditorUtility.DisplayDialog("Strip Colliders From ithappy Assets", summary, "OK");
    }
}
