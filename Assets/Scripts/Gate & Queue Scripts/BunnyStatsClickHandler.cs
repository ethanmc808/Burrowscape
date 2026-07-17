using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BunnyStatsClickHandler : MonoBehaviour
{
    private NPCBunny npcBunny;

    private void Awake()
    {
        npcBunny = GetComponent<NPCBunny>();
        if (npcBunny == null)
            Debug.LogWarning($"{name}: BunnyStatsClickHandler requires an NPCBunny component on the same object.");
    }

    private void OnMouseDown()
    {
        // Opposite gate from BunnyApprovalClickHandler — only opens for bunnies that have already
        // passed the gate and aren't still awaiting approval. Both handlers fire on click since Unity
        // calls every OnMouseDown on the GameObject, but the gates are mutually exclusive so only one
        // ever acts.
        if (npcBunny == null || !npcBunny.HasEnteredBase || npcBunny.IsAwaitingApproval) return;

        BunnyStatsUI.Instance.OpenForBunny(npcBunny);
    }
}
