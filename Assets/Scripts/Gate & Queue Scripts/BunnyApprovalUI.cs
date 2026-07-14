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

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        approveButton.onClick.AddListener(OnApproveClicked);
        rejectButton.onClick.AddListener(OnRejectClicked);
    }

    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;
        bunnyNameLabel.text = bunny.name;
        panelRoot.SetActive(true);
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
    }

    private void OnApproveClicked()
    {
        if (currentBunny == null) return;
        GateQueueManager.Instance.ApproveFrontBunny();
        Close();
    }

    private void OnRejectClicked()
    {
        if (currentBunny == null) return;
        GateQueueManager.Instance.RejectFrontBunny();
        Close();
    }
}
