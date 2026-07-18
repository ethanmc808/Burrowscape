using UnityEngine;
using TMPro;
using System.Collections;

public class NotificationToast : MonoBehaviour
{
    public static NotificationToast Instance { get; private set; }

    [SerializeField] private GameObject toastRoot;
    [SerializeField] private TextMeshProUGUI toastText;
    [SerializeField] private float displayDuration = 2f;

    private Coroutine activeRoutine;

    private void Awake()
    {
        Instance = this;
        toastRoot.SetActive(false);

        // Purely informational — no button, nothing to dismiss — so it should never intercept a click
        // meant for whatever's underneath it (e.g. a build-placement click on a floor the toast happens
        // to be covering). Deliberately on THIS object (the script's own root), not toastRoot — the root
        // stays active permanently (only toastRoot toggles), and it turned out to have its own stray
        // TextMeshProUGUI directly on it that was blocking raycasts regardless of whether a message was
        // even showing. A CanvasGroup here cascades to block raycasts for the whole hierarchy underneath,
        // covering that and anything else on this object, not just toastRoot's own subtree.
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
        toastText.text = message;
        toastRoot.SetActive(true);
        yield return new WaitForSeconds(displayDuration);
        toastRoot.SetActive(false);
    }
}