using UnityEngine;

public class BunnyAnimationEvents : MonoBehaviour
{
    private BunnyMovement bunnyMovement;

    private void Awake()
    {
        bunnyMovement = GetComponentInParent<BunnyMovement>();
    }

    public void PerformJump()
    {
        if (bunnyMovement != null)
            bunnyMovement.PerformJump();
    }
}