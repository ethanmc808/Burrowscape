using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BunnyStatsUI : MonoBehaviour
{
    public static BunnyStatsUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI bunnyNameLabel;
    [SerializeField] private Button closeButton;

    [Header("Need Bars (Image.Type = Filled)")]
    [SerializeField] private Image hungerBar;
    [SerializeField] private Image thirstBar;
    [SerializeField] private Image energyBar;
    [SerializeField] private Image moodBar;

    [Header("HP")]
    [SerializeField] private Image hpBar;
    [SerializeField] private TextMeshProUGUI hpLabel;
    // Consumes 1 potion from ForagingInventoryManager's stock and heals ForagingManager.PotionHealAmount
    // — same flat amount Foraging's own auto-use potion path applies, just player-triggered instead of
    // an encounter-loss auto-use.
    [SerializeField] private Button usePotionButton;
    [SerializeField] private TextMeshProUGUI potionButtonLabel;

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
        usePotionButton.onClick.AddListener(UsePotionOnCurrentBunny);
    }

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

    private void Update()
    {
        if (!panelRoot.activeSelf) return;

        // Bunny may have been destroyed (e.g. deleted/despawned) while the panel was open.
        if (currentBunny == null)
        {
            Close();
            return;
        }

        RefreshBars();
    }

    public void OpenForBunny(NPCBunny bunny)
    {
        currentBunny = bunny;
        bunnyNameLabel.text = bunny.name;
        panelRoot.SetActive(true);
        AudioManager.EnsureInstance().PlayUIOpen();
        RefreshBars();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        AudioManager.EnsureInstance().PlayUIClose();
        currentBunny = null;
    }

    // Polled every frame rather than event-driven — unlike Carrots' discrete-event pattern, these
    // values change continuously (decay/regen ticking every frame in NPCBunny.Update()).
    private void RefreshBars()
    {
        hungerBar.fillAmount = currentBunny.HungerValue / 100f;
        thirstBar.fillAmount = currentBunny.ThirstValue / 100f;
        energyBar.fillAmount = currentBunny.EnergyValue / 100f;
        moodBar.fillAmount = currentBunny.MoodValue / 100f;

        int maxHP = currentBunny.Stats.HP;
        hpBar.fillAmount = maxHP > 0 ? (float)currentBunny.HPValue / maxHP : 0f;
        hpLabel.text = $"{currentBunny.HPValue}/{maxHP}";

        RefreshPotionButton();
    }

    // Disabled while there's no potion stock, the bunny's already at full HP, or it's Fainted — see
    // NPCBunny.HealHP's own no-op guard for why a fainted bunny can't be healed mid-battle.
    private void RefreshPotionButton()
    {
        if (currentBunny == null) return;

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
    }
}
