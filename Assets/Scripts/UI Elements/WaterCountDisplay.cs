using UnityEngine;
using TMPro;

// Shows the normal WaterManager stockpile PLUS whatever's currently banked in the (separate) Water
// Rationing Pool — the combined total is "how much water you have," mirroring PowerCountDisplay's same
// treatment. Listens to both managers' change events since either one changing should refresh the total.
public class WaterCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI waterText;

    private void OnEnable()
    {
        if (WaterManager.Instance != null)
            WaterManager.Instance.OnWaterCountChanged += OnWaterChanged;
        else
            Debug.LogWarning($"{name}: WaterManager.Instance was null during OnEnable.");

        WaterRationingManager.OnAnyWaterRationingChanged += OnRationingChanged;

        UpdateDisplay();
    }

    private void OnDisable()
    {
        if (WaterManager.Instance != null)
            WaterManager.Instance.OnWaterCountChanged -= OnWaterChanged;

        WaterRationingManager.OnAnyWaterRationingChanged -= OnRationingChanged;
    }

    private void OnWaterChanged(int newCount) => UpdateDisplay();
    private void OnRationingChanged() => UpdateDisplay();

    private void UpdateDisplay()
    {
        int normalPool = WaterManager.Instance != null ? WaterManager.Instance.CurrentWater : 0;
        float rationingPool = WaterRationingManager.Instance != null ? WaterRationingManager.Instance.RationingPoolCurrent : 0f;
        waterText.text = Mathf.RoundToInt(normalPool + rationingPool).ToString();
    }
}
