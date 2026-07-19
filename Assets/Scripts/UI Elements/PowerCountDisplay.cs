using UnityEngine;
using TMPro;

// Mirrors GoldCountDisplay's pattern. Power is tracked and arbitrated per floor internally (see
// PowerManager), but the player only ever sees one aggregate number in the HUD, same visual treatment
// as Food/Water/Gold — this is purely a display simplification.
public class PowerCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI powerText;

    private void OnEnable()
    {
        PowerManager.OnAnyPowerChanged += UpdateDisplay;
        UpdateDisplay();
    }

    private void OnDisable()
    {
        PowerManager.OnAnyPowerChanged -= UpdateDisplay;
    }

    private void UpdateDisplay()
    {
        powerText.text = Mathf.RoundToInt(PowerManager.TotalActiveProduction).ToString();
    }
}
