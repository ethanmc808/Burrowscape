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
