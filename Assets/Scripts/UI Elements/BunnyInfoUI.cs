using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Replaces BunnyApprovalUI (Gate & Queue Scripts) + BunnyStatsUI (UI Elements) with one shared panel —
// same info whether the bunny is still awaiting gate approval or already living in the base, with the
// Approve/Reject controls shown only for the former. Neither old panel knew about Gender/Type/Level/
// Stats/Traits/Passives (added by the bunny type system after both were built) — rather than duplicate
// all of that into two nearly-identical panels, this merges them into one.
//
// Fixed screen position — deliberately NOT world-anchored to the queue spot like the old BunnyApprovalUI
// was (dropped per design call: didn't read well, and Fallout Shelter's equivalent panel stays fixed to
// the side of the screen instead of floating over the dweller).
//
// CUTOVER COMPLETE: BunnyInfoClickHandler is on every NPC Bunny Default prefab and BunnyClickPriority
// points at BunnyInfoUI.Instance. BunnyApprovalUI.cs/BunnyStatsUI.cs and their click handlers have been
// deleted (dead — see git history for the HP bar/potion button BunnyStatsUI had, since ported into this
// panel below).
public class BunnyInfoUI : MonoBehaviour
{
    public static BunnyInfoUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Identity")]
    [SerializeField] private TextMeshProUGUI bunnyNameLabel;
    [Tooltip("Swapped between maleGenderIcon/femaleGenderIcon based on bunny.Gender.")]
    [SerializeField] private Image genderIconImage;
    [SerializeField] private Sprite maleGenderIcon;
    [SerializeField] private Sprite femaleGenderIcon;
    [SerializeField] private TextMeshProUGUI typeLabel;
    [Tooltip("Optional — set BunnyTypeDefinition.icon per type to populate this. Hidden automatically for types with no icon assigned yet.")]
    [SerializeField] private Image typeIconImage;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [Tooltip("Optional — set once a Nature/Zodiac label element exists in the Inspector; skipped otherwise (see the Bunny Stat System Redesign design doc).")]
    [SerializeField] private TextMeshProUGUI natureLabel;

    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI hpLabel;
    [SerializeField] private TextMeshProUGUI attackLabel;
    [SerializeField] private TextMeshProUGUI defenseLabel;
    [SerializeField] private TextMeshProUGUI speedLabel;
    [SerializeField] private TextMeshProUGUI luckLabel;

    [Header("HP Bar + Potion (Image.Type = Filled) — ported from the retired BunnyStatsUI")]
    [SerializeField] private Image hpBar;
    // Consumes 1 potion from ForagingInventoryManager's stock and heals ForagingManager.PotionHealAmount
    // — same flat amount Foraging's own auto-use potion path applies, just player-triggered instead of
    // an encounter-loss auto-use.
    [SerializeField] private Button usePotionButton;
    [SerializeField] private TextMeshProUGUI potionButtonLabel;

    [Header("Traits & Passives")]
    [Tooltip("Simple prefab: a single TextMeshProUGUI (no Button needed) — shared by both lists below, same instantiate-per-item pattern AssignmentUI uses for its bunny buttons.")]
    [SerializeField] private GameObject listEntryPrefab;
    [SerializeField] private Transform traitListContainer;
    [SerializeField] private Transform passiveListContainer;

    [Header("Need Bars (Image.Type = Filled)")]
    [SerializeField] private Image hungerBar;
    [SerializeField] private Image thirstBar;
    [SerializeField] private Image energyBar;
    [SerializeField] private Image moodBar;

    [Header("Approval Controls (shown only while the bunny IsAwaitingApproval)")]
    [SerializeField] private GameObject approvalControlsRoot;
    [SerializeField] private Button approveButton;
    [SerializeField] private Button rejectButton;

    [Header("Foraging (Dispatch entry point A — see Foraging_DesignDoc.md)")]
    [Tooltip("Optional — hidden entirely if not wired up. Skips straight to ForagingDispatchUI's Location step, since the bunny is already chosen.")]
    [SerializeField] private Button sendForagingButton;

    [Header("Foraging Accessory Slot (persistent equip — see Foraging Trip Detail Panel + Item Expansion design doc)")]
    [Tooltip("Shows bunny.EquippedAccessory's icon; empty/hidden when nothing's equipped. Clicking toggles accessoryPickerRoot.")]
    [SerializeField] private Button accessorySlotButton;
    [SerializeField] private Image accessorySlotIcon;
    [Tooltip("Simple popup list, toggled open/closed by accessorySlotButton — rebuilt from ForagingInventoryManager.GetAccessoriesInStock() every time it opens.")]
    [SerializeField] private GameObject accessoryPickerRoot;
    [SerializeField] private Transform accessoryPickerListContainer;
    [SerializeField] private GameObject accessoryButtonPrefab; // Button + TextMeshProUGUI child

    [Header("Foraging Fruit Feeding (see FruitFeeding_DesignDoc.md) — placement in the panel is Ethan's call, just wiring the button/lists here")]
    [Tooltip("Opens fruitPickerRoot. Only interactable while the base has at least 1 fruit in stock.")]
    [SerializeField] private Button feedFruitButton;
    [Tooltip("List step — rebuilt from ForagingInventoryManager.GetFruitsInStock() every time it opens.")]
    [SerializeField] private GameObject fruitPickerRoot;
    [SerializeField] private Transform fruitPickerListContainer;
    [Tooltip("Row prefab: a Button, an Image (icon) child, and 2 TextMeshProUGUI children — [0] name+count, [1] stat boosted.")]
    [SerializeField] private GameObject fruitPickerRowPrefab;
    [Tooltip("Detail/confirm step — shown after a fruit is picked from the list.")]
    [SerializeField] private GameObject fruitDetailRoot;
    [SerializeField] private Image fruitDetailIcon;
    [SerializeField] private TextMeshProUGUI fruitDetailNameText;
    [SerializeField] private TextMeshProUGUI fruitDetailStatText;
    [SerializeField] private Button eatFruitButton;
    [Tooltip("Returns to the list without feeding.")]
    [SerializeField] private Button fruitDetailBackButton;
    [Tooltip("Horizontal gap (pixels) between the clicked fruit row's right edge and FruitDetail's own anchor point (its left-middle). NOT the same as the visible gap to the Eat Fruit/Back buttons — those sit at their own local offset INSIDE FruitDetail (whatever you positioned them at in the Editor), so this value also has to cancel that out. If you ever move the buttons within FruitDetail, retune this to match.")]
    [SerializeField] private float fruitDetailHorizontalOffset = -150f;

    [Header("Adaptable — Neutral-only trait re-roll (see BunnyTypeNiches_DesignDoc.md)")]
    [Tooltip("The whole block, including the button below — shown only while currentBunny.HasAdaptablePassive is true (Neutral type AND Ghost has discovered this passive). Stays visible while on cooldown; only the button itself becomes non-interactable then.")]
    [SerializeField] private GameObject adaptableRoot;
    [Tooltip("Opens traitPickerRoot. Non-interactable while currentBunny.CanRerollTrait() is false (on cooldown).")]
    [SerializeField] private Button rerollTraitButton;
    [Tooltip("Ready when not on cooldown; a countdown otherwise. Optional — skipped if not wired.")]
    [SerializeField] private TextMeshProUGUI adaptableCooldownText;
    [Tooltip("List step — one row per entry in currentBunny.Traits, rebuilt every time it opens. Clicking a row immediately calls TryRerollTrait for that trait (no separate confirm step, unlike Fruit Feeding's list->detail flow — trait re-roll has no extra info to show per row worth a detail panel).")]
    [SerializeField] private GameObject traitPickerRoot;
    [SerializeField] private Transform traitPickerListContainer;
    [Tooltip("Row prefab needs a TraitRerollRowUI component (nameText + button) — see that file.")]
    [SerializeField] private GameObject traitPickerRowPrefab;

    private ForagingFruitDefinition selectedFruit;
    private RectTransform fruitDetailRect;

    private NPCBunny currentBunny;

    private void OnEnable()
    {
        if (ForagingInventoryManager.Instance != null)
            ForagingInventoryManager.Instance.OnInventoryChanged += RefreshPotionButton;
    }

    private void OnDisable()
    {
        if (ForagingInventoryManager.Instance != null)
            ForagingInventoryManager.Instance.OnInventoryChanged -= RefreshPotionButton;
    }

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
        approveButton.onClick.AddListener(OnApproveClicked);
        rejectButton.onClick.AddListener(OnRejectClicked);

        if (usePotionButton != null)
            usePotionButton.onClick.AddListener(UsePotionOnCurrentBunny);

        if (sendForagingButton != null)
            sendForagingButton.onClick.AddListener(OnSendForagingClicked);

        if (accessorySlotButton != null)
            accessorySlotButton.onClick.AddListener(ToggleAccessoryPicker);
        if (accessoryPickerRoot != null)
            accessoryPickerRoot.SetActive(false);

        if (feedFruitButton != null)
            feedFruitButton.onClick.AddListener(ToggleFruitPicker);
        if (eatFruitButton != null)
            eatFruitButton.onClick.AddListener(OnEatFruitClicked);
        if (fruitDetailBackButton != null)
            fruitDetailBackButton.onClick.AddListener(ShowFruitList);
        if (fruitPickerRoot != null)
            fruitPickerRoot.SetActive(false);
        if (fruitDetailRoot != null)
        {
            fruitDetailRoot.SetActive(false);
            fruitDetailRect = fruitDetailRoot.GetComponent<RectTransform>();
        }

        if (rerollTraitButton != null)
            rerollTraitButton.onClick.AddListener(ToggleTraitPicker);
        if (traitPickerRoot != null)
            traitPickerRoot.SetActive(false);
    }

    private void Update()
    {
        if (!panelRoot.activeSelf) return;

        // Bunny may have been destroyed (deleted/despawned) while the panel was open.
        if (currentBunny == null)
        {
            Close();
            return;
        }

        // Re-checked every frame, not just on Open, so the Approve/Reject controls correctly disappear
        // live if the bunny gets approved/rejected through some other path while this panel is open.
        approvalControlsRoot.SetActive(currentBunny.IsAwaitingApproval);

        if (sendForagingButton != null)
            sendForagingButton.gameObject.SetActive(CanShowSendForagingButton());

        RefreshBars();
        RefreshStatLabels();
        RefreshPotionButton();
        RefreshAccessorySlot();
        RefreshFeedFruitButton();
        RefreshAdaptableBlock();

        // TEMP debug logging — remove once the "click does nothing" bug is confirmed fixed. Logs every UI
        // element under the cursor, front-to-back, on any left click while this panel is open — to catch
        // an invisible raycast-blocking element sitting on top of the Reroll Trait button.
        if (Input.GetMouseButtonDown(0) && EventSystem.current != null)
        {
            PointerEventData ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0)
            {
                Debug.Log("[TraitReroll] Click raycast: NOTHING hit (no UI element under cursor).");
            }
            else
            {
                string report = string.Join(" | ", hits.Select(h => GetHierarchyPath(h.gameObject.transform)));
                Debug.Log($"[TraitReroll] Click raycast hits (front-to-back): {report}");
            }
        }
    }

    // TEMP debug helper — remove alongside the raycast logging above.
    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;

        bunnyNameLabel.text = bunny.BunnyName;
        if (genderIconImage != null)
            genderIconImage.sprite = bunny.Gender == BunnyGender.Male ? maleGenderIcon : femaleGenderIcon;
        typeLabel.text = bunny.Type.ToString();
        // Guarded — typeIconImage is optional until an Image element exists for it in the Inspector.
        // Without this check, an unwired field here would NullReferenceException before panelRoot ever
        // gets activated below, silently breaking the whole panel instead of just missing an icon.
        if (typeIconImage != null)
        {
            typeIconImage.sprite = bunny.TypeIcon;
            typeIconImage.gameObject.SetActive(bunny.TypeIcon != null);
        }
        levelLabel.text = bunny.Level.ToString();
        // Guarded — see typeIconImage's comment above; natureLabel is optional until wired.
        if (natureLabel != null)
            natureLabel.text = bunny.Nature.ToString();

        RefreshStatLabels();

        PopulateList(traitListContainer, bunny.Traits, t => t.displayName);
        PopulateList(passiveListContainer, bunny.ActivePassives, p => p.displayName);

        approvalControlsRoot.SetActive(bunny.IsAwaitingApproval);
        if (sendForagingButton != null)
            sendForagingButton.gameObject.SetActive(CanShowSendForagingButton());

        panelRoot.SetActive(true);
        AudioManager.EnsureInstance().PlayUIOpen();
        RefreshBars();
        RefreshPotionButton();
        if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
        RefreshAccessorySlot();
        if (fruitPickerRoot != null) fruitPickerRoot.SetActive(false);
        if (fruitDetailRoot != null) fruitDetailRoot.SetActive(false);
        selectedFruit = null;
        RefreshFeedFruitButton();
        if (traitPickerRoot != null) traitPickerRoot.SetActive(false);
        RefreshAdaptableBlock();
    }

    public void Close() => Close(true);

    // playSound=false is used by confirm actions (OnApproveClicked/OnRejectClicked) that close this
    // panel as a side effect of succeeding — PlayUIClose is reserved for an actual Close/Back button
    // press, not layered on top of the confirm action's own PlayButtonClick.
    public void Close(bool playSound)
    {
        panelRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentBunny = null;
        if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
        if (fruitPickerRoot != null) fruitPickerRoot.SetActive(false);
        if (fruitDetailRoot != null) fruitDetailRoot.SetActive(false);
        selectedFruit = null;
        if (traitPickerRoot != null) traitPickerRoot.SetActive(false);
    }

    private void RefreshBars()
    {
        hungerBar.fillAmount = currentBunny.HungerValue / 100f;
        thirstBar.fillAmount = currentBunny.ThirstValue / 100f;
        energyBar.fillAmount = currentBunny.EnergyValue / 100f;
        moodBar.fillAmount = currentBunny.MoodValue / 100f;

        if (hpBar != null)
        {
            int maxHP = currentBunny.Stats.HP;
            hpBar.fillAmount = maxHP > 0 ? (float)currentBunny.HPValue / maxHP : 0f;
        }
    }

    // Disabled while there's no potion stock, the bunny's already at full HP, or it's Fainted — see
    // NPCBunny.HealHP's own no-op guard for why a fainted bunny can't be healed mid-battle.
    private void RefreshPotionButton()
    {
        if (usePotionButton == null || currentBunny == null) return;

        int stock = ForagingInventoryManager.Instance != null ? ForagingInventoryManager.Instance.PotionStock : 0;
        bool atFullHP = currentBunny.HPValue >= currentBunny.Stats.HP;
        bool isFainted = currentBunny.CurrentState == BunnyState.Fainted;
        usePotionButton.interactable = stock > 0 && !atFullHP && !isFainted;

        if (potionButtonLabel != null) potionButtonLabel.text = $"Use Potion ({stock})";
    }

    private void UsePotionOnCurrentBunny()
    {
        if (currentBunny == null || ForagingInventoryManager.Instance == null || ForagingManager.Instance == null) return;
        if (!ForagingInventoryManager.Instance.TryWithdrawPotions(1)) return;

        currentBunny.HealHP(ForagingManager.Instance.PotionHealAmount);
        RefreshBars();
        RefreshPotionButton();
    }

    // Re-run every Update (not just on Open) so a stat change from another source while the panel is
    // open — e.g. feeding a Fruit via the picker below, which deliberately doesn't reopen the whole
    // panel — actually shows up without needing to close/reopen. Previously only ran once in
    // OpenForBunny, which silently left these numbers stale after a feed.
    private void RefreshStatLabels()
    {
        if (currentBunny == null) return;

        hpLabel.text = currentBunny.Stats.HP.ToString(); // Nature never affects HP — no indicator possible here
        attackLabel.text = FormatStatWithNatureIndicator(currentBunny, currentBunny.Stats.Attack, NatureStat.Attack);
        defenseLabel.text = FormatStatWithNatureIndicator(currentBunny, currentBunny.Stats.Defense, NatureStat.Defense);
        speedLabel.text = FormatStatWithNatureIndicator(currentBunny, currentBunny.Stats.Speed, NatureStat.Speed);
        luckLabel.text = FormatStatWithNatureIndicator(currentBunny, currentBunny.Stats.Luck, NatureStat.Luck);
    }

    // ---------- Foraging accessory slot (persistent equip — see BunnyInfoUI's Header comment above) ----------

    private void RefreshAccessorySlot()
    {
        if (accessorySlotIcon == null || currentBunny == null) return;

        ForagingAccessoryDefinition equipped = currentBunny.EquippedAccessory;
        accessorySlotIcon.sprite = equipped != null ? equipped.icon : null;
        accessorySlotIcon.enabled = equipped != null && equipped.icon != null;
    }

    private void ToggleAccessoryPicker()
    {
        if (accessoryPickerRoot == null) return;

        bool opening = !accessoryPickerRoot.activeSelf;
        // Only one picker open at a time.
        if (opening) CloseFruitPicker();
        accessoryPickerRoot.SetActive(opening);
        if (opening) RefreshAccessoryPicker();
    }

    private void RefreshAccessoryPicker()
    {
        if (accessoryPickerListContainer == null || accessoryButtonPrefab == null) return;

        foreach (Transform child in accessoryPickerListContainer)
            Destroy(child.gameObject);

        // "None" option first, so an equipped accessory can be unequipped back to stock.
        GameObject noneObj = Instantiate(accessoryButtonPrefab, accessoryPickerListContainer);
        noneObj.GetComponentInChildren<TextMeshProUGUI>().text = "None";
        noneObj.GetComponent<Button>().onClick.AddListener(() => OnAccessoryPicked(null));

        if (ForagingInventoryManager.Instance == null) return;

        // Only accessories actually held in the base stockpile are offered — same "stock is the catalog"
        // approach ForagingDispatchUI's old picker used.
        foreach (ForagingAccessoryDefinition accessory in ForagingInventoryManager.Instance.GetAccessoriesInStock())
        {
            GameObject buttonObj = Instantiate(accessoryButtonPrefab, accessoryPickerListContainer);
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = accessory.displayName;
            buttonObj.GetComponent<Button>().onClick.AddListener(() => OnAccessoryPicked(accessory));
        }
    }

    private void OnAccessoryPicked(ForagingAccessoryDefinition accessory)
    {
        if (currentBunny == null || ForagingInventoryManager.Instance == null) return;

        ForagingInventoryManager.Instance.TryEquipAccessory(currentBunny, accessory);
        RefreshAccessorySlot();
        if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
    }

    // ---------- Foraging fruit feeding (see FruitFeeding_DesignDoc.md) ----------

    private void RefreshFeedFruitButton()
    {
        if (feedFruitButton == null) return;
        feedFruitButton.interactable = ForagingInventoryManager.Instance != null
            && ForagingInventoryManager.Instance.GetFruitsInStock().Any();
    }

    private void ToggleFruitPicker()
    {
        if (fruitPickerRoot == null) return;

        bool opening = !fruitPickerRoot.activeSelf;
        // Only one picker open at a time.
        if (opening && accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
        fruitPickerRoot.SetActive(opening);
        if (opening) ShowFruitList();
    }

    private void CloseFruitPicker()
    {
        if (fruitPickerRoot != null) fruitPickerRoot.SetActive(false);
        if (fruitDetailRoot != null) fruitDetailRoot.SetActive(false);
        selectedFruit = null;
    }

    // Back to the list step (from the detail/confirm step, or right after opening) — kept open rather
    // than closing the whole picker so the player can feed several fruits in one sitting. The main
    // panel's closeButton still works independently to dismiss everything.
    private void ShowFruitList()
    {
        selectedFruit = null;
        if (fruitDetailRoot != null) fruitDetailRoot.SetActive(false);
        RefreshFruitPickerList();
    }

    private void RefreshFruitPickerList()
    {
        if (fruitPickerListContainer == null || fruitPickerRowPrefab == null
            || ForagingInventoryManager.Instance == null || currentBunny == null) return;

        foreach (Transform child in fruitPickerListContainer)
            Destroy(child.gameObject);

        foreach (ForagingFruitDefinition fruit in ForagingInventoryManager.Instance.GetFruitsInStock())
        {
            GameObject rowObj = Instantiate(fruitPickerRowPrefab, fruitPickerListContainer);

            FruitRowUI row = rowObj.GetComponent<FruitRowUI>();
            if (row == null)
            {
                Debug.LogWarning("BunnyInfoUI: fruitPickerRowPrefab needs a FruitRowUI component with icon/nameText/statText/button wired in the Inspector.");
                continue;
            }

            if (row.icon != null)
            {
                row.icon.sprite = fruit.icon;
                row.icon.enabled = fruit.icon != null;
            }

            int count = ForagingInventoryManager.Instance.GetFruitCount(fruit);
            if (row.nameText != null) row.nameText.text = $"{fruit.displayName} x{count}";
            if (row.statText != null) row.statText.text = FormatFruitStatLine(fruit);

            // Greyed out (non-interactable) once this bunny can't gain any more of the stat this fruit
            // boosts — Ethan's call: with 200+ bunnies, there's no realistic way to track per-bunny EV
            // totals by memory alone, so the button itself needs to say "this would do nothing."
            if (row.button != null)
            {
                RectTransform rowRect = rowObj.GetComponent<RectTransform>();
                row.button.interactable = currentBunny.CanGainEV(fruit.boostedStat);
                row.button.onClick.AddListener(() => OnFruitSelected(fruit, rowRect));
            }
        }
    }

    private void OnFruitSelected(ForagingFruitDefinition fruit, RectTransform rowRect)
    {
        selectedFruit = fruit;
        if (fruitDetailRoot == null) return;

        PositionDetailNextToRow(rowRect);

        fruitDetailRoot.SetActive(true);
        if (fruitDetailIcon != null)
        {
            fruitDetailIcon.sprite = fruit.icon;
            fruitDetailIcon.enabled = fruit.icon != null;
        }
        if (fruitDetailNameText != null) fruitDetailNameText.text = fruit.displayName;
        if (fruitDetailStatText != null) fruitDetailStatText.text = FormatFruitStatLine(fruit);
    }

    // Positions fruitDetailRoot (WITHOUT reparenting it — it stays under whatever parent it was
    // designed with; needs to be under FruitPicker, a close sibling of fruitPickerListContainer, NOT
    // BunnyStatsPanel directly — the latter had a degenerate zero-size rect that made this maths huge and
    // impossible to reason about) at a point that's fruitDetailHorizontalOffset pixels to the right of the
    // CLICKED ROW's own right edge, vertically centered on that same row. Deliberately NOT anchored off
    // fruitPickerListContainer's edge (an earlier version was) — the container can be wider than the row
    // itself, since rows don't stretch to fill it, which put the popup too far right. The offset is
    // negative in practice because it also has to cancel out wherever Eat Fruit/Back happen to sit
    // INSIDE FruitDetail (see the field's Tooltip) — this only ever moves FruitDetail's own anchor point,
    // never touches its children directly.
    private void PositionDetailNextToRow(RectTransform rowRect)
    {
        if (fruitDetailRect == null || rowRect == null) return;

        RectTransform parentRect = fruitDetailRect.parent as RectTransform;
        if (parentRect == null) return;

        Vector3 targetWorld = rowRect.TransformPoint(
            new Vector3(rowRect.rect.xMax + fruitDetailHorizontalOffset, rowRect.rect.center.y, 0f));

        fruitDetailRect.anchorMin = new Vector2(0f, 0f);
        fruitDetailRect.anchorMax = new Vector2(0f, 0f);
        fruitDetailRect.pivot = new Vector2(0f, 0.5f); // "N px to the right" reads as the popup's LEFT edge

        Vector3 localInParent = parentRect.InverseTransformPoint(targetWorld);
        fruitDetailRect.anchoredPosition = new Vector2(
            localInParent.x - parentRect.rect.xMin,
            localInParent.y - parentRect.rect.yMin);
    }

    // Player-facing wording — deliberately hides the underlying EV number (e.g. "+4 EV") behind a
    // simple "+1 Defense" per Ethan's ask: the exact EV-per-stat-point math is an internal implementation
    // detail, not something the player needs to reason about.
    private static string FormatFruitStatLine(ForagingFruitDefinition fruit) => $"+1 {fruit.boostedStat}";

    private void OnEatFruitClicked()
    {
        if (currentBunny == null || selectedFruit == null || ForagingInventoryManager.Instance == null) return;

        ForagingInventoryManager.Instance.TryFeedFruit(currentBunny, selectedFruit);
        RefreshFeedFruitButton();
        // Back to the list (refreshed — stock/greying may have changed), not closing the whole picker,
        // so the player can immediately feed another fruit.
        ShowFruitList();
    }

    // Re-checked every frame (like everything else in Update()) rather than only on Open, so the block
    // appears immediately if Ghost discovers Adaptable while this panel happens to be open on a Neutral
    // bunny, and so the cooldown countdown/button interactability stay live without needing their own timer.
    private void RefreshAdaptableBlock()
    {
        if (adaptableRoot == null || currentBunny == null) return;

        bool hasPassive = currentBunny.HasAdaptablePassive;
        adaptableRoot.SetActive(hasPassive);
        if (!hasPassive)
        {
            if (traitPickerRoot != null) traitPickerRoot.SetActive(false);
            return;
        }

        bool canUse = currentBunny.CanRerollTrait();
        if (rerollTraitButton != null) rerollTraitButton.interactable = canUse;
        if (adaptableCooldownText != null)
            adaptableCooldownText.text = canUse ? "Ready" : FormatCooldown(currentBunny.TraitRerollCooldownRemaining);

        // TEMP debug logging — remove once the "click does nothing" bug is confirmed fixed. Throttled to
        // once/sec since this runs every Update() frame while the panel is open.
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[TraitReroll] RefreshAdaptableBlock: Type={currentBunny.Type}, canUse={canUse}, cooldownRemaining={currentBunny.TraitRerollCooldownRemaining}, button.interactable={(rerollTraitButton != null ? rerollTraitButton.interactable : (bool?)null)}, button.raycastTarget-ish enabled={(rerollTraitButton != null ? rerollTraitButton.enabled : (bool?)null)}, buttonGO.activeInHierarchy={(rerollTraitButton != null ? rerollTraitButton.gameObject.activeInHierarchy : (bool?)null)}");
    }

    private static string FormatCooldown(float seconds)
    {
        System.TimeSpan span = System.TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";
    }

    private void ToggleTraitPicker()
    {
        // TEMP debug logging — remove once the "click does nothing" bug is confirmed fixed.
        Debug.Log($"[TraitReroll] ToggleTraitPicker called. traitPickerRoot={(traitPickerRoot != null)}, currentBunny={(currentBunny != null)}, CanRerollTrait={(currentBunny != null && currentBunny.CanRerollTrait())}");
        if (traitPickerRoot == null || currentBunny == null || !currentBunny.CanRerollTrait()) return;

        bool opening = !traitPickerRoot.activeSelf;
        // Only one picker open at a time — same rule ToggleFruitPicker/ToggleAccessoryPicker follow.
        if (opening)
        {
            if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
            if (fruitPickerRoot != null) fruitPickerRoot.SetActive(false);
        }
        traitPickerRoot.SetActive(opening);
        Debug.Log($"[TraitReroll] traitPickerRoot set active={opening}, actual activeSelf={traitPickerRoot.activeSelf}, activeInHierarchy={traitPickerRoot.activeInHierarchy}");
        if (opening) RefreshTraitPickerList();
    }

    private void RefreshTraitPickerList()
    {
        if (traitPickerListContainer == null || traitPickerRowPrefab == null || currentBunny == null) return;

        // TEMP debug logging — remove once the "click does nothing" bug is confirmed fixed.
        Debug.Log($"[TraitReroll] RefreshTraitPickerList: currentBunny.Traits.Count={currentBunny.Traits.Count}");

        foreach (Transform child in traitPickerListContainer)
            Destroy(child.gameObject);

        // One row per CURRENT trait — clicking a row discards that specific trait and rolls a random
        // replacement (see NPCBunny.TryRerollTrait). No greying-out logic needed here (unlike
        // RefreshFruitPickerList's EV check) — every current trait is always a valid thing to discard.
        foreach (BunnyTraitDefinition trait in currentBunny.Traits)
        {
            GameObject rowObj = Instantiate(traitPickerRowPrefab, traitPickerListContainer);

            TraitRerollRowUI row = rowObj.GetComponent<TraitRerollRowUI>();
            if (row == null)
            {
                Debug.LogWarning("BunnyInfoUI: traitPickerRowPrefab needs a TraitRerollRowUI component with nameText/button wired in the Inspector.");
                continue;
            }

            if (row.nameText != null) row.nameText.text = trait.displayName;
            if (row.button != null)
                row.button.onClick.AddListener(() => OnTraitSelectedForReroll(trait));
        }
    }

    private void OnTraitSelectedForReroll(BunnyTraitDefinition traitToDiscard)
    {
        if (currentBunny == null) return;

        bool succeeded = currentBunny.TryRerollTrait(traitToDiscard);
        if (!succeeded) return; // no-op — precondition failed or no valid replacement was available

        // Refresh the read-only Traits list at the top of the panel too, not just this picker — the
        // bunny's actual trait set just changed.
        PopulateList(traitListContainer, currentBunny.Traits, t => t.displayName);

        if (traitPickerRoot != null) traitPickerRoot.SetActive(false);
        RefreshAdaptableBlock();
    }

    // Green "+" for the stat Nature boosts, red "-" for the one it lowers, plain number otherwise --
    // including for a Stoic bunny, where NPCBunny.BoostedStat/LoweredStat are both null (see
    // NPCBunny.ApplyNatureEffects) so neither branch below ever matches. TMP's inline <color> tag needs
    // no extra Inspector wiring since hpLabel/attackLabel/etc. are already TextMeshProUGUI, which parses
    // rich text by default.
    private static string FormatStatWithNatureIndicator(NPCBunny bunny, int value, NatureStat stat)
    {
        if (bunny.BoostedStat == stat) return $"{value} <color=#2E7D32>+</color>"; // darker than TMP's named "green" -- readable against a white panel
        if (bunny.LoweredStat == stat) return $"{value} <color=red>-</color>";
        return value.ToString();
    }

    // Shared by Traits and Passives — both are just "a list of names." Joined into a single
    // ", "-separated entry rather than one instantiated prefab per item — the container's
    // HorizontalLayoutGroup uses a fixed spacing between children, which clipped longer names when
    // there were multiple separate entries. One entry sidesteps that entirely. Empty list = no entry,
    // which is the expected/normal state today (no trait or passive content has been authored for most
    // types yet) rather than an error case needing special handling.
    private void PopulateList<T>(Transform container, IReadOnlyList<T> items, System.Func<T, string> getLabel)
    {
        foreach (Transform child in container)
            Destroy(child.gameObject);

        if (items.Count == 0) return;

        List<string> labels = new List<string>();
        foreach (T item in items)
            labels.Add(getLabel(item));

        GameObject entry = Instantiate(listEntryPrefab, container);
        entry.GetComponentInChildren<TextMeshProUGUI>().text = string.Join(", ", labels);
    }

    private void OnApproveClicked()
    {
        if (currentBunny == null) return;
        AudioManager.EnsureInstance().PlayButtonClick();
        GateQueueManager.Instance.ApproveFrontBunny(currentBunny);
        Close(false); // click sound already fired above — don't also play the close cue
    }

    private void OnRejectClicked()
    {
        if (currentBunny == null) return;
        AudioManager.EnsureInstance().PlayButtonClick();
        GateQueueManager.Instance.RejectFrontBunny(currentBunny);
        Close(false); // click sound already fired above — don't also play the close cue
    }

    // Visible for an already-resident bunny that's actually eligible to depart (mirrors
    // NPCBunny.CanDepartForForaging's own gating) and not already out on a trip.
    private bool CanShowSendForagingButton()
    {
        if (currentBunny == null) return false;
        if (ForagingManager.Instance != null && ForagingManager.Instance.IsCurrentlyForaging(currentBunny)) return false;
        return currentBunny.CanDepartForForaging();
    }

    private void OnSendForagingClicked()
    {
        if (currentBunny == null) return;
        ForagingDispatchUI.Instance?.OpenForBunny(currentBunny);
        Close();
    }
}
