using UnityEngine;

// Replaces BunnyApprovalClickHandler + BunnyStatsClickHandler (Gate & Queue Scripts) — a bunny is
// clickable in both states (still awaiting gate approval, or already admitted into the base), and
// BunnyInfoUI itself decides whether to show the Approve/Reject controls based on IsAwaitingApproval,
// so a single handler covers what used to need two mutually-gated ones. See BunnyInfoUI.cs for cutover
// status — do not add this to any prefab until that panel is built and wired up in the scene.
[RequireComponent(typeof(Collider))]
public class BunnyInfoClickHandler : MonoBehaviour
{
    private NPCBunny npcBunny;

    private void Awake()
    {
        npcBunny = GetComponent<NPCBunny>();
        if (npcBunny == null)
            Debug.LogWarning($"{name}: BunnyInfoClickHandler requires an NPCBunny component on the same object.");
    }

    private void OnMouseDown()
    {
        if (UIPointerGuard.IsPointerOverUI()) return;
        if (npcBunny == null) return;

        // Nothing meaningful to show before this — a bunny not yet awaiting approval and not yet
        // admitted has no real state (still walking to the queue), and isn't reliably clickable then
        // anyway (see BunnyClickPriority's own reasoning for why room colliders usually win the
        // raycast).
        if (!npcBunny.IsAwaitingApproval && !npcBunny.HasEnteredBase) return;

        BunnyInfoUI.Instance.OpenForBunny(npcBunny);
    }
}
