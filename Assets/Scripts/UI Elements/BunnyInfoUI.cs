using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
// CUTOVER STATUS: not yet wired into the live click path. BunnyApprovalClickHandler/
// BunnyStatsClickHandler/BunnyClickPriority still point at the old BunnyApprovalUI/BunnyStatsUI panels
// until this script's Canvas layout is built and its fields wired up in the Inspector — swapping the
// click path over before then would NullReferenceException on every bunny click (Instance unset). Once
// the panel exists: replace BunnyApprovalClickHandler/BunnyStatsClickHandler with BunnyInfoClickHandler
// on every NPC Bunny Default prefab, repoint BunnyClickPriority.cs at BunnyInfoUI.Instance, then delete
// BunnyApprovalUI.cs/BunnyStatsUI.cs and their scene panels.
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

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
        approveButton.onClick.AddListener(OnApproveClicked);
        rejectButton.onClick.AddListener(OnRejectClicked);

        if (sendForagingButton != null)
            sendForagingButton.onClick.AddListener(OnSendForagingClicked);

        if (accessorySlotButton != null)
            accessorySlotButton.onClick.AddListener(ToggleAccessoryPicker);
        if (accessoryPickerRoot != null)
            accessoryPickerRoot.SetActive(false);
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
        RefreshAccessorySlot();
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

        hpLabel.text = bunny.Stats.HP.ToString(); // Nature never affects HP — no indicator possible here
        attackLabel.text = FormatStatWithNatureIndicator(bunny, bunny.Stats.Attack, NatureStat.Attack);
        defenseLabel.text = FormatStatWithNatureIndicator(bunny, bunny.Stats.Defense, NatureStat.Defense);
        speedLabel.text = FormatStatWithNatureIndicator(bunny, bunny.Stats.Speed, NatureStat.Speed);
        luckLabel.text = FormatStatWithNatureIndicator(bunny, bunny.Stats.Luck, NatureStat.Luck);

        PopulateList(traitListContainer, bunny.Traits, t => t.displayName);
        PopulateList(passiveListContainer, bunny.ActivePassives, p => p.displayName);

        approvalControlsRoot.SetActive(bunny.IsAwaitingApproval);
        if (sendForagingButton != null)
            sendForagingButton.gameObject.SetActive(CanShowSendForagingButton());

        panelRoot.SetActive(true);
        RefreshBars();
        if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
        RefreshAccessorySlot();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
        if (accessoryPickerRoot != null) accessoryPickerRoot.SetActive(false);
    }

    private void RefreshBars()
    {
        hungerBar.fillAmount = currentBunny.HungerValue / 100f;
        thirstBar.fillAmount = currentBunny.ThirstValue / 100f;
        energyBar.fillAmount = currentBunny.EnergyValue / 100f;
        moodBar.fillAmount = currentBunny.MoodValue / 100f;
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
        GateQueueManager.Instance.ApproveFrontBunny(currentBunny);
        Close();
    }

    private void OnRejectClicked()
    {
        if (currentBunny == null) return;
        GateQueueManager.Instance.RejectFrontBunny(currentBunny);
        Close();
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
