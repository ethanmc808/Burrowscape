using UnityEngine;
using TMPro;

public class GoldCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI goldText;

    private void Start()
    {
        // Start(), not OnEnable() — GoldManager is a singleton that initializes in its own Awake(),
        // and there's no cross-object ordering guarantee between two different objects' Awake() calls.
        // This exact race already broke a UI display component in this project once.
        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnGoldChanged += UpdateDisplay;
            UpdateDisplay(GoldManager.Instance.CurrentGold);
        }
        else
        {
            Debug.LogWarning($"{name}: GoldManager.Instance was null during Start.");
        }
    }

    private void OnDestroy()
    {
        if (GoldManager.Instance != null)
            GoldManager.Instance.OnGoldChanged -= UpdateDisplay;
    }

    private void UpdateDisplay(int newAmount)
    {
        goldText.text = newAmount.ToString();
    }
}
