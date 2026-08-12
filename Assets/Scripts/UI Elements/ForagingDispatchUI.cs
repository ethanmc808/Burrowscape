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
    [Tooltip("Lookup table from BunnyType to its icon sprite (BunnyTypeDefinition.icon) — same art WildBunnySpawner's own roster uses, just referenced here too so recommendedTypesIconContainer can resolve an icon per enum value without needing a live NPCBunny instance.")]
    [SerializeField] private List<BunnyTypeDefinition> bunnyTypeIconCatalog;
    [Tooltip("Cleared and rebuilt with one icon per selectedLocation.recommendedTypes entry every location change — same instantiate-per-item pattern as the accessory list. A type with no icon authored yet is silently skipped rather than showing a blank image.")]
    [SerializeField] private Transform recommendedTypesIconContainer;
    [Tooltip("Simple prefab: a single Image, sized however each recommended-type icon should look.")]
    [SerializeField] private GameObject recommendedTypeIconPrefab;
    [Tooltip("No-ops at the start of the list, same as ForagingDispatchUI's potion +/- buttons clamping at their own bounds.")]
    [SerializeField] private Button previousLocationButton;
    [Tooltip("No-ops at the end of the list.")]
    [SerializeField] private Button nextLocationButton;

    [Header("Equip Items")]
    [Tooltip("The base 'Potion' tier ConsumableDefinition — the only tier trips can currently carry (Great/Super Potion have no in-trip consumer yet).")]
    [SerializeField] private ConsumableDefinition basicHealthPotion;
    [SerializeField] private TextMeshProUGUI potionCountLabel;
    [SerializeField] private Button potionIncrementButton;
    [SerializeField] private Button potionDecrementButton;
    [Tooltip("Read-only — accessories are a persistent equip slot on the bunny now (see BunnyInfoUI), not chosen per trip. Shows \"Currently Equipped: {name}\" or \"None\".")]
    [SerializeField] private TextMeshProUGUI equippedAccessoryLabel;
    [SerializeField] private Button confirmButton;

    private NPCBunny currentBunny;
    private List<ForagingLocationDefinition> unlockedLocations = new List<ForagingLocationDefinition>();
    private int selectedLocationIndex;
    private ForagingLocationDefinition selectedLocation;
    private int selectedPotionCount;

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
        AudioManager.EnsureInstance().PlayUIOpen();

        // OrderBy populationThreshold, not the Inspector-authored order of ForagingManager.Locations —
        // that list has no guaranteed relationship to unlock progression (whichever order someone dragged
        // entries into it), so without this, a location with a HIGHER threshold listed earlier in the
        // Inspector would wrongly display/default (index 0, see below) ahead of the actual first-unlocked
        // one. Sorting here derives the correct order from the one thing that actually defines "which
        // unlocks first" instead of relying on it being hand-kept in sync forever.
        unlockedLocations = ForagingManager.Instance != null && ForagingLocationUnlockTracker.Instance != null
            ? ForagingManager.Instance.Locations
                .Where(l => l != null && ForagingLocationUnlockTracker.Instance.IsUnlocked(l))
                .OrderBy(l => l.populationThreshold)
                .ToList()
            : new List<ForagingLocationDefinition>();

        selectedLocationIndex = 0;
        ApplySelectedLocation();
    }

    public void Close() => Close(true);

    // playSound=false is used by confirm actions (OnConfirmClicked) that close this panel as a side
    // effect of succeeding — PlayUIClose is reserved for an actual Close/Back button press, not layered
    // on top of the confirm action's own PlayButtonClick.
    public void Close(bool playSound)
    {
        panelRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
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

    // Called on open and whenever Previous/Next changes the index. Potion selection resets on every
    // location change, same as before — the accessory is no longer a per-trip selection at all (see
    // equippedAccessoryLabel), so there's nothing to reset for it here anymore.
    private void ApplySelectedLocation()
    {
        selectedPotionCount = 0;

        selectedLocation = selectedLocationIndex >= 0 && selectedLocationIndex < unlockedLocations.Count
            ? unlockedLocations[selectedLocationIndex] : null;

        selectedLocationLabel.text = selectedLocation != null ? selectedLocation.displayName : "";

        selectedLocationIcon.gameObject.SetActive(selectedLocation != null && selectedLocation.icon != null);
        selectedLocationIcon.sprite = selectedLocation != null ? selectedLocation.icon : null;

        RefreshRecommendedTypeIcons();
        RefreshEquipStep();
    }

    private void RefreshRecommendedTypeIcons()
    {
        foreach (Transform child in recommendedTypesIconContainer)
            Destroy(child.gameObject);

        if (selectedLocation?.recommendedTypes == null) return;

        foreach (BunnyType type in selectedLocation.recommendedTypes)
        {
            Sprite icon = GetTypeIcon(type);
            if (icon == null) continue; // no icon authored for this type yet — skip rather than show a blank image

            GameObject iconObj = Instantiate(recommendedTypeIconPrefab, recommendedTypesIconContainer);
            Image image = iconObj.GetComponent<Image>();
            if (image != null) image.sprite = icon;
        }
    }

    private Sprite GetTypeIcon(BunnyType type)
    {
        if (bunnyTypeIconCatalog == null) return null;

        foreach (BunnyTypeDefinition def in bunnyTypeIconCatalog)
            if (def != null && def.type == type) return def.icon;

        return null;
    }

    // Reads the bunny's persistent equip slot now, not a per-trip selection (see NPCBunny.EquippedAccessory).
    private int CarryCapacity => ForagingManager.Instance != null && currentBunny != null
        ? ForagingManager.Instance.GetCarryCapacity(currentBunny.EquippedAccessory) : 0;

    private void ChangePotionCount(int delta)
    {
        int stock = ForagingInventoryManager.Instance != null ? ForagingInventoryManager.Instance.GetConsumableCount(basicHealthPotion) : 0;
        selectedPotionCount = Mathf.Clamp(selectedPotionCount + delta, 0, Mathf.Min(stock, CarryCapacity));
        RefreshEquipStep();
    }

    private void RefreshEquipStep()
    {
        potionCountLabel.text = selectedPotionCount.ToString();

        if (equippedAccessoryLabel != null)
        {
            ForagingAccessoryDefinition equipped = currentBunny != null ? currentBunny.EquippedAccessory : null;
            equippedAccessoryLabel.text = equipped != null ? $"Currently Equipped: {equipped.displayName}" : "Currently Equipped: None";
        }
    }

    private void OnConfirmClicked()
    {
        if (currentBunny == null || selectedLocation == null || ForagingManager.Instance == null) return;

        AudioManager.EnsureInstance().PlayButtonClick();

        bool dispatched = ForagingManager.Instance.TryDispatch(currentBunny, selectedLocation, selectedPotionCount);
        if (dispatched)
            Close(false); // click sound already fired above — don't also play the close cue
        // On failure, ForagingManager.TryDispatch already surfaces a NotificationManager Error explaining why —
        // leave the panel open so the player can adjust and retry rather than losing their selections.
    }
}
