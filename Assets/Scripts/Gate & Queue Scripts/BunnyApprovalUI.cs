using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BunnyApprovalUI : MonoBehaviour
{
    public static BunnyApprovalUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI bunnyNameLabel;
    [SerializeField] private Button approveButton;
    [SerializeField] private Button rejectButton;
    [SerializeField] private Button closeButton;

    [Header("Needs Bars")]
    [SerializeField] private Image hungerBar;
    [SerializeField] private Image thirstBar;
    [SerializeField] private Image energyBar;
    [SerializeField] private Image moodBar;

    // The panel tracks the fixed front-queue-spot Transform's world position (see
    // GateQueueManager.FrontQueueSpot) rather than following the screen — that spot never moves, so this
    // is a per-frame world->screen conversion, not per-bunny tracking.
    [Header("World Anchoring")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        if (worldCamera == null) worldCamera = Camera.main;

        approveButton.onClick.AddListener(OnApproveClicked);
        rejectButton.onClick.AddListener(OnRejectClicked);
        closeButton.onClick.AddListener(Close);
    }

    private void Update()
    {
        if (!panelRoot.activeSelf) return;

        Transform frontSpot = GateQueueManager.Instance != null ? GateQueueManager.Instance.FrontQueueSpot : null;
        if (frontSpot != null && worldCamera != null)
            panelRoot.transform.position = worldCamera.WorldToScreenPoint(frontSpot.position + worldOffset);

        RefreshBars();
    }

    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;
        bunnyNameLabel.text = bunny.name;
        panelRoot.SetActive(true);
        RefreshBars();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
    }

    private void RefreshBars()
    {
        if (currentBunny == null) return;

        hungerBar.fillAmount = currentBunny.HungerValue / 100f;
        thirstBar.fillAmount = currentBunny.ThirstValue / 100f;
        energyBar.fillAmount = currentBunny.EnergyValue / 100f;
        moodBar.fillAmount = currentBunny.MoodValue / 100f;
    }

    private void OnApproveClicked()
    {
        if (currentBunny == null) return;
        GateQueueManager.Instance.ApproveFrontBunny(currentBunny);
        Close();
    }

    private void OnRejectClicked()
    {
        if (currentBunny == null) return;
        GateQueueManager.Instance.RejectFrontBunny(currentBunny);
        Close();
    }
}