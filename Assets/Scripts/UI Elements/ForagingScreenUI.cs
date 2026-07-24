using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Dispatch entry point B (a dedicated "Foraging" button opens a bunny-picker first — see
// Foraging_DesignDoc.md's "Dispatch flow") PLUS a live status list of every bunny currently out. Picking
// a bunny in the picker hands off to ForagingDispatchUI for the shared Location -> Equip -> Confirm
// steps, same as entry point A (BunnyInfoUI's "Send Foraging" button). Picking a row in the active-trips
// list instead selects that bunny (highlighted) for the shared Return button below the list.
public class ForagingScreenUI : MonoBehaviour
{
    public static ForagingScreenUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;

    [Header("Bunny Picker (click to dispatch)")]
    [SerializeField] private Transform pickerListContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // simple prefab: Button + TextMeshProUGUI child

    [Header("Active Trips (click a row to select, then Return)")]
    [SerializeField] private Transform activeTripListContainer;
    [SerializeField] private GameObject activeTripRowPrefab; // prefab with a ForagingActiveTripRowUI component
    [Tooltip("Optional — shows \"Selected: <name>\" once a row is picked, blank otherwise.")]
    [SerializeField] private TextMeshProUGUI selectedBunnyLabel;
    [SerializeField] private Button returnButton;
    [Tooltip("How often the picker/active-trip lists rebuild while this screen is open — throttled rather than every frame, since HP/Energy don't need tighter-than-this granularity and rebuilding the list is a Destroy+Instantiate churn.")]
    [SerializeField] private float listRefreshInterval = 0.5f;
    private float listRefreshTimer;

    private NPCBunny selectedForagingBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        if (openButton != null)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(Open);
        }
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(Close);

        returnButton.onClick.RemoveAllListeners();
        returnButton.onClick.AddListener(OnReturnClicked);
    }

    public void Open()
    {
        panelRoot.SetActive(true);
        listRefreshTimer = 0f;
        selectedForagingBunny = null;
        RefreshPicker();
        RefreshActiveTrips();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
    }

    // Throttled rebuild (see listRefreshInterval) rather than every frame — HP/Energy/carry-fullness
    // change continuously while a trip is active, but a Destroy+Instantiate list rebuild every single
    // frame is wasteful churn for a status readout that doesn't need tighter granularity.
    private void Update()
    {
        if (!panelRoot.activeSelf) return;

        listRefreshTimer += Time.deltaTime;
        if (listRefreshTimer < listRefreshInterval) return;

        listRefreshTimer = 0f;
        RefreshPicker();
        RefreshActiveTrips();
    }

    private void RefreshPicker()
    {
        foreach (Transform child in pickerListContainer)
            Destroy(child.gameObject);

        if (DwellerRoster.Instance == null) return;

        foreach (NPCBunny bunny in DwellerRoster.Instance.GetForageableBunnies())
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, pickerListContainer);
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = bunny.BunnyName;
            buttonObj.GetComponent<Button>().onClick.AddListener(() => OnBunnyChosen(bunny));
        }
    }

    private void OnBunnyChosen(NPCBunny bunny)
    {
        // Deliberately doesn't close this screen — ForagingDispatchUI layers on top for
        // Location -> Equip -> Confirm, and this screen's own lists just refresh (via Update) once
        // dispatch completes or is cancelled.
        ForagingDispatchUI.Instance?.OpenForBunny(bunny);
    }

    private void RefreshActiveTrips()
    {
        foreach (Transform child in activeTripListContainer)
            Destroy(child.gameObject);

        if (ForagingManager.Instance == null) return;

        bool selectedStillActive = false;

        foreach (NPCBunny bunny in ForagingManager.Instance.GetActiveTrips())
        {
            ForagingTripState trip = ForagingManager.Instance.GetTripState(bunny);
            if (trip == null) continue;

            if (bunny == selectedForagingBunny) selectedStillActive = true;

            GameObject rowObj = Instantiate(activeTripRowPrefab, activeTripListContainer);
            ForagingActiveTripRowUI row = rowObj.GetComponent<ForagingActiveTripRowUI>();
            row?.Setup(bunny, trip, bunny == selectedForagingBunny, () => OnRowSelected(bunny));
        }

        // The selected bunny may have finished its trip (auto-return) between refreshes without ever
        // being recalled here — drop the stale selection rather than leaving Return enabled for nothing.
        if (!selectedStillActive) selectedForagingBunny = null;

        UpdateReturnControls();
    }

    private void OnRowSelected(NPCBunny bunny)
    {
        selectedForagingBunny = bunny;
        RefreshActiveTrips();
    }

    private void UpdateReturnControls()
    {
        returnButton.interactable = selectedForagingBunny != null;
        if (selectedBunnyLabel != null)
            selectedBunnyLabel.text = selectedForagingBunny != null ? $"Selected: {selectedForagingBunny.BunnyName}" : "";
    }

    private void OnReturnClicked()
    {
        if (selectedForagingBunny == null || ForagingManager.Instance == null) return;

        ForagingManager.Instance.RecallBunny(selectedForagingBunny);
        selectedForagingBunny = null;
        RefreshActiveTrips();
    }
}
