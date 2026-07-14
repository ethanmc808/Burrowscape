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