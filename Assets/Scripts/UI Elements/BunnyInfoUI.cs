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
    [SerializeField] private TextMeshProUGUI genderLabel;
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

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
        approveButton.onClick.AddListener(OnApproveClicked);
        rejectButton.onClick.AddListener(OnRejectClicked);
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

        RefreshBars();
    }

    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;

        bunnyNameLabel.text = bunny.BunnyName;
        genderLabel.text = bunny.Gender.ToString();
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

        panelRoot.SetActive(true);
        RefreshBars();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentBunny = null;
    }

    private void RefreshBars()
    {
        hungerBar.fillAmount = currentBunny.HungerValue / 100f;
        thirstBar.fillAmount = currentBunny.ThirstValue / 100f;
        energyBar.fillAmount = currentBunny.EnergyValue / 100f;
        moodBar.fillAmount = currentBunny.MoodValue / 100f;
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

    // Shared by Traits and Passives — both are just "a list of names," same clear-then-instantiate
    // pattern AssignmentUI already uses for its bunny-button lists (UI Elements/AssignmentUI.cs).
    // Empty list = no rows, which is the expected/normal state today (no trait or passive content has
    // been authored for most types yet) rather than an error case needing special handling.
    private void PopulateList<T>(Transform container, IReadOnlyList<T> items, System.Func<T, string> getLabel)
    {
        foreach (Transform child in container)
            Destroy(child.gameObject);

        foreach (T item in items)
        {
            GameObject entry = Instantiate(listEntryPrefab, container);
            entry.GetComponentInChildren<TextMeshProUGUI>().text = getLabel(item);
        }
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
}
