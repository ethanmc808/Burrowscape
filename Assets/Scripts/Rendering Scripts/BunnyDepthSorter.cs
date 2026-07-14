using UnityEngine;
using UnityEngine.Rendering;

// Makes a bunny's whole sprite rig (all its individually-layered body-part
// SpriteRenderers) sort as one unit against other bunnies, based on true
// distance from the camera along its view direction. Sorting by raw world
// position only works if the camera looks straight down one axis with zero
// tilt; any angled camera can put two bunnies at the same world axis value
// while they're still at different real distances from the camera. Distance
// along the camera's forward vector is correct regardless of camera angle,
// and "closer renders in front" needs no per-prefab sign guessing.
[RequireComponent(typeof(SortingGroup))]
public class BunnyDepthSorter : MonoBehaviour
{
    [Tooltip("World units per sorting-order step. Position is rounded to this resolution so tiny movement doesn't trigger a re-sort.")]
    [SerializeField] private float sortingResolution = 0.1f;

    private SortingGroup sortingGroup;
    private Transform cameraTransform;
    private int lastSortStep = int.MinValue;

    private void Awake()
    {
        sortingGroup = GetComponent<SortingGroup>();
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cameraTransform = cam.transform;
        }

        Vector3 toObject = transform.position - cameraTransform.position;
        float distanceAlongView = Vector3.Dot(toObject, cameraTransform.forward);

        int step = Mathf.RoundToInt(distanceAlongView / sortingResolution);

        if (step == lastSortStep)
            return;

        lastSortStep = step;
        sortingGroup.sortingOrder = -step; // closer to camera -> higher order -> renders in front
    }
}
