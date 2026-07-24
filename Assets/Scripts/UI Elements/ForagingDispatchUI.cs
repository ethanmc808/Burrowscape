using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Shared Location -> Equip Items -> Confirm dispatch flow — both entry points (BunnyInfoUI's "Send
// Foraging" button, and ForagingScreenUI's bunny-picker) converge here once a bunny is chosen, since the
// bunny is picked BEFORE this panel opens either way. See Foraging_DesignDoc.md's "Dispatch flow".
// Same instantiate-a-button-into-a-container pattern AssignmentUI/BuildMenuUI already use for their own
// lists — no dedicated list-item component needed for a simple "name + click" row.
public class ForagingDispatchUI : MonoBehaviour
{
    public static ForagingDispatchUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Step 1: Location")]
    [SerializeField] private GameObject locationStepRoot;
    [SerializeField] private Transform locationListContainer;
    [SerializeField] private GameObject locationButtonPrefab; // Button + TextMeshProUGUI child

    [Header("Step 2: Equip Items")]
    [SerializeField] private GameObject equipStepRoot;
    [SerializeField] private TextMeshProUGUI selectedLocationLabel;
    [SerializeField] private TextMeshProUGUI potionCountLabel;
    [SerializeField] private Button potionIncrementButton;
    [SerializeField] private Button potionDecrementButton;
    [SerializeField] private Transform accessoryListContainer;
    [SerializeField] private GameObject accessoryButtonPrefab; // Button + TextMeshProUGUI child
    [SerializeField] private Color selectedAccessoryColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalAccessoryColor = Color.white;
    [SerializeField] private Button confirmButton;

    private NPCBunny currentBunny;
    private ForagingLocationDefinition selectedLocation;
    private ForagingAccessoryDefinition selectedAccessory;
    private int selectedPotionCount;
    private Button selectedAccessoryButton;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
        potionIncrementButton.onClick.AddListener(() => ChangePotionCount(1));
        potionDecrementButton.onClick.AddListener(() => ChangePotionCount(-1));
        confirmButton.onClick.AddListener(OnConfirmClicked);
    }

    // Both dispatch entry points call this — the bunny is already chosen either way (BunnyInfoUI already
    // has it selected; ForagingScreenUI's bunny-picker chooses one before opening this), so this always
    // starts at Step 1 (Location), never a bunny-picker of its own. No back-nav from Equip to Location —
    // the bunny hasn't departed yet at this point, unlike ForagingScreenUI's Recall (which acts on a
    // bunny already out on a trip), so there's nothing to "go back" from here; Close-and-reopen covers
    // changing your mind about the location.
    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;
        selectedLocation = null;
        selectedAccessory = null;
        selectedPotionCount = 0;

        panelRoot.SetActive(true);
        ShowLocationStep();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
    }

    // Re-evaluated fresh every time Step 1 is shown — live-checked population unlocks need to reflect
    // current state, same reasoning as BuildMenuUI.RefreshList.
    private void ShowLocationStep()
    {
        locationStepRoot.SetActive(true);
        equipStepRoot.SetActive(false);

        foreach (Transform child in locationListContainer)
            Destroy(child.gameObject);

        if (ForagingManager.Instance == null || ForagingLocationUnlockTracker.Instance == null) return;

        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations
            .Where(l => l != null && ForagingLocationUnlockTracker.Instance.IsUnlocked(l)))
        {
            GameObject buttonObj = Instantiate(locationButtonPrefab, locationListContainer);
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = location.displayName;
            buttonObj.GetComponent<Button>().onClick.AddListener(() => OnLocationChosen(location));
        }
    }

    private void OnLocationChosen(ForagingLocationDefinition location)
    {
        selectedLocation = location;
        selectedAccessory = null;
        selectedAccessoryButton = null;
        selectedPotionCount = 0;

        locationStepRoot.SetActive(false);
        equipStepRoot.SetActive(true);

        selectedLocationLabel.text = location.displayName;
        RefreshEquipStep();
    }

    private int CarryCapacity => ForagingManager.Instance != null ? ForagingManager.Instance.GetCarryCapacity(selectedAccessory) : 0;

    private void ChangePotionCount(int delta)
    {
        int stock = ForagingInventoryManager.Instance != null ? ForagingInventoryManager.Instance.PotionStock : 0;
        selectedPotionCount = Mathf.Clamp(selectedPotionCount + delta, 0, Mathf.Min(stock, CarryCapacity));
        RefreshEquipStep();
    }

    private void RefreshEquipStep()
    {
        potionCountLabel.text = selectedPotionCount.ToString();

        foreach (Transform child in accessoryListContainer)
            Destroy(child.gameObject);
        selectedAccessoryButton = null;

        // "None" option first, so a previously-equipped accessory can be un-equipped.
        GameObject noneObj = Instantiate(accessoryButtonPrefab, accessoryListContainer);
        noneObj.GetComponentInChildren<TextMeshProUGUI>().text = "None";
        Button noneButton = noneObj.GetComponent<Button>();
        noneButton.onClick.AddListener(() => OnAccessoryChosen(null, noneButton));
        if (selectedAccessory == null) SelectAccessoryButton(noneButton);

        if (ForagingInventoryManager.Instance == null) return;

        // Only accessories actually held in the base stockpile are offered — Foraging v1's accessory
        // "catalog" is just whatever's in ForagingInventoryManager's stock, no separate master list.
        foreach (ForagingAccessoryDefinition accessory in ForagingInventoryManager.Instance.GetAccessoriesInStock())
        {
            GameObject buttonObj = Instantiate(accessoryButtonPrefab, accessoryListContainer);
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = accessory.displayName;
            Button button = buttonObj.GetComponent<Button>();
            button.onClick.AddListener(() => OnAccessoryChosen(accessory, button));
            if (selectedAccessory == accessory) SelectAccessoryButton(button);
        }
    }

    private void OnAccessoryChosen(ForagingAccessoryDefinition accessory, Button button)
    {
        selectedAccessory = accessory;
        SelectAccessoryButton(button);

        // Equipping/unequipping an accessory can change carry capacity — clamp the already-chosen potion
        // count back down if it now exceeds the new capacity.
        selectedPotionCount = Mathf.Min(selectedPotionCount, CarryCapacity);
        potionCountLabel.text = selectedPotionCount.ToString();
    }

    private void SelectAccessoryButton(Button button)
    {
        ResetButtonColor(selectedAccessoryButton);
        selectedAccessoryButton = button;
        SetButtonColor(button, selectedAccessoryColor);
    }

    private void SetButtonColor(Button button, Color color)
    {
        if (button == null) return;
        Image img = button.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private void ResetButtonColor(Button button)
    {
        SetButtonColor(button, normalAccessoryColor);
    }

    private void OnConfirmClicked()
    {
        if (currentBunny == null || selectedLocation == null || ForagingManager.Instance == null) return;

        bool dispatched = ForagingManager.Instance.TryDispatch(currentBunny, selectedLocation, selectedPotionCount, selectedAccessory);
        if (dispatched)
            Close();
        // On failure, ForagingManager.TryDispatch already surfaces a NotificationToast explaining why —
        // leave the panel open so the player can adjust and retry rather than losing their selections.
    }
}
