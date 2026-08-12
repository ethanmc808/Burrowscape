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
    // to everything above — the normal assign/unassign flow is completely unmodified. ----------
    [Header("Laboratory Extras (shown only when currentRoom is LaboratoryRoom)")]
    [SerializeField] private GameObject laboratoryExtrasRoot;
    [Tooltip("One LaboratoryBrewRowUI per bunny DwellerRoster.GetBunniesAssignedTo(currentRoom) returns — automatically the right length for the room's grade since capacity is just BrewSpot count.")]
    [SerializeField] private Transform laboratoryBrewListContainer;
    [Tooltip("Row prefab needs a LaboratoryBrewRowUI component — see that file.")]
    [SerializeField] private GameObject laboratoryBrewRowPrefab;

    [Header("Laboratory — Recipe Picker (opened per-bunny from a LaboratoryBrewRowUI's selectCraftButton)")]
    [SerializeField] private GameObject recipePickerRoot;
    [Tooltip("Optional — clicking a bunny's craft button again also closes it (same toggle idiom as BunnyInfoUI's pickers).")]
    [SerializeField] private Button recipePickerCloseButton;
    [SerializeField] private Transform recipePickerListContainer;
    [Tooltip("Row prefab needs a RecipeRowUI component — see that file.")]
    [SerializeField] private GameObject recipeRowPrefab;
    [Tooltip("Small prefab: an Image (herb icon) + a TextMeshProUGUI ('x3') — one instantiated per RecipeIngredientCost inside each RecipeRowUI's ingredientCostContainer. Needs an IngredientCostSlotUI component.")]
    [SerializeField] private GameObject ingredientCostSlotPrefab;

    [Header("Laboratory — Recently Crafted")]
    [SerializeField] private Transform recentlyCraftedListContainer;
    [Tooltip("Simple prefab: an Image (icon) + TextMeshProUGUI child (name) — same shape as bunnyButtonPrefab minus the button.")]
    [SerializeField] private GameObject recentlyCraftedRowPrefab;

    private LaboratoryRoom currentLabRoom;
    private NPCBunny recipePickerTargetBunny;

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

        if (recipePickerCloseButton != null)
            recipePickerCloseButton.onClick.AddListener(CloseRecipePicker);
        CloseRecipePicker();
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
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentRoom = null;
        currentLabRoom = null;
        CloseRecipePicker();
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

        // The unassigned bunny's LaboratoryRoom.ReleaseSpot (called inside UnassignFromJob) already
        // cancelled any active brew on the room's side — this just syncs the visible row list to match.
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

    // One row per bunny assigned to the room (whether physically arrived yet or not — StartBrew below
    // no-ops cleanly if a bunny clicks "select item to craft" before it's actually present at its spot).
    // Destroy-and-rebuild every refresh, same idiom every other picker/list in this codebase uses.
    private void RefreshLaboratoryExtras()
    {
        if (laboratoryExtrasRoot == null || laboratoryBrewListContainer == null || laboratoryBrewRowPrefab == null || currentLabRoom == null) return;

        foreach (Transform child in laboratoryBrewListContainer)
            Destroy(child.gameObject);

        foreach (NPCBunny bunny in DwellerRoster.Instance.GetBunniesAssignedTo(currentRoom))
        {
            GameObject rowObj = Instantiate(laboratoryBrewRowPrefab, laboratoryBrewListContainer);
            LaboratoryBrewRowUI row = rowObj.GetComponent<LaboratoryBrewRowUI>();
            if (row == null)
            {
                Debug.LogWarning("AssignmentUI: laboratoryBrewRowPrefab needs a LaboratoryBrewRowUI component with timer/idle sub-elements wired in the Inspector.");
                continue;
            }

            if (row.bunnyNameText != null) row.bunnyNameText.text = bunny.name;

            bool brewing = currentLabRoom.IsBrewing(bunny);
            if (row.timerRoot != null) row.timerRoot.SetActive(brewing);
            if (row.idleRoot != null) row.idleRoot.SetActive(!brewing);

            if (brewing)
            {
                RefreshBrewTimer(row.timer, bunny);
            }
            else if (row.selectCraftButton != null)
            {
                row.selectCraftButton.onClick.RemoveAllListeners();
                row.selectCraftButton.onClick.AddListener(() => ToggleRecipePicker(bunny));
            }
        }

        RefreshRecentlyCrafted();
    }

    private void RefreshBrewTimer(BrewTimerUI timer, NPCBunny bunny)
    {
        if (timer == null || currentLabRoom == null) return;

        RecipeDefinition recipe = currentLabRoom.GetActiveBrewRecipe(bunny);
        if (recipe == null || recipe.output == null) return;

        if (timer.itemIcon != null) timer.itemIcon.sprite = recipe.output.icon;
        if (timer.recipeNameLabel != null) timer.recipeNameLabel.text = recipe.output.displayName;
        if (timer.fillBar != null) timer.fillBar.fillAmount = currentLabRoom.GetBrewProgress01(bunny);
        if (timer.timeRemainingLabel != null)
            timer.timeRemainingLabel.text = BunnyInfoUI.FormatCooldown(currentLabRoom.GetBrewRemainingSeconds(bunny));
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

    // Same toggle idiom as BunnyInfoUI's ToggleTraitPicker/ToggleFruitPicker — clicking the SAME bunny's
    // craft button again closes it; clicking a DIFFERENT bunny's button while already open just retargets
    // and refreshes rather than requiring a close-then-reopen.
    private void ToggleRecipePicker(NPCBunny bunny)
    {
        if (recipePickerRoot == null || currentLabRoom == null) return;

        bool opening = !recipePickerRoot.activeSelf || recipePickerTargetBunny != bunny;
        recipePickerTargetBunny = opening ? bunny : null;
        recipePickerRoot.SetActive(opening);
        if (opening) RefreshRecipePickerList();
    }

    private void CloseRecipePicker()
    {
        if (recipePickerRoot != null) recipePickerRoot.SetActive(false);
        recipePickerTargetBunny = null;
    }

    // Two kinds of rows, in order: (1) "Join [Bunny]'s brew" for every batch currently short a second
    // worker (no herb cost — the batch already paid at StartBrew time — and always affordable/interactable
    // regardless of BatchCapacity, since joining doesn't start a new batch), then (2) the normal
    // discovered-recipe list, gated by LaboratoryRecipeUnlockTracker.IsDiscovered (a recipe that's ever
    // been discovered stays listed forever — sticky ledger, which alone satisfies "discovered or
    // previously owned") and disabled once the room's already brewing BatchCapacity distinct recipes.
    // Both kinds reuse RecipeRowUI — a join row just leaves ingredientCostContainer empty.
    private void RefreshRecipePickerList()
    {
        if (recipePickerListContainer == null || recipeRowPrefab == null || currentLabRoom == null) return;
        if (LaboratoryRecipeCatalog.Instance == null || LaboratoryRecipeUnlockTracker.Instance == null) return;

        foreach (Transform child in recipePickerListContainer)
            Destroy(child.gameObject);

        NPCBunny targetBunny = recipePickerTargetBunny;

        foreach ((NPCBunny existingWorker, RecipeDefinition recipe) in currentLabRoom.GetJoinableBrews())
        {
            if (existingWorker == targetBunny || recipe == null) continue;

            GameObject rowObj = Instantiate(recipeRowPrefab, recipePickerListContainer);
            RecipeRowUI row = rowObj.GetComponent<RecipeRowUI>();
            if (row == null)
            {
                Debug.LogWarning("AssignmentUI: recipeRowPrefab needs a RecipeRowUI component with icon/name/ingredientCostContainer/selectButton wired in the Inspector.");
                continue;
            }

            if (row.recipeIcon != null) row.recipeIcon.sprite = recipe.icon;
            if (row.recipeNameText != null) row.recipeNameText.text = $"Join {existingWorker.name}: {recipe.displayName}";
            if (row.ingredientCostContainer != null)
                foreach (Transform child in row.ingredientCostContainer) Destroy(child.gameObject);

            if (row.selectButton != null)
            {
                row.selectButton.interactable = true;
                row.selectButton.onClick.RemoveAllListeners();
                row.selectButton.onClick.AddListener(() => OnJoinBrewSelected(targetBunny, existingWorker));
            }
        }

        bool canStartNew = currentLabRoom.CanStartNewBatch;
        foreach (RecipeDefinition recipe in LaboratoryRecipeCatalog.Instance.AllRecipes)
        {
            if (recipe == null || !LaboratoryRecipeUnlockTracker.Instance.IsDiscovered(recipe)) continue;

            GameObject rowObj = Instantiate(recipeRowPrefab, recipePickerListContainer);
            RecipeRowUI row = rowObj.GetComponent<RecipeRowUI>();
            if (row == null)
            {
                Debug.LogWarning("AssignmentUI: recipeRowPrefab needs a RecipeRowUI component with icon/name/ingredientCostContainer/selectButton wired in the Inspector.");
                continue;
            }

            if (row.recipeIcon != null) row.recipeIcon.sprite = recipe.icon;
            if (row.recipeNameText != null) row.recipeNameText.text = recipe.displayName;

            bool affordable = BuildIngredientCostRows(row, recipe);

            if (row.selectButton != null)
            {
                row.selectButton.interactable = affordable && canStartNew;
                row.selectButton.onClick.RemoveAllListeners();
                row.selectButton.onClick.AddListener(() => OnRecipeSelected(targetBunny, recipe));
            }
        }
    }

    // Returns true if every ingredient is currently affordable — drives whether the row's select button
    // is interactable. Red-tints any short herb's count text rather than hiding the row outright, so the
    // player can see what they're missing.
    private bool BuildIngredientCostRows(RecipeRowUI row, RecipeDefinition recipe)
    {
        bool affordable = true;
        if (row.ingredientCostContainer == null || ingredientCostSlotPrefab == null) return affordable;

        foreach (Transform child in row.ingredientCostContainer)
            Destroy(child.gameObject);

        foreach (RecipeIngredientCost cost in recipe.ingredients)
        {
            bool haveEnough = cost.herb != null && ForagingInventoryManager.Instance != null
                && ForagingInventoryManager.Instance.GetMaterialCount(cost.herb) >= cost.amount;
            if (!haveEnough) affordable = false;

            GameObject slotObj = Instantiate(ingredientCostSlotPrefab, row.ingredientCostContainer);
            IngredientCostSlotUI slot = slotObj.GetComponent<IngredientCostSlotUI>();
            if (slot == null) continue;

            if (slot.herbIcon != null) slot.herbIcon.sprite = cost.herb != null ? cost.herb.icon : null;
            if (slot.countText != null)
            {
                slot.countText.text = $"x{cost.amount}";
                slot.countText.color = haveEnough ? Color.white : Color.red;
            }
        }

        return affordable;
    }

    private void OnRecipeSelected(NPCBunny bunny, RecipeDefinition recipe)
    {
        if (bunny == null || recipe == null || currentLabRoom == null) return;

        // Failure (unaffordable, bunny not actually present yet, room already at BatchCapacity, etc.)
        // already surfaces its own notification from StartBrew/TryWithdrawIngredients — nothing else to
        // do here on a false return.
        if (!currentLabRoom.StartBrew(bunny, recipe)) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        CloseRecipePicker();
        RefreshLaboratoryExtras();
    }

    private void OnJoinBrewSelected(NPCBunny bunny, NPCBunny existingWorker)
    {
        if (bunny == null || existingWorker == null || currentLabRoom == null) return;
        if (!currentLabRoom.JoinBrew(bunny, existingWorker)) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        CloseRecipePicker();
        RefreshLaboratoryExtras();
    }
}