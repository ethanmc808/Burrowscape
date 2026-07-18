using UnityEngine;
using TMPro;

// Two independent, indefinite-duration banners (unlike NotificationToast's single auto-dismissing slot)
// telling the player a triggered upgrade/merge is still waiting on a bunny to physically arrive before
// RoomTransitionService can swap the room — without this, the delay between clicking Upgrade and the
// room actually changing looks like nothing happened. Upgrade and Merge each get their own slot (shown
// at different screen positions via the Inspector-placed roots) so a chained upgrade->merge, or two
// separate rooms being upgraded/merged around the same time, can show both messages at once without one
// overwriting or hiding the other. Each slot is reference-counted (not a simple bool) for the same
// reason — one overlapping operation of the same kind finishing shouldn't hide the banner while another
// of that kind is still genuinely in progress.
public class RoomTransitionNotice : MonoBehaviour
{
    public static RoomTransitionNotice Instance { get; private set; }

    [Header("Upgrade")]
    [SerializeField] private GameObject upgradeNoticeRoot;
    [SerializeField] private TextMeshProUGUI upgradeNoticeText;
    [SerializeField] private string upgradeMessage = "Upgrade in progress — a bunny is en route.";

    [Header("Merge")]
    [SerializeField] private GameObject mergeNoticeRoot;
    [SerializeField] private TextMeshProUGUI mergeNoticeText;
    [SerializeField] private string mergeMessage = "Merge in progress — a bunny is en route.";

    private int activeUpgrades;
    private int activeMerges;

    private void Awake()
    {
        Instance = this;
        upgradeNoticeRoot.SetActive(false);
        mergeNoticeRoot.SetActive(false);

        // Same reasoning as NotificationToast's own CanvasGroup — purely informational, must never
        // intercept a click meant for whatever's underneath (e.g. a build-placement click on a floor
        // one of these banners happens to be covering).
        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    public void Show(RoomTransitionKind kind)
    {
        if (kind == RoomTransitionKind.Upgrade)
        {
            activeUpgrades++;
            upgradeNoticeText.text = upgradeMessage;
            upgradeNoticeRoot.SetActive(true);
        }
        else
        {
            activeMerges++;
            mergeNoticeText.text = mergeMessage;
            mergeNoticeRoot.SetActive(true);
        }
    }

    public void Hide(RoomTransitionKind kind)
    {
        if (kind == RoomTransitionKind.Upgrade)
        {
            activeUpgrades = Mathf.Max(0, activeUpgrades - 1);
            if (activeUpgrades == 0) upgradeNoticeRoot.SetActive(false);
        }
        else
        {
            activeMerges = Mathf.Max(0, activeMerges - 1);
            if (activeMerges == 0) mergeNoticeRoot.SetActive(false);
        }
    }
}

public enum RoomTransitionKind
{
    Upgrade,
    Merge
}
