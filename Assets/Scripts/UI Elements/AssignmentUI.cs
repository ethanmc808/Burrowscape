using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class AssignmentUI : MonoBehaviour
{
    public static AssignmentUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;

    [Header("Unassigned Bunnies (click to assign)")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // simple prefab: Button + TextMeshProUGUI child
    [Tooltip("Small type-symbol icon prefab (TypeIconSmall) plugged in at the start of each bunny's name via BunnyTypeIconHelper. Skipped for types with no icon assigned yet.")]
    [SerializeField] private GameObject typeIconPrefab;

    [Header("Currently Assigned Bunnies (click to select, then Unassign)")]
    [SerializeField] private Transform assignedButtonContainer;
    [SerializeField] private Button unassignButton;

    [Header("Selection Highlight")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalColor = Color.white;

    // ---------- Laboratory extras (see LaboratoryRoom design notes) — shown only while currentRoom is a
    // LaboratoryRoom, folded into this same panel rather than a separate one (same precedent BunnyInfoUI's
    // Adaptable block already set: one shared panel showing an extra block only when relevant). Additive
    // to everything above — the normal assign/unassign flow is completely unmodified.
    //
    // REDESIGNED from an earlier one-row-per-assigned-bunny layout — that didn't fit on screen once a
    // Grade-3 (12x2x6) room could hold 6 bunnies at once. LaboratoryRoom is now a single ROOM-WIDE brew, so
    // this only ever needs ONE status display regardless of room grade: either the idle "Select item to
    // craft" button, or one BrewTimerUI showing whatever's currently brewing. ----------
    [Header("Laboratory Extras (shown only when currentRoom is LaboratoryRoom)")]
    [SerializeField] private GameObject laboratoryExtrasRoot;
    [Tooltip("Shown when the room isn't brewing — contains the one 'Select item to craft' button below.")]
    [SerializeField] private GameObject laboratoryIdleRoot;
    [SerializeField] private Button laboratorySelectCraftButton;
    [Tooltip("Shown while the room IS brewing — a single BrewTimerUI wired directly (not instantiated per row, there's only ever one active brew now).")]
    [SerializeField] private GameObject laboratoryBrewTimerRoot;
    [SerializeField] private BrewTimerUI laboratoryBrewTimer;

    [Header("Laboratory — Recipe Picker, step 1 of 2 (opened from the room-level Select Craft button)")]
    [SerializeField] private GameObject recipePickerRoot;
    [Tooltip("Optional — clicking the craft button again also closes it (same toggle idiom as BunnyInfoUI's pickers).")]
    [SerializeField] private Button recipePickerCloseButton;
    [SerializeField] private Transform recipePickerListContainer;
    [Tooltip("Row prefab needs a RecipeRowUI component — see that file.")]
    [SerializeField] private GameObject recipeRowPrefab;

    [Tooltip("Just 3 plain buttons (1x/5x/10x) — the recipe row above already shows per-1x herb cost, so an unaffordable quantity just greys its button out (Button.interactable = false) rather than repeating cost text per option.")]
    [Header("Laboratory — Quantity Picker, step 2 of 2 (1x/5x/10x, greyed out by affordability)")]
    [SerializeField] private GameObject quantityPickerRoot;
    [SerializeField] private TextMeshProUGUI quantityPickerRecipeNameText;
    [Tooltip("Optional close/back button — returns to the recipe list without starting a brew.")]
    [SerializeField] private Button quantityPickerCloseButton;
    [SerializeField] private Button quantity1Button;
    [SerializeField] private Button quantity5Button;
    [SerializeField] private Button quantity10Button;

    [Header("Laboratory — Recently Crafted")]
    [SerializeField] private Transform recentlyCraftedListContainer;
    [Tooltip("Simple prefab: an Image (icon) + TextMeshProUGUI child (name) — same shape as bunnyButtonPrefab minus the button.")]
    [SerializeField] private GameObject recentlyCraftedRowPrefab;

    private LaboratoryRoom currentLabRoom;
    private RecipeDefinition quantityPickerRecipe;

    // Throttled, not every Update() frame — matches ForagingTripDetailUI's convention for a live countdown
    // readout; 0.5s granularity is visually indistinguishable from per-frame for a brew timer.
    private const float LaboratoryRefreshInterval = 0.5f;
    private float laboratoryRefreshTimer;

    private IJobRoom currentRoom;
    private NPCBunny selectedAssignedBunny;
    private Button selectedAssignedButton;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        // Defensive: if UnassignButton was duplicated from another button (e.g. CloseButton),
        // it may have carried over an Inspector-configured OnClick() entry pointing at the wrong
        // method. Clearing here guarantees only our code-driven handler is wired, regardless of
        // whatever is (or isn't) sitting in the Inspector's OnClick() list.
        unassignButton.onClick.RemoveAllListeners();
        unassignButton.onClick.AddListener(OnUnassignClicked);
        unassignButton.interactable = false;

        if (laboratorySelectCraftButton != null)
            laboratorySelectCraftButton.onClick.AddListener(ToggleRecipePicker);
        if (recipePickerCloseButton != null)
            recipePickerCloseButton.onClick.AddListener(CloseRecipePicker);
        if (quantityPickerCloseButton != null)
            quantityPickerCloseButton.onClick.AddListener(CloseQuantityPicker);
        if (quantity1Button != null) quantity1Button.onClick.AddListener(() => OnQuantitySelected(1));
        if (quantity5Button != null) quantity5Button.onClick.AddListener(() => OnQuantitySelected(5));
        if (quantity10Button != null) quantity10Button.onClick.AddListener(() => OnQuantitySelected(10));
        CloseRecipePicker();
        CloseQuantityPicker();
    }

    private void Update()
    {
        if (currentLabRoom == null || !panelRoot.activeSelf) return;

        laboratoryRefreshTimer += Time.deltaTime;
        if (laboratoryRefreshTimer < LaboratoryRefreshInterval) return;
        laboratoryRefreshTimer = 0f;

        RefreshLaboratoryExtras();
    }

    public void OpenForRoom(IJobRoom room, string roomDisplayName)
    {
        currentRoom = room;
        currentLabRoom = room as LaboratoryRoom;
        roomNameLabel.text = roomDisplayName;
        panelRoot.SetActive(true);
        AudioManager.EnsureInstance().PlayUIOpen();
        ClearSelection();
        PopulateLists();

        if (laboratoryExtrasRoot != null) laboratoryExtrasRoot.SetActive(currentLabRoom != null);
        CloseRecipePicker();
        CloseQuantityPicker();
        if (currentLabRoom != null) RefreshLaboratoryExtras();
    }

    public void Close() => Close(true);

    // playSound=false is used by confirm actions (e.g. AssignAndClose) that close this panel as a side
    // effect of succeeding — PlayUIClose is reserved for an actual Close/Back button press, not layered
    // on top of the confirm action's own PlayButtonClick. Propagated through the RoomUpgradeUI/PatientUI
    // cross-closes too, so assigning a bunny never plays any of the three panels' close cue twice.
    public void Close(bool playSound)
    {
        // Room clicks always open AssignmentUI alongside RoomUpgradeUI (see RoomClickHandler/
        // RoomUpgradeClickHandler) and, for a Hospital specifically, PatientUI too — so any one panel's
        // Close button should close all of them rather than making the player dismiss each separately.
        // PatientUI has no close button of its own for exactly this reason (see its own header comment:
        // "opens alongside AssignmentUI"). The activeSelf guard makes this idempotent, which is what
        // stops the Close() calls from recursing into each other forever.
        if (!panelRoot.activeSelf) return;

        panelRoot.SetActive(false);
        // LaboratoryExtrasRoot lives as its own top-level panel now (not nested under panelRoot — see its
        // header comment), so hiding panelRoot alone no longer hides it for free; needs an explicit call.
        if (laboratoryExtrasRoot != null) laboratoryExtrasRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentRoom = null;
        currentLabRoom = null;
        CloseRecipePicker();
        CloseQuantityPicker();
        ClearSelection();
        RoomUpgradeUI.Instance?.Close(playSound);
        PatientUI.Instance?.Close(playSound);
        // GuardDeployUI also opens alongside AssignmentUI (RoomClickHandler), but deliberately has no
        // close button of its own (see its header comment — stays open across deploys so the player can
        // send reinforcements one at a time). Without this it only ever closed via OnInvasionCleared, so
        // assigning a bunny into an invaded room through the normal AssignAndClose flow left it stranded
        // open with no way to dismiss it until the battle ended.
        GuardDeployUI.Instance?.Close(playSound);
    }

    private void PopulateLists()
    {
        PopulateUnassignedList();
        PopulateAssignedList();
    }

    private void PopulateUnassignedList()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        List<NPCBunny> unassigned = DwellerRoster.Instance.GetUnassignedBunnies();

        // Guard Room specifically wants its strongest candidates surfaced first — a simple re-sort of the
        // same list, no bunnies hidden. Every other room type is unaffected (registration order, as before).
        if (currentRoom is GuardRoom)
            unassigned = unassigned.OrderByDescending(b => b.Level).ToList();

        // Bedroom (breeding) is the one room type that actually HIDES ineligible candidates rather than
        // just re-sorting — a bunny whose gender's slot is already filled can't be assigned here at all
        // (see Bedroom.IsEligibleForBreeding), so there's no reason to let the player click one and
        // silently fail. See the Breeding System plan.
        if (currentRoom is Bedroom bedroom)
            unassigned = unassigned.Where(b => bedroom.IsEligibleForBreeding(b)).ToList();

        foreach (NPCBunny bunny in unassigned)
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => AssignAndClose(bunny));
        }
    }

    private void PopulateAssignedList()
    {
        foreach (Transform child in assignedButtonContainer)
            Destroy(child.gameObject);

        List<NPCBunny> assigned = DwellerRoster.Instance.GetBunniesAssignedTo(currentRoom);

        foreach (NPCBunny bunny in assigned)
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, assignedButtonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectAssignedBunny(bunny, btn));
        }
    }

    private void AssignAndClose(NPCBunny bunny)
    {
        if (!currentRoom.HasAvailableSpot())
        {
            NotificationManager.Instance.Show(NotificationType.RoomFull);
            return; // don't assign, don't close the panel — let them pick a different bunny or cancel
        }

        AudioManager.EnsureInstance().PlayButtonClick();
        bunny.AssignToJob(currentRoom);
        Close(false); // click sound already fired above — don't also play the close cue
    }

    private void SelectAssignedBunny(NPCBunny bunny, Button btn)
    {
        DebugLog.Log($"AssignmentUI: selected {bunny.name} for possible unassignment.");

        // Clicking the already-selected bunny again deselects it.
        if (selectedAssignedBunny == bunny)
        {
            ClearSelection();
            return;
        }

        ResetButtonColor(selectedAssignedButton);

        selectedAssignedBunny = bunny;
        selectedAssignedButton = btn;
        // Disabled while mid-mating-animation (see NPCBunny.IsMidMatingSequence's own comment) — the
        // player CAN still select the bunny to see it highlighted, just can't confirm the unassign yet.
        // UnassignFromJob() itself also guards this (see its own check), so this is UX polish, not the
        // only thing standing between a click and a stranded reservation.
        unassignButton.interactable = !bunny.IsMidMatingSequence;

        SetButtonColor(btn, selectedColor);
    }

    private void OnUnassignClicked()
    {
        DebugLog.Log($"AssignmentUI: Unassign button clicked. selectedAssignedBunny={(selectedAssignedBunny != null ? selectedAssignedBunny.name : "NULL")}");

        if (selectedAssignedBunny == null) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        selectedAssignedBunny.UnassignFromJob();
        DebugLog.Log($"AssignmentUI: UnassignFromJob() called on {selectedAssignedBunny.name}.");

        ClearSelection();
        PopulateLists(); // bunny moves from the assigned list back into the unassigned list

        // An active brew isn't tied to any one bunny anymore — it just keeps ticking (possibly at 0 speed,
        // if that was the room's last present worker) — this just refreshes the displayed rate/remaining.
        if (currentLabRoom != null) RefreshLaboratoryExtras();
    }

    private void ClearSelection()
    {
        ResetButtonColor(selectedAssignedButton);
        selectedAssignedBunny = null;
        selectedAssignedButton = null;
        unassignButton.interactable = false;
    }

    private void SetButtonColor(Button btn, Color color)
    {
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private void ResetButtonColor(Button btn)
    {
        if (btn == null) return;
        SetButtonColor(btn, normalColor);
    }

    // ---------- Laboratory extras ----------

    // Single room-wide status — either the idle "Select item to craft" button, or the one active brew's
    // timer. No per-bunny list anymore (see the class-header comment for why).
    private void RefreshLaboratoryExtras()
    {
        if (laboratoryExtrasRoot == null || currentLabRoom == null) return;

        bool brewing = currentLabRoom.IsBrewing;
        if (laboratoryIdleRoot != null) laboratoryIdleRoot.SetActive(!brewing);
        if (laboratoryBrewTimerRoot != null) laboratoryBrewTimerRoot.SetActive(brewing);

        if (brewing) RefreshBrewTimer();

        RefreshRecentlyCrafted();
    }

    private void RefreshBrewTimer()
    {
        if (laboratoryBrewTimer == null || currentLabRoom == null) return;

        RecipeDefinition recipe = currentLabRoom.GetActiveRecipe();
        if (recipe == null || recipe.output == null) return;

        int quantity = currentLabRoom.GetActiveQuantity();
        if (laboratoryBrewTimer.itemIcon != null) laboratoryBrewTimer.itemIcon.sprite = recipe.output.icon;
        if (laboratoryBrewTimer.recipeNameLabel != null)
            laboratoryBrewTimer.recipeNameLabel.text = quantity > 1 ? $"{recipe.displayName} x{quantity}" : recipe.displayName;
        if (laboratoryBrewTimer.fillBar != null) laboratoryBrewTimer.fillBar.fillAmount = currentLabRoom.GetBrewProgress01();
        if (laboratoryBrewTimer.timeRemainingLabel != null)
        {
            float remaining = currentLabRoom.GetBrewRemainingSeconds();
            laboratoryBrewTimer.timeRemainingLabel.text = float.IsInfinity(remaining)
                ? "No bunnies working" // rate is 0 — every present worker left, brewing is stalled, not broken
                : BunnyInfoUI.FormatCooldown(remaining);
        }
    }

    private void RefreshRecentlyCrafted()
    {
        if (recentlyCraftedListContainer == null || recentlyCraftedRowPrefab == null || currentLabRoom == null) return;

        foreach (Transform child in recentlyCraftedListContainer)
            Destroy(child.gameObject);

        foreach (ConsumableDefinition consumable in currentLabRoom.GetRecentlyCrafted())
        {
            if (consumable == null) continue;

            GameObject rowObj = Instantiate(recentlyCraftedRowPrefab, recentlyCraftedListContainer);
            Image icon = rowObj.GetComponentInChildren<Image>();
            if (icon != null) icon.sprite = consumable.icon;
            TextMeshProUGUI label = rowObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = consumable.displayName;
        }
    }

    // Same toggle idiom as BunnyInfoUI's ToggleTraitPicker/ToggleFruitPicker.
    private void ToggleRecipePicker()
    {
        if (recipePickerRoot == null || currentLabRoom == null) return;

        bool opening = !recipePickerRoot.activeSelf;
        CloseQuantityPicker();
        recipePickerRoot.SetActive(opening);
        if (opening) RefreshRecipePickerList();
    }

    private void CloseRecipePicker()
    {
        if (recipePickerRoot != null) recipePickerRoot.SetActive(false);
    }

    // Step 1: the discovered-recipe list, gated by LaboratoryRecipeUnlockTracker.IsDiscovered (a recipe
    // that's ever been discovered stays listed forever — sticky ledger, which alone satisfies "discovered
    // or previously owned") and disabled entirely while the room's already brewing something (no queueing —
    // confirmed with Ethan). Selecting a row doesn't start the brew directly anymore — it opens the
    // quantity picker (step 2) first.
    private void RefreshRecipePickerList()
    {
        if (recipePickerListContainer == null || recipeRowPrefab == null || currentLabRoom == null) return;
        if (LaboratoryRecipeCatalog.Instance == null || LaboratoryRecipeUnlockTracker.Instance == null) return;

        foreach (Transform child in recipePickerListContainer)
            Destroy(child.gameObject);

        bool canStartNew = currentLabRoom.CanStartNewBrew;
        foreach (RecipeDefinition recipe in LaboratoryRecipeCatalog.Instance.AllRecipes)
        {
            if (recipe == null || !LaboratoryRecipeUnlockTracker.Instance.IsDiscovered(recipe)) continue;

            GameObject rowObj = Instantiate(recipeRowPrefab, recipePickerListContainer);
            RecipeRowUI row = rowObj.GetComponent<RecipeRowUI>();
            if (row == null)
            {
                Debug.LogWarning("AssignmentUI: recipeRowPrefab needs a RecipeRowUI component with icon/name/ingredientIcon/ingredientCountText/selectButton wired in the Inspector.");
                continue;
            }

            if (row.recipeIcon != null) row.recipeIcon.sprite = recipe.icon;
            if (row.recipeNameText != null) row.recipeNameText.text = recipe.displayName;

            // Cost preview at 1x here — the real per-quantity affordability check happens in the quantity
            // picker (step 2), this row only needs to convey "can I make this at all."
            bool affordableAt1x = SetIngredientDisplay(row, recipe, 1);

            if (row.selectButton != null)
            {
                row.selectButton.interactable = affordableAt1x && canStartNew;
                row.selectButton.onClick.RemoveAllListeners();
                row.selectButton.onClick.AddListener(() => OpenQuantityPicker(recipe));
            }
        }
    }

    // Every recipe costs some quantity of exactly ONE herb tier (confirmed with Ethan as a permanent
    // assumption, not just true of today's 3 starter recipes) — reads RecipeDefinition.ingredients[0]
    // directly rather than instantiating a sub-list. Returns true if that single cost is currently
    // affordable at the given quantity multiplier — drives whether the row/button is interactable.
    // Red-tints the count text rather than hiding the row outright, so the player can see what's short.
    private bool SetIngredientDisplay(RecipeRowUI row, RecipeDefinition recipe, int quantity)
    {
        if (recipe.ingredients == null || recipe.ingredients.Count == 0) return false;

        RecipeIngredientCost cost = recipe.ingredients[0];
        int neededAmount = cost.amount * quantity;
        bool affordable = cost.herb != null && ForagingInventoryManager.Instance != null
            && ForagingInventoryManager.Instance.GetMaterialCount(cost.herb) >= neededAmount;

        if (row.ingredientIcon != null) row.ingredientIcon.sprite = cost.herb != null ? cost.herb.icon : null;
        if (row.ingredientCountText != null)
        {
            row.ingredientCountText.text = $"x{neededAmount}";
            // Only override to red when short — each row is a fresh Instantiate every refresh, so leaving
            // the affordable case untouched preserves whatever color was authored on the prefab (black)
            // instead of forcing it to white.
            if (!affordable) row.ingredientCountText.color = Color.red;
        }

        return affordable;
    }

    // Step 2: 1x/5x/10x, each greyed out (not interactable) if the recipe's ingredient cost times that
    // multiplier isn't currently affordable. Fixed 3-option set — no per-option prefab needed, just the 3
    // hardcoded buttons wired in the Inspector.
    private void OpenQuantityPicker(RecipeDefinition recipe)
    {
        if (quantityPickerRoot == null || recipe == null) return;

        quantityPickerRecipe = recipe;
        recipePickerRoot.SetActive(false);
        quantityPickerRoot.SetActive(true);

        if (quantityPickerRecipeNameText != null) quantityPickerRecipeNameText.text = recipe.displayName;

        SetQuantityOptionAffordability(quantity1Button, recipe, 1);
        SetQuantityOptionAffordability(quantity5Button, recipe, 5);
        SetQuantityOptionAffordability(quantity10Button, recipe, 10);
    }

    // Greys the button itself out (Button.interactable = false) when unaffordable — no separate cost text
    // per option, the recipe row one screen back already shows the per-1x herb cost.
    private void SetQuantityOptionAffordability(Button button, RecipeDefinition recipe, int quantity)
    {
        if (button == null) return;

        bool affordable = recipe.ingredients.All(cost =>
            cost.herb != null && ForagingInventoryManager.Instance != null
            && ForagingInventoryManager.Instance.GetMaterialCount(cost.herb) >= cost.amount * quantity);
        button.interactable = affordable;
    }

    private void CloseQuantityPicker()
    {
        if (quantityPickerRoot != null) quantityPickerRoot.SetActive(false);
        quantityPickerRecipe = null;
    }

    private void OnQuantitySelected(int quantity)
    {
        if (quantityPickerRecipe == null || currentLabRoom == null) return;

        // Failure (unaffordable, room already brewing, etc.) already surfaces its own notification from
        // StartBrew/TryWithdrawIngredients — nothing else to do here on a false return.
        if (!currentLabRoom.StartBrew(quantityPickerRecipe, quantity)) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        CloseRecipePicker();
        CloseQuantityPicker();
        RefreshLaboratoryExtras();
    }
}