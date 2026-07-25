using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Single-screen Location + Equip Items + Confirm dispatch flow — both entry points (BunnyInfoUI's "Send
// Foraging" button, and ForagingScreenUI's bunny-picker) converge here once a bunny is chosen. Opens
// with the first unlocked location auto-selected (index 0); Previous/Next step through the same
// population-gated unlocked-locations list ForagingLocationUnlockTracker already filters, rather than a
// separate location-picking screen. See Foraging_DesignDoc.md's "Dispatch flow".
public class ForagingDispatchUI : MonoBehaviour
{
    public static ForagingDispatchUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Location (Previous/Next step through the unlocked-locations list)")]
    [SerializeField] private TextMeshProUGUI selectedLocationLabel;
    [Tooltip("Shows the chosen location's own icon — hidden entirely if that location has no icon assigned yet (some later-tier locations may not have art authored).")]
    [SerializeField] private Image selectedLocationIcon;
    [Tooltip("No-ops at the start of the list, same as ForagingDispatchUI's potion +/- buttons clamping at their own bounds.")]
    [SerializeField] private Button previousLocationButton;
    [Tooltip("No-ops at the end of the list.")]
    [SerializeField] private Button nextLocationButton;

    [Header("Equip Items")]
    [SerializeField] private TextMeshProUGUI potionCountLabel;
    [SerializeField] private Button potionIncrementButton;
    [SerializeField] private Button potionDecrementButton;
    [SerializeField] private Transform accessoryListContainer;
    [SerializeField] private GameObject accessoryButtonPrefab; // Button + TextMeshProUGUI child
    [SerializeField] private Color selectedAccessoryColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalAccessoryColor = Color.white;
    [SerializeField] private Button confirmButton;

    private NPCBunny currentBunny;
    private List<ForagingLocationDefinition> unlockedLocations = new List<ForagingLocationDefinition>();
    private int selectedLocationIndex;
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
        previousLocationButton.onClick.AddListener(PreviousLocation);
        nextLocationButton.onClick.AddListener(NextLocation);
    }

    // Both dispatch entry points call this — the bunny is already chosen either way (BunnyInfoUI already
    // has it selected; ForagingScreenUI's bunny-picker chooses one before opening this). Re-evaluated
    // fresh on every open since population-gated unlocks (see ForagingLocationUnlockTracker) can change
    // between trips, same reasoning as BuildMenuUI.RefreshList.
    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;
        panelRoot.SetActive(true);

        unlockedLocations = ForagingManager.Instance != null && ForagingLocationUnlockTracker.Instance != null
            ? ForagingManager.Instance.Locations.Where(l => l != null && ForagingLocationUnlockTracker.Instance.IsUnlocked(l)).ToList()
            : new List<ForagingLocationDefinition>();

        selectedLocationIndex = 0;
        ApplySelectedLocation();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
    }

    private void PreviousLocation()
    {
        if (selectedLocationIndex <= 0) return;
        selectedLocationIndex--;
        ApplySelectedLocation();
    }

    private void NextLocation()
    {
        if (selectedLocationIndex >= unlockedLocations.Count - 1) return;
        selectedLocationIndex++;
        ApplySelectedLocation();
    }

    // Called on open and whenever Previous/Next changes the index. Accessory/potion selections reset on
    // every location change, same as the old per-click reset in what used to be a separate step.
    private void ApplySelectedLocation()
    {
        selectedAccessory = null;
        selectedAccessoryButton = null;
        selectedPotionCount = 0;

        selectedLocation = selectedLocationIndex >= 0 && selectedLocationIndex < unlockedLocations.Count
            ? unlockedLocations[selectedLocationIndex] : null;

        selectedLocationLabel.text = selectedLocation != null ? selectedLocation.displayName : "";

        selectedLocationIcon.gameObject.SetActive(selectedLocation != null && selectedLocation.icon != null);
        selectedLocationIcon.sprite = selectedLocation != null ? selectedLocation.icon : null;

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
