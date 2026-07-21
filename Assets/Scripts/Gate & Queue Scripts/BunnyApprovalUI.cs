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

    // LateUpdate, not Update — TestCamera pans/zooms the camera in its own Update(), and Unity doesn't
    // guarantee script Update() order. Computing WorldToScreenPoint in Update() could run before the
    // camera moves for this frame, leaving the panel one frame stale and visibly trailing while the
    // player drags/zooms. LateUpdate always runs after every Update() this frame, so the camera is
    // guaranteed to already be in its final position/zoom before we anchor to it.
    private void LateUpdate()
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
        if (hungerBar == null || thirstBar == null || energyBar == null || moodBar == null) return;

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