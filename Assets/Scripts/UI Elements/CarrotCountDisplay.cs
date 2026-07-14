using UnityEngine;
using TMPro;

public class CarrotCountDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI carrotText;

    private void OnEnable()
    {
        Debug.Log("CarrotCountDisplay OnEnable running");

        if (CarrotManager.Instance != null)
        {
            CarrotManager.Instance.OnCarrotCountChanged += UpdateDisplay;
            UpdateDisplay(CarrotManager.Instance.CurrentCarrots);
        }
        else
        {
            Debug.LogWarning($"{name}: CarrotManager.Instance was null during OnEnable.");
        }
    }

    private void OnDisable()
    {
        if (CarrotManager.Instance != null)
            CarrotManager.Instance.OnCarrotCountChanged -= UpdateDisplay;
    }

    private void UpdateDisplay(int newCount)
    {
        Debug.Log($"UpdateDisplay called with newCount = {newCount}");
        carrotText.text = newCount.ToString();
    }
}