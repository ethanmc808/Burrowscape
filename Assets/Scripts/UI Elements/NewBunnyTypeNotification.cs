using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// Dedicated to the "a new Bunny type appeared" reveal (first-ever spawn of any type outside the base
// Neutral/Water/Plant/Shock starting types — e.g. the Fire bunny, and future types as they unlock).
// Deliberately separate from NotificationToast/RoomDeletionNotification so it can carry the type's
// icon and be styled/animated as its own moment instead of a plain text toast.
public class NewBunnyTypeNotification : MonoBehaviour
{
    public static NewBunnyTypeNotification Instance { get; private set; }

    [SerializeField] private GameObject notificationRoot;
    [SerializeField] private TextMeshProUGUI notificationText;
    [Tooltip("Optional — shows the type's BunnyTypeDefinition.icon. Hidden automatically if that type has no icon assigned.")]
    [SerializeField] private Image typeIconImage;
    [SerializeField] private float displayDuration = 3f;

    private Coroutine activeRoutine;

    private void Awake()
    {
        Instance = this;
        notificationRoot.SetActive(false);

        // Purely informational — no button, nothing to dismiss — so it should never intercept a click
        // meant for whatever's underneath it. Deliberately on THIS object, not notificationRoot — the
        // root toggles on/off, but a CanvasGroup here cascades to block raycasts for the whole hierarchy
        // underneath regardless of which child is currently active.
        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    public void Show(BunnyTypeDefinition bunnyType)
    {
        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(ShowRoutine(bunnyType));
    }

    private IEnumerator ShowRoutine(BunnyTypeDefinition bunnyType)
    {
        notificationText.text = $"A {bunnyType.displayName} Bunny Appeared!";

        if (typeIconImage != null)
        {
            typeIconImage.sprite = bunnyType.icon;
            typeIconImage.enabled = bunnyType.icon != null;
        }

        notificationRoot.SetActive(true);
        yield return new WaitForSeconds(displayDuration);
        notificationRoot.SetActive(false);
    }
}
