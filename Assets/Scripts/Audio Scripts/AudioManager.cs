using UnityEngine;

// Centralized SFX playback — the FIRST audio infrastructure in this project (previously no
// AudioSource/PlayOneShot/AudioClip usage existed anywhere). See WorkRoomXP_DesignDoc.md's "Sound
// effect" section. Two entry points, since world-anchored and UI/non-positional sounds need different
// playback shapes:
//   - PlaySFXAtPosition: world-anchored (e.g. NPCBunny's level-up cheer), built on Unity's own
//     AudioSource.PlayClipAtPoint — free overlapping playback per call, no pooling/throttle needed.
//   - PlaySFX2D: non-positional (future UI button clicks, toasts, menu open/close — Ethan's own
//     motivating example), backed by one dedicated AudioSource living on this GameObject with
//     spatialBlend = 0.
// Works whether manually placed in the scene (so masterVolume is Inspector-tweakable, same as
// CarrotManager/GoldManager) or left alone to self-create via EnsureInstance() — mirrors
// PowerManager's own self-creating pattern.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;

    private AudioSource sfx2DSource;

    public static AudioManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Check the scene for a manually-placed instance first — same reasoning as
        // PowerManager.EnsureInstance: a hand-tuned masterVolume should never be silently replaced or
        // destroyed by a creation-order race.
        AudioManager existing = FindAnyObjectByType<AudioManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("AudioManager (Global)");
        return go.AddComponent<AudioManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        sfx2DSource = gameObject.AddComponent<AudioSource>();
        sfx2DSource.spatialBlend = 0f; // flat 2D — no world position to anchor to
        sfx2DSource.playOnAwake = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // World-anchored one-shot (e.g. a specific bunny's level-up cheer). Each call is fully independent
    // — no shared AudioSource to steal, so any number of these can overlap freely.
    public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, masterVolume * volumeScale);
    }

    // Non-positional one-shot (UI clicks, toasts, menu open/close). Layers via PlayOneShot onto the
    // single 2D source, so multiple calls in the same frame still all play.
    public void PlaySFX2D(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfx2DSource == null) return;
        sfx2DSource.PlayOneShot(clip, masterVolume * volumeScale);
    }
}
