using UnityEngine;

// Cancels an ancestor's X flip (e.g. NPCBunny's facing-direction flip on bunnyScaleRoot)
// so this object's own sprite/text always renders unmirrored, no matter which way the
// bunny is facing. Target sign is negative because this rig's art defaults to facing
// LEFT unflipped (bunnyFacesLeftByDefault) — the text was authored to read correctly
// when bunnyScaleRoot is flipped negative (facing right), not at its default pose.
public class CounterFlipX : MonoBehaviour
{
    private const float TargetWorldSign = -1f;

    private float baseScaleX;

    private void Awake()
    {
        baseScaleX = Mathf.Abs(transform.localScale.x);
    }

    private void LateUpdate()
    {
        float parentSign = transform.parent != null ? Mathf.Sign(transform.parent.lossyScale.x) : 1f;
        if (parentSign == 0f) parentSign = 1f;

        Vector3 local = transform.localScale;
        local.x = baseScaleX * TargetWorldSign * parentSign;
        transform.localScale = local;
    }
}
