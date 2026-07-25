using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Same Subscribe/Start/OnEnable pattern as PopulationCountDisplay: subscribing in Start (not
// OnEnable) relies on Unity's guarantee that every object's Awake() has run before any object's
// Start() runs, so GameSpeedManager.Instance is guaranteed set by the time this subscribes.
public class GameSpeedButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label; // e.g. "2x"

    private bool hasStarted = false;

    private void Start()
    {
        hasStarted = true;
        Subscribe();

        if (button != null)
            button.onClick.AddListener(OnClicked);
    }

    private void OnEnable()
    {
        if (!hasStarted) return;
        Subscribe();
    }

    private void OnDisable()
    {
        if (GameSpeedManager.Instance != null)
            GameSpeedManager.Instance.OnSpeedChanged -= UpdateDisplay;
    }

    private void Subscribe()
    {
        if (GameSpeedManager.Instance != null)
        {
            GameSpeedManager.Instance.OnSpeedChanged += UpdateDisplay;
            UpdateDisplay();
        }
        else
        {
            Debug.LogWarning($"{name}: GameSpeedManager.Instance was still null during Start.");
        }
    }

    private void OnClicked()
    {
        GameSpeedManager.Instance?.CycleSpeed();
    }

    private void UpdateDisplay()
    {
        if (GameSpeedManager.Instance == null) return;

        if (label != null)
            label.text = $"{GameSpeedManager.Instance.CurrentSpeed:0}x";

        if (button != null)
            button.interactable = !GameSpeedManager.Instance.IsCombatLocked;
    }
}
