using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One row per bunny assigned to a Laboratory (see AssignmentUI.RefreshLaboratoryExtras) — timerRoot/timer
// show while that bunny has an active brew, idleRoot/selectCraftButton show while they're standing at
// their spot with nothing picked yet. Exactly one of timerRoot/idleRoot is active at a time, toggled by
// AssignmentUI based on LaboratoryRoom.IsBrewing.
public class LaboratoryBrewRowUI : MonoBehaviour
{
    public TextMeshProUGUI bunnyNameText;

    [Header("Shown while brewing")]
    public GameObject timerRoot;
    public BrewTimerUI timer;

    [Header("Shown while idle-at-spot")]
    public GameObject idleRoot;
    public Button selectCraftButton;
}
