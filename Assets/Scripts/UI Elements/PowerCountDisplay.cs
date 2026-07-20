using UnityEngine;
using TMPro;

// Mirrors GoldCountDisplay's pattern. Power is tracked and arbitrated globally internally (see
// PowerManager), but the player only ever sees one aggregate number in the HUD, same visual treatment
// as Food/Water/Gold. Shows raw active production (all Coal Rooms combined) PLUS whatever's currently
// banked in the Rationing Pool — that combined total is "how much power you have," matching how Gold/
// Carrots/Water are each a single stockpile-style number.
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
        powerText.text = Mathf.RoundToInt(PowerManager.TotalPowerAvailable).ToString();
    }
}
