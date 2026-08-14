using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Physical egg placed in a Hatchery (see the Breeding System plan). A plain Component, not an NPCBunny —
// matches RoomSpot.TryClaim(Component)'s existing occupant-type flexibility, same reasoning EnemySpots
// already use to hold an EnemyInstance occupant instead of a bunny.
//
// type and litterMembers are both carried over verbatim from the mother's NPCBunny fields at lay time
// (NPCBunny.TryLayEgg) — everything about this litter was already fully resolved at conception
// (Bedroom.TriggerMatingSequence); nothing is rolled here or at hatch, only revealed (see decision #4 /
// point 4 in the plan — this is what makes hatching save-scum-proof).
public class Egg : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    // ---------- Animator (see the Egg Progression plan — hand-authored clips, not procedural code) ----------
    // Only two motion states: "egg_incubating" (loop) and "egg_closetohatching" (loop). Deliberately no
    // third "split open" state/sprite — the actual hatch moment is carried entirely by SpawnHatchVFX/
    // PlayHatchSFX below instead, not an animation. One egg sprite for its whole life (eggSprite on
    // BunnyTypeDefinition); the wobble clips are the only thing that changes visually pre-hatch.
    [SerializeField] private Animator animator;
    // One-shot TRIGGER, same shape/convention as NPCBunny's isAttackingParam etc. — fired once, exactly
    // when incubation crosses BreedingConfig.eggHatchingSpriteThreshold (75%). Animator Controller wiring:
    // Incubating (default state, loops "egg_incubating") -> CloseToHatching (condition: this trigger, Has
    // Exit Time off), looping "egg_closetohatching" for the remaining 25%. No outgoing transition needed
    // back to Incubating — progress only ever moves forward, and this egg is destroyed outright (see
    // HatchNow below) once incubation completes, not transitioned to some third Animator state.
    [SerializeField] private string closeToHatchingParam = "CloseToHatching";
    // Set once, right when CloseToHatching is entered, to a random 0-1 value — a single hand-authored
    // "egg_closetohatching" clip loops IDENTICALLY every cycle, so with several eggs in one Hatchery all
    // entering that stage around the same time (litters laid close together, or several eggs crossing the
    // 75% mark near-simultaneously since HatchSpeedMultiplier applies uniformly to every egg in the room),
    // they'd wobble in perfect lockstep — reads as robotic despite the clip's own hand-authored randomness.
    // Not consumed by any code here; wire it in the Animator Controller however suits the art — e.g. as
    // the Speed Parameter on the CloseToHatching state (remap 0-1 to something like 0.85-1.15x speed) so
    // instances drift out of phase with each other, or as a 1D Blend Tree parameter across 2-3
    // hand-authored wobble variants so different eggs get visually different wobbles. Purely a hook; the
    // actual randomness mechanism is your call in the Animator, not dictated here.
    [SerializeField] private string wobbleSeedParam = "WobbleSeed";

    public BunnyType Type { get; private set; }
    public List<LitterMemberData> LitterMembers { get; private set; } = new List<LitterMemberData>();
    public HatcheryRoom HatcheryRoom { get; private set; }
    public RoomSpot ClaimedSpot { get; private set; }

    private float incubationElapsed;
    private float incubationDuration;
    // Read by SaveManager.SaveEggs — mirrors NPCBunny's own read-only accessor pattern for its private
    // save-relevant fields.
    public float IncubationElapsed => incubationElapsed;
    public float IncubationDuration => incubationDuration;

    // One-way latch — incubationElapsed only ever increases in practice, but this costs nothing and
    // guarantees the trigger can never re-fire once it's happened.
    private bool hasEnteredCloseToHatching;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    // elapsedSoFar defaults to 0 for a freshly-laid egg; SaveManager.LoadEggs (Phase 5) passes the saved
    // incubationElapsed instead when reconstructing an egg that was already partway through incubating.
    public void Initialize(BunnyType type, List<LitterMemberData> litterMembers, HatcheryRoom hatcheryRoom, RoomSpot claimedSpot, float duration, float elapsedSoFar = 0f)
    {
        Type = type;
        LitterMembers = litterMembers;
        HatcheryRoom = hatcheryRoom;
        ClaimedSpot = claimedSpot;
        incubationDuration = duration;
        incubationElapsed = elapsedSoFar;

        // See BreedingConfig.eggPlacementYOffset's own comment — the sprite's center pivot means placing
        // this exactly at the spot's own position sinks its bottom half below the floor, so it's nudged up
        // by roughly half the sprite's world-space height instead.
        transform.position = claimedSpot.transform.position + new Vector3(0f, BreedingConfig.Instance.eggPlacementYOffset, 0f);
        ApplyTypeSprite();

        // A save reloaded partway through incubation should resume in the right wobble stage, not restart
        // at the calm "just laid" one — cheapest way to do that is just replaying the same progress-gated
        // check Update() runs every frame, once, right here. (A reload landing exactly at/past 100%
        // progress just hatches immediately below, same as Update() would next frame — no special-casing
        // needed since HatchNow has no animation of its own to wait on.)
        if (ProgressRatio() >= 1f)
        {
            HatchNow();
            return;
        }
        if (ProgressRatio() >= BreedingConfig.Instance.eggHatchingSpriteThreshold)
            EnterCloseToHatching();
    }

    private float ProgressRatio() => incubationDuration > 0f ? Mathf.Clamp01(incubationElapsed / incubationDuration) : 1f;

    private void Update()
    {
        // Eggs hatch on their own regardless of tending — HatchSpeedMultiplier is 1 (no bonus) whenever
        // no Fire/Light bunny is actively tending the room, it never gates progress, only speeds it up.
        // See HatcheryRoom.HatchSpeedMultiplier's own comment — read live every frame, not cached, so a
        // tender arriving/leaving mid-incubation takes effect immediately.
        float speedMultiplier = HatcheryRoom != null ? HatcheryRoom.HatchSpeedMultiplier : 1f;
        incubationElapsed += Time.deltaTime * speedMultiplier;

        if (!hasEnteredCloseToHatching && ProgressRatio() >= BreedingConfig.Instance.eggHatchingSpriteThreshold)
            EnterCloseToHatching();

        if (incubationElapsed >= incubationDuration)
            HatchNow();
    }

    // Fired once, the moment incubation crosses BreedingConfig.eggHatchingSpriteThreshold (75% by
    // default — see the Egg Progression plan). Purely a motion change — triggers the hand-authored
    // "egg_closetohatching" wobble state; no sprite swap accompanies it (see the plan's decision to skip
    // a dedicated cracked-egg sprite).
    private void EnterCloseToHatching()
    {
        hasEnteredCloseToHatching = true;

        if (animator != null)
        {
            // See wobbleSeedParam's own comment — set before the trigger so it's already in place by the
            // time the transition into CloseToHatching actually evaluates this frame.
            animator.SetFloat(wobbleSeedParam, Random.value);
            animator.SetTrigger(closeToHatchingParam);
        }
    }

    // Fired once incubationElapsed reaches incubationDuration — the whole hatch moment, immediate and
    // final (unlike EnterCloseToHatching, there's no clip to wait on here; see the Egg Progression plan's
    // decision to carry the hatch purely via VFX/SFX instead of a "split open" animation/sprite). Spawns
    // the VFX + SFX at this egg's position, hands off to HatcheryRoom.HatchEgg to reveal the litter, then
    // destroys this egg.
    private void HatchNow()
    {
        SpawnHatchVFX();
        PlayHatchSFX();
        HatcheryRoom.HatchEgg(this);
        Destroy(gameObject);
    }

    // See BreedingConfig.eggHatchVFXPrefab's own comment for why a flat lifetime cleans up the spawned
    // instance rather than reading the particle system's own timing. Graceful no-op if no VFX has been
    // authored yet.
    private void SpawnHatchVFX()
    {
        BreedingConfig cfg = BreedingConfig.Instance;
        if (cfg.eggHatchVFXPrefab == null) return;

        GameObject vfx = Instantiate(cfg.eggHatchVFXPrefab, transform.position, Quaternion.identity);
        // Same "playOnAwake off, explicit Play() on spawn" convention as every other one-shot VFX in the
        // project (NPCBunny.growthFireworksVFX, Bedroom.heartsVFX, AttackInstance.impactVFX) — the prefab's
        // ParticleSystem doesn't start itself.
        ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
        if (ps != null) ps.Play(true);

        Destroy(vfx, Mathf.Max(0.01f, cfg.eggHatchVFXLifetimeSeconds));
    }

    // World-anchored one-shot via AudioManager.PlaySFXAtPosition — same pattern as NPCBunny's levelUpClip
    // (PlayLevelUpFeedback). Graceful no-op (PlaySFXAtPosition already null-checks the clip) if no hatch
    // sound has been authored yet.
    private void PlayHatchSFX()
    {
        AudioManager.EnsureInstance().PlaySFXAtPosition(BreedingConfig.Instance.eggHatchClip, transform.position);
    }

    // Resolves this egg's per-type sprite (BunnyTypeDefinition.eggSprite) via WildBunnySpawner.BunnyTypes
    // — kept independent from HatcheryRoom.HatchEgg's own identical-shaped lookup since Egg needs it
    // immediately at lay time, well before hatch. Leaves whatever sprite is already on the prefab's
    // SpriteRenderer untouched if this type has no eggSprite authored yet (not yet authored isn't an
    // error state, same philosophy as BunnyTypeDefinition.icon). This is the ONLY sprite Egg ever shows —
    // see the class header comment for why there's no second/third stage sprite.
    private void ApplyTypeSprite()
    {
        if (spriteRenderer == null) return;

        WildBunnySpawner spawner = FindAnyObjectByType<WildBunnySpawner>();
        BunnyTypeDefinition typeDef = spawner != null
            ? spawner.BunnyTypes.FirstOrDefault(t => t != null && t.type == Type)
            : null;

        if (typeDef != null && typeDef.eggSprite != null)
            spriteRenderer.sprite = typeDef.eggSprite;
    }
}
