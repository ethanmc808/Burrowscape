using UnityEngine;
using UnityEngine.Rendering;

// Same approach as BunnyDepthSorter, applied to enemies: makes an enemy's
// whole sprite rig sort as one unit based on true distance from the camera
// along its view direction, so enemies and bunnies compare correctly
// regardless of camera angle.
[RequireComponent(typeof(SortingGroup))]
public class EnemyDepthSorter : MonoBehaviour
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
