using UnityEditor;
using UnityEngine;

// One-off Editor utility — generates a simple N-sided cone Mesh asset for Horn Sting's mesh-based
// replacement of its old 2D billboard triangle (see project_burrowscape_hornsting_attack_vfx memory).
// Not a runtime script; run once via the menu item below, then delete/ignore.
//
// Tip points along local +X with the base centered at the origin, matching AttackInstance's existing
// "art must be authored pointing along local +X" convention for rotateToFaceTravelDirection (see that
// field's comment on AttackInstance.cs) — so no extra facing correction is needed on top of this.
public static class ConeMeshGenerator
{
    private const int Sides = 14;
    private const float Length = 1f; // tip at local +X = Length, base ring at local X = 0
    private const float Radius = 0.28f;
    private const string SavePath = "Assets/Art/Attacks/BaseArt/Mesh_HornCone.asset";

    [MenuItem("Burrowscape/Generate Horn Cone Mesh")]
    private static void Generate()
    {
        // Vertices: 1 tip + Sides base-ring verts (for the tapered side faces) + 1 base-center +
        // Sides base-ring verts again (a second copy, so the cap can have its own normals distinct from
        // the tapered sides without RecalculateNormals blending them into a smeared average).
        var vertices = new Vector3[1 + Sides + 1 + Sides];
        var triangles = new int[(Sides * 3) + (Sides * 3)];

        vertices[0] = new Vector3(Length, 0f, 0f); // tip

        for (int i = 0; i < Sides; i++)
        {
            float angle = i * Mathf.PI * 2f / Sides;
            Vector3 ringPoint = new Vector3(0f, Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius);
            vertices[1 + i] = ringPoint; // side ring
            vertices[1 + Sides + 1 + i] = ringPoint; // cap ring (duplicate verts, distinct normals)
        }
        vertices[1 + Sides] = new Vector3(0f, 0f, 0f); // base center

        int t = 0;
        // Tapered side faces (tip -> ring[i] -> ring[i+1]), wound for an outward-facing normal.
        for (int i = 0; i < Sides; i++)
        {
            int a = 1 + i;
            int b = 1 + (i + 1) % Sides;
            triangles[t++] = 0;
            triangles[t++] = b;
            triangles[t++] = a;
        }
        // Base cap (center -> ring[i+1] -> ring[i]), wound to face backward (-X) so the cap is visible
        // from behind rather than culled.
        int capCenter = 1 + Sides;
        int capRingStart = capCenter + 1;
        for (int i = 0; i < Sides; i++)
        {
            int a = capRingStart + i;
            int b = capRingStart + (i + 1) % Sides;
            triangles[t++] = capCenter;
            triangles[t++] = a;
            triangles[t++] = b;
        }

        var mesh = new Mesh { name = "Mesh_HornCone" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        AssetDatabase.CreateAsset(mesh, SavePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"ConeMeshGenerator: saved {SavePath}");
    }
}
