using System.Collections.Generic;
using UnityEngine;

// Runtime behavior for the "AttackInstance" prefab concept from Combat_DesignDoc.md's "Attack visuals"
// section — the moving (or stationary, for melee) carrier that a Collider + particle-effect VFX would be
// authored onto in-Editor. This script only handles the LOGIC side (homing, arrival, fizzle, calling
// CombatResolver); the actual Collider/ParticleSystem hierarchy on the prefab is still art/Editor work
// Ethan needs to do, same "null until content exists" boundary as everywhere else in this project.
//
// Deliberately does NOT use physics collision (OnTriggerEnter) to detect arrival — see the design doc's
// reasoning (fast small colliders can tunnel between physics steps, and this project has hit collision/
// click fragility issues before). Arrival is a plain distance check against the target's live position.
//
// NOT wired to anything yet — nothing in the codebase currently calls Launch(). Whatever attack-trigger
// logic eventually decides "this bunny/enemy attacks now" (part of the not-yet-built combat AI/state
// machine integration) should Instantiate this prefab and call Launch() once.
public class AttackInstance : MonoBehaviour
{
    [SerializeField] private float travelSpeed = 10f; // units/second, irrelevant when stationary
    [SerializeField] private float arrivalThreshold = 0.1f;

    // Melee-only fine-tuning — added on top of target.VisualCenter once the instance snaps there (see
    // isStationary handling in Launch), since a melee swipe/hit VFX often reads better nudged slightly off
    // dead-center rather than exactly on the target's computed midpoint. Irrelevant for ranged attacks
    // (their carrier homes toward VisualCenter directly, unaffected by this). Authored assuming the
    // attacker faces right — X mirrors automatically for a left-facing attacker, same as attackOriginOffset.
    [SerializeField] private Vector3 meleeImpactOffset;

    // "Charge-up" look (e.g. Giga Drain) — the carrier holds at its spawn position for this many seconds
    // before it starts actually traveling, instead of homing the instant it's launched. Purely a delay on
    // the MoveTowards step below; irrelevant when stationary. Defaults to 0 (no behavior change) for every
    // existing AttackInstance prefab.
    [SerializeField] private float travelDelaySeconds;
    private float elapsedSinceLaunch;

    // Lobbed-projectile look (e.g. Sludge Hurl) instead of a flat line to the target. Purely a visual
    // offset layered on top of the normal MoveTowards path — basePosition (below) tracks the actual
    // ground-path progress so the arc height doesn't get baked into next frame's distance math.
    [SerializeField] private bool useArcMotion;
    [SerializeField] private float arcHeight = 2f;

    // Trail particle systems (e.g. Sludge_Trail) sit at the same local (0,0,0) as the core sprite by
    // default, so their spawn point rides in perfect lockstep with it — world-space sim still spreads
    // already-spawned particles out fine, but every newly spawned particle is born exactly on top of the
    // core, which reads as "overlapping" rather than "trailing". trailFollower is placed on the core's own
    // recorded path (see positionHistory below), trailLagDistance behind its current position — this
    // deliberately does NOT derive a direction from frame-to-frame deltas (an earlier attempt did, and
    // under arc motion that's a numeric derivative through the parabola's changing slope: fine most
    // frames, but noisy right around the apex and on any uneven-deltaTime frame, which is exactly why that
    // version snapped unpredictably instead of trailing smoothly). Reusing exact past positions instead of
    // estimating a direction can't produce that kind of discontinuity.
    [SerializeField] private Transform trailFollower;
    [SerializeField] private float trailLagDistance = 0.4f;

    // Recorded world positions of transform.position (i.e. including arc height) since Launch(), oldest
    // first. Trimmed once it holds comfortably more than trailLagDistance worth of path so it can't grow
    // unbounded on a long-flight attack.
    private readonly List<Vector3> positionHistory = new List<Vector3>();

    private Vector3 basePosition;
    private float totalTravelDistance;

    // Giga Drain-style moves travel backwards from the normal attacker->target flow (visually "pulled"
    // from the target back to the attacker, e.g. a drain/heal effect). When true, this instance spawns at
    // the target's position and homes toward the attacker instead. Purely a carrier-motion flag — Resolve()
    // still always resolves attacker/target in the same roles for CombatResolver.
    [SerializeField] private bool reverseDirection;

    // Optional one-shot impact VFX (e.g. a splat/burst particle system) — played on Resolve() before the
    // object despawns. Left unassigned, behavior is identical to before this field existed (immediate
    // destroy). Set impactLingerSeconds to roughly the burst's Start Lifetime so it finishes rendering.
    [SerializeField] private ParticleSystem impactVFX;
    [SerializeField] private float impactLingerSeconds = 1f;

    // Most travel VFX (e.g. Giga Drain's looping orb stream) should keep fading naturally on arrival — see
    // the StopEmitting comment in Resolve(). Some carriers (e.g. Fire Ball's core sprite) instead need to
    // vanish the instant they hit, rather than lingering while their last few particles die out. Listing a
    // system here opts it into StopEmittingAndClear instead of the default StopEmitting; everything else
    // (and every other existing AttackInstance prefab, since this defaults to empty) is unaffected.
    [SerializeField] private ParticleSystem[] clearInstantlyOnResolve;

    // One-shot SFX, following the same "left unassigned = no behavior change" convention as impactVFX
    // above. Both are fire-and-forget via AudioManager.PlaySFXAtPosition (matches the existing
    // NPCBunny.PlaySFXAtPosition precedent) rather than a moving AudioSource — launchSFX plays once at the
    // launch position and does not follow the carrier as it travels.
    [SerializeField] private AudioClip launchSFX;
    [SerializeField] private AudioClip impactSFX;

    private ICombatant attacker;
    private ICombatant target;
    private bool isStationary;
    private bool resolved;

    // Read-only exposure for VFX components (e.g. LineAttackRenderer) that need to track both
    // endpoints' live positions independently of this instance's own transform.
    public Transform AttackerTransform => attacker?.CombatTransform;
    public Transform TargetTransform => target?.CombatTransform;

    // Live endpoints for anything drawing a line/bolt between the two combatants (LightningBoltFlash,
    // LineAttackRenderer) instead of floor-to-floor via CombatTransform. Deliberately asymmetric: the
    // attacker end uses AttackOrigin (a hand-tuned per-type spawn point, e.g. this bunny's own mouth/ear
    // height — not every type wants the bolt literally leaving from its body center), while the target
    // end uses VisualCenter (auto-computed from live sprite bounds, see ICombatant.VisualCenter) so the
    // bolt always lands center-of-body on whatever it hits, with no per-enemy tuning needed.
    public Vector3 AttackerOrigin => attacker != null ? attacker.AttackOrigin : transform.position;
    public Vector3 TargetOrigin => target != null ? target.VisualCenter : transform.position;

    // Fires once Launch() has actually assigned attacker/target — child VFX components (e.g.
    // LightningBoltFlash) that need AttackerTransform/TargetTransform should hook their positioning logic
    // here instead of Awake/OnEnable, since those run at Instantiate() time, before Launch() is called.
    public event System.Action OnLaunched;

    // isStationary should be true for melee attacks (per AttackSource.isMelee on the attacker) — the
    // instance snaps straight to the target's position and resolves the same frame, rather than homing
    // across distance like a ranged projectile.
    public void Launch(ICombatant attacker, ICombatant target, bool isStationary)
    {
        this.attacker = attacker;
        this.target = target;
        this.isStationary = isStationary;

        if (isStationary)
        {
            // meleeImpactOffset is authored assuming the attacker faces right, same convention as
            // NPCBunny/EnemyInstance's own attackOriginOffset — X mirrors whenever it's actually facing
            // the other way, so one authored value reads correctly from either flank side. Anchored to
            // CombatTransform.position (the target's stable ground pivot), NOT VisualCenter — VisualCenter
            // is a live bounds encapsulation that rides along with whatever the target's own Idle/Attacking
            // animation is doing to its body sprite (e.g. Enemy_Slime_Idle's bone_root squash-breathing,
            // Enemy_Slime_Attacking's bone_root/Body/Eyes lunge), so two swipes fired moments apart can land
            // at genuinely different heights just from sampling a different animation phase — confirmed via
            // [SWIPEDEBUG] logging showing VisualCenter.y drift by ~0.27 with target.IsFacingRight and
            // target.CombatTransform.position both identical between samples. Same reasoning as
            // CombatEngagement.ComputeFlankPosition's own VisualCenter -> CombatTransform.position fix.
            Vector3 offset = meleeImpactOffset;
            if (!attacker.IsFacingRight) offset.x = -offset.x;
            transform.position = target.CombatTransform.position + offset;
        }
        else if (reverseDirection)
            transform.position = target.VisualCenter;

        // Mirrors the whole instance (and everything nested under it, e.g. a melee swipe sprite) to match
        // the attacker's own facing. Sign confirmed empirically in Play mode (opposite of the naive guess)
        // once NPCBunny/EnemyInstance's own facingRight-vs-flank-side sign bug was fixed — the Swipe art's
        // default (unflipped) orientation reads correctly for a LEFT-facing attacker, not a right-facing
        // one. Ranged attacks ignore this; their direction already reads from travel motion.
        if (isStationary)
        {
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (attacker.IsFacingRight ? -1f : 1f);
            transform.localScale = scale;
        }

        basePosition = transform.position;
        positionHistory.Clear();
        positionHistory.Add(transform.position);
        Vector3 homingPos = reverseDirection ? attacker.AttackOrigin : target.VisualCenter;
        totalTravelDistance = Vector3.Distance(basePosition, homingPos);
        elapsedSinceLaunch = 0f;

        if (launchSFX != null)
            AudioManager.EnsureInstance().PlaySFXAtPosition(launchSFX, transform.position);

        OnLaunched?.Invoke();
    }

    private void Update()
    {
        if (resolved) return;

        // Fizzle: target's GameObject is gone, it's already been defeated by something else, or (bunny
        // targets only) it's already been recalled out of combat entirely — closes a real race window
        // where a bunny's killing blow on the last enemy clears the invasion and walks it back to its job
        // (StopDefendingAndReturn, called from InvasionManager's recall loop) before this already-in-
        // flight attack, fired moments earlier, actually arrives. Without this check the attack would still
        // land and faint a bunny with no active invasion left to ever revive it — StopDefendingAndReturn's
        // battle-end recall is the ONLY thing that ever un-faints anyone. Checked via CombatGameObject (a
        // real UnityEngine.Object) rather than `target == null` directly — a destroyed MonoBehaviour
        // referenced through an interface doesn't trip Unity's overridden null check the way a direct
        // Component/GameObject reference does.
        if (target == null || target.CombatGameObject == null || !target.IsAlive
            || (target is NPCBunny bunny && !bunny.IsDefending))
        {
            Fizzle();
            return;
        }

        if (isStationary)
        {
            Resolve();
            return;
        }

        // Holds at the spawn position until travelDelaySeconds has elapsed — the "charge-up" look, e.g.
        // Giga Drain's orb pulling from the target before it actually starts flying toward the attacker.
        // Still records into positionHistory so a trailFollower just sits in place with it instead of
        // being left behind once real travel starts.
        elapsedSinceLaunch += Time.deltaTime;
        if (elapsedSinceLaunch < travelDelaySeconds)
        {
            positionHistory.Add(transform.position);
            TrimPositionHistory();
            if (trailFollower != null)
                trailFollower.position = GetLaggedPosition(trailLagDistance);
            return;
        }

        // Reversed carriers home toward the attacker instead of the target (see reverseDirection above) —
        // AttackOrigin (this bunny's own tunable point, e.g. hands/mouth), not VisualCenter, so where the
        // orb is "caught" is as tunable as where a normal attack's projectile spawns from.
        Vector3 targetPos = reverseDirection ? attacker.AttackOrigin : target.VisualCenter;

        basePosition = Vector3.MoveTowards(basePosition, targetPos, travelSpeed * Time.deltaTime);

        if (useArcMotion && totalTravelDistance > 0f)
        {
            float progress = 1f - Mathf.Clamp01(Vector3.Distance(basePosition, targetPos) / totalTravelDistance);
            transform.position = basePosition + Vector3.up * (arcHeight * Mathf.Sin(progress * Mathf.PI));
        }
        else
        {
            transform.position = basePosition;
        }

        positionHistory.Add(transform.position);
        TrimPositionHistory();

        if (trailFollower != null)
            trailFollower.position = GetLaggedPosition(trailLagDistance);

        if (Vector3.Distance(basePosition, targetPos) <= arrivalThreshold)
            Resolve();
    }

    // Walks backward through positionHistory from the newest sample, accumulating segment distance until
    // it's covered lagDistance, then interpolates within that segment. This is exact reuse of the core's
    // own already-rendered path (arc included) rather than an estimate, so it can't introduce the kind of
    // discontinuity a frame-to-frame derivative can. Clamps to the oldest recorded sample (i.e. the launch
    // position) if the carrier hasn't traveled lagDistance yet.
    private Vector3 GetLaggedPosition(float lagDistance)
    {
        if (positionHistory.Count == 0)
            return transform.position;

        float remaining = lagDistance;
        for (int i = positionHistory.Count - 1; i > 0; i--)
        {
            Vector3 newer = positionHistory[i];
            Vector3 older = positionHistory[i - 1];
            float segmentLength = Vector3.Distance(newer, older);

            if (segmentLength >= remaining)
            {
                float t = segmentLength > 0f ? remaining / segmentLength : 0f;
                return Vector3.Lerp(newer, older, t);
            }

            remaining -= segmentLength;
        }

        return positionHistory[0];
    }

    // Keeps positionHistory from growing unbounded on a long-flight attack — trims from the oldest end
    // once the recorded path comfortably exceeds what GetLaggedPosition could ever need to look back.
    private void TrimPositionHistory()
    {
        float keepDistance = trailLagDistance * 2f;
        float accumulated = 0f;
        int cutoff = 0;

        for (int i = positionHistory.Count - 1; i > 0; i--)
        {
            accumulated += Vector3.Distance(positionHistory[i], positionHistory[i - 1]);
            if (accumulated > keepDistance)
            {
                cutoff = i - 1;
                break;
            }
        }

        if (cutoff > 0)
            positionHistory.RemoveRange(0, cutoff);
    }

    private void Resolve()
    {
        resolved = true;
        CombatHitResult result = CombatResolver.ResolveHit(attacker, target);
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        AudioManager audio = AudioManager.EnsureInstance();

        if (!result.Hit)
        {
            SpawnComboEffect(cfg.missTextSprite);
        }
        else
        {
            // The flash represents the ATTACK's type (attacker.Type), not the target's own — a Fire
            // attack flashes orange regardless of what type got hit.
            target.PlayHitFlash(TypeHitFlashPalette.GetColor(attacker.Type));

            if (result.Crit)
            {
                SpawnComboEffect(cfg.critTextSprite);
                if (cfg.critSFX != null) audio.PlaySFXAtPosition(cfg.critSFX, transform.position);
            }

            // Not mutually exclusive with Crit above — a crit that's also super effective plays both
            // sounds layered, per Ethan's ask (no priority/suppression between the two signals).
            if (result.TypeMultiplier > TypeChart.Neutral && cfg.superEffectiveSFX != null)
                audio.PlaySFXAtPosition(cfg.superEffectiveSFX, transform.position);
            else if (result.TypeMultiplier < TypeChart.Neutral && cfg.notVeryEffectiveSFX != null)
                audio.PlaySFXAtPosition(cfg.notVeryEffectiveSFX, transform.position);
        }

        // Any travel VFX (e.g. Giga Drain's looping orb stream) needs to be told to stop emitting on
        // arrival — otherwise a looping particle system just keeps emitting for the entire lingerSeconds
        // window instead of handing off to the impact VFX. StopEmitting (not StopEmittingAndClear) lets
        // particles already in flight finish their own lifetime naturally instead of vanishing instantly,
        // unless the system is listed in clearInstantlyOnResolve (see field comment above).
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == impactVFX) continue;

            bool clearInstantly = clearInstantlyOnResolve != null && System.Array.IndexOf(clearInstantlyOnResolve, ps) >= 0;
            ps.Stop(true, clearInstantly ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        // Linger applies regardless of whether impactVFX exists — a non-particle visual (e.g. Shock's
        // Line Renderer bolt) still needs a beat to actually render before despawning, same as a particle
        // burst needs time to finish playing. impactLingerSeconds defaults to 1s; set it to 0 on any
        // AttackInstance that should still despawn instantly (none currently need to, since nothing calls
        // Launch() yet outside the VFX preview harness).
        if (impactVFX != null)
            impactVFX.Play();

        if (impactSFX != null)
            AudioManager.EnsureInstance().PlaySFXAtPosition(impactSFX, transform.position);

        Destroy(gameObject, impactLingerSeconds);
    }

    // Spawns CombatBalanceConfig.floatingComboTextPrefab as a child of the TARGET (not this AttackInstance,
    // which is about to be destroyed) so it inherits the target's world position/scale — same parenting
    // idea as the bunny rig's permanent Effect_Trigger_LevelUp child. Passes `target` itself (not just a
    // one-time IsVisuallyMirrored snapshot) so FloatingComboEffect can keep resyncing against the target's
    // ACTUAL current flip every frame — see that class's own comment for why a snapshot taken here isn't
    // safe. No-ops if the prefab or sprite isn't assigned yet (art not made) or the target is already gone
    // — FloatingComboEffect.Show has its own null-sprite guard too, this just avoids instantiating at all
    // in the common "not authored yet" case.
    private void SpawnComboEffect(Sprite sprite)
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        if (sprite == null || cfg.floatingComboTextPrefab == null) return;
        if (target == null || target.CombatGameObject == null) return;

        GameObject instance = Instantiate(cfg.floatingComboTextPrefab, target.CombatTransform);
        FloatingComboEffect effect = instance.GetComponent<FloatingComboEffect>();
        if (effect != null) effect.Show(sprite, target);
    }

    private void Fizzle()
    {
        resolved = true;
        // Per the design doc: disappears with no damage. Optionally plays impact VFX in place — no such
        // VFX exists yet, so this is a plain despawn for now.
        Destroy(gameObject);
    }
}
