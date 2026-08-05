using System.Collections;
using UnityEngine;

// Fades a full-screen black overlay out at scene start, masking whatever pop-in/snap-into-place happens
// while every manager's own Start() finishes restoring state (rooms, bunnies, lift doors resetting to
// their default closed pose, etc — see [[project_burrowscape_lift_system]]'s "cosmetic gap" note, the
// motivating case for this). Doesn't need to explicitly wait for SaveManager.LoadGame() to finish first:
// Unity runs every MonoBehaviour's Start() (SaveManager's load is fully synchronous within its own
// Start()) before the very first frame renders, so by the time this fade's first visible frame draws,
// every other manager has already finished restoring — whichever Start() happens to run first doesn't
// matter.
public class ScreenFader : MonoBehaviour
{
    [Tooltip("The full-screen black Image's own CanvasGroup — starts opaque and blocking, fades to fully transparent and non-blocking.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeInDuration = 1f;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        // Blocks input while black so an early click can't reach anything underneath mid-restore; not
        // "interactable" since this overlay has no interactive elements of its own to enable/disable.
        canvasGroup.blocksRaycasts = true;
    }

    private void Start()
    {
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        // Fully disable rather than just leaving it transparent — this Canvas would otherwise sit on top
        // of every other UI Canvas indefinitely, adding an idle raycast-target check to the UI's overdraw
        // for the rest of the session for zero visual benefit.
        gameObject.SetActive(false);
    }
}
