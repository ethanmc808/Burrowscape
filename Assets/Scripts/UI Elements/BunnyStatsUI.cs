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

    private NPCBunny currentBunny;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        closeButton.onClick.AddListener(Close);
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
        RefreshBars();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
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
    }
}
