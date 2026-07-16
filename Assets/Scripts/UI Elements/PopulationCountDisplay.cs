using UnityEngine;
using TMPro;

public class PopulationCountDisplay : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private TextMeshProUGUI totalText; // e.g. "12"

    [Header("Optional breakdown — leave any of these empty if you don't want to show them")]
    [SerializeField] private TextMeshProUGUI inBaseText;
    [SerializeField] private TextMeshProUGUI questingText;
    [SerializeField] private TextMeshProUGUI foragingText;
    [SerializeField] private TextMeshProUGUI eggText;

    // Unity guarantees every object's Awake() in the scene has already run by the time ANY object's
    // Start() runs — unlike OnEnable(), which has no such guarantee relative to other objects' Awake().
    // PopulationManager.Instance is set in its own Awake(), so subscribing here (rather than in
    // OnEnable) avoids a race where this object activates before PopulationManager does and silently
    // never subscribes.
    private bool hasStarted = false;

    private void Start()
    {
        hasStarted = true;
        Subscribe();
    }

    private void OnEnable()
    {
        // Skip on the very first activation — Start() handles that case with the execution-order
        // guarantee described above. This only matters if the object is disabled and re-enabled later
        // (e.g. the panel gets toggled), at which point PopulationManager.Instance is already long since set.
        if (!hasStarted) return;
        Subscribe();
    }

    private void OnDisable()
    {
        if (PopulationManager.Instance != null)
            PopulationManager.Instance.OnPopulationChanged -= UpdateDisplay;
    }

    private void Subscribe()
    {
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.OnPopulationChanged += UpdateDisplay;
            UpdateDisplay();
        }
        else
        {
            Debug.LogWarning($"{name}: PopulationManager.Instance was still null during Start.");
        }
    }

    private void UpdateDisplay()
    {
        if (totalText != null)
            totalText.text = PopulationManager.Instance.TotalResidents.ToString();

        if (inBaseText != null)
            inBaseText.text = PopulationManager.Instance.GetCount(ResidentCategory.InBase).ToString();

        if (questingText != null)
            questingText.text = PopulationManager.Instance.GetCount(ResidentCategory.Questing).ToString();

        if (foragingText != null)
            foragingText.text = PopulationManager.Instance.GetCount(ResidentCategory.Foraging).ToString();

        if (eggText != null)
            eggText.text = PopulationManager.Instance.GetCount(ResidentCategory.Egg).ToString();
    }
}