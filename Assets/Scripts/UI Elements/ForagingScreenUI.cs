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
    [Tooltip("Highlight for whichever bunny was most recently picked — same color/shape as ForagingActiveTripRowUI's own selected/normal pair, just applied directly to the plain button's Image since the picker uses the shared SimpleListButton prefab rather than a dedicated row script.")]
    [SerializeField] private Color selectedPickerColor = new Color(0.65f, 0.93f, 0.65f, 1f);
    [SerializeField] private Color normalPickerColor = Color.white;

    [Header("Active Trips (click a row to select, then Return)")]
    [SerializeField] private Transform activeTripListContainer;
    [SerializeField] private GameObject activeTripRowPrefab; // prefab with a ForagingActiveTripRowUI component
    [Tooltip("Hidden entirely until a row is selected — see UpdateReturnControls.")]
    [SerializeField] private Button returnButton;
    [Tooltip("How often the picker/active-trip lists rebuild while this screen is open — throttled rather than every frame, since HP/Energy don't need tighter-than-this granularity and rebuilding the list is a Destroy+Instantiate churn.")]
    [SerializeField] private float listRefreshInterval = 0.5f;
    private float listRefreshTimer;

    private NPCBunny selectedForagingBunny;
    private NPCBunny selectedPickerBunny;

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
        selectedPickerBunny = null;
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

            Image image = buttonObj.GetComponent<Image>();
            if (image != null)
                image.color = bunny == selectedPickerBunny ? selectedPickerColor : normalPickerColor;
        }
    }

    private void OnBunnyChosen(NPCBunny bunny)
    {
        // Highlight immediately rather than waiting for the next throttled poll — the bunny naturally
        // drops out of this list on its own once actually dispatched (DwellerRoster.GetForageableBunnies
        // excludes anything mid-trip), so there's no separate "clear selection" case to handle there.
        selectedPickerBunny = bunny;
        RefreshPicker();

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

    // The row's own highlight (see ForagingActiveTripRowUI) already shows which bunny is selected — the
    // Return button itself only needs to exist once there's something to act on.
    private void UpdateReturnControls()
    {
        returnButton.gameObject.SetActive(selectedForagingBunny != null);
    }

    private void OnReturnClicked()
    {
        if (selectedForagingBunny == null || ForagingManager.Instance == null) return;

        // Recall itself has no other feedback — the bunny keeps walking/ticking exactly as before until
        // its return countdown starts, so without this the player has no sign the click did anything.
        NotificationToast.Instance?.Show($"{selectedForagingBunny.BunnyName} is heading back from foraging.");

        ForagingManager.Instance.RecallBunny(selectedForagingBunny);
        selectedForagingBunny = null;
        RefreshActiveTrips();
    }
}
