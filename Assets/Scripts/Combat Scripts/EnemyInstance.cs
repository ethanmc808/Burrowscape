using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Runtime counterpart to EnemyDefinition, playing the same role for enemies that NPCBunny plays for
// bunnies — but deliberately minimal, since enemies are flat stat-sticks (no IV/EV/Nature/traits, no
// job/need state machine). Level is rolled once at spawn from the player's population
// (CombatBalanceConfig.RollEnemyLevel) and never changes afterward, same "spawn-time-only level" rule
// WildBunnySpawner already uses for wild bunnies. Stats reuse BunnyStatCalculator's raw-stat overload
// with IV/EV fixed at 0 (no individual variance) — same formula, same level-scaling curve as a bunny,
// just without the individuality layer.
//
// NOT wired to any spawner yet — nothing in the codebase creates an EnemyInstance today. Whatever
// invasion-trigger system eventually spawns enemies into a room should Instantiate EnemyDefinition.prefab
// and call Initialize() once, the same way WildBunnySpawner calls NPCBunny.SetTypeAndProgression.
public class EnemyInstance : MonoBehaviour, ICombatant
{
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private Animator animator;
    [Tooltip("How long the Dying clip needs to finish playing before this GameObject is destroyed. Tune to match the actual clip length.")]
    [SerializeField] private float deathAnimationSeconds = 1f;
    [Tooltip("Local offset from this enemy's root added to CombatTransform.position when spawning attack VFX, so the attack can leave from mouth height instead of the floor.")]
    [SerializeField] private Vector3 attackOriginOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Movement speed while walking to a flank position — melee enemies only, irrelevant for ranged (which never moves after spawn).")]
    [SerializeField] private float meleeMoveSpeed = 3f;
    [Tooltip("Root transform whose localScale.x sign is flipped to face left/right, same mechanism as NPCBunny's own scale-root flip. Consulted by every movement leg (walk-in, relocation) AND by ranged/melee attacks to face the current target — leave unset only if this prefab never needs to visibly turn.")]
    [SerializeField] private Transform visualScaleRoot;
    [Tooltip("Whether this prefab's UN-flipped art (positive visualScaleRoot.localScale.x) visually faces left rather than right — same per-prefab flag as NPCBunny.bunnyFacesLeftByDefault, since the PSB rig pipeline's default unflipped pose isn't consistently one direction across every rig. Get this wrong and every flip will visibly happen backwards.")]
    [SerializeField] private bool facesLeftByDefault = true;
    [Tooltip("Movement speed while walking in from the gate to the interior target room (once the siege phase has been broken) AND while walking up to a claimed siege spot from the offscreen spawn point beforehand (see ApproachingSiegeSpot/HandleApproachUpdate). Separate from meleeMoveSpeed (local flank shuffling) since both of these are base-scale walking.")]
    [SerializeField] private float walkInMoveSpeed = 2f;
    [Tooltip("Animator bool parameter driven true whenever this enemy is actually moving under code control (offscreen siege approach, post-breach interior walk-in, melee flank approach) and false the rest of the time — mirrors NPCBunny.isMovingParam's own naming convention. Only meaningful once this enemy's own Animator Controller actually has a matching parameter + Idle<->Walking transition wired (the Walking state existed with zero transitions in/out of it before this — see SetMoving's own comment).")]
    [SerializeField] private string isMovingParam = "IsMoving";

    // Which of the four combat phases this enemy is currently in — replaces the old implicit
    // "room == null means not spawned yet" assumption, since room == null is now a legitimate state for
    // every phase before RaidingRoom (see Combat_DesignDoc.md's Gate Siege section). Defaults to
    // RaidingRoom so the original Initialize(def, room) interior-spawn path is completely unaffected.
    // ApproachingSiegeSpot is the newest addition — a siege enemy used to just appear already standing at
    // its claimed spot; now it spawns at one of EntranceGate.EnemySpawnPoints (offscreen) and walks in
    // first, mirroring WildBunnySpawner's own offscreen-arrival visual for bunnies.
    public enum EnemyPhase { ApproachingSiegeSpot, BesiegingGate, WalkingIn, RaidingRoom }
    private EnemyPhase phase = EnemyPhase.RaidingRoom;
    // True once this enemy has actually finished walking in and is standing at its claimed EnemySpot —
    // false for the entire WalkingIn leg, even though InvasionManager.HandleGateBreached registers it into
    // activeInvasions[room] (making it show up in GetEnemiesInRoom) the instant the gate breaks, well
    // before it physically arrives. Mirrors the equivalent bunny-side filter EnemyInstance.Update already
    // applies (bunny.CurrentState == BunnyState.Defending excludes a bunny still walking to its CombatSpot)
    // — enemies never had the symmetric check, which let melee bunnies flank a target that was still
    // outside near the gate and compute a flank point next to wherever it was AT THAT MOMENT, then walk
    // there only to find it long gone ("attack empty air", confirmed 2026-08-03).
    public bool HasArrivedInRoom => phase == EnemyPhase.RaidingRoom;
    // The outside RoomSpot (ranged or melee) this enemy claimed at siege-spawn time — released the
    // moment it starts walking in (BeginWalkToRoom), not on eventual death, since siege-phase enemies are
    // invulnerable and can't die pre-breach (nothing can damage them: bunnies can't defend the gate, and
    // the gate never attacks back).
    private RoomSpot claimedSiegeSpot;

    // The EnemySpot this enemy currently holds for RaidingRoom purposes — either claimed directly by an
    // interior spawn via SetClaimedSpot, or adopted immediately (not deferred to arrival) by
    // BeginWalkToRoom for a gate-siege walk-in or a room-to-room relocation, since the caller has already
    // claimed it via RoomSpot.TryClaim before calling either. Tracked so InvasionManager can read this
    // enemy's CURRENT room/spot (via CurrentRoom/ClaimedInteriorSpot below) when relocating it or handling
    // its death, rather than closing over stale locals captured at spawn time.
    private RoomSpot claimedInteriorSpot;

    public EnemyDefinition Definition => definition;
    // Read fresh by InvasionManager's OnDefeated subscription instead of a captured local, so a death
    // after this enemy has relocated to a different room still reports the room/spot it actually died in.
    public RoomBase CurrentRoom => room;
    public RoomSpot ClaimedInteriorSpot => claimedInteriorSpot;
    public BunnyType Type { get; private set; }
    public int Level { get; private set; } = 1;
    public BunnyStats Stats { get; private set; }

    private int currentHP;
    private RoomBase room;

    // Cached once here rather than re-queried by VisualCenter every frame an attack homes toward this
    // enemy — GetComponentsInChildren allocates, and the set of renderers a rig has never changes at
    // runtime.
    private Renderer[] visualRenderers;

    private void Awake()
    {
        visualRenderers = GetComponentsInChildren<Renderer>();

        // Records facingRight's TRUE starting state (matching whatever this prefab's art was actually
        // authored to show unflipped) rather than leaving it at its compile-time default of true — without
        // this, a rig where facesLeftByDefault is true would have facingRight lying about which way it's
        // actually facing from frame one, same fix as NPCBunny.Awake's identical call.
        SetFacing(!facesLeftByDefault);
    }

    // Combat AI (see CombatEngagement) — always targets whichever Defending bunny in `room` is closest,
    // per Ethan's design (a future quest system will let the player override this via clicking a target;
    // that's a separate, not-yet-built concern and doesn't touch this field).
    private ICombatant currentTarget;
    private float attackCooldownRemaining;
    // Snapshotted by Update when a wind-up starts, consumed by ReleasePendingAttack (called via an
    // Animation Event on this enemy's Attacking clip, at its "release" frame) — see CombatEngagement's
    // own comment for why firing is split into these two phases instead of instant-on-cooldown.
    private ICombatant pendingAttackTarget;

    // Melee-only state (see HandleMeleeUpdate) — mirrors NPCBunny.HandleMeleeDefending's shape, but uses
    // a small local state enum instead of reusing an existing umbrella state, since EnemyInstance has no
    // equivalent to NPCBunny's BunnyState.Defending to fold this into.
    private enum MeleeApproachState { SeekingTarget, MovingToFlank, Flanking }
    private MeleeApproachState meleeApproachState = MeleeApproachState.SeekingTarget;
    private FlankSlots claimedSlots;
    private ICombatant claimedTarget;
    private Vector3 flankDestination;
    private bool facingRight = true;
    // Forces SetFacing's very first real call to actually apply instead of silently no-op'ing — without
    // this, if the first requested direction happens to match facingRight's true default, the sprite
    // stays at whatever raw orientation the prefab's art was authored in (which may not match
    // facesLeftByDefault's assumption) until a genuine flip-flop happens to occur. Same fix as
    // NPCBunny.hasInitializedFacing.
    private bool hasInitializedFacing;

    // WalkingIn state (see BeginWalkToRoom/HandleWalkInUpdate) — deliberately simpler than
    // NPCBunny.MoveAlongPath (no facing-override table, no job-arrival dispatch), just a queue of
    // waypoints walked in order via a plain Vector3.MoveTowards-per-frame advance.
    private Queue<Transform> walkInPath;
    private Transform currentWalkTarget;
    private RoomBase pendingTargetRoom;
    private RoomSpot pendingTargetSpot;

    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    BunnyTypeDefinition ICombatant.AttackSource => definition != null ? definition.attackSource : null;
    Transform ICombatant.CombatTransform => transform;
    // `this == null` (Unity's real destroyed-object check) rather than unconditionally dereferencing
    // gameObject — Component.gameObject throws MissingReferenceException once this instance has been
    // Destroy()'d, instead of the "fake null" every caller checking CombatGameObject == null expects.
    GameObject ICombatant.CombatGameObject => this == null ? null : gameObject;
    // X negation now conditional on live facing — was unconditional before melee enemies needed to flip
    // at runtime (see facingRight/SetFacing). For any enemy that never calls SetFacing (every current
    // ranged prefab), facingRight stays at its default true, producing the exact same sign as the old
    // unconditional negation — behavior-preserving for existing content. Mirrors NPCBunny.AttackOrigin's
    // own live-facing check, just against facingRight instead of a scale-root read.
    Vector3 ICombatant.AttackOrigin => transform.position + new Vector3(facingRight ? -attackOriginOffset.x : attackOriginOffset.x, attackOriginOffset.y, attackOriginOffset.z);
    Vector3 ICombatant.VisualCenter => CombatEngagement.ComputeVisualCenter(visualRenderers, transform.position);
    bool ICombatant.IsFacingRight => facingRight;

    public event System.Action OnDefeated;

    // Called once by whatever spawns this enemy, immediately after Instantiate — mirrors
    // NPCBunny.SetTypeAndProgression's role. Rolls this instance's level from the current population if
    // no explicit level is passed (the normal case); an explicit level is there for testing/quest-spawned
    // enemies that might want a fixed difficulty instead of the population ramp.
    public void Initialize(EnemyDefinition def, RoomBase spawnedRoom, int? explicitLevel = null)
    {
        // GetComponentInChildren, not GetComponent — this prefab's Animator lives on a child (the
        // nested rig prefab instance), not the root GameObject EnemyInstance itself sits on.
        if (animator == null) animator = GetComponentInChildren<Animator>();

        definition = def;
        room = spawnedRoom;
        Type = def.type;
        Level = explicitLevel ?? CombatBalanceConfig.Instance.RollEnemyLevel();

        Stats = BunnyStatCalculator.Resolve(
            def.baseHP, def.baseAttack, def.baseDefense, def.baseSpeed, def.baseLuck, Level,
            ivHP: 0, ivAttack: 0, ivDefense: 0, ivSpeed: 0, ivLuck: 0,
            evHP: 0, evAttack: 0, evDefense: 0, evSpeed: 0, evLuck: 0);

        currentHP = Stats.HP;
    }

    // Called by InvasionManager right after claiming an EnemySpot for a direct interior spawn (the
    // gate-siege walk-in / relocation paths don't need this — BeginWalkToRoom sets claimedInteriorSpot
    // itself, immediately at walk-start, not deferred to arrival — see that method's own comment).
    public void SetClaimedSpot(RoomSpot spot)
    {
        claimedInteriorSpot = spot;
    }

    // Called by InvasionManager.TrySpawnInvasion instead of Initialize for the gate-siege phase — spawns
    // with no room (siege enemies aren't in any room yet, they're outside attacking the gate) and tracks
    // the outside spot claimed for it, released once it starts walking in (see BeginWalkToRoom). Caller
    // already Instantiate()'d this at spawnPoint's position/rotation (or the siege spot's, if spawnPoint
    // wasn't configured) — this just decides which phase to start in based on that.
    public void InitializeForSiege(EnemyDefinition def, RoomSpot spot, Transform spawnPoint, int? explicitLevel = null)
    {
        Initialize(def, spawnedRoom: null, explicitLevel);
        claimedSiegeSpot = spot;
        // No spawnPoint configured (EntranceGate.EnemySpawnPoints empty) — fall back to today's
        // instant-appear-at-spot behavior rather than an approach walk with nowhere real to walk from.
        phase = spawnPoint != null ? EnemyPhase.ApproachingSiegeSpot : EnemyPhase.BesiegingGate;
    }

    private void Update()
    {
        if (currentHP <= 0 || DwellerRoster.Instance == null) return;

        if (phase == EnemyPhase.ApproachingSiegeSpot) { HandleApproachUpdate(); return; }
        if (phase == EnemyPhase.BesiegingGate) { HandleSiegeUpdate(); return; }
        if (phase == EnemyPhase.WalkingIn) { HandleWalkInUpdate(); return; }

        // phase == RaidingRoom — existing interior-invasion logic below, unchanged.
        if (room == null) return; // defensive — RaidingRoom should always have a room, but guard anyway

        BunnyTypeDefinition attackSource = definition != null ? definition.attackSource : null;
        if (attackSource != null && attackSource.isMelee)
        {
            HandleMeleeUpdate();
            return;
        }

        List<NPCBunny> defendersInRoom = DwellerRoster.Instance.GetBunniesDefendingRoom(room);
        List<ICombatant> candidatePool = defendersInRoom
            .Where(b => b.CurrentState == BunnyState.Defending)
            .Cast<ICombatant>()
            .ToList();

        // TEMP — chasing "enemy stops attacking entirely after fainting one of several defenders" bug.
        if (Time.frameCount % 30 == 0)
            Debug.Log($"[VFXDEBUG] EnemyInstance.Update({name}): defendersInRoom={string.Join(", ", defendersInRoom.Select(b => $"{b.name}:{b.CurrentState}"))} poolCount={candidatePool.Count} currentTarget={(currentTarget != null ? currentTarget.CombatGameObject?.name : "NULL")} cooldown={attackCooldownRemaining:F2}");

        bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, candidatePool, out ICombatant attackTarget);

        // TryBeginAttack re-acquires the closest alive candidate into currentTarget EVERY call regardless
        // of cooldown (see its own comment), so this stays accurate continuously — not just at wind-up
        // start — and turns to face a newly-closer target immediately even mid-cooldown. Same world-X
        // convention as ComputeFlankPosition/HandleMeleeUpdate's facing (higher world-X = screen-left).
        if (currentTarget != null)
            SetFacing(currentTarget.CombatTransform.position.x < transform.position.x);

        if (!startedWindUp) return;

        pendingAttackTarget = attackTarget;

        // Same one-shot trigger shape as "Die" below — Any State -> Attacking (Has Exit Time off),
        // Attacking -> Idle (Has Exit Time on) once wired in this enemy's own Animator Controller.
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    // Pre-siege approach leg — walks straight from this enemy's offscreen EntranceGate.EnemySpawnPoints entry to
    // enemy's already-claimed siege spot, then hands off to the real siege phase. Deliberately a single
    // MoveTowards rather than reusing HandleWalkInUpdate's Queue<Transform>-of-waypoints shape: unlike the
    // post-breach walk (which crosses real room geometry), there's nothing between the offscreen spawn
    // point and the siege spot to route around, so one destination is all this needs. Mirrors
    // HandleWalkInUpdate's facing-follows-travel-direction convention for consistency.
    private void HandleApproachUpdate()
    {
        if (claimedSiegeSpot == null) { phase = EnemyPhase.BesiegingGate; return; } // defensive — a siege spot is always assigned before this phase starts

        Vector3 destination = claimedSiegeSpot.transform.position;
        Vector3 toTarget = destination - transform.position;
        if (toTarget.magnitude <= 0.05f)
        {
            transform.position = destination;
            transform.rotation = claimedSiegeSpot.transform.rotation;
            phase = EnemyPhase.BesiegingGate;
            SetMoving(false);
            return;
        }

        Vector3 dir = toTarget.normalized;
        transform.position += dir * walkInMoveSpeed * Time.deltaTime;
        if (Mathf.Abs(dir.x) > 0.01f) SetFacing(dir.x < 0f);
        SetMoving(true);
    }

    // Drives isMovingParam — see that field's own comment. Centralized here rather than inlined at every
    // call site so every movement leg (approach, interior walk-in, melee flank-approach) stays consistent
    // if this ever needs to change (e.g. a future per-phase movement sound/dust-puff hook).
    private void SetMoving(bool isMoving)
    {
        if (animator != null) animator.SetBool(isMovingParam, isMoving);
    }

    // Siege-phase combat — every siege enemy (melee or ranged) has exactly one possible target, the gate
    // itself, so this is simpler than the interior path: no room-wide candidate pool, no death handling
    // (siege enemies can't take damage pre-breach — see the EnemyPhase field comment). Ranged fires from
    // wherever it's standing (its siege spot), same as interior ranged always has; melee reuses the
    // existing HandleMeleeUpdate flanking logic unmodified, just pointed at the gate via candidateOverride.
    private void HandleSiegeUpdate()
    {
        ICombatant gate = EntranceGate.Instance;
        if (gate == null || !gate.IsAlive) return; // breach already resolved elsewhere (InvasionManager.HandleGateBreached) — just idle until this enemy is told to walk in

        BunnyTypeDefinition attackSource = definition != null ? definition.attackSource : null;
        if (attackSource != null && attackSource.isMelee)
        {
            // Only the enemy actually standing at the front meleeSiegeSpot is close enough to strike —
            // one claiming a MeleeWaitingSpots entry instead just idles here for the whole siege (siege
            // enemies are invulnerable and never die pre-breach, so the front slot never opens up for a
            // waiting enemy to advance into mid-siege). It still walks in normally with everyone else
            // once the gate breaks (BeginWalkToRoom/HandleGateBreached don't care which spot it held).
            if (claimedSiegeSpot != ((EntranceGate)gate).MeleeSiegeSpot) return;

            HandleMeleeUpdate(candidateOverride: new List<ICombatant> { gate });
            return;
        }

        List<ICombatant> candidatePool = new List<ICombatant> { gate };
        bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, candidatePool, out ICombatant attackTarget);

        if (currentTarget != null)
            SetFacing(currentTarget.CombatTransform.position.x < transform.position.x);

        if (!startedWindUp) return;

        pendingAttackTarget = attackTarget;
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    // Called by InvasionManager.HandleGateBreached once this enemy has a real interior target, AND by
    // InvasionManager.RelocateGroup when an already-RaidingRoom enemy is being moved to a different room —
    // releases whichever spot this enemy currently holds (outside siege spot OR interior EnemySpot,
    // whichever is set — a gate-siege walk-in only ever has the former, a relocation only ever has the
    // latter) and walks the given path, set on arrival to RaidingRoom so normal Update() dispatch takes
    // over next frame exactly as any interior-spawned enemy.
    public void BeginWalkToRoom(List<Transform> path, RoomBase targetRoom, RoomSpot targetSpot)
    {
        claimedSiegeSpot?.Release(this);
        claimedSiegeSpot = null;
        claimedInteriorSpot?.Release(this);

        // room/claimedInteriorSpot are set to the DESTINATION immediately, not deferred to arrival — the
        // caller has already claimed targetSpot before calling this (see HandleGateBreached/RelocateGroup),
        // so this enemy is already correctly "in" targetRoom for activeInvasions/OnDefeated bookkeeping
        // purposes for the whole WalkingIn leg, exactly like the old code's spawn-time-captured closure
        // used to behave. Without this, a death mid-walk (e.g. the debug kill context menu) would read
        // CurrentRoom/ClaimedInteriorSpot as null/stale and either NRE or leak the claimed spot.
        room = targetRoom;
        claimedInteriorSpot = targetSpot;

        if (path == null || path.Count == 0)
        {
            // No walkable route — fail-soft straight into the room rather than stranding this enemy
            // outside forever (same "don't leave an unreachable target reserved" tolerance BeginDefending
            // already uses elsewhere).
            phase = EnemyPhase.RaidingRoom;
            return;
        }

        pendingTargetRoom = targetRoom;
        pendingTargetSpot = targetSpot;
        walkInPath = new Queue<Transform>(path);
        phase = EnemyPhase.WalkingIn;
        currentWalkTarget = walkInPath.Count > 0 ? walkInPath.Dequeue() : null;
    }

    private void HandleWalkInUpdate()
    {
        if (currentWalkTarget == null)
        {
            // room/claimedInteriorSpot were already set to these same values back in BeginWalkToRoom —
            // just flipping phase here, arrival doesn't change which room/spot this enemy belongs to.
            phase = EnemyPhase.RaidingRoom;
            transform.position = pendingTargetSpot != null ? pendingTargetSpot.transform.position : transform.position;
            SetMoving(false);
            return;
        }

        Vector3 toTarget = currentWalkTarget.position - transform.position;
        if (toTarget.magnitude <= 0.05f)
        {
            transform.position = currentWalkTarget.position;
            currentWalkTarget = walkInPath.Count > 0 ? walkInPath.Dequeue() : null;
            return; // still walking overall (just advancing to the next waypoint) — SetMoving stays true from below
        }

        Vector3 dir = toTarget.normalized;
        transform.position += dir * walkInMoveSpeed * Time.deltaTime;
        if (Mathf.Abs(dir.x) > 0.01f) SetFacing(dir.x < 0f); // reuses existing SetFacing/visualScaleRoot, no-op if unset
        SetMoving(true);
    }

    // Melee counterpart to Update above — mirrors NPCBunny.HandleMeleeDefending's shape exactly (pick a
    // target with an open FlankSlots slot, walk to a computed position beside it, attack from a pinned
    // single-target pool once there), just with EnemyInstance's own simpler local move (enemies don't do
    // general base pathing the way bunnies do — a straight MoveTowards within the room is enough, since a
    // melee enemy only ever repositions locally after its one fixed EnemySpot spawn).
    //
    // candidateOverride: when non-null (the siege-phase call from HandleSiegeUpdate), used as the
    // candidate pool instead of the normal DwellerRoster-based room query — lets the gate-siege phase
    // reuse this entire method, including its flank-claim/walk/attack logic, completely unmodified.
    private void HandleMeleeUpdate(IEnumerable<ICombatant> candidateOverride = null)
    {
        if (claimedSlots != null)
        {
            if (claimedTarget == null || claimedTarget.CombatGameObject == null || !claimedTarget.IsAlive)
            {
                claimedSlots.ReleaseSlot(this);
                claimedSlots = null;
                claimedTarget = null;
                meleeApproachState = MeleeApproachState.SeekingTarget;
                SetMoving(false);
                return;
            }

            if (meleeApproachState == MeleeApproachState.MovingToFlank)
            {
                transform.position = Vector3.MoveTowards(transform.position, flankDestination, meleeMoveSpeed * Time.deltaTime);
                if (Vector3.Distance(transform.position, flankDestination) <= 0.05f)
                    meleeApproachState = MeleeApproachState.Flanking;
                SetMoving(true);
                return; // no attacking mid-walk
            }

            SetMoving(false); // Flanking — standing in place, attacking below

            List<ICombatant> pinnedPool = new List<ICombatant> { claimedTarget };
            bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, pinnedPool, out ICombatant attackTarget);
            if (!startedWindUp) return;

            pendingAttackTarget = attackTarget;
            if (animator != null)
                animator.SetTrigger("Attack");
            return;
        }

        List<ICombatant> candidatePool;
        if (candidateOverride != null)
        {
            candidatePool = candidateOverride.ToList();
        }
        else
        {
            List<NPCBunny> defendersInRoom = DwellerRoster.Instance.GetBunniesDefendingRoom(room);
            candidatePool = defendersInRoom
                .Where(b => b.CurrentState == BunnyState.Defending)
                .Cast<ICombatant>()
                .ToList();
        }

        ICombatant chosen = CombatEngagement.FindBestMeleeTarget(transform.position, candidatePool);
        if (chosen == null) return; // nobody defending has an open flank slot right now

        FlankSlots slots = CombatEngagement.GetFlankSlots(chosen);
        if (slots == null || !slots.TryClaimSlot(this, out FlankSide side)) return;

        claimedSlots = slots;
        claimedTarget = chosen;
        flankDestination = CombatEngagement.ComputeFlankPosition(chosen, side);
        meleeApproachState = MeleeApproachState.MovingToFlank;
        // FlankSide.Left gets a NEGATIVE world-X offset in ComputeFlankPosition, and this project's
        // world-X is inverted from screen space (bigger world-X = screen-left) — so "Left" actually lands
        // the attacker on the target's screen-RIGHT, and it must face left (toward the target) from there.
        // Same fix as NPCBunny.HandleMeleeDefending, confirmed empirically there in Play mode.
        SetFacing(side != FlankSide.Left);
    }

    // No-op if visualScaleRoot isn't wired. Mirrors NPCBunny.SetFacing exactly — a hardcoded
    // faceRight?1:-1 ternary (the original version of this method) is what caused the Slime's movement
    // facing to never visibly update in Play mode: its art's unflipped pose actually reads as facing left,
    // and once facingRight's compile-time default (true) already matched the first requested direction,
    // the old early-return guard skipped the flip entirely with no way to recover. facesLeftByDefault +
    // Awake's SetFacing(!facesLeftByDefault) call together fix both the wrong-sign-for-this-rig problem
    // AND the stale-default-never-applies problem, exactly the way NPCBunny already solved it.
    private void SetFacing(bool faceRight)
    {
        if (visualScaleRoot == null) return;

        if (!hasInitializedFacing)
            hasInitializedFacing = true;
        else if (facingRight == faceRight)
            return;

        facingRight = faceRight;
        bool flip = facesLeftByDefault ? faceRight : !faceRight;
        Vector3 scale = visualScaleRoot.localScale;
        float magnitude = Mathf.Abs(scale.x);
        scale.x = flip ? -magnitude : magnitude;
        visualScaleRoot.localScale = scale;
    }

    // Animation Event receiver — place this on this enemy's Attacking clip at the frame the attack
    // visually "releases", so the AttackInstance VFX spawns in sync with the animation instead of
    // instantly on cooldown. Re-validates the target itself (see CombatEngagement.ReleaseAttack) since a
    // few frames of wind-up may have passed since TryBeginAttack snapshotted it. Deliberately no
    // retargetPool here (unlike NPCBunny.ReleasePendingAttack's ranged case) — an enemy whose original
    // bunny target died/got recalled just fizzles rather than sniping a different bunny, per Ethan's ask:
    // a lingering enemy attack landing on the "wrong" bunny would be a real fairness/surprise problem the
    // way a fainted bunny's own stray attack still landing on an enemy simply isn't.
    public void ReleasePendingAttack()
    {
        if (pendingAttackTarget == null) return;
        CombatEngagement.ReleaseAttack(this, pendingAttackTarget);
        pendingAttackTarget = null;
    }

    // Test-only hook for VFXPreviewHarness — triggers the same wind-up animation Update does (skipping
    // its DwellerRoster/room/cooldown gating, which the isolated preview scene has no reason to set up),
    // so the Attacking clip's existing Animation Event still fires ReleasePendingAttack above at the real
    // "release" frame, the same path actual combat uses.
    public void TestFireAttack(ICombatant target)
    {
        pendingAttackTarget = target;
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    public void TakeCombatDamage(int amount)
    {
        if (currentHP <= 0) return;
        currentHP = Mathf.Max(0, currentHP - Mathf.Max(0, amount));
        if (currentHP != 0) return;

        // Same "release immediately, don't leak the slot" reasoning as NPCBunny.TakeCombatDamage — a
        // defeated melee enemy would otherwise permanently occupy a flank slot on whoever it was flanking.
        if (claimedSlots != null)
        {
            claimedSlots.ReleaseSlot(this);
            claimedSlots = null;
            claimedTarget = null;
        }

        if (animator != null) animator.SetTrigger("Die");
        OnDefeated?.Invoke();

        // Self-destructs after the Dying clip has time to play, rather than InvasionManager destroying
        // this instantly on defeat — gameplay bookkeeping (EnemySpot release, invasion tracking) still
        // happens immediately via OnDefeated above, only the visual corpse lingers.
        Destroy(gameObject, deathAnimationSeconds);
    }

    // Test-only hook — until the AI/attack-triggering layer exists, there's no other way to defeat an
    // enemy and exercise InvasionManager's OnInvasionCleared/defender-recall path. Right-click this
    // component's header in Play mode to use.
    [ContextMenu("Debug: Kill (Test)")]
    private void DebugKill()
    {
        TakeCombatDamage(currentHP);
    }
}
