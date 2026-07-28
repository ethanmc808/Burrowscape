using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Centralized SFX/music playback — see SoundSystem_DesignDoc.md at the project root for the full plan
// this implements (global vs. proximity-based cues). Four playback shapes:
//   - PlaySFXAtPosition: world-anchored one-shot (e.g. NPCBunny's level-up cheer), built on Unity's own
//     AudioSource.PlayClipAtPoint — free overlapping playback per call, no pooling/throttle needed.
//   - PlaySFX2D: non-positional one-shot (UI clicks, gate, build/destroy/upgrade, new-type reveal —
//     everything the design doc calls "global"), backed by one dedicated AudioSource on this GameObject.
//   - Music playlist: a separate AudioSource, sequential or shuffled, fading out/in with a silent gap
//     between tracks (MusicPlaybackLoop) rather than cutting hard from one clip to the next.
//   - Proximity loops (IncrementProximityLoop/DecrementProximityLoop): ref-counted per caller-supplied
//     key (typically the room instance), each backed by its own always-looping AudioSource whose volume
//     is driven every frame by full 3D distance-from-camera, never by Unity's own 3D spatialBlend
//     attenuation (spatialBlend stays 0 on every proximity source — we do the falloff ourselves). The
//     camera is Perspective (see UpdateProximityVolumes), so "zoom" is TestCamera's own middle-mouse drag
//     moving the camera in X/Z, which real 3D distance already captures correctly on its own.
// Works whether manually placed in the scene (so masterVolume/musicVolume are Inspector-tweakable, same
// as CarrotManager/GoldManager) or left alone to self-create via EnsureInstance() — mirrors PowerManager's
// own self-creating pattern.
// One playlist entry — paired with its own volume knob because tracks pulled from different sources
// (e.g. a batch of OpenGameArt ambient tracks) rarely land at exactly the same loudness even after
// normalizing them externally (Audacity, etc.); this is the code-side fallback for whatever nudge is
// still needed per-track after that pass. Defaults to 1 (full musicVolume) via the field initializer —
// deliberately a class, not a struct, since Unity's Inspector "add element" on a List<T> only actually
// runs field initializers for reference types, so a struct here would silently default new entries to 0
// (silent) instead of 1.
[System.Serializable]
public class MusicTrack
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volumeScale = 1f;
}

// Which category slider a given sound is scaled by, on top of masterVolume — see AudioManager's
// "Master Volumes" header for the actual sliders. Misc covers everything not explicitly categorized
// (gate, room build/destroy/upgrade, new-type reveal, the level-up cheer) — these stay under
// masterVolume alone rather than getting their own dedicated slider (Ethan's call — they don't fit UI/
// Ambient/Bunny Noises cleanly yet, may get their own category once more sounds like them exist).
public enum AudioCategory
{
    Misc,
    UI,
    Ambient,
    BunnyNoise
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Master Volumes")]
    [Tooltip("True overall multiplier — applies on top of every category below (UI/Ambient/Bunny Noises/Misc) AND every proximity loop, but deliberately NOT Music (see musicVolume) — Music stays independent so a master-volume tweak never ducks the background track.")]
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float musicVolume = 1f;
    [Tooltip("Scales UI open/close/button-click one-shots.")]
    [SerializeField] [Range(0f, 1f)] private float uiVolume = 1f;
    [Tooltip("Scales proximity-based room ambience (Work Room ambient, etc.) — NOT bunny sounds, see bunnyNoiseVolume.")]
    [SerializeField] [Range(0f, 1f)] private float ambientVolume = 1f;
    [Tooltip("Scales proximity-based bunny sounds (Eating/Drinking/Sleeping).")]
    [SerializeField] [Range(0f, 1f)] private float bunnyNoiseVolume = 1f;

    [Header("Music")]
    [Tooltip("Played in order (or shuffled, see shuffleMusic) starting from scene load. A single-entry list still 'loops' via wraparound once the track ends. Each entry has its own volumeScale — a code-side fallback knob for matching loudness across tracks pulled from different sources.")]
    [SerializeField] private List<MusicTrack> musicPlaylist = new List<MusicTrack>();
    [SerializeField] private bool shuffleMusic = false;
    [Tooltip("Fade-out length at the end of a track and fade-in length at the start of the next one — avoids a hard cut between tracks.")]
    [SerializeField] private float musicFadeDuration = 2f;
    [Tooltip("Silent gap between one track fading out and the next fading in. Editable here if 2s ends up feeling too long/short.")]
    [SerializeField] private float musicGapDuration = 2f;

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
    [Tooltip("World-space radius within which a proximity source is fully audible. If the camera is orthographic, this is scaled by orthographicSize/referenceOrthographicSize below (zoomInfluence-blended) since orthographic zoom doesn't move the camera at all. If the camera is Perspective (Burrowscape's actual current setup — TestCamera's middle-mouse drag moves the camera in X AND Z, which IS the zoom), distance already responds to real camera movement on its own, so this radius is used as-is with no scaling.")]
    [SerializeField] private float baseProximityRadius = 6f;
    [Tooltip("Orthographic-camera only (see baseProximityRadius) — the camera's orthographicSize this radius was tuned at.")]
    [SerializeField] private float referenceOrthographicSize = 5f;
    [Tooltip("Orthographic-camera only — how much zoom changes the audible radius (see baseProximityRadius's Tooltip). Has no effect at all for a Perspective camera, since orthographicSize never changes on one.")]
    [SerializeField] [Range(0f, 1f)] private float zoomInfluence = 1f;
    [Tooltip("Outer fraction of the audible radius over which volume fades linearly to 0, instead of snapping off.")]
    [SerializeField] [Range(0.01f, 1f)] private float proximityFadeBandFraction = 0.2f;
    [Tooltip("Seconds for a proximity loop's volume to fully ramp between silent and its target level — smooths BOTH occupancy starting/stopping (e.g. a worker joining/leaving) and camera pan/zoom moving a source in or out of range, so neither ever snaps instantly.")]
    [SerializeField] private float proximityFadeDuration = 1.5f;

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
        public AudioCategory category;
        // refCount hit 0 — fading out toward silence (see proximityFadeDuration) rather than being
        // destroyed immediately, so the last worker leaving/room disabling doesn't cut the sound off
        // mid-note. Actually removed by UpdateProximityVolumes once the fade-out reaches ~0. A fresh
        // Increment on the same key before that finishes just clears this flag and fades back up.
        public bool pendingRemoval;
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
        musicSource.loop = false; // looping (of a single track OR the whole playlist) is handled by MusicPlaybackLoop instead

        StartCoroutine(MusicPlaybackLoop());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
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

    // Non-positional one-shot, uncategorized (gate, build/destroy/upgrade, new-type reveal — see
    // AudioCategory.Misc). Layers via PlayOneShot onto the single 2D source, so multiple calls in the
    // same frame still all play.
    public void PlaySFX2D(AudioClip clip, float volumeScale = 1f)
    {
        PlayCategorizedSFX2D(clip, AudioCategory.Misc, volumeScale);
    }

    private void PlayCategorizedSFX2D(AudioClip clip, AudioCategory category, float volumeScale = 1f)
    {
        if (clip == null || sfx2DSource == null) return;
        sfx2DSource.PlayOneShot(clip, masterVolume * GetCategoryVolume(category) * volumeScale);
    }

    private float GetCategoryVolume(AudioCategory category)
    {
        switch (category)
        {
            case AudioCategory.UI: return uiVolume;
            case AudioCategory.Ambient: return ambientVolume;
            case AudioCategory.BunnyNoise: return bunnyNoiseVolume;
            default: return 1f; // Misc — no dedicated slider, masterVolume alone applies
        }
    }

    // ---------- UI ----------

    public void PlayUIOpen(AudioClip clip = null)
    {
        PlayCategorizedSFX2D(clip != null ? clip : defaultMenuOpenClip, AudioCategory.UI);
    }

    public void PlayUIClose(AudioClip clip = null)
    {
        PlayCategorizedSFX2D(clip != null ? clip : defaultMenuCloseClip, AudioCategory.UI);
    }

    public void PlayButtonClick(AudioClip clip = null)
    {
        PlayCategorizedSFX2D(clip != null ? clip : defaultButtonClickClip, AudioCategory.UI);
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

    // Fade-out -> silent gap -> fade-in between tracks instead of a hard cut — an earlier version just
    // polled musicSource.isPlaying in Update() and started the next clip at full volume the instant the
    // old one ended, which was jarring, especially for the soft/eerie-ambient style this is tuned for.
    // Runs for the lifetime of the AudioManager — an empty/not-yet-assigned playlist just idles a frame
    // at a time rather than ending the coroutine, so clips dropped in later still get picked up.
    private IEnumerator MusicPlaybackLoop()
    {
        while (true)
        {
            if (musicPlaylist.Count == 0)
            {
                yield return null;
                continue;
            }

            musicTrackIndex = shuffleMusic
                ? Random.Range(0, musicPlaylist.Count)
                : (musicTrackIndex + 1) % musicPlaylist.Count;

            MusicTrack track = musicPlaylist[musicTrackIndex];
            if (track == null || track.clip == null)
            {
                yield return null; // unassigned playlist slot — try the next index on the following tick
                continue;
            }

            musicSource.clip = track.clip;
            musicSource.volume = 0f;
            musicSource.Play();

            // Per-track volumeScale folds in here, not just musicVolume — see MusicTrack's own comment
            // for why each entry needs its own loudness knob.
            float trackTargetVolume = musicVolume * Mathf.Clamp01(track.volumeScale);
            yield return FadeMusicVolume(0f, trackTargetVolume, musicFadeDuration);

            // Hold at full volume until it's time to start fading out before the clip's natural end —
            // clip.length rather than isPlaying, since isPlaying only flips false AFTER the clip already
            // finished, which would leave no room for a fade-out.
            float fadeOutStartTime = Mathf.Max(0f, track.clip.length - musicFadeDuration);
            while (musicSource.isPlaying && musicSource.time < fadeOutStartTime)
                yield return null;

            yield return FadeMusicVolume(musicSource.volume, 0f, musicFadeDuration);
            musicSource.Stop();

            yield return new WaitForSeconds(musicGapDuration);
        }
    }

    private IEnumerator FadeMusicVolume(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            musicSource.volume = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        musicSource.volume = to;
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
    // paired, it just never creates an AudioSource. `category` picks which Master Volume slider this
    // loop is scaled by (e.g. AudioCategory.Ambient for Work Room ambience, .BunnyNoise for Eating/
    // Drinking/Sleeping) — fixed for the lifetime of the entry, set on the first Increment for a key.
    public void IncrementProximityLoop(object key, AudioClip clip, Transform anchor, AudioCategory category, float volumeScale = 1f)
    {
        if (key == null) return;

        if (proximityLoops.TryGetValue(key, out ProximityLoopEntry existing))
        {
            existing.refCount++;
            existing.pendingRemoval = false; // cancel any in-progress fade-out — see ProximityLoopEntry
            return;
        }

        ProximityLoopEntry entry = new ProximityLoopEntry { refCount = 1, anchor = anchor, volumeScale = volumeScale, category = category };

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

        // Don't Destroy() here — that would cut the sound off instantly. UpdateProximityVolumes fades
        // this entry's volume to ~0 first (over proximityFadeDuration) and removes it once silent. If no
        // AudioSource was ever created (null clip), there's nothing to fade, so remove immediately.
        if (entry.source == null)
            proximityLoops.Remove(key);
        else
            entry.pendingRemoval = true;
    }

    private void UpdateProximityVolumes()
    {
        if (proximityLoops.Count == 0) return;

        if (proximityCamera == null)
            proximityCamera = Camera.main;

        bool hasCamera = proximityCamera != null;
        Vector3 cameraPos = default;
        float radius = 0f;
        float fadeBandStart = 0f;

        if (hasCamera)
        {
            cameraPos = proximityCamera.transform.position;

            // Burrowscape's actual camera (confirmed 2026-07 — TestCamera.cs's own scroll-wheel zoom code
            // assumed orthographic and is dead on this project's Perspective camera; the REAL "zoom" is
            // TestCamera's middle-mouse drag, which moves transform.position in X AND Z) is Perspective,
            // not orthographic — so orthographicSize never changes and zoomInfluence has nothing to scale.
            // Full 3D distance (below) already responds correctly to that real camera movement on its own,
            // no compensation needed. The orthographicSize-based radius scaling only kicks in if the
            // camera IS actually orthographic (future-proofing in case that ever changes), using the same
            // zoomInfluence blend as before.
            float radiusScale = 1f;
            if (proximityCamera.orthographic && referenceOrthographicSize > 0f)
            {
                float rawZoomScale = proximityCamera.orthographicSize / referenceOrthographicSize;
                radiusScale = Mathf.Lerp(1f, rawZoomScale, zoomInfluence);
            }
            radius = Mathf.Max(0.01f, baseProximityRadius * radiusScale);
            fadeBandStart = radius * (1f - proximityFadeBandFraction);
        }

        float maxVolumeStep = proximityFadeDuration > 0f ? Time.deltaTime / proximityFadeDuration : 1f;
        List<object> toRemove = null;

        foreach (KeyValuePair<object, ProximityLoopEntry> kvp in proximityLoops)
        {
            ProximityLoopEntry entry = kvp.Value;
            if (entry.source == null) continue; // no clip assigned — nothing to fade or play

            float targetVolume = 0f;
            if (!entry.pendingRemoval && hasCamera && entry.anchor != null)
            {
                // Full 3D distance, not just X/Y — see the camera-mode comment above for why Z matters here.
                float distance = Vector3.Distance(cameraPos, entry.anchor.position);

                float proximityFactor;
                if (distance >= radius) proximityFactor = 0f;
                else if (distance <= fadeBandStart) proximityFactor = 1f;
                else proximityFactor = 1f - Mathf.InverseLerp(fadeBandStart, radius, distance);

                targetVolume = masterVolume * GetCategoryVolume(entry.category) * entry.volumeScale * proximityFactor;
            }

            // Ramp toward the target over proximityFadeDuration rather than snapping — this is what makes
            // BOTH occupancy toggling and panning/zooming a source in or out of range feel like a gradual
            // swell instead of an instant cut, matching the fade Ethan asked for.
            entry.source.volume = Mathf.MoveTowards(entry.source.volume, targetVolume, maxVolumeStep);

            if (entry.pendingRemoval && entry.source.volume <= 0.0001f)
                (toRemove ??= new List<object>()).Add(kvp.Key);
        }

        if (toRemove == null) return;
        foreach (object key in toRemove)
        {
            if (proximityLoops.TryGetValue(key, out ProximityLoopEntry entry) && entry.source != null)
                Destroy(entry.source);
            proximityLoops.Remove(key);
        }
    }
}
