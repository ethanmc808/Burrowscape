using UnityEngine;
using TMPro;

public class WaterCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI waterText;

    private void OnEnable()
    {
        DebugLog.Log("WaterCountDisplay OnEnable running");

        if (WaterManager.Instance != null)
        {
            WaterManager.Instance.OnWaterCountChanged += UpdateDisplay;
            UpdateDisplay(WaterManager.Instance.CurrentWater);
        }
        else
        {
            Debug.LogWarning($"{name}: WaterManager.Instance was null during OnEnable.");
        }
    }

    private void OnDisable()
    {
        if (WaterManager.Instance != null)
            WaterManager.Instance.OnWaterCountChanged -= UpdateDisplay;
    }

    private void UpdateDisplay(int newCount)
    {
        DebugLog.Log($"UpdateDisplay called with newCount = {newCount}");
        waterText.text = newCount.ToString();
    }
}
