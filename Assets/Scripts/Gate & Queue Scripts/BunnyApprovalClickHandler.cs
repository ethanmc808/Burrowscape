using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BunnyApprovalClickHandler : MonoBehaviour
{
    private NPCBunny npcBunny;

    private void Awake()
    {
        npcBunny = GetComponent<NPCBunny>();
        if (npcBunny == null)
            Debug.LogWarning($"{name}: BunnyApprovalClickHandler requires an NPCBunny component on the same object.");
    }

    private void OnMouseDown()
    {
        Debug.Log($"{name}: clicked. npcBunny={(npcBunny != null ? npcBunny.name : "NULL")}, IsAwaitingApproval={(npcBunny != null ? npcBunny.IsAwaitingApproval.ToString() : "N/A")}, BunnyApprovalUI.Instance={(BunnyApprovalUI.Instance != null ? "set" : "NULL")}");

        if (npcBunny == null || !npcBunny.IsAwaitingApproval) return;

        BunnyApprovalUI.Instance.OpenForBunny(npcBunny);
    }
}
