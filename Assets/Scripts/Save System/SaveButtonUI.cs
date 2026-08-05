using UnityEngine;
using UnityEngine.UI;

// Thin wiring shim — drop this on whatever GameObject holds your Save button (pause menu, settings
// panel, wherever) and assign the button in the Inspector. Mirrors how BuildMenuUI/other panels wire
// their own buttons (onClick.AddListener in Awake) rather than requiring an Editor-assigned callback.
public class SaveButtonUI : MonoBehaviour
{
    [SerializeField] private Button saveButton;

    private void Awake()
    {
        if (saveButton != null)
            saveButton.onClick.AddListener(HandleSaveClicked);
    }

    private void HandleSaveClicked()
    {
        AudioManager.EnsureInstance().PlayButtonClick();

        bool saved = SaveManager.Instance != null && SaveManager.Instance.SaveGame();
        NotificationManager.Instance?.Show(saved ? NotificationType.GameSaved : NotificationType.SaveFailedInvasionActive);
    }
}
