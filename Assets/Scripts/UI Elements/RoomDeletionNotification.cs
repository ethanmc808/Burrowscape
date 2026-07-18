using UnityEngine;
using TMPro;
using System.Collections;

// A dedicated notification for "can't delete this room" messages — deliberately separate from the
// generic NotificationToast (used for things like "room is full") so it can be styled, positioned, or
// animated independently without affecting every other toast in the game.
public class RoomDeletionNotification : MonoBehaviour
{
    public static RoomDeletionNotification Instance { get; private set; }

    [SerializeField] private GameObject notificationRoot;
    [SerializeField] private TextMeshProUGUI notificationText;
    [SerializeField] private float displayDuration = 2f;

    private Coroutine activeRoutine;

    private void Awake()
    {
        Instance = this;
        notificationRoot.SetActive(false);

        // Purely informational — no button, nothing to dismiss — so it should never intercept a click
        // meant for whatever's underneath it (e.g. a build-placement click on a floor the notification
        // happens to be covering). Deliberately on THIS object (the script's own root), not
        // notificationRoot — the root stays active permanently (only notificationRoot toggles), and it
        // turned out to have its own stray TextMeshProUGUI directly on it that was blocking raycasts
        // regardless of whether a message was even showing. A CanvasGroup here cascades to block
        // raycasts for the whole hierarchy underneath, covering that and anything else on this object,
        // not just notificationRoot's own subtree.
        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    public void Show(string message)
    {
        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(ShowRoutine(message));
    }

    private IEnumerator ShowRoutine(string message)
    {
        notificationText.text = message;
        notificationRoot.SetActive(true);
        yield return new WaitForSeconds(displayDuration);
        notificationRoot.SetActive(false);
    }
}
