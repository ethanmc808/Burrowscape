using UnityEngine;

// Drives a Line Renderer's positions to look like a crackling connective line between two world-space
// points, by re-jittering the in-between points on a short interval instead of every frame (a line that
// re-jitters every single frame reads as "vibrating noise" rather than "crackling" at normal frame rates).
// Put this on the same GameObject as the Line Renderer it should drive (auto-fetched via GetComponent).
//
// Not currently used by any shipped attack — Shock ended up wanting a punchier sprite-flash snapshot
// instead (see LightningBoltFlash), so this is parked here for whichever future attack/type wants a
// continuously-animating connective line effect.
[RequireComponent(typeof(LineRenderer))]
public class LineAttackRenderer : MonoBehaviour
{
    [SerializeField] private int segmentCount = 10;
    [SerializeField] private float jitterAmount = 0.15f;
    [SerializeField] private float reJitterInterval = 0.05f;

    private LineRenderer line;
    private AttackInstance ownerAttack;
    private Vector3 fromPoint;
    private Vector3 toPoint;
    private float timer;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = segmentCount;
        // Auto-tracks the parent AttackInstance's attacker/target so no manual wiring is needed on the
        // prefab — SetEndpoints below remains available for a standalone/test setup with no AttackInstance.
        ownerAttack = GetComponentInParent<AttackInstance>();
    }

    // Call once per frame (or once at setup, for a stationary melee bolt) with the two endpoints the bolt
    // should stretch between — attacker's origin and the target's position. Only needed if this isn't a
    // child of an AttackInstance (which is auto-tracked instead).
    public void SetEndpoints(Vector3 from, Vector3 to)
    {
        fromPoint = from;
        toPoint = to;
    }

    private void Update()
    {
        if (ownerAttack != null)
        {
            if (ownerAttack.AttackerTransform != null) fromPoint = ownerAttack.AttackerTransform.position;
            if (ownerAttack.TargetTransform != null) toPoint = ownerAttack.TargetTransform.position;
        }

        timer += Time.deltaTime;
        if (timer < reJitterInterval) return;
        timer = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (float)(segmentCount - 1);
            Vector3 pointOnLine = Vector3.Lerp(fromPoint, toPoint, t);

            // Endpoints (t == 0 or 1) stay exactly on the attacker/target so the bolt doesn't visibly
            // detach from either end — only the interior points jitter.
            if (i > 0 && i < segmentCount - 1)
            {
                pointOnLine += (Vector3)Random.insideUnitCircle * jitterAmount;
            }

            line.SetPosition(i, pointOnLine);
        }
    }
}
