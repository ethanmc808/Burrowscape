using UnityEngine;

// A cheap translucent box used while dragging a placement around — deliberately NOT the real prefab
// (avoids instantiating every sprite/animator/RoomSpot child just to drag it around). Sized and
// positioned from the ACTUAL prefab's measured mesh bounds rather than its nominal footprint (e.g.
// "4x2x6"), since real prefab meshes run slightly larger than their nominal grid size (wall thickness,
// back wall overhang) — hardcoding the nominal size left the ghost visibly not flush with real rooms.
public class RoomGhostPreview : MonoBehaviour
{
    private static readonly Color ValidColor = new Color(0.2f, 1f, 0.2f, 0.45f);
    private static readonly Color InvalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);

    private MeshRenderer meshRenderer;

    // Rooms are instantiated with a 180-degree Y rotation (see BuildModeController), which negates
    // local X and local Z once mapped into world space — so this is the measured mesh's local center,
    // rotated the same way, giving the exact world-space offset from the placement anchor to where the
    // real prefab's mesh will actually sit once placed.
    private Vector3 pivotOffset;

    public static RoomGhostPreview Create(GameObject prefab)
    {
        Bounds bounds = MeasurePrefabLocalBounds(prefab);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "RoomGhostPreview";

        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.localScale = bounds.size;

        RoomGhostPreview ghost = go.AddComponent<RoomGhostPreview>();
        ghost.meshRenderer = go.GetComponent<MeshRenderer>();
        ghost.pivotOffset = new Vector3(-bounds.center.x, bounds.center.y, -bounds.center.z);

        // Dedicated overlay shader (GhostOverlay.shader) instead of a cloned/tweaked Standard material.
        // The old approach only disabled ZWrite (so the ghost didn't occlude things behind IT), but still
        // respected the depth TEST against opaque geometry already in the depth buffer — so any opaque
        // object nearer the camera (like a dirt tile) could still hide the ghost entirely. GhostOverlay's
        // ZTest Always ignores the depth buffer altogether, which is the only fix that's robust regardless
        // of how dirt/rooms happen to be positioned in Z. Falls back to the old cloned-primitive-material
        // approach if the shader can't be found (e.g. not yet added to the project), so this never leaves
        // the ghost using Unity's broken magenta error material.
        Shader ghostShader = Shader.Find("Custom/GhostOverlay");
        Material mat;
        if (ghostShader != null)
        {
            mat = new Material(ghostShader);
            mat.renderQueue = 4000; // Overlay — draws after every other queue (Background/Geometry/AlphaTest/Transparent)
        }
        else
        {
            Debug.LogWarning("RoomGhostPreview: Custom/GhostOverlay shader not found — falling back to a transparent clone of the default material. Add GhostOverlay.shader to the project to fix depth-occlusion against dirt/other opaque geometry.");
            mat = new Material(ghost.meshRenderer.sharedMaterial);
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }
        ghost.meshRenderer.material = mat;
        ghost.SetValid(false);

        return ghost;
    }

    // Instantiates the real prefab at the origin just long enough to measure its combined renderer
    // bounds, then destroys it immediately (synchronously, so it never lingers registered with
    // BaseLayoutManager for even a frame). Only runs once per EnterBuildMode call, not per-frame.
    private static Bounds MeasurePrefabLocalBounds(GameObject prefab)
    {
        if (prefab == null) return new Bounds(Vector3.zero, Vector3.one * 4f);

        GameObject temp = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();

        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one * 4f);
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        DestroyImmediate(temp);
        return bounds;
    }

    // worldPosition is the same anchor point BuildModeController instantiates the real prefab at.
    public void SetPosition(Vector3 worldPosition)
    {
        transform.position = worldPosition + pivotOffset;
    }

    public void SetValid(bool valid)
    {
        meshRenderer.material.color = valid ? ValidColor : InvalidColor;
    }

    public void DestroyGhost()
    {
        Destroy(gameObject);
    }
}