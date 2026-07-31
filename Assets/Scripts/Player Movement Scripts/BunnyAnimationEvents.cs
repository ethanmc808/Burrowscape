using UnityEngine;

public class BunnyAnimationEvents : MonoBehaviour
{
    private BunnyMovement bunnyMovement;
    private NPCBunny npcBunny;

    private void Awake()
    {
        bunnyMovement = GetComponentInParent<BunnyMovement>();
        npcBunny = GetComponentInParent<NPCBunny>();
    }

    public void PerformJump()
    {
        if (bunnyMovement != null)
            bunnyMovement.PerformJump();
    }

    // Placed on each type's Attacking clip at the "release" frame — relays up to NPCBunny.
    // ReleasePendingAttack, same shared-rig pattern as PerformJump above (this component sits on the rig,
    // NPCBunny lives on the wrapper prefab that nests it).
    public void ReleaseAttack()
    {
        if (npcBunny != null)
            npcBunny.ReleasePendingAttack();
    }
}