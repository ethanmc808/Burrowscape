using UnityEngine;

// Room prefabs now carry a root-level BoxCollider spanning their whole footprint (added by
// RoomDataGenerator to satisfy RoomUpgradeClickHandler), which physically encloses any bunny standing
// inside. Unity's OnMouseDown only fires on the single nearest collider along the click ray, and the
// room's collider is essentially always that nearest hit — so a bunny's own OnMouseDown effectively
// never fires while it's inside a room. Room click handlers call TryOpenInstead() first and bail out
// if it returns true, giving the bunny explicit priority via an actual RaycastAll instead of relying on
// whichever collider Unity's single-hit raycast happens to prefer.
public static class BunnyClickPriority
{
    public static bool TryOpenInstead()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray);

        foreach (RaycastHit hit in hits)
        {
            NPCBunny bunny = hit.collider.GetComponentInParent<NPCBunny>();
            if (bunny == null) continue;

            // Mirrors BunnyApprovalClickHandler/BunnyStatsClickHandler's own gating exactly.
            if (bunny.IsAwaitingApproval)
            {
                BunnyApprovalUI.Instance.OpenForBunny(bunny);
                return true;
            }
            if (bunny.HasEnteredBase)
            {
                BunnyStatsUI.Instance.OpenForBunny(bunny);
                return true;
            }
        }

        return false;
    }
}
