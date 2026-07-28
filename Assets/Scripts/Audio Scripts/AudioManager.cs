using UnityEngine;
using System.Collections.Generic;

// Centralized SFX/music playback — see SoundSystem_DesignDoc.md at the project root for the full plan
// this implements (global vs. proximity-based cues). Four playback shapes:
//   - PlaySFXAtPosition: world-anchored one-shot (e.g. NPCBunny's level-up cheer), built on Unity's own
//     AudioSource.PlayClipAtPoint — free overlapping playback per call, no pooling/throttle needed.
//   - PlaySFX2D: non-positional one-shot (UI clicks, gate, build/destroy/upgrade, new-type reveal —
//     everything the design doc calls "global"), backed by one dedicated AudioSource on this GameObject.
//   - Music playlist: a separate looping-by-advance AudioSource, sequential or shuffled.
//   - Proximity loops (IncrementProximityLoop/DecrementProximityLoop): ref-counted per caller-supplied
//     key (typically the room instance), each backed by its own always-looping AudioSource whose volume
//     is driven every frame by distance-from-camera + zoom, never by Unity's own 3D spatialBlend
//     attenuation (spatialBlend stays 0 on every proximity source — we do the falloff ourselves so it can
//     scale with the orthographic camera's zoom, which Unity's built-in attenuation knows nothing about).
// Works whether manually placed in the scene (so masterVolume/musicVolume are Inspector-tweakable, same
// as CarrotManager/GoldManager) or left alone to self-create via EnsureInstance() — mirrors PowerManager's
// own self-creating pattern.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Master Volumes")]
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 1f;

    [Header("Music")]
    [Tooltip("Played in order (or shuffled, see shuffleMusic) starting from scene load. A single-entry list still 'loops' via wraparound once the track ends.")]
    [SerializeField] private List<AudioClip> musicPlaylist = new List<AudioClip>();
    [SerializeField] private bool shuffleMusic = false;

    [Header("UI")]
    [Tooltip("Fallback used by PlayUIOpen/PlayUIClose/PlayButtonClick when a caller doesn't supply its own clip.")]
    [SerializeField] private AudioClip defaultMenuOpenClip;
    [SerializeField] private AudioClip defaultMenuCloseClip;
    [SerializeField] private AudioClip defaultButtonClickClip;

    [Header("Gate")]
    [Tooltip("Separate open/close clips — the gate can stay open for a while with traffic passing both ways, so these are independent events, not a reversible single cue.")]
    [SerializeField] private AudioClip gateOpenClip;
    [SerializeField] private AudioClip gateCloseClip;

    [Header("Room Build / Destroy / Upgrade")]
    [SerializeField] private AudioClip roomBuildClip;
    [SerializeField] private AudioClip roomDestroyClip;
    [Tooltip("Played instead of (not alongside) roomBuildClip/roomDestroyClip for the merge/upgrade swap path (RoomTransitionService) — a distinct third sound, not a build+destroy combo.")]
    [SerializeField] private AudioClip roomUpgradeClip;

    [Header("New Bunny Type Reveal")]
    [SerializeField] private AudioClip newBunnyTypeClip;

    [Header("Proximity Audio")]
    [Tooltip("World-space radius (before zoom scaling) within which a proximity source is fully audible.")]
    [SerializeField] private float baseProximityRadius = 6f;
    [Tooltip("The camera's orthographicSize this radius was tuned at. Actual audible radius = baseProximityRadius * (camera.orthographicSize / this).")]
    [SerializeField] private float referenceOrthographicSize = 5f;
    [Tooltip("Outer fraction of the audible radius over which volume fades linearly to 0, instead of snapping off.")]
    [SerializeField] [Range(0.01f, 1f)] private float proximityFadeBandFraction = 0.2f;

    private AudioSource sfx2DSource;
    private AudioSource musicSource;
    private int musicTrackIndex = -1;

    private Camera proximityCamera;

    private class ProximityLoopEntry
    {
        public int refCount;
        public AudioSource source; // null if the caller passed no clip — key still tracked so refcounts stay paired
        public Transform anchor;
        public float volumeScale;
    }

    private readonly Dictionary<object, ProximityLoopEntry> proximityLoops = new Dictionary<object, ProximityLoopEntry>();

    public static AudioManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Check the scene for a manually-placed instance first — same reasoning as
        // PowerManager.EnsureInstance: hand-tuned volumes/clips should never be silently replaced or
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

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.spatialBlend = 0f;
        musicSource.playOnAwake = false;
        musicSource.loop = false; // looping (of a single track OR the whole playlist) is handled by AdvanceMusicTrack instead

        PlayNextMusicTrack();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (musicSource != null && !musicSource.isPlaying && musicPlaylist.Count > 0)
            PlayNextMusicTrack();

        UpdateProximityVolumes();
    }

    // ---------- ONE-SHOT SFX ----------

    // World-anchored one-shot (e.g. a specific bunny's level-up cheer). Each call is fully independent
    // — no shared AudioSource to steal, so any number of these can overlap freely.
    public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, masterVolume * volumeScale);
    }

    // Non-positional one-shot (UI clicks, gate, build/destroy/upgrade, new-type reveal). Layers via
    // PlayOneShot onto the single 2D source, so multiple calls in the same frame still all play.
    public void PlaySFX2D(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfx2DSource == null) return;
        sfx2DSource.PlayOneShot(clip, masterVolume * volumeScale);
    }

    // ---------- UI ----------

    public void PlayUIOpen(AudioClip clip = null)
    {
        PlaySFX2D(clip != null ? clip : defaultMenuOpenClip);
    }

    public void PlayUIClose(AudioClip clip = null)
    {
        PlaySFX2D(clip != null ? clip : defaultMenuCloseClip);
    }

    public void PlayButtonClick(AudioClip clip = null)
    {
        PlaySFX2D(clip != null ? clip : defaultButtonClickClip);
    }

    // ---------- GATE ----------

    public void PlayGateOpen() => PlaySFX2D(gateOpenClip);
    public void PlayGateClose() => PlaySFX2D(gateCloseClip);

    // ---------- ROOM BUILD / DESTROY / UPGRADE ----------

    public void PlayRoomBuilt() => PlaySFX2D(roomBuildClip);
    public void PlayRoomDestroyed() => PlaySFX2D(roomDestroyClip);
    public void PlayRoomUpgraded() => PlaySFX2D(roomUpgradeClip);

    // ---------- NEW BUNNY TYPE ----------

    public void PlayNewBunnyTypeRevealed() => PlaySFX2D(newBunnyTypeClip);

    // ---------- MUSIC ----------

    private void PlayNextMusicTrack()
    {
        if (musicSource == null || musicPlaylist.Count == 0) return;

        musicTrackIndex = shuffleMusic
            ? Random.Range(0, musicPlaylist.Count)
            : (musicTrackIndex + 1) % musicPlaylist.Count;

        AudioClip track = musicPlaylist[musicTrackIndex];
        if (track == null) return; // skip an unassigned slot on the next Update tick rather than erroring

        musicSource.clip = track;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    // ---------- PROXIMITY LOOPS ----------

    // Registers one more listener for a looping ambient sound tied to `key`. `key` is deliberately
    // `object`, not a room reference directly — a WaterRoom, for instance, is BOTH a work room (Power/
    // Water production ambient) AND the drinking-spot room (drinking ambient), two unrelated sounds on
    // the same instance. Callers must key on something that disambiguates the category, not just the
    // room (e.g. a (room, "Work") / (room, "Drinking") value-tuple — ValueTuple has structural equality
    // even when boxed to object, so this works as a Dictionary key without any extra wrapper type).
    // Multiple Increment calls for the same key share a single AudioSource — the ref count just tracks
    // how many bunnies/callers currently want it playing, so "last one out" is what actually stops it.
    // A null clip (Inspector not yet filled in) still tracks the ref count so a later Decrement stays
    // paired, it just never creates an AudioSource.
    public void IncrementProximityLoop(object key, AudioClip clip, Transform anchor, float volumeScale = 1f)
    {
        if (key == null) return;

        if (proximityLoops.TryGetValue(key, out ProximityLoopEntry existing))
        {
            existing.refCount++;
            return;
        }

        ProximityLoopEntry entry = new ProximityLoopEntry { refCount = 1, anchor = anchor, volumeScale = volumeScale };

        if (clip != null && anchor != null)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f; // falloff is driven manually by UpdateProximityVolumes, not Unity's own attenuation
            source.playOnAwake = false;
            source.volume = 0f; // UpdateProximityVolumes sets the real volume on the next tick
            source.Play();
            entry.source = source;
        }

        proximityLoops[key] = entry;
    }

    public void DecrementProximityLoop(object key)
    {
        if (key == null) return;
        if (!proximityLoops.TryGetValue(key, out ProximityLoopEntry entry)) return;

        entry.refCount--;
        if (entry.refCount > 0) return;

        if (entry.source != null) Destroy(entry.source);
        proximityLoops.Remove(key);
    }

    private void UpdateProximityVolumes()
    {
        if (proximityLoops.Count == 0) return;

        if (proximityCamera == null)
            proximityCamera = Camera.main;
        if (proximityCamera == null) return;

        // Orthographic camera looking down Z — X and Y are both meaningful for "what's visibly close"
        // (floors are stacked along Y, rooms spread along X), Z is just view depth and never changes
        // what's on screen, so it's deliberately excluded from the distance check.
        Vector2 cameraPos = proximityCamera.transform.position;
        float zoomScale = referenceOrthographicSize > 0f ? proximityCamera.orthographicSize / referenceOrthographicSize : 1f;
        float radius = Mathf.Max(0.01f, baseProximityRadius * zoomScale);
        float fadeBandStart = radius * (1f - proximityFadeBandFraction);

        foreach (ProximityLoopEntry entry in proximityLoops.Values)
        {
            if (entry.source == null || entry.anchor == null) continue;

            Vector2 anchorPos = entry.anchor.position;
            float distance = Vector2.Distance(cameraPos, anchorPos);

            float proximityFactor;
            if (distance >= radius) proximityFactor = 0f;
            else if (distance <= fadeBandStart) proximityFactor = 1f;
            else proximityFactor = 1f - Mathf.InverseLerp(fadeBandStart, radius, distance);

            entry.source.volume = masterVolume * entry.volumeScale * proximityFactor;
        }
    }
}
