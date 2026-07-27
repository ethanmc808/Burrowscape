using UnityEngine;

// Renders a decorative, non-gameplay duplicate of whichever bunny is currently selected in
// ForagingTripDetailUI, endlessly "walking" across a small offscreen stage — see the Foraging Trip
// Detail Panel + Item Expansion design doc. spawnAnchor should sit far outside any real camera's view
// (isolation by distance, e.g. (0, -5000, 0)) so the duplicate never appears in the actual game world —
// only through previewCamera's RenderTexture, shown via a RawImage in the detail panel. The duplicate
// has its NPCBunny script and every collider disabled, so none of the real AI/state machine or click
// handling ever runs on it — it only exists to sit there and play its walk animation.
public class ForagingPreviewStage : MonoBehaviour
{
    public static ForagingPreviewStage Instance { get; private set; }

    [Tooltip("Where the duplicate spawns and resets to once it loops. Should sit far from any real camera's view.")]
    [SerializeField] private Transform spawnAnchor;
    [SerializeField] private float walkSpeed = 1f;
    [Tooltip("Duplicate resets back to spawnAnchor.position once it walks this far to the right, faking an endless walk on a finite stage.")]
    [SerializeField] private float loopDistance = 6f;
    [Tooltip("The duplicate's own default-facing convention (NPCBunny.bunnyFacesLeftByDefault) isn't reachable with its script disabled — flip this by eye if it looks like it's walking backwards.")]
    [SerializeField] private bool flipToFaceRight = false;
    [Tooltip("Animator bool parameter that plays the walk cycle — matches NPCBunny's own default 'IsMoving' parameter name.")]
    [SerializeField] private string isMovingParam = "IsMoving";

    private GameObject currentDuplicate;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Destroys any previous duplicate and spawns a fresh one matching sourceBunny's own prefab.
    public void ShowBunny(NPCBunny sourceBunny)
    {
        ClearBunny();

        if (sourceBunny == null || sourceBunny.TypeDefinition == null || sourceBunny.TypeDefinition.prefab == null || spawnAnchor == null)
            return;

        currentDuplicate = Instantiate(sourceBunny.TypeDefinition.prefab, spawnAnchor.position, spawnAnchor.rotation, spawnAnchor);

        // Strip every piece of real gameplay behavior — this object only exists to sit on the stage and
        // play its walk animation, never to think, path, or be clickable.
        foreach (NPCBunny npc in currentDuplicate.GetComponentsInChildren<NPCBunny>())
            npc.enabled = false;
        foreach (Collider2D col in currentDuplicate.GetComponentsInChildren<Collider2D>())
            col.enabled = false;
        foreach (Collider col in currentDuplicate.GetComponentsInChildren<Collider>())
            col.enabled = false;

        if (flipToFaceRight)
        {
            Vector3 scale = currentDuplicate.transform.localScale;
            currentDuplicate.transform.localScale = new Vector3(-Mathf.Abs(scale.x), scale.y, scale.z);
        }

        Animator animator = currentDuplicate.GetComponentInChildren<Animator>();
        if (animator != null) animator.SetBool(isMovingParam, true);
    }

    public void ClearBunny()
    {
        if (currentDuplicate != null) Destroy(currentDuplicate);
        currentDuplicate = null;
    }

    private void Update()
    {
        if (currentDuplicate == null || spawnAnchor == null) return;

        currentDuplicate.transform.position += Vector3.right * walkSpeed * Time.deltaTime;
        if (currentDuplicate.transform.position.x >= spawnAnchor.position.x + loopDistance)
            currentDuplicate.transform.position = spawnAnchor.position;
    }
}
