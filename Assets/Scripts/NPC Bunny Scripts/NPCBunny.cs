using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BunnyState
{
    Idle,
    MovingToSpot,
    Working,
    Eating,
    Drinking,
    Relaxing,
    Sleeping,
    Wandering,
    PassingGate,
    Despawning,
    WaitingForLift,
    RidingLift,
    DisembarkingLift,
    // Arrival marker only (see MoveAlongPath/HandleMovingToSpot's pendingStateOnArrival dispatch) — not
    // a persistent state anything sits in. Fires once a departing bunny reaches the gate exit point,
    // triggering OnArrivedAtDepartingGate's own gate-wait + final walk to the offscreen staging point.
    // Mirrors PassingGate, which is the arrival-direction equivalent.
    DepartingThroughGate,
    // Persistent state a bunny sits in for the whole length of a Foraging trip — parked offscreen at the
    // staging point, alive and simulated (see ForagingManager), not despawned. See Foraging_DesignDoc.md.
    Foraging,
    // Posted at a room's CombatSpot during an active invasion — either a resident auto-interrupted from
    // Working/Relaxing in that same room, or a Guard Room bunny manually deployed there. Positioning only
    // for now (see BeginDefending/StopDefendingAndReturn) — target-picking and actually attacking is the
    // AI/decision-logic layer, not built yet.
    Defending,
    // Reached 0 HP while Defending (see TakeCombatDamage). Sits at its CombatSpot doing nothing — not a
    // valid attack target (ICombatant.IsAlive is false) and HandleFainted is a no-op, so it also can't
    // attack back. defendingRoom/defendingSpot are deliberately left set so InvasionManager's existing
    // OnInvasionCleared recall (via StopDefendingAndReturn) still reaches it once the fight ends, at which
    // point it revives to 1 HP and walks back to its previous activity like any other defender.
    Fainted,
    // Occupying a Hospital BedSpot (see HospitalRoom, assigned via the Patient UI — a separate concept
    // from a job assignment, so this doesn't route through IJobRoom at all). HealPatientsRoutine on
    // HospitalRoom itself raises currentHP each tick; HandleRecovering just watches for full HP and walks
    // the bunny back out via ReturnFromHospital once it's healed.
    Recovering,
}
public enum BunnyArrivalType
{
    Wild,
    ReturningFromQuest,
    ReturningFromForaging
}
public enum BunnyGender
{
    Male,
    Female
}
// Full 20-type roster (see BunnyTypeSystem_DesignDoc.md at the project root). Only the Group 1 types
// (Neutral/Fire/Water/Plant/Shock) have art/prefabs today — the rest exist here so BunnyTypeDefinition
// data (base stats, unlock thresholds) can be authored ahead of their art, per that doc's scaffolding
// approach.
public enum BunnyType
{
    Neutral, Fire, Water, Plant, Shock,
    Insect, Melee, Stone,
    Mind, Toxic, Ice,
    Sound, Air, Earth,
    Pixie, Light,
    Metal, Ghost, Dark,
    Draco
}
// What a bunny was doing in a room being merged/upgraded, returned by EvacuateForRoomTransition so
// RoomTransitionService knows how (or whether) to resettle it into the replacement room afterward.
// The "Away" variants (WorkingAway/SleepingAway) mean the bunny wasn't physically standing in the room
// at all — they were off Eating/Drinking/Sleeping elsewhere with their claim just reserved on it — so
// Resettle can hold an equivalent spot on the replacement room instead of dropping them to Idle first.
public enum RoomTransitionRole
{
    None,
    Working,
    WorkingAway,
    Relaxing,
    Sleeping,
    SleepingAway
}

[RequireComponent(typeof(Animator))]
public class NPCBunny : MonoBehaviour, ICombatant
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.5f;
    [SerializeField] private float wanderSpeedMultiplier = 0.5f; // wandering bunnies move slower than working/eating bunnies
    [SerializeField] private float arrivalThreshold = 0.05f;

    [Header("Experience (Foraging is the only source — see BunnyLevelCurve)")]
    [Tooltip("Running cumulative total, never reset per level — see AddExperience/BunnyLevelCurve.")]
    [SerializeField] private float experience = 0f;

    [Header("Hunger")]
    [SerializeField] private float hunger = 100f; // 0-100
    [SerializeField] private float hungerDecayPerSecond = 0.1f;
    [SerializeField] private float hungerThresholdLow = 35f;
    [SerializeField] private float hungerThresholdFull = 75f;
    [SerializeField] private float hungerThresholdCritical = 15f; // only this threshold interrupts Sleeping
    [SerializeField] private float hungerGainPerCarrot = 25f;

    [Header("Thirst")]
    [SerializeField] private float thirst = 100f; // 0-100, mechanically clones Hunger
    [SerializeField] private float thirstDecayPerSecond = 0.1f;
    [SerializeField] private float thirstThresholdLow = 35f;
    [SerializeField] private float thirstThresholdFull = 75f;
    [SerializeField] private float thirstThresholdCritical = 15f; // only this threshold interrupts Sleeping
    [SerializeField] private float thirstGainPerDrink = 25f;

    [Header("Energy")]
    [SerializeField] private float energy = 100f; // 0-100
    [SerializeField] private float energyDecayPerSecond = 0.1f; // passive decay, ticks in every state except Sleeping
    [SerializeField] private float energyDecayPerSecondWorking = 0.2f; // faster than passive while actually working
    [SerializeField] private float energyDecayPerSecondQuesting = 0.2f; // Quest state doesn't exist yet — tunable ahead of time
    [SerializeField] private float energyDecayPerSecondForaging = 0.2f; // Foraging state doesn't exist yet — tunable ahead of time
    [SerializeField] private float energyThresholdLow = 35f;
    [SerializeField] private float energyGainPerSecondSleeping = 5f; // regen only goes up to 100, never past

    [Header("HP (Foraging's encounter resolver and base-defense combat both spend this — see Foraging_DesignDoc.md / Combat_DesignDoc.md)")]
    [Tooltip("Current HP. Initialized to Stats.HP whenever Stats changes (spawn, LevelUp) and never exceeds it. Foraging's placeholder encounter resolver floors at 1 and never reduces this to 0; base-defense combat (TakeCombatDamage) is the only thing that can, at which point the bunny faints (BunnyState.Fainted) until revived to 1 HP once the invasion clears.")]
    [SerializeField] private int currentHP = 1;
    // Grade 3 Guard Room shield buffer — a separate absorb pool, not extra max HP (see TakeCombatDamage).
    // Both 0 whenever this bunny isn't currently posted at a Grade 3+ Guard Room; granted/cleared wholesale
    // by GuardRoom, never regenerates, never persists across unassignment.
    private int currentShieldHP;
    private int maxShieldHP;
    [Tooltip("Sleeping's HP regen is derived from actual energy gained, not a flat per-second rate — this is the fraction of max HP restored per point of energy gained (0.005 = 0.5%, i.e. 2 energy restored = 1% max HP, so a full 0->100 energy night heals 50% of max HP). Automatically inherits Bedroom.GradeMultiplier scaling since it's driven off the energy gain, which is already scaled.")]
    [SerializeField] private float hpPercentOfMaxPerEnergyRestored = 0.005f;
    [Tooltip("Slow always-on passive regen (ticks in every state except Sleeping, which uses the formula above instead, and Fainted, which must stay at 0 until StopDefendingAndReturn's battle-end revive). 0.005 = 0.5% max HP/sec, i.e. 1% every 2 seconds. Multiplied by any active Regrowth-style HPRegenMultiplier passive (see GetPassiveHPRegenMultiplier).")]
    [SerializeField] private float hpPassiveRegenPercentPerSecond = 0.005f;
    private float hpRegenRemainder; // fractional carry-over so a slow regen rate isn't rounded away to 0 every frame

    [Header("Mood")]
    [SerializeField] private float mood = 100f; // 0-100, pure passive stat, no room/travel of its own
    [SerializeField] private float moodDecayPerSecondWorking = 0.05f;
    [SerializeField] private float moodGainPerSecondRelaxing = 0.05f;
    [SerializeField] private float moodGainPerSecondEatingOrDrinking = 0.2f; // fast but short (Eating/Drinking don't last long)
    [SerializeField] private float moodGainPerSecondSleeping = 0.02f; // slow but long
    [SerializeField] private float moodDecayPerSecondIdlePacing = 0.1f; // idle-pacing with no relax spot available

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isMovingParam = "IsMoving";
    [SerializeField] private string isWorkingParam = "IsWorking";
    [SerializeField] private string isEatingParam = "IsEating";
    [SerializeField] private string isDrinkingParam = "IsDrinking";
    [SerializeField] private string isSleepingParam = "IsSleeping";
    // One-shot Cheer TRIGGER param (despite the "Is" prefix matching the bool params above for naming
    // consistency, this must stay an Animator Trigger, not a Bool — see PlayLevelUpFeedback's
    // animator.SetTrigger call, and WorkRoomXP_DesignDoc.md's "Key decision: the cheer never touches
    // CurrentState" section). Animator Controller wiring: Any State -> Cheering (condition: this
    // trigger, Has Exit Time off), then Cheering -> back out (Has Exit Time ON, no conditions, ~0
    // transition duration) so it falls back into whatever IsWorking/IsEating/etc. already say once the
    // clip finishes, with no code ever touching CurrentState.
    [SerializeField] private string isCheeringParam = "IsCheering";
    // Same one-shot TRIGGER shape as isCheeringParam above (Any State -> Attacking, Has Exit Time off;
    // Attacking -> Idle, Has Exit Time on) — fired once per landed attack from HandleDefending, never tied
    // to CurrentState (which stays Defending throughout).
    [SerializeField] private string isAttackingParam = "IsAttacking";
    // A persistent BOOL synced every frame in UpdateAnimator (like isWorkingParam/isSleepingParam above),
    // deliberately NOT a one-shot Trigger like isAttackingParam/isCheeringParam — Fainted can last an
    // unpredictable, indefinite length of time (until StopDefendingAndReturn's battle-end revive), so
    // there's no fixed clip length to key a Has-Exit-Time auto-return off of the way Attacking/Cheering
    // do. Animator wiring must mirror this rig's existing IsWorking/IsSleeping pairwise-transition
    // convention, NOT Attacking/Cheering's Any-State-in/Has-Exit-Time-out shape: e.g. Idle -> Fainted (if
    // this bool), Fainted -> Idle (if NOT this bool) — an "Any State -> Fainted" entry is fine (safer,
    // catches any current animator state), but Fainted's own outgoing transition back out is required,
    // since Any-State transitions only handle entry, never exit.
    [SerializeField] private string isFaintedParam = "IsFainted";

    [Header("Faint Fade (see TakeCombatDamage/FaintFadeRoutine)")]
    [Tooltip("Seconds after fainting before the sprite starts fading — matches the Fainting animation clip's own collapse timing (frame 40 @ 60fps = 0.667s), not tied to the clip's actual playback since Fainted is a persistent state that may loop/hold indefinitely.")]
    [SerializeField] private float faintFadeDelaySeconds = 40f / 60f;
    [Tooltip("How long the fade itself takes once it starts.")]
    [SerializeField] private float faintFadeDurationSeconds = 0.3f;
    [Tooltip("Alpha the sprite settles at once fully faded. Restored to 1 immediately (no fade-in) the moment StopDefendingAndReturn revives this bunny.")]
    [SerializeField, Range(0f, 1f)] private float faintFadeTargetAlpha = 0.5f;

    [Header("Level-Up SFX (see AudioManager)")]
    [SerializeField] private AudioClip levelUpClip;

    [Header("Proximity Ambient SFX (see AudioManager — only audible near the camera)")]
    [SerializeField] private AudioClip eatingAmbientClip;
    [SerializeField] private AudioClip drinkingAmbientClip;
    [SerializeField] private AudioClip sleepingAmbientClip;

    [Header("Facing Direction")]
    [SerializeField] private Transform bunnyScaleRoot;
    [Tooltip("Local offset from this bunny's root added to CombatTransform.position when spawning attack VFX, so the attack can leave from mouth height instead of the floor. Author X assuming this bunny faces RIGHT — it's automatically mirrored when facing left.")]
    [SerializeField] private Vector3 attackOriginOffset = new Vector3(0f, 1f, 0f);
    [SerializeField] private bool bunnyFacesLeftByDefault = true;

    [Header("Idle Pacing (fallback when no Living Room spot is available anywhere on the base)")]
    [SerializeField] private float wanderPauseDuration = 3f; // how long to idle at each stop before moving again
    private float wanderTimer = 0f;

    [Header("Warnings")]
    [SerializeField] private float noSpotWarningCooldown = 5f; // throttles repeated "no spot available" toasts
    private float lastNoEatSpotWarningTime = -999f;
    private float lastNoDrinkSpotWarningTime = -999f;
    private float lastNoSleepSpotWarningTime = -999f;

    [Header("Lift Visual Timing")]
    [Tooltip("Brief pause after teleporting onto the new floor before walking out to the landing spot, leaving room for a future doors-opening animation.")]
    [SerializeField] private float liftDisembarkRevealDelay = 0.5f;

    public BunnyState CurrentState { get; private set; } = BunnyState.Idle;
    public bool IsHungry => hunger < hungerThresholdLow;
    public bool IsThirsty => thirst < thirstThresholdLow;
    public bool IsTired => energy < energyThresholdLow;
    public bool IsCriticallyHungry => hunger < hungerThresholdCritical;
    public bool IsCriticallyThirsty => thirst < thirstThresholdCritical;
    public float HungerValue => hunger;
    public float ThirstValue => thirst;
    public float EnergyValue => energy;
    public float MoodValue => mood;
    public int HPValue => currentHP;
    public int ShieldValue => currentShieldHP;
    public int MaxShieldValue => maxShieldHP;
    public bool IsAssignedToJob => assignedJobRoom != null;
    public IJobRoom AssignedJobRoom => assignedJobRoom;
    public LivingRoom ClaimedRelaxRoom => claimedRelaxRoom;
    // True once the bunny has actually passed through the entrance gate (set in EnterBaseAndWander,
    // which only ever runs from OnArrivedAtGateExit). False while spawned-but-queued or still awaiting
    // approval — a bunny in that state has no currentRoom yet, so routing it to a job would build a
    // path straight from the base entrance to the job room, skipping the gate entirely.
    public bool HasEnteredBase { get; private set; }
    public bool IsAwaitingApproval { get; private set; }
    public BunnyArrivalType ArrivalType { get; private set; } = BunnyArrivalType.Wild;
    public BunnyGender Gender { get; private set; } = BunnyGender.Male;
    public string BunnyName { get; private set; } = "Unnamed";
    public BunnyType Type { get; private set; }
    public Sprite TypeIcon { get; private set; }
    // Exposed for ForagingPreviewStage, which needs this bunny's own prefab to spawn a decorative
    // walking duplicate — not used anywhere else that cares about identity, just art lookup.
    public BunnyTypeDefinition TypeDefinition => typeDefinition;
    // Persistent Foraging accessory slot (Foraging Trip Detail Panel + Item Expansion design doc) —
    // equipped/unequipped anytime via BunnyInfoUI while resident, unlike potions which stay a fresh
    // per-trip choice. Stock withdraw/return bookkeeping lives in ForagingInventoryManager.TryEquipAccessory;
    // this is deliberately just a dumb reference holder.
    public ForagingAccessoryDefinition EquippedAccessory { get; private set; }
    public void SetEquippedAccessory(ForagingAccessoryDefinition accessory) => EquippedAccessory = accessory;
    public int Level { get; private set; } = 1;
    public BunnyStats Stats { get; private set; }
    public IReadOnlyList<BunnyTraitDefinition> Traits { get; private set; } = new List<BunnyTraitDefinition>();
    public IReadOnlyList<BunnyPassiveDefinition> ActivePassives { get; private set; } = new List<BunnyPassiveDefinition>();

    // Individuality layer (Bunny Stat System Redesign design doc) — Nature/IVs are rolled once at spawn
    // (see RollIndividuality) and never change afterward. EVs default to 0 and stay there: no EV-earning
    // mechanism exists yet (explicitly out of scope in the design doc), so nothing sets these today.
    // MaxTotalEV is defined here for whatever future system grants EVs to respect, unenforced until then.
    public const int MaxTotalEV = 512;
    public BunnyNature Nature { get; private set; }
    // Null for both when the bunny has Stoic (see ApplyNatureEffects) -- read by BunnyInfoUI to decide
    // which stat gets the green "+"/red "-" indicator, without having to re-derive the Stoic check itself.
    public NatureStat? BoostedStat { get; private set; }
    public NatureStat? LoweredStat { get; private set; }
    public int IVHP { get; private set; }
    public int IVAttack { get; private set; }
    public int IVDefense { get; private set; }
    public int IVSpeed { get; private set; }
    public int IVLuck { get; private set; }
    public int EVHP { get; private set; }
    public int EVAttack { get; private set; }
    public int EVDefense { get; private set; }
    public int EVSpeed { get; private set; }
    public int EVLuck { get; private set; }
    // Read externally by GardenRoom/WaterRoom when computing this bunny's per-tick output (see
    // TraitEffectType.ProductionMultiplier). Defaults to 1 (no trait effect) — everything else
    // (energy/mood/hunger/thirst decay, move speed) is applied in place to this bunny's own fields
    // instead, since nothing outside NPCBunny needs to read those.
    public float ProductionMultiplier { get; private set; } = 1f;

    // Read externally by ForagingManager's loot roll (Treasure Finder trait, see TraitEffectType.
    // RareLootChanceBonus) — an ADDITIVE percentage-point bonus to the Uncommon/Rare roll, not a
    // multiplier like every other trait effect here, since the design doc calls for it to stack
    // additively alongside Luck's own separate contribution to the same roll rather than compete with
    // it. Defaults to 0 (no trait effect), same reasoning as ProductionMultiplier's default of 1.
    public float ForagingRareLootBonus { get; private set; } = 0f;

    // Applied universally to XP from EVERY source — see TraitEffectType.XPGainMultiplier and
    // AddExperience's own comment for why this is applied centrally there rather than by each XP
    // source individually. Still exposed publicly (not folded away entirely) since a future UI might
    // want to display it, e.g. a "+25% XP" indicator, the same way BoostedStat/LoweredStat are exposed
    // for Nature's UI indicator. Defaults to 1 (no trait effect), same reasoning as ProductionMultiplier.
    public float XPGainMultiplier { get; private set; } = 1f;

    // Fires once per AddExperience call that leveled the bunny up at all (never once per individual
    // LevelUp — a single big XP grant can cross several level thresholds at once, see AddExperience),
    // passing the new Level. Purely a notification hook — PlayLevelUpFeedback (this class's own
    // reaction: Cheer animation + SFX) subscribes to nothing; it's called directly from AddExperience
    // instead. This event exists so external systems that don't live on NPCBunny can react too, without
    // NPCBunny needing to know they exist — e.g. ForagingManager logging a bold trip-log entry when a
    // bunny levels up while parked BunnyState.Foraging (where the Cheer/SFX are suppressed — see
    // PlayLevelUpFeedback). See WorkRoomXP_DesignDoc.md's "Foraging's bold log entry" section.
    public event System.Action<int> OnLevelUp;

    // Which BunnyTypeDefinition this bunny spawned from — needed later by LevelUp to re-resolve
    // Stats/ActivePassives against the same base stats/passive list. Not exposed publicly; external
    // code should read the resolved Type/Stats/ActivePassives properties above instead.
    private BunnyTypeDefinition typeDefinition;

    private RoomSpot currentTargetSpot;
    private Queue<Transform> currentPath;
    private Transform currentWaypointTarget;
    private RoomSpot claimedWorkSpot; // remembers the garden spot to return to after eating
    private IJobRoom assignedJobRoom;
    private CafeteriaRoom cafeteriaBeingUsed;
    private WaterRoom waterRoomBeingUsed; // mirrors cafeteriaBeingUsed, for the drinking-spot side of a WaterRoom
    private RoomSpot claimedRelaxSpot; // Living Room spot reserved/occupied while idle; mirrors claimedWorkSpot
    private LivingRoom claimedRelaxRoom; // which room claimedRelaxSpot belongs to
    private RoomSpot claimedSleepSpot; // Bedroom spot reserved/occupied while sleeping; mirrors claimedRelaxSpot
    private Bedroom claimedSleepRoom; // which room claimedSleepSpot belongs to

    // Room-defense posting — set by BeginDefending, cleared by StopDefendingAndReturn. Deliberately does
    // NOT touch claimedWorkSpot/assignedJobRoom/claimedRelaxSpot (same "pause without releasing claim"
    // shape as ForceIdleDueToRoomShutdown/LeaveWorkForCafeteria), so ReturnToPreviousActivity already
    // knows how to send the bunny back to whatever it was doing — a resident resumes its own claim, a
    // deployed Guard Room bunny's claimedWorkSpot/assignedJobRoom already point at its Guard Room post.
    private RoomSpot defendingSpot;
    private RoomBase defendingRoom;
    public bool IsDefending => defendingRoom != null;
    public RoomBase DefendingRoom => defendingRoom;

    // Hospital bed claim — set by AssignToHospitalBed (Patient UI), cleared by ReturnFromHospital. Mirrors
    // defendingSpot/defendingRoom's shape exactly; kept as its own pair rather than reusing
    // defendingSpot/defendingRoom since a bunny could in principle be recalled from Recovering while
    // totally unrelated to any invasion.
    private RoomSpot claimedHospitalBedSpot;
    private HospitalRoom claimedHospitalRoom;
    public bool IsHospitalized => claimedHospitalRoom != null;
    public HospitalRoom ClaimedHospitalRoom => claimedHospitalRoom;

    // Combat AI (see CombatEngagement) — always targets whichever enemy in defendingRoom is closest, per
    // Ethan's design (a future quest system will let the player override this by clicking a target; that's
    // a separate, not-yet-built concern and doesn't touch this field).
    private ICombatant currentCombatTarget;
    private float attackCooldownRemaining;
    // Snapshotted by HandleDefending when a wind-up starts, consumed by ReleasePendingAttack (called via
    // an Animation Event on this type's Attacking clip, at its "release" frame) — see CombatEngagement's
    // own comment for why firing is split into these two phases instead of instant-on-cooldown.
    private ICombatant pendingAttackTarget;

    // Melee-only combat state (see HandleMeleeDefending) — null/unset whenever this bunny isn't currently
    // holding a flank slot on a target. Distinct from currentCombatTarget/attackCooldownRemaining above
    // (shared ranged+melee cooldown bookkeeping, unchanged) since only melee needs to track "who am I
    // physically flanking right now" and on which side.
    private FlankSlots claimedFlankSlots;
    private ICombatant claimedFlankTarget;
    private FlankSide claimedFlankSide;

    // Lazily created the first time this bunny ever needs to walk to a flank position, then reused for
    // every subsequent flank walk (repositioned in place rather than Instantiate/Destroy per trip) — a
    // flank destination is a computed Vector3 next to a target, not an authored room Transform, so
    // MoveAlongPath's normal RoomSpot-based waypoints don't apply here.
    private Transform flankWaypoint;

    // Which room (if any) this bunny currently holds a proximity-ambient ref count against, per need —
    // see SyncProximityAmbient. Tracked separately from cafeteriaBeingUsed/waterRoomBeingUsed/
    // claimedSleepRoom because those stay set across an interrupt-chain (e.g. woken early from Sleeping
    // to eat, see LeaveSleepingForCafeteria) while CurrentState itself has already moved on — the ambient
    // loop needs to follow CurrentState, not the reservation.
    private CafeteriaRoom proximityEatingRoom;
    private WaterRoom proximityDrinkingRoom;
    private Bedroom proximitySleepingRoom;
    private RoomBase currentRoom;
    private RoomSpot currentSpot; // the spot bunny is currently occupying, null if none/mid-transit
    private Transform currentWanderPoint; // last wander destination reached, null if occupying a RoomSpot instead

    // The floor the bunny is ACTUALLY on right now. Tracked separately from currentRoom.FloorIndex
    // because currentRoom can be a LiftRoom, whose own FloorIndex only ever represents its primary
    // floor — not necessarily the floor of the specific landing spot the bunny is currently standing at.
    private int currentFloorIndex = 0;
    private RoomBase pendingArrivalRoom; // room to attribute to the bunny once it finishes crossing the gate
    private Transform pendingArrivalPoint; // gate exit point, becomes the bunny's known position on arrival

    // Foraging departure bookkeeping — the offscreen staging point a departing bunny walks to once past
    // the gate (see DepartForForaging/OnArrivedAtDepartingGate/OnArrivedAtForagingStagingPoint below).
    private Transform pendingForagingStagingPoint;

    // A picked-but-not-yet-reached wander destination. Deliberately NOT committed to currentRoom/
    // currentWanderPoint until the bunny actually arrives (see OnArrivedAtWanderPoint) — committing
    // early would make anything that routes off currentRoom mid-walk (e.g. a job assignment) build a
    // path from a location the bunny hasn't physically reached yet.
    private RoomBase pendingWanderRoom;
    private Transform pendingWanderDestination;

    // Cross-floor (lift) trip bookkeeping — the "real" destination beyond the lift ride itself.
    private LiftRoom pendingLift;
    private int pendingLiftOriginFloor;
    private RoomBase pendingFinalRoom;
    private RoomSpot pendingFinalSpot;
    private Transform pendingFinalWanderPoint;
    private BunnyState pendingFinalState;
    private Transform pendingDisembarkLandingSpot; // where to walk back out to after stepping off the car

    private bool facingRight;
    private bool hasInitializedFacing;
    private bool? pendingFacingOverride;

    // Cached once here rather than re-queried by VisualCenter every frame an attack homes toward this
    // bunny — GetComponentsInChildren allocates, and the set of renderers a rig has never changes at
    // runtime (see the bunny outline rework's multi-part body/face-overlay setup).
    private Renderer[] visualRenderers;
    // Separate from visualRenderers above (that one stays Renderer[] for ComputeVisualCenter's bounds
    // calc) — this is specifically for FaintFadeRoutine's alpha fade, which needs .color, only present on
    // SpriteRenderer, not the base Renderer type.
    private SpriteRenderer[] spriteRenderers;
    private Coroutine faintFadeRoutine;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        visualRenderers = GetComponentsInChildren<Renderer>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();

        SetFacing(!bunnyFacesLeftByDefault);
    }
    private void OnEnable()
    {
        DwellerRoster.Instance?.Register(this);
    }

    private void OnDisable()
    {
        DwellerRoster.Instance?.Unregister(this);
    }

    private void Update()
    {
        // Needs are frozen entirely until the bunny has actually passed the gate (HasEnteredBase) — a
        // bunny spawned-but-queued or awaiting approval shouldn't get hungry/thirsty/tired/moody before
        // it's even been let in, and freezing here (rather than at each individual threshold check) is
        // what lets RandomizeStartingNeeds' spawn-time stagger actually mean something: without this, a
        // bunny stuck in a long queue would just decay its randomized head start away before ever being
        // approved.
        if (HasEnteredBase)
        {
            // Hunger/Thirst/Mood are entirely frozen for the whole length of a Foraging trip (both the
            // active phase and the return countdown) — see Foraging_DesignDoc.md's "Other needs while
            // foraging" section. Only Energy is affected by Foraging, and even that stops decaying
            // during the return countdown specifically (see IsForagingReturnCountdownActive below).
            bool foragingNeedsFrozen = CurrentState == BunnyState.Foraging;

            if (!foragingNeedsFrozen)
            {
                hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * Time.deltaTime);
                thirst = Mathf.Max(0f, thirst - thirstDecayPerSecond * Time.deltaTime);
            }

            // Energy always decays except while Sleeping (the only way to regen it, capped at 100) or
            // mid-Foraging-return-countdown (safe timer, no further risk — see ForagingManager). The
            // decay rate depends on activity — see GetEnergyDecayRate(). Sleeping's gain is scaled by the
            // Bedroom's GradeMultiplier — a nicer bed restores energy (and HP, below) faster.
            if (CurrentState == BunnyState.Sleeping)
            {
                float bedroomMultiplier = (claimedSleepRoom != null) ? claimedSleepRoom.GradeMultiplier : 1f;
                float energyBeforeGain = energy;
                energy = Mathf.Min(100f, energy + energyGainPerSecondSleeping * bedroomMultiplier * Time.deltaTime);
                RegenerateHPFromEnergyRestored(energy - energyBeforeGain);
            }
            else if (foragingNeedsFrozen && IsForagingReturnCountdownActive)
            {
                // Return countdown: purely a timer counting down to the walk-in animation, no further
                // decay of any kind.
            }
            else
                energy = Mathf.Max(0f, energy - GetEnergyDecayRate() * Time.deltaTime);

            // Slow always-on regen — see hpPassiveRegenPercentPerSecond's own comment for why Sleeping
            // (has its own stronger formula above) and Fainted (must stay at 0 until the battle-end
            // revive) are excluded.
            if (CurrentState != BunnyState.Sleeping && CurrentState != BunnyState.Fainted)
                RegeneratePassiveHP();
        }

        switch (CurrentState)
        {
            case BunnyState.Idle:
                HandleIdle();
                break;

            case BunnyState.MovingToSpot:
                HandleMovingToSpot();
                break;

            case BunnyState.Working:
                HandleNeedsCheckWhileWorking();
                break;

            case BunnyState.Defending:
                HandleDefending();
                break;

            // No-op — see BunnyState.Fainted's own comment. The bunny just sits at its CombatSpot until
            // StopDefendingAndReturn revives and recalls it once the invasion clears.
            case BunnyState.Fainted:
                break;

            case BunnyState.Recovering:
                HandleRecovering();
                break;

            case BunnyState.Relaxing:
                if (assignedJobRoom != null && claimedWorkSpot == null)
                    RequestNewJobSpot();
                else
                    HandleNeedsCheckWhileRelaxing();
                break;

            // A job assignment never interrupts Sleeping (unlike Relaxing, above) — no job check here
            // at all. Only a critical hunger/thirst threshold or energy reaching 100 ends a sleep.
            case BunnyState.Sleeping:
                if (energy >= 100f)
                    FinishSleepingAndReturnToPrevious();
                else
                    HandleCriticalNeedsCheckWhileSleeping();
                break;

            case BunnyState.Eating:
            case BunnyState.Drinking:
                // Eating/Drinking logic is driven by CafeteriaRoom's/WaterRoom's own coroutine/timer, not here
                break;

            case BunnyState.WaitingForLift:
            case BunnyState.RidingLift:
                // Both driven externally by LiftRoom's own state machine, not here
                break;
        }

        // Ticked after the switch so a state transition that happens this same frame (e.g. Idle ->
        // MovingToSpot the instant a relax spot is claimed) is reflected immediately rather than a
        // frame late. Gated on HasEnteredBase for the same reason as the needs block above — otherwise
        // TickMood()'s idle-pacing-decay default case would drain a queued bunny's Mood too.
        if (HasEnteredBase)
            TickMood();

        UpdateAnimator();
        SyncProximityAmbient();
    }

    // Called every frame from Update(), same as UpdateAnimator — recomputed from CurrentState directly
    // rather than at each individual state-transition call site, since Eating/Drinking/Sleeping can each
    // be entered/exited from several different places (normal finish, critical-need interrupt chains,
    // room merge/upgrade resettle) and this way there's exactly one place that can ever get it wrong.
    private void SyncProximityAmbient()
    {
        SyncProximityAmbientRoom(ref proximityEatingRoom, CurrentState == BunnyState.Eating ? cafeteriaBeingUsed : null, eatingAmbientClip, "Eating");
        SyncProximityAmbientRoom(ref proximityDrinkingRoom, CurrentState == BunnyState.Drinking ? waterRoomBeingUsed : null, drinkingAmbientClip, "Drinking");
        SyncProximityAmbientRoom(ref proximitySleepingRoom, CurrentState == BunnyState.Sleeping ? claimedSleepRoom : null, sleepingAmbientClip, "Sleeping");
    }

    // `category` disambiguates the AudioManager proximity-loop key from any OTHER concern keyed on the
    // same room instance — e.g. a WaterRoom is also keyed as (room, "Work") for its own production
    // ambient (see WaterRoom.NotifyBunnyReadyToWork). Without the category, a WaterRoom with active
    // workers AND a bunny drinking would collide onto the same ref-counted AudioSource/clip.
    private void SyncProximityAmbientRoom<T>(ref T registeredRoom, T desiredRoom, AudioClip clip, string category) where T : Component
    {
        if (registeredRoom == desiredRoom) return;

        if (registeredRoom != null)
            AudioManager.EnsureInstance().DecrementProximityLoop((registeredRoom, category));
        if (desiredRoom != null)
            AudioManager.EnsureInstance().IncrementProximityLoop((desiredRoom, category), clip, desiredRoom.transform, AudioCategory.BunnyNoise);

        registeredRoom = desiredRoom;
    }

    private void OnDestroy()
    {
        // Mirrors SyncProximityAmbientRoom's decrement — a bunny destroyed mid-Eating/Drinking/Sleeping
        // (deleted/despawned) would otherwise leak a ref count and leave that room's ambient loop stuck on.
        if (proximityEatingRoom != null) AudioManager.EnsureInstance().DecrementProximityLoop((proximityEatingRoom, "Eating"));
        if (proximityDrinkingRoom != null) AudioManager.EnsureInstance().DecrementProximityLoop((proximityDrinkingRoom, "Drinking"));
        if (proximitySleepingRoom != null) AudioManager.EnsureInstance().DecrementProximityLoop((proximitySleepingRoom, "Sleeping"));
    }

    // Passive decay applies in every state except Sleeping (handled separately in Update()). Working
    // drains faster than passive; Questing/Foraging will too once those states exist — their rate
    // fields already exist for tuning ahead of time, this switch is the one-line extension point for
    // wiring them in once the states themselves are added. Working's rate is authored per-ROOM (see
    // RoomBase.WorkerEnergyDecayPerSecond) rather than this flat field, so different job rooms (e.g. a
    // Coal Room vs. a Garden) can drain energy at different rates; the flat field is kept only as a
    // defensive fallback in case assignedJobRoom somehow isn't a RoomBase (shouldn't happen in practice
    // — every IJobRoom implementer is also a RoomBase).
    private float GetEnergyDecayRate()
    {
        switch (CurrentState)
        {
            case BunnyState.Working:
                return (assignedJobRoom is RoomBase jobRoomBase) ? jobRoomBase.WorkerEnergyDecayPerSecond : energyDecayPerSecondWorking;
            // case BunnyState.Questing: return energyDecayPerSecondQuesting;
            case BunnyState.Foraging: return energyDecayPerSecondForaging;
            default:
                return energyDecayPerSecond;
        }
    }

    // Mood is a pure passive stat: no room, no travel, no interrupt of its own. Ticked once per frame
    // based on whatever CurrentState already resolved to this frame.
    private void TickMood()
    {
        switch (CurrentState)
        {
            case BunnyState.Working:
                {
                    // Per-room rate, same reasoning as GetEnergyDecayRate above.
                    float rate = (assignedJobRoom is RoomBase jobRoomBase) ? jobRoomBase.WorkerMoodDecayPerSecond : moodDecayPerSecondWorking;
                    mood = Mathf.Max(0f, mood - rate * Time.deltaTime);
                }
                break;

            case BunnyState.Relaxing:
                {
                    // Living Room's GradeMultiplier — a nicer relax spot restores mood faster.
                    float multiplier = (claimedRelaxRoom != null) ? claimedRelaxRoom.GradeMultiplier : 1f;
                    mood = Mathf.Min(100f, mood + moodGainPerSecondRelaxing * multiplier * Time.deltaTime);
                }
                break;

            case BunnyState.Eating:
                {
                    // Cafeteria/Kitchen's GradeMultiplier — same field also scales hunger gain, see
                    // ReceiveCarrotNutrition().
                    float multiplier = (cafeteriaBeingUsed != null) ? cafeteriaBeingUsed.GradeMultiplier : 1f;
                    mood = Mathf.Min(100f, mood + moodGainPerSecondEatingOrDrinking * multiplier * Time.deltaTime);
                }
                break;

            case BunnyState.Drinking:
                {
                    // Water Room's GradeMultiplier — same field also scales thirst gain, see
                    // ReceiveWaterHydration().
                    float multiplier = (waterRoomBeingUsed != null) ? waterRoomBeingUsed.GradeMultiplier : 1f;
                    mood = Mathf.Min(100f, mood + moodGainPerSecondEatingOrDrinking * multiplier * Time.deltaTime);
                }
                break;

            case BunnyState.Sleeping:
                {
                    // Bedroom's GradeMultiplier — same field also scales energy gain, see Update() above.
                    float multiplier = (claimedSleepRoom != null) ? claimedSleepRoom.GradeMultiplier : 1f;
                    mood = Mathf.Min(100f, mood + moodGainPerSecondSleeping * multiplier * Time.deltaTime);
                }
                break;

            // Mood is entirely frozen for the whole Foraging trip (see the Update() needs-freeze block
            // above) — an explicit no-op case rather than falling through to default, so a future change
            // to IsIdlePacingWithoutRelaxSpot/IsIdledByRoomShutdown can never accidentally start draining
            // a foraging bunny's Mood.
            case BunnyState.Foraging:
                break;

            default:
                // Idled by a room shutdown (lost Power or Water) rather than genuinely having nothing to
                // do — treat it like a break for them, same regen as Relaxing, instead of the idle-pacing
                // penalty below.
                if (IsIdledByRoomShutdown())
                    mood = Mathf.Min(100f, mood + moodGainPerSecondRelaxing * Time.deltaTime);
                else if (IsIdlePacingWithoutRelaxSpot())
                    mood = Mathf.Max(0f, mood - moodDecayPerSecondIdlePacing * Time.deltaTime);
                break;
        }
    }

    // True exactly for the signature ForceIdleDueToRoomShutdown/ResumeWorkAfterRoomRestored produce: a
    // bunny still assigned to (and still holding a claimed spot in) its job room, but parked Idle rather
    // than Working because the room itself went dark. No dedicated flag is needed to distinguish this
    // from any other idling — HandleIdle already no-ops on exactly this combination (assignedJobRoom !=
    // null && claimedWorkSpot != null while Idle just returns), and no other code path produces it.
    private bool IsIdledByRoomShutdown()
    {
        return CurrentState == BunnyState.Idle && assignedJobRoom != null && claimedWorkSpot != null;
    }

    // True whenever the bunny has no job and no claimed relax spot, and is either parked Idle waiting
    // for one to free up, or mid-pace between wander points while it waits (see PickNewWanderDestination
    // / IsWanderPacedLeg) — the two states control flow actually visits while no Living Room anywhere
    // has an open spot.
    private bool IsIdlePacingWithoutRelaxSpot()
    {
        if (assignedJobRoom != null) return false;
        if (claimedRelaxSpot != null) return false;
        if (CurrentState == BunnyState.Idle) return true;
        if (CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg()) return true;
        return false;
    }

    // ---------- JOB ASSIGNMENT ----------

    public void AssignToJob(IJobRoom jobRoom)
    {
        if (!HasEnteredBase)
        {
            // Still spawned-but-queued at the gate (or mid-approval) — currentRoom isn't set yet, so
            // routing to a job would path straight from the base entrance to the job room, skipping
            // the gate/queue entirely. Guarded here (not just in the assignment UI) so no caller —
            // present or future — can trigger this by routing around the UI.
            Debug.LogWarning($"{name}: can't assign to a job before entering the base (still awaiting gate approval).");
            return;
        }

        assignedJobRoom = jobRoom;
        RequestNewJobSpot();
    }

    // Used by RoomBase.CanBeDeleted (via DwellerRoster) to check whether a room is safe to demolish —
    // covers physically standing/wandering/working/eating there (currentRoom), being assigned there but
    // still mid-transit toward it (assignedJobRoom, e.g. WaitingForLift/RidingLift/walking), and actively
    // walking toward it as a wander destination but not arrived yet (pendingWanderRoom for a same-floor
    // walk, pendingFinalRoom for a wander OR job trip currently in progress via a lift) — currentRoom is
    // deliberately NOT committed until actual arrival (see OnArrivedAtWanderPoint/ResumeTripAfterLift),
    // so without these a bunny visibly walking into/through a room wouldn't count as occupying it yet.
    public void UnassignFromJob()
    {
        if (assignedJobRoom == null) return;

        // Release the reserved spot. In practice claimedWorkSpot is always set the moment a job is
        // actively pursued (RequestNewJobSpot claims it via assignedJobRoom.RequestSpot before any
        // movement starts), so this covers "currently working" AND "still walking toward the spot."
        // The null check is just a defensive guard.
        if (claimedWorkSpot != null)
        {
            assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
            claimedWorkSpot = null;
        }

        assignedJobRoom = null;

        // wanderTimer accumulates only while Idle and is never reset except when a wander pick actually
        // fires — so a bunny that sat idle for a while before ITS FIRST job (e.g. right after entering
        // the base) can carry a near-threshold timer indefinitely, dormant through Working/Eating, ready
        // to fire on the very next Idle tick no matter how much later that is. Without this reset, going
        // Idle here can trigger an immediate random (possibly cross-floor) wander pick that wins a race
        // against the player's very next reassignment click — the bunny visibly detours through a whole
        // extra lift trip before the real reassignment gets its turn (it's only deferred, not lost — see
        // HandleIdle's assignedJobRoom != null retry — but looks like a broken/random path in the
        // meantime). Resetting here guarantees a full wanderPauseDuration grace window after any
        // unassign before wandering can resume.
        wanderTimer = 0f;

        // Currently working: stop immediately and go idle right where we're standing.
        if (CurrentState == BunnyState.Working)
        {
            CurrentState = BunnyState.Idle;
        }
        // Still walking toward the (now-released) work spot: let the bunny finish that walk rather
        // than snapping it to a mid-path position, but arrive Idle instead of starting work.
        else if (CurrentState == BunnyState.MovingToSpot && pendingStateOnArrival == BunnyState.Working)
        {
            pendingStateOnArrival = BunnyState.Idle;
        }
        // Eating, or mid-lift-trip toward the job: deliberately left alone. FinishEatingAndReturnToWork
        // and ResumeTripAfterLift both already check assignedJobRoom == null and fall back to
        // wandering/idle on their own once that leg finishes.
    }

    // Called by a job room's OnRoomShutdown (IJobRoom) when it loses Power or Water while this bunny is
    // actively Working there. Deliberately NOT routed through ReturnToPreviousActivity — that method
    // always re-paths the bunny back to its spot, which every OTHER interrupt needs because it
    // physically walks the bunny away first. A room shutdown never moves the bunny: claimedWorkSpot/
    // currentRoom/currentSpot are untouched, only CurrentState flips, so the bunny just stands still —
    // still correctly counted as occupying the room (RoomBase.CanBeDeleted's occupancy check isn't
    // affected) — until ResumeWorkAfterRoomRestored is called.
    public void ForceIdleDueToRoomShutdown()
    {
        if (CurrentState != BunnyState.Working) return;
        CurrentState = BunnyState.Idle;
    }

    // Called by the same job room's OnRoomRestored once it's operational again, for everyone it idled.
    // Only actually resumes if the bunny is still exactly where ForceIdleDueToRoomShutdown left it (Idle,
    // job claim intact, per IsIdledByRoomShutdown) — a bunny can be added to a room's idledByShutdown
    // list at shutdown time and then move on to something else entirely before restore happens (e.g. an
    // injured worker sent to the Hospital mid-shutdown: AssignToHospitalBed deliberately leaves
    // assignedJobRoom/claimedWorkSpot untouched, so without this guard, the ORIGINAL job room coming back
    // online would forcibly yank CurrentState back to Working — visibly interrupting Recovering and
    // silently halting HospitalRoom.HealPatientsRoutine's healing, confirmed as a real bug in testing).
    public void ResumeWorkAfterRoomRestored()
    {
        if (!IsIdledByRoomShutdown()) return;
        CurrentState = BunnyState.Working;
        assignedJobRoom.NotifyBunnyReadyToWork(this);
    }

    public bool IsAssociatedWithRoom(RoomBase room)
    {
        if (currentRoom == room) return true;
        if (assignedJobRoom is RoomBase jobRoomBase && jobRoomBase == room) return true;
        if (pendingWanderRoom == room) return true;
        if (pendingFinalRoom == room) return true;
        if (claimedRelaxRoom == room) return true;
        if (claimedSleepRoom == room) return true;
        return false;
    }

    // ---------- ROOM TRANSITION (MERGE/UPGRADE) ----------
    // Supports RoomTransitionService's Evacuate -> Swap -> Resettle coroutine. See
    // RoomMergeUpgrade_DesignDoc.md at the project root for the full design.

    // Narrower than IsAssociatedWithRoom, above — that method covers everything from "settled and
    // working here" to "still walking toward it," which is exactly what's needed to gather WHO to
    // evacuate. This one instead answers "is this bunny mid-flight into/through the room right now, in
    // a state with no clean claim to detach?" — used to make RoomTransitionService's quiescence wait
    // defer until every associated bunny has settled into an actual claimed RoomSpot (or dropped back
    // to plain Idle), so Evacuate never has to interrupt a walk or a lift trip in progress.
    public bool IsTransientlyInRoom(RoomBase room)
    {
        bool midLiftTrip = (CurrentState == BunnyState.WaitingForLift
            || CurrentState == BunnyState.RidingLift
            || CurrentState == BunnyState.DisembarkingLift)
            && pendingFinalRoom == room;

        if (midLiftTrip) return true;

        if (CurrentState == BunnyState.MovingToSpot)
        {
            if (pendingFinalRoom == room) return true;
            if (pendingWanderRoom == room) return true;

            RoomBase directTarget = currentTargetSpot != null ? currentTargetSpot.GetComponentInParent<RoomBase>() : null;
            if (directTarget == room) return true;
        }

        if (CurrentState == BunnyState.Eating && cafeteriaBeingUsed == room) return true;
        if (CurrentState == BunnyState.Drinking && waterRoomBeingUsed == room) return true;

        // Idle-pacing-without-a-claim: standing in the room on a raw wander-point Transform, with no
        // job/relax/sleep claim at all to cleanly release. Rare now that base-wide wandering is gone,
        // but still possible for a beat between losing a job and claiming a relax spot.
        if (CurrentState == BunnyState.Idle && currentRoom == room && currentSpot == null
            && assignedJobRoom == null && claimedRelaxSpot == null && claimedSleepSpot == null)
            return true;

        return false;
    }

    // True if the bunny is either literally in needState right now, mid-walk toward it (MovingToSpot with
    // pendingStateOnArrival == needState), or mid-lift-trip toward it (WaitingForLift/RidingLift/
    // DisembarkingLift with pendingFinalState == needState — NOT pendingStateOnArrival, which gets
    // overwritten with each individual lift-leg's own arrival state as the trip progresses; the ultimate
    // destination activity survives the whole trip only in pendingFinalState, see TryBeginCrossFloorTripToSpot).
    // Used by EvacuateForRoomTransition below to recognize "away and guaranteed to self-resolve," since
    // arriving at any of these leads to a dedicated finish callback (FinishEatingAndReturnToWork /
    // FinishDrinkingAndReturnToPrevious / waking) that routes back through ReturnToPreviousActivity.
    private bool IsHeadedToOrCurrentlyInState(BunnyState needState)
    {
        if (CurrentState == needState) return true;
        if (CurrentState == BunnyState.MovingToSpot && pendingStateOnArrival == needState) return true;
        if ((CurrentState == BunnyState.WaitingForLift || CurrentState == BunnyState.RidingLift || CurrentState == BunnyState.DisembarkingLift)
            && pendingFinalState == needState) return true;
        return false;
    }

    // Called by RoomTransitionService once quiescence is confirmed, for every bunny where
    // IsAssociatedWithRoom is true for one of the rooms being replaced. Releases whichever claim ties
    // this bunny to one of those rooms (at most one of the three checks below can match, since a bunny
    // can only hold one of a job/relax/sleep claim at a time) and reports which one, so the caller knows
    // whether a resettle call is needed afterward. Deliberately does NOT touch currentRoom/currentSpot —
    // see RelocateToRoomAfterTransition for that, which must run separately once the replacement room
    // actually exists.
    //
    // Working/Sleeping split into a physically-present and an "Away" variant. Physical presence is judged
    // by CurrentState, NOT currentRoom — currentRoom is deliberately not committed until actual arrival
    // (see IsAssociatedWithRoom's own doc comment), so a bunny who just started walking away to go
    // Eat/Drink still has currentRoom pointing at the room they're leaving right up until they arrive
    // somewhere else. Checking currentRoom here would misclassify that bunny as still physically present
    // (this was tried and confirmed wrong against a real playtest log: a bunny mid-walk away from her job
    // to go eat still got fully unassigned instead of held, because currentRoom hadn't moved yet).
    // "Away" additionally requires CurrentState to be genuinely Eating/Drinking/Sleeping OR headed toward
    // one of those (via IsHeadedToOrCurrentlyInState, above) — states with a dedicated "finish" callback
    // guaranteed to route back through ReturnToPreviousActivity on its own, since hunger/thirst/tiredness
    // interrupts all keep the original claim reserved rather than releasing it (see LeaveWorkForCafeteria
    // and friends). Anything else away from the room (e.g. Idle after some other bail, or MovingToSpot on
    // an unrelated errand) has no such guaranteed callback to pick a held claim back up, so it falls back
    // to the old unassign-then-Idle-then-reassign path, which HandleIdle's own retry already covers.
    // "Away" bunnies keep their claim pending (just the now-doomed spot on the OLD room instance is
    // released) so Resettle can immediately hold an equivalent spot on the NEW room for them — see
    // TryHoldJobSpotOnNewRoom/TryHoldSleepSpotOnNewRoom — letting them walk straight there once their trip
    // finishes instead of detouring through Idle/a Living Room and (for jobs) flickering "unassigned" in
    // the Assignment Menu in the meantime.
    public RoomTransitionRole EvacuateForRoomTransition(List<RoomBase> rooms)
    {
        if (assignedJobRoom is RoomBase jobRoomBase && rooms.Contains(jobRoomBase))
        {
            bool physicallyWorking = CurrentState == BunnyState.Working && currentRoom == jobRoomBase;
            bool away = !physicallyWorking
                && (IsHeadedToOrCurrentlyInState(BunnyState.Eating)
                    || IsHeadedToOrCurrentlyInState(BunnyState.Drinking)
                    || IsHeadedToOrCurrentlyInState(BunnyState.Sleeping));

            if (!away)
            {
                UnassignFromJob();
                return RoomTransitionRole.Working;
            }

            if (claimedWorkSpot != null)
            {
                assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
                claimedWorkSpot = null;
            }
            assignedJobRoom = null;
            return RoomTransitionRole.WorkingAway;
        }

        if (claimedRelaxRoom != null && rooms.Contains(claimedRelaxRoom))
        {
            claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
            claimedRelaxSpot = null;
            claimedRelaxRoom = null;
            if (CurrentState == BunnyState.Relaxing)
                CurrentState = BunnyState.Idle;
            return RoomTransitionRole.Relaxing;
        }

        if (claimedSleepRoom != null && rooms.Contains(claimedSleepRoom))
        {
            bool physicallySleeping = CurrentState == BunnyState.Sleeping && currentRoom == claimedSleepRoom;
            // Sleeping role can only be "away" via Eating/Drinking (a critical-need wake) — CurrentState
            // can never be/head-toward Sleeping while away from claimedSleepRoom in the first place.
            bool away = !physicallySleeping
                && (IsHeadedToOrCurrentlyInState(BunnyState.Eating) || IsHeadedToOrCurrentlyInState(BunnyState.Drinking));

            if (!away)
            {
                claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
                if (CurrentState == BunnyState.Sleeping)
                    CurrentState = BunnyState.Idle;
                return RoomTransitionRole.Sleeping;
            }

            claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);
            claimedSleepSpot = null;
            claimedSleepRoom = null;
            return RoomTransitionRole.SleepingAway;
        }

        return RoomTransitionRole.None;
    }

    // Called by RoomTransitionService right after the replacement room is instantiated, for every
    // evacuated bunny, BEFORE any resettle call. Only actually relocates if this bunny was PHYSICALLY
    // standing in one of the old rooms (currentRoom is one of oldRooms) — a Working/Sleeping evacuee can
    // be evicted while off eating/drinking at a completely different room (their claim pointed at the
    // room being replaced, but currentRoom already correctly points at wherever they actually are, e.g.
    // via OnArrivedAtEatingSpot), and that valid pointer must NOT be overwritten. For the case that DOES
    // apply: the bunny's transform.position never moves during a merge/upgrade (the new room is built at
    // the same world location the old one(s) occupied), but currentRoom/currentSpot would otherwise keep
    // pointing at the just-destroyed old room — and the next path-building call
    // (BaseLayoutManager.GetRouteToSpot) treats a null startRoom as "arriving from the base entrance,"
    // which would route a resettling bunny across the entire base instead of the short hop it actually
    // needs. currentSpot is cleared since it doesn't survive the swap (the old spot's RoomSpot is gone).
    // currentWanderPoint is set to this bunny's own transform, NOT cleared — GetRouteToSpot's same-room
    // fallback (no known spot AND no wander point) resorts to the room's own root/pivot Transform, which
    // is a grid-alignment anchor floating outside the walkable floor, not an actual position; visually
    // that sent resettling bunnies on a detour up and out of the room before walking back in. The bunny's
    // own transform is always exactly where they're really standing (it never moves during the swap), so
    // using it here keeps the eventual path starting from the truth instead of a fake anchor point.
    public void RelocateToRoomAfterTransition(RoomBase newRoom, List<RoomBase> oldRooms)
    {
        if (currentRoom == null || !oldRooms.Contains(currentRoom)) return;

        currentRoom = newRoom;
        currentSpot = null;
        currentWanderPoint = transform;
        currentFloorIndex = newRoom.FloorIndex;
    }

    // Called by RoomTransitionService's Resettle step for a Working-role evacuee. Unlike AssignToJob
    // (called directly by every other reassignment path in the game, e.g. AssignmentUI), this defers
    // until the bunny reaches a state where a job hand-off can't corrupt something already in flight —
    // specifically not mid-walk toward a spot and not mid-Eating/Drinking, both of which are driven by
    // their own external coroutines (e.g. CafeteriaRoom.EatingRoutine) that have no idea CurrentState
    // was just hijacked out from under them. This case is reachable here in a way it normally isn't:
    // Evacuate can release a Working bunny's job claim while they're off eating/drinking at a completely
    // different room (assignedJobRoom pointed at the room being replaced, but they were never physically
    // there), so RequestNewJobSpot's existing IsHeadedToRelaxSpot/IsAsleepOrHeadedToSleepSpot guards
    // don't cover it. Idle and Relaxing are both safe hand-off points — Relaxing already supports exactly
    // this interruption via its own Update() case (NPCBunny.cs's BunnyState.Relaxing branch).
    public void ResettleJobWhenSafe(IJobRoom newJobRoom)
    {
        StartCoroutine(ResettleJobWhenSafeRoutine(newJobRoom));
    }

    private IEnumerator ResettleJobWhenSafeRoutine(IJobRoom newJobRoom)
    {
        while (CurrentState != BunnyState.Idle && CurrentState != BunnyState.Relaxing)
            yield return null;

        AssignToJob(newJobRoom);
    }

    // Called by RoomTransitionService's Resettle step for a WorkingAway evacuee (job claim was in a
    // room being replaced, but the bunny is off Eating/Drinking/asleep elsewhere, not physically there).
    // Claims a spot on the replacement room and re-points assignedJobRoom/claimedWorkSpot directly —
    // deliberately NOT via AssignToJob/RequestNewJobSpot, which would call MoveAlongPath immediately and
    // yank the bunny out of whatever they're currently mid-way through. Once their trip actually
    // finishes, FinishEatingAndReturnToWork/FinishDrinkingAndReturnToPrevious -> ReturnToPreviousActivity
    // finds assignedJobRoom/claimedWorkSpot already set and walks straight there — the same "spot was
    // never released while eating" path already used for an ordinary (non-transition) eating trip, so no
    // Idle/Living-Room detour and no "unassigned" flicker in the Assignment Menu. Returns false if the
    // new room has no spot free right now (shouldn't normally happen — same or greater capacity than the
    // room(s) it replaced); the caller falls back to ResettleJobWhenSafe in that case.
    public bool TryHoldJobSpotOnNewRoom(IJobRoom newJobRoom)
    {
        RoomSpot spot = newJobRoom.RequestSpot(this);
        if (spot == null) return false;

        assignedJobRoom = newJobRoom;
        claimedWorkSpot = spot;
        return true;
    }

    // Called by RoomTransitionService's Resettle step for a Sleeping-role evacuee. Only reclaims a
    // bedroom spot if the bunny was genuinely settled asleep — EvacuateForRoomTransition already drops
    // CurrentState straight to Idle for that case, with no yield in between, so it's still true here.
    public void RequestBedroomFromCurrentPosition()
    {
        if (CurrentState == BunnyState.Idle)
            ChainToBedroomFromCurrentSpot();
    }

    // Mirrors TryHoldJobSpotOnNewRoom, above, for a SleepingAway evacuee (sleep claim was in a room being
    // replaced, but the bunny is off Eating/Drinking elsewhere, woken by a critical need). Claims a spot
    // directly on the replacement Bedroom and re-points claimedSleepRoom/claimedSleepSpot without
    // touching CurrentState or starting any movement, so FinishEatingAndReturnToWork's/
    // FinishDrinkingAndReturnToPrevious's ReturnToPreviousActivity finds the sleep claim already pointing
    // at the new room and walks straight back to it — same reasoning as the job case. Returns false if
    // the new room has no spot free right now; the caller falls back to RequestBedroomFromCurrentPosition
    // (which self-resolves to the nearest bed with a spot once tiredness routes them there).
    public bool TryHoldSleepSpotOnNewRoom(Bedroom newBedroom)
    {
        RoomSpot spot = newBedroom.RequestSpot(this);
        if (spot == null) return false;

        claimedSleepRoom = newBedroom;
        claimedSleepSpot = spot;
        return true;
    }

    public string DebugState()
    {
        string jobRoomName = assignedJobRoom is RoomBase jobRoomBase ? jobRoomBase.name : "NULL";
        string finalRoomName = pendingFinalRoom != null ? pendingFinalRoom.name : "NULL";
        string relaxRoomName = claimedRelaxRoom != null ? claimedRelaxRoom.name : "NULL";
        return $"{name}: currentRoom={(currentRoom != null ? currentRoom.name : "NULL")}, currentState={CurrentState}, pendingStateOnArrival={pendingStateOnArrival}, assignedJobRoom={jobRoomName}, pendingWanderRoom={(pendingWanderRoom != null ? pendingWanderRoom.name : "NULL")}, pendingFinalRoom={finalRoomName}, claimedRelaxRoom={relaxRoomName}, currentWaypointTarget={(currentWaypointTarget != null ? currentWaypointTarget.name : "NULL")}, pos={transform.position}";
    }

    public void SetAwaitingApproval(bool value)
    {
        IsAwaitingApproval = value;
    }
    public void SetArrivalType(BunnyArrivalType type)
    {
        ArrivalType = type;
    }
    public void SetIdentity(BunnyGender gender, string name)
    {
        Gender = gender;
        BunnyName = name;
        gameObject.name = name; // keeps Hierarchy/debugging readable too
    }

    // Called once by WildBunnySpawner right after Instantiate, alongside SetIdentity — stores which
    // BunnyTypeDefinition this bunny spawned from (needed later by LevelUp to re-resolve Stats/
    // ActivePassives against the same base stats/passive list) plus the level/stats/traits/passives
    // already resolved for that spawn. See BunnyTypeSystem_DesignDoc.md.
    public void SetTypeAndProgression(BunnyTypeDefinition def, int level, BunnyStats stats,
        List<BunnyTraitDefinition> traits, List<BunnyPassiveDefinition> passives)
    {
        typeDefinition = def;
        Type = def.type;
        TypeIcon = def.icon;
        Level = level;
        Stats = stats;
        Traits = traits;
        ActivePassives = passives;

        ApplyTraitEffects();
        ApplyNatureEffects();

        currentHP = Stats.HP; // full HP at spawn — nothing has spent it yet
    }

    private const int MinIV = 1;
    private const int MaxIV = 32;

    // Called once by WildBunnySpawner, before BunnyStatCalculator.Resolve is called for this bunny (so
    // the rolled IVs are ready in time to feed into it) — mirrors SetIdentity/SetArrivalType, both also
    // called directly on the bunny ahead of SetTypeAndProgression. Nature is rolled here too since it's
    // spawn-time-random the same way, even though its stat EFFECT isn't applied until ApplyNatureEffects
    // (which needs Traits, set later in SetTypeAndProgression, to check for Stoic first).
    public void RollIndividuality()
    {
        Nature = (BunnyNature)Random.Range(0, System.Enum.GetValues(typeof(BunnyNature)).Length);

        IVHP = Random.Range(MinIV, MaxIV + 1);
        IVAttack = Random.Range(MinIV, MaxIV + 1);
        IVDefense = Random.Range(MinIV, MaxIV + 1);
        IVSpeed = Random.Range(MinIV, MaxIV + 1);
        IVLuck = Random.Range(MinIV, MaxIV + 1);
    }

    // Second pass after ApplyTraitEffects, per the design doc's Option B — Stats already holds the
    // pre-Nature values from BunnyStatCalculator.Resolve (NatureMultiplier = 1 baked in); this applies
    // the real +-10%/x1 per Nature's (boosted, lowered) pair, or leaves every stat at x1 if the bunny
    // has a trait with effectType == IgnoresNature (Stoic). Nature/IVs themselves are never touched
    // here, only the resolved Stats — so a future respec mechanic could re-run this against the same
    // Nature later without needing a fresh roll.
    private void ApplyNatureEffects()
    {
        bool ignoresNature = false;
        foreach (BunnyTraitDefinition trait in Traits)
        {
            if (trait.effectType == TraitEffectType.IgnoresNature) { ignoresNature = true; break; }
        }

        if (ignoresNature)
        {
            BoostedStat = null;
            LoweredStat = null;
        }
        else
        {
            BunnyNatureData.GetModifiers(Nature, out NatureStat b, out NatureStat l);
            BoostedStat = b;
            LoweredStat = l;
        }

        BunnyStats s = Stats;
        s.Attack = ApplyNatureMultiplier(s.Attack, NatureStat.Attack, BoostedStat, LoweredStat);
        s.Defense = ApplyNatureMultiplier(s.Defense, NatureStat.Defense, BoostedStat, LoweredStat);
        s.Speed = ApplyNatureMultiplier(s.Speed, NatureStat.Speed, BoostedStat, LoweredStat);
        s.Luck = ApplyNatureMultiplier(s.Luck, NatureStat.Luck, BoostedStat, LoweredStat);
        Stats = s;
    }

    private static int ApplyNatureMultiplier(int preNatureValue, NatureStat stat, NatureStat? boosted, NatureStat? lowered)
    {
        float multiplier = stat == boosted ? 1.1f : (stat == lowered ? 0.9f : 1f);
        return Mathf.Max(1, Mathf.FloorToInt(preNatureValue * multiplier));
    }

    // Proof-of-concept trait effects — see TraitEffectType in BunnyTraitDefinition.cs. Runs once, right
    // after Traits is set, well before HasEnteredBase (needs are frozen until the gate, see Update()),
    // so mutating these instance fields here can never race a decay tick already in progress. Multiplies
    // in place rather than storing a separate "effective rate" — these are this bunny's OWN instance
    // fields (Instantiate gives every spawned bunny its own copy), so there's nothing else to keep in
    // sync. Deliberately re-derives ProductionMultiplier from scratch (not additive onto whatever it was
    // before) so calling this twice on the same bunny — which nothing does today, but LevelUp's future
    // trait-gain hook plausibly could — can't compound the same trait's effect twice.
    private void ApplyTraitEffects()
    {
        ProductionMultiplier = 1f;
        XPGainMultiplier = 1f;

        foreach (BunnyTraitDefinition trait in Traits)
        {
            switch (trait.effectType)
            {
                case TraitEffectType.EnergyDecayMultiplier:
                    energyDecayPerSecond *= trait.effectMultiplier;
                    energyDecayPerSecondWorking *= trait.effectMultiplier;
                    energyDecayPerSecondQuesting *= trait.effectMultiplier;
                    energyDecayPerSecondForaging *= trait.effectMultiplier;
                    break;
                case TraitEffectType.MoodDecayMultiplier:
                    moodDecayPerSecondWorking *= trait.effectMultiplier;
                    moodDecayPerSecondIdlePacing *= trait.effectMultiplier;
                    break;
                case TraitEffectType.HungerDecayMultiplier:
                    hungerDecayPerSecond *= trait.effectMultiplier;
                    break;
                case TraitEffectType.ThirstDecayMultiplier:
                    thirstDecayPerSecond *= trait.effectMultiplier;
                    break;
                case TraitEffectType.MoveSpeedMultiplier:
                    moveSpeed *= trait.effectMultiplier;
                    break;
                case TraitEffectType.ProductionMultiplier:
                    ProductionMultiplier *= trait.effectMultiplier;
                    break;
                case TraitEffectType.RareLootChanceBonus:
                    // Additive, not multiplicative — see ForagingRareLootBonus's own doc comment for why
                    // this case doesn't match the "*=" shape every other case above uses.
                    ForagingRareLootBonus += trait.effectMultiplier;
                    break;
                case TraitEffectType.XPGainMultiplier:
                    XPGainMultiplier *= trait.effectMultiplier;
                    break;
            }
        }
    }

    // Recomputes Stats/ActivePassives at a new level, reusing the exact same resolver functions used at
    // spawn — see BunnyTypeSystem_DesignDoc.md's "Leveling scaffold" section. Traits are deliberately NOT
    // touched here — trait gain is bound to breeding/kid-bunny rules that don't exist yet. currentHP is
    // carried forward by the same delta as max HP (mirrors the familiar "level up mid-battle, current HP
    // rises with the new max" convention) rather than snapping to full — a bunny that just leveled up
    // mid-Foraging-trip shouldn't get a free full heal out of it.
    public void LevelUp(int newLevel)
    {
        if (newLevel <= Level || typeDefinition == null) return;

        int previousMaxHP = Stats.HP;

        Level = newLevel;
        Stats = BunnyStatCalculator.Resolve(typeDefinition, Level,
            IVHP, IVAttack, IVDefense, IVSpeed, IVLuck,
            EVHP, EVAttack, EVDefense, EVSpeed, EVLuck);
        ApplyNatureEffects();
        ActivePassives = BunnyPassiveResolver.ResolvePassives(typeDefinition, Level);

        currentHP = Mathf.Clamp(currentHP + (Stats.HP - previousMaxHP), 1, Stats.HP);
    }

    // First real EV-earning source (Fruits, via ForagingInventoryManager.TryFeedFruit) — MaxTotalEV has
    // sat unenforced on this class since the Bunny Stat System Redesign, waiting for exactly this. Clamps
    // to BOTH the per-stat cap (256) and the total-across-all-5 cap (MaxTotalEV) — granting less than
    // requested rather than overflowing either. Re-resolves Stats the same way LevelUp already does
    // (BunnyStatCalculator.Resolve already divides EV by 4 internally — no separate division needed here).
    // Lets the Fruit Feeding picker (BunnyInfoUI) grey out fruits that would currently grant 0 EV —
    // mirrors the same per-stat (256) / total (MaxTotalEV) caps AddEV enforces, without exposing the
    // private EV fields themselves. See FruitFeeding_DesignDoc.md.
    public bool CanGainEV(BunnyStatType stat)
    {
        if (typeDefinition == null) return false;
        int currentTotal = EVHP + EVAttack + EVDefense + EVSpeed + EVLuck;
        return GetEV(stat) < 256 && currentTotal < MaxTotalEV;
    }

    public void AddEV(BunnyStatType stat, int amount)
    {
        if (amount <= 0 || typeDefinition == null) return;

        int currentTotal = EVHP + EVAttack + EVDefense + EVSpeed + EVLuck;
        int currentStat = GetEV(stat);
        int actualAmount = Mathf.Max(0, Mathf.Min(amount, Mathf.Min(256 - currentStat, MaxTotalEV - currentTotal)));
        if (actualAmount <= 0) return;

        SetEV(stat, currentStat + actualAmount);

        int previousEVMaxHP = Stats.HP;
        Stats = BunnyStatCalculator.Resolve(typeDefinition, Level,
            IVHP, IVAttack, IVDefense, IVSpeed, IVLuck,
            EVHP, EVAttack, EVDefense, EVSpeed, EVLuck);
        ApplyNatureEffects();
        currentHP = Mathf.Clamp(currentHP + (Stats.HP - previousEVMaxHP), 1, Stats.HP);
    }

    private int GetEV(BunnyStatType stat)
    {
        switch (stat)
        {
            case BunnyStatType.HP: return EVHP;
            case BunnyStatType.Attack: return EVAttack;
            case BunnyStatType.Defense: return EVDefense;
            case BunnyStatType.Speed: return EVSpeed;
            default: return EVLuck;
        }
    }

    private void SetEV(BunnyStatType stat, int value)
    {
        switch (stat)
        {
            case BunnyStatType.HP: EVHP = value; break;
            case BunnyStatType.Attack: EVAttack = value; break;
            case BunnyStatType.Defense: EVDefense = value; break;
            case BunnyStatType.Speed: EVSpeed = value; break;
            default: EVLuck = value; break;
        }
    }

    // Single entry point for XP from EVERY source (Foraging today, Work Rooms too — see
    // WorkRoomXP_DesignDoc.md — and whatever comes later). `experience` is a running CUMULATIVE total
    // (never reset per level), checked against BunnyLevelCurve's cumulative-XP-per-level curve; LevelUp
    // fires as many times as the new total justifies (covers a big single grant crossing more than one
    // level at once), capped at level 50 to match WildBunnySpawner.RollSpawnLevel's own clamp.
    //
    // XPGainMultiplier (Smart/Dumb traits) is applied HERE, once, to `amount` itself — deliberately NOT
    // left for each caller to apply on their own end. That makes it universal automatically: every
    // existing and future XP source funnels through this one method, so a future XP source can't
    // forget to apply it the way a per-caller convention could. Callers (ForagingManager,
    // RoomBase.GrantWorkXPRoutine) pass their own raw, pre-multiplier amount.
    public void AddExperience(float amount)
    {
        int levelBefore = Level;
        experience += amount * XPGainMultiplier;

        while (Level < 50 && experience >= BunnyLevelCurve.CumulativeXPForLevel(Level + 1))
            LevelUp(Level + 1);

        // Fires once for this whole grant, not once per LevelUp() call above — a single big grant can
        // cross several level thresholds at once (see the loop's own comment), and firing the Cheer/SFX
        // that many times in the same frame would stutter rather than read as one satisfying level-up.
        // See WorkRoomXP_DesignDoc.md's "Multi-level-in-one-grant handling" section.
        if (Level > levelBefore)
        {
            PlayLevelUpFeedback();
            OnLevelUp?.Invoke(Level);
        }
    }

    // XP remaining to the next level, for a future UI progress bar — computed on demand from the curve
    // rather than stored, since `experience` is the only value that actually needs to persist.
    public float ExperienceToNextLevel => Level >= 50 ? 0f : BunnyLevelCurve.CumulativeXPForLevel(Level + 1) - experience;

    // Cheer animation + SFX — see WorkRoomXP_DesignDoc.md's "Level-Up Feedback" section. Deliberately
    // never touches CurrentState (only a one-shot Animator Trigger, see isCheeringParam's own comment)
    // so no job-room production/XP coroutine (which self-terminates on
    // `CurrentState != BunnyState.Working`) can ever mistake a level-up cheer for the bunny quitting its
    // job. Two states are deliberately suppressed here (Ethan's calls, both confirmed unreachable by any
    // code today but built now for a hypothetical future passive/accessory XP-over-time source):
    //   - Transit states: a bunny sliding along a path shouldn't visibly/audibly interrupt to jump —
    //     both animation and SFX suppressed together, no special-casing, since nothing grants XP
    //     mid-transit today anyway (e.g. riding a lift doesn't earn XP).
    //   - BunnyState.Foraging: parked off-screen at the staging point for the whole trip, so there's no
    //     visible/audible bunny for the cheer to attach to — suppressed here too, but OnLevelUp still
    //     fires above regardless of state, which is what lets ForagingManager log a bold trip-log entry
    //     instead (see its own subscription in RunDispatchRoutine).
    private void PlayLevelUpFeedback()
    {
        if (IsTransitStateForLevelUpFeedback(CurrentState) || CurrentState == BunnyState.Foraging) return;

        if (animator != null) animator.SetTrigger(isCheeringParam);
        if (levelUpClip != null) AudioManager.EnsureInstance().PlaySFXAtPosition(levelUpClip, transform.position);
    }

    private static bool IsTransitStateForLevelUpFeedback(BunnyState state)
    {
        return state == BunnyState.MovingToSpot || state == BunnyState.PassingGate
            || state == BunnyState.DepartingThroughGate || state == BunnyState.WaitingForLift
            || state == BunnyState.RidingLift || state == BunnyState.DisembarkingLift
            || state == BunnyState.Despawning;
    }

    // ---------- HP (spent by Foraging's placeholder encounter resolver — see ForagingManager) ----------

    // energyRestored is the ACTUAL delta this frame (post the 100-cap clamp at the Sleeping call site),
    // not the nominal rate — matters near the top of the energy bar where the nominal rate would overstate
    // how much was really gained. hpPercentOfMaxPerEnergyRestored already folds in Bedroom.GradeMultiplier
    // by virtue of being derived from the (already-scaled) energy delta.
    private void RegenerateHPFromEnergyRestored(float energyRestored)
    {
        if (energyRestored <= 0f || currentHP >= Stats.HP) return;
        AccumulateHPRegen(energyRestored * hpPercentOfMaxPerEnergyRestored * Stats.HP);
    }

    // Slow always-on regen — see hpPassiveRegenPercentPerSecond's own comment for exclusions. Multiplied
    // by GetPassiveHPRegenMultiplier() so a type's Regrowth-style passive (see BunnyPassiveDefinition)
    // scales this without needing its own separate regen path.
    private void RegeneratePassiveHP()
    {
        if (currentHP >= Stats.HP) return;
        AccumulateHPRegen(hpPassiveRegenPercentPerSecond * Stats.HP * GetPassiveHPRegenMultiplier() * Time.deltaTime);
    }

    // Shared fractional remainder carry-over (hpRegenRemainder) so a sub-1-HP/sec regen rate still
    // eventually grants whole HP once enough has accumulated, instead of Mathf.RoundToInt silently
    // discarding it every frame — energy/mood don't need this since they're floats themselves, but HP is
    // deliberately an int (matches BunnyStats.HP, which nothing has ever needed as a float). Shared by
    // both regen sources above since only one of them ever runs in a given frame (Sleeping vs. not).
    private void AccumulateHPRegen(float amount)
    {
        hpRegenRemainder += amount;
        int wholeHP = Mathf.FloorToInt(hpRegenRemainder);
        if (wholeHP <= 0) return;

        hpRegenRemainder -= wholeHP;
        currentHP = Mathf.Min(Stats.HP, currentHP + wholeHP);
    }

    // Aggregates every currently-unlocked (Level >= unlockLevel) HPRegenMultiplier passive on this
    // bunny's type — e.g. Plant's "Regrowth" (3x, tunable on the BunnyPassiveDefinition asset itself, not
    // hardcoded here). Passives are universal per-TYPE abilities (every bunny of that type has them),
    // deliberately separate from the per-individual Trait system (see TraitEffectType/ApplyTraits) even
    // though the shapes look similar. Multiple stacking passives would multiply together; none exist yet.
    private float GetPassiveHPRegenMultiplier()
    {
        if (typeDefinition == null || typeDefinition.passives == null) return 1f;

        float multiplier = 1f;
        foreach (BunnyPassiveDefinition passive in typeDefinition.passives)
        {
            if (passive == null || passive.effectType != PassiveEffectType.HPRegenMultiplier) continue;
            if (Level < passive.unlockLevel) continue;
            multiplier *= passive.effectMultiplier;
        }
        return multiplier;
    }

    // Called by ForagingManager's placeholder encounter resolver on a loss. Floored at 1, never 0 — see
    // Foraging_DesignDoc.md's "No death from Foraging, ever (this pass)" decision. The caller is
    // responsible for treating HP == 1 with no potions left as an auto-return trigger; this method only
    // guarantees the floor itself.
    public void ApplyForagingDamage(int amount)
    {
        currentHP = Mathf.Max(1, currentHP - Mathf.Max(0, amount));
    }

    // Called by ForagingManager when a carried healing potion auto-uses, and by the Bunny UI's potion
    // button (see BunnyInfoUI). No-ops while Fainted — a fainted bunny only revives via
    // StopDefendingAndReturn's battle-end flow, never mid-fight; otherwise a potion would push currentHP
    // off 0 (clamped to 1 below) while CurrentState is still Fainted, an inconsistent state where the
    // bunny reads as alive (ICombatant.IsAlive) and can be re-targeted, but still can't act back since
    // nothing transitions it out of Fainted except the battle-end recall.
    public void HealHP(int amount)
    {
        if (CurrentState == BunnyState.Fainted) return;
        currentHP = Mathf.Clamp(currentHP + Mathf.Max(0, amount), 1, Stats.HP);
    }

    // Called by GuardRoom on assignment to a Grade 3+ post — grants the shield at full, no ramp-up.
    // Overwrites whatever shield state existed before (there's no stacking/accumulation across
    // re-assignment — see the header comment on currentShieldHP/maxShieldHP).
    public void GrantShield(int amount)
    {
        maxShieldHP = Mathf.Max(0, amount);
        currentShieldHP = maxShieldHP;
    }

    // Called by GuardRoom on unassignment (or shutdown) — full reset, no partial persistence.
    public void ClearShield()
    {
        maxShieldHP = 0;
        currentShieldHP = 0;
    }

    // ---------- ICombatant (see Combat_DesignDoc.md) ----------
    // Deliberately separate from ApplyForagingDamage above — that method floors at 1 by design (Foraging
    // never kills, per its own design doc), whereas real combat damage can and does reach 0. Fainting
    // (BunnyState.Fainted) is the state-machine reaction to OnDefeated — see TakeCombatDamage/
    // StopDefendingAndReturn.
    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    BunnyTypeDefinition ICombatant.AttackSource => typeDefinition;
    Transform ICombatant.CombatTransform => transform;
    // `this == null` (Unity's real destroyed-object check) rather than unconditionally dereferencing
    // gameObject — see EnemyInstance's identical fix for why the naive version throws instead of
    // returning the "fake null" every caller checking CombatGameObject == null expects.
    GameObject ICombatant.CombatGameObject => this == null ? null : gameObject;
    // attackOriginOffset is authored assuming the bunny faces right (see the field's tooltip) — X must
    // mirror whenever the bunny is actually facing/oriented the other way, or the offset point stays on
    // the sprite's original-facing side and ends up behind its head once it turns. Reads the LIVE visual
    // state directly (bunnyScaleRoot's own scale sign, plus this transform's actual rotation) rather than
    // the separately-tracked facingRight flag — facingRight only updates through SetFacing's normal
    // scale-flip path, so it wouldn't reflect e.g. manually rotating this GameObject's Y in the Inspector
    // to preview the other facing, which transform.rotation picks up automatically.
    Vector3 ICombatant.AttackOrigin
    {
        get
        {
            Vector3 offset = attackOriginOffset;
            if (bunnyScaleRoot != null && bunnyScaleRoot.localScale.x < 0f)
                offset.x = -offset.x;
            return transform.position + transform.rotation * offset;
        }
    }
    Vector3 ICombatant.VisualCenter => CombatEngagement.ComputeVisualCenter(visualRenderers, transform.position);

    public event System.Action OnDefeated;

    public void TakeCombatDamage(int amount)
    {
        if (currentHP <= 0) return; // already fainted — no further hits matter until StopDefendingAndReturn revives it

        // Grade 3 Guard Room shield — a separate absorb pool, not extra max HP, so it reads as its own
        // thing ("37/50 shield") and is always the first thing depleted. Granted/cleared wholesale by
        // GuardRoom on assignment/unassignment (see GrantShield/ClearShield) — no regen, no persistence.
        int remaining = Mathf.Max(0, amount);
        if (currentShieldHP > 0)
        {
            int shieldAbsorbed = Mathf.Min(currentShieldHP, remaining);
            currentShieldHP -= shieldAbsorbed;
            remaining -= shieldAbsorbed;
        }

        currentHP = Mathf.Max(0, currentHP - remaining);
        if (currentHP != 0) return;

        OnDefeated?.Invoke();
        CurrentState = BunnyState.Fainted; // UpdateAnimator's per-frame SetBool(isFaintedParam, ...) picks this up next frame

        if (faintFadeRoutine != null) StopCoroutine(faintFadeRoutine);
        faintFadeRoutine = StartCoroutine(FaintFadeRoutine());

        // Release the CombatSpot claim immediately rather than waiting for the battle-end recall — a
        // fainted body would otherwise hog a finite combat slot for the rest of the fight, and if every
        // defender faints, the room deadlocks permanently (no free slot to send a reinforcement into, but
        // nothing left alive to clear the invasion and free one up). defendingSpot/defendingRoom stay set
        // on this bunny (deliberately NOT cleared here) so StopDefendingAndReturn/GetBunniesDefendingRoom
        // still find and revive/recall it once the invasion clears, same as before this change.
        if (defendingRoom != null && defendingSpot != null)
            defendingRoom.ReleaseCombatSpot(defendingSpot, this);

        // Same "release immediately, don't wait for battle-end" reasoning as the CombatSpot above — a
        // fainted melee bunny would otherwise permanently occupy a flank slot on its target for the rest
        // of the fight (and beyond, since nothing else ever clears it).
        if (claimedFlankSlots != null)
        {
            claimedFlankSlots.ReleaseSlot(this);
            claimedFlankSlots = null;
            claimedFlankTarget = null;
        }
    }

    // Fades this bunny's sprite to faintFadeTargetAlpha once it collapses — timed to start at
    // faintFadeDelaySeconds (frame 40 of the Fainting clip @ 60fps by default) rather than tracking the
    // Animator's actual clip time, since Fainted is a persistent Bool-driven state that may loop/hold
    // indefinitely (see isFaintedParam's own comment) and a fade tied to a looping clip would repeat every
    // loop instead of happening once. Cancelled and snapped back to full alpha the instant
    // StopDefendingAndReturn revives this bunny — see its own faintFadeRoutine cleanup.
    private IEnumerator FaintFadeRoutine()
    {
        yield return new WaitForSeconds(faintFadeDelaySeconds);

        float elapsed = 0f;
        while (elapsed < faintFadeDurationSeconds)
        {
            elapsed += Time.deltaTime;
            SetSpriteAlpha(Mathf.Lerp(1f, faintFadeTargetAlpha, elapsed / faintFadeDurationSeconds));
            yield return null;
        }

        SetSpriteAlpha(faintFadeTargetAlpha);
        faintFadeRoutine = null;
    }

    private void SetSpriteAlpha(float alpha)
    {
        if (spriteRenderers == null) return;
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }

    // Set by ForagingManager the instant a trip's return countdown begins (any of the four return
    // triggers fired) and cleared once the bunny arrives home. While true, Energy stops decaying
    // entirely (see the needs-freeze block in Update()) — the countdown is purely a timer to the walk-in
    // animation, not a continuation of the trip's danger.
    public bool IsForagingReturnCountdownActive { get; private set; }
    public void SetForagingReturnCountdownActive(bool value)
    {
        IsForagingReturnCountdownActive = value;
    }

    // Called once by WildBunnySpawner right after spawn, before the bunny enters the gate queue —
    // staggers when different bunnies first cross a "low" need threshold so they don't all go on break
    // at once, especially impactful early game when resource balance is tightest. Each need is rolled
    // independently (not one shared roll applied to all four) so a single bunny's own needs are
    // staggered from each other too. Needs stay frozen for the whole time the bunny is queued/awaiting
    // approval (see the HasEnteredBase gate in Update()), so this starting roll — not decay time spent
    // waiting at the gate — is what determines the stagger.
    public void RandomizeStartingNeeds(float min, float max)
    {
        hunger = Random.Range(min, max);
        thirst = Random.Range(min, max);
        energy = Random.Range(min, max);
        mood = Random.Range(min, max);
    }

    private void RequestNewJobSpot()
    {
        if (assignedJobRoom == null) { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, assignedJobRoom is null."); return; }

        RoomBase jobRoomBase = (RoomBase)assignedJobRoom;

        // A fallback pacing leg never leaves the bunny's current room (see PickNewWanderDestination),
        // so if a job assignment interrupts one, the worst case is a short, bounded, same-room hop.
        // That's cheap enough to just defer: let the current leg finish naturally, and HandleIdle's
        // existing retry (once idle) picks the job back up.
        if (CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg())
        { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, mid wander-paced leg."); return; }

        // A relax trip CAN cross floors via lift, unlike fallback pacing — interrupting it mid-flight
        // would corrupt the in-progress lift bookkeeping (pendingLift/pendingFinalRoom etc. are single-
        // slot fields already in use for that trip). Safer to let it finish naturally; the
        // BunnyState.Relaxing case in Update() immediately pulls the bunny back out for the job once it
        // settles in, at worst a single frame late.
        if (IsHeadedToRelaxSpot())
        { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, IsHeadedToRelaxSpot true (CurrentState={CurrentState})."); return; }

        // Like Sleeping (below), a job assignment never interrupts Eating/Drinking at all — the job is
        // left pending until FinishEatingAndReturnToWork/FinishDrinkingAndReturnToPrevious's
        // ReturnToPreviousActivity picks it back up once satiated.
        if (IsHeadedToOrCurrentlyEatingOrDrinking())
        { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, IsHeadedToOrCurrentlyEatingOrDrinking true (CurrentState={CurrentState})."); return; }

        // Unlike Relaxing (above), a job assignment never interrupts Sleeping at all — the job is left
        // pending (assignedJobRoom set, claimedWorkSpot never claimed) until the bunny actually wakes.
        // Critically, this path never releases claimedSleepSpot: waking for a critical need loops
        // straight back to the same sleep spot regardless of this pending job (see
        // LeaveSleepingForCafeteria/WaterRoom), and only FinishSleepingAndReturnToPrevious (energy
        // reaching 100) clears claimedSleepSpot, at which point ReturnToPreviousActivity's own
        // job-check calls this method again and it proceeds normally.
        if (IsAsleepOrHeadedToSleepSpot())
        { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, IsAsleepOrHeadedToSleepSpot true (CurrentState={CurrentState})."); return; }

        // Pull the bunny out of any already-claimed Living Room spot — a job takes priority. Mirrors
        // UnassignFromJob's claimedWorkSpot release.
        if (claimedRelaxSpot != null)
        {
            claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
            claimedRelaxSpot = null;
            claimedRelaxRoom = null;
        }

        RoomSpot spot = assignedJobRoom.RequestSpot(this);
        if (spot == null)
        { Debug.Log($"[PathDebug] {name} RequestNewJobSpot: bailed, RequestSpot returned null (room full or no spots authored)."); return; }

        claimedWorkSpot = spot;

        Debug.Log($"[PathDebug] {name} RequestNewJobSpot: claimed {spot.name}, currentRoom={(currentRoom != null ? currentRoom.name : "NULL")}, currentFloorIndex={currentFloorIndex}, jobRoomBase.FloorIndex={jobRoomBase.FloorIndex}");

        // currentFloorIndex alone decides cross-floor vs. same-floor — NOT currentRoom's nullness.
        // currentRoom can legitimately be null (BaseLayoutManager's routing treats that as "start from
        // the base entrance," used deliberately by the room-transition system) while currentFloorIndex
        // still correctly reflects a different floor. Requiring currentRoom != null here used to let a
        // genuinely cross-floor request silently fall through to the same-floor path below, which has no
        // concept of floors/lifts at all and produced a straight-line walk between waypoints on different
        // Y-levels (wall/floor clipping) instead of a proper lift trip.
        if (currentFloorIndex != jobRoomBase.FloorIndex)
        {
            bool started = TryBeginCrossFloorTripToSpot(jobRoomBase, spot, BunnyState.Working);
            Debug.Log($"[PathDebug] {name} RequestNewJobSpot: took cross-floor branch, TryBeginCrossFloorTripToSpot={started}.");
            if (!started)
            {
                // No lift connects these floors — don't leave the spot reserved for an unreachable bunny.
                assignedJobRoom.ReleaseSpot(spot, this);
                claimedWorkSpot = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, jobRoomBase, spot, currentFloorIndex);

        DebugLogPath("AssignToJob", path);

        MoveAlongPath(path, spot, BunnyState.Working);
    }

    // True while the bunny is walking toward, or mid-lift-trip toward, an already-claimed Living Room
    // relax spot — covers every leg of that trip (direct walk, or WaitingForLift/RidingLift/
    // DisembarkingLift/final-walk if it crosses floors). Used by RequestNewJobSpot to avoid interrupting
    // an in-flight lift trip; deliberately excludes the "already arrived and sitting" case (CurrentState
    // == Relaxing), which Update()'s Relaxing case handles directly instead.
    private bool IsHeadedToRelaxSpot()
    {
        // pendingLift being set is the authoritative "mid a lift trip" signal on its own, true for the
        // trip's ENTIRE duration — checked unconditionally, before the CurrentState-based transit check
        // below, because DisembarkFromLift briefly parks the bunny in plain Idle (a reveal-delay pause)
        // between the lift ride and its own delayed walk-out leg actually starting. Idle isn't one of
        // the "in transit" states below, so a job assignment landing in that exact window used to sail
        // past this guard entirely and release claimedRelaxSpot/claimedRelaxRoom out from under a trip
        // that was still very much in flight (tracked separately via pendingFinalSpot/pendingFinalRoom,
        // untouched by that release) — corrupting OnArrivedAtRelaxSpot's eventual currentRoom/currentSpot
        // assignment once the trip actually completed.
        if (pendingLift != null && pendingFinalState == BunnyState.Relaxing) return true;

        bool inTransit = CurrentState == BunnyState.MovingToSpot
            || CurrentState == BunnyState.WaitingForLift
            || CurrentState == BunnyState.RidingLift
            || CurrentState == BunnyState.DisembarkingLift;

        if (!inTransit) return false;

        return pendingStateOnArrival == BunnyState.Relaxing;
    }

    // Broader than IsHeadedToRelaxSpot on purpose: a job assignment must never pull a bunny out of
    // Sleeping at all (unlike Relaxing, which IS pulled out immediately once settled — see the Relaxing
    // case in Update()), so this needs to stay true for the ENTIRE reservation window, not just mid-
    // transit. claimedSleepSpot is set the instant the spot is claimed (before any travel begins, in
    // LeaveWorkForBedroom/LeaveRelaxingForBedroom) and only ever cleared by
    // FinishSleepingAndReturnToPrevious — so checking it directly (rather than CurrentState) is what
    // makes this correct at the exact moment that method clears it and re-enters RequestNewJobSpot via
    // ReturnToPreviousActivity: CurrentState is still Sleeping for that one call, but claimedSleepSpot is
    // already null, so this must NOT block it.
    private bool IsAsleepOrHeadedToSleepSpot()
    {
        return claimedSleepSpot != null;
    }

    // Mirrors IsAsleepOrHeadedToSleepSpot, above, for Eating/Drinking: a job assignment must never
    // interrupt a meal either (bunnies finish eating/drinking until satiated, THEN walk to a newly
    // assigned job — never mid-bite), so this stays true for the whole reservation window, not just
    // mid-transit. Same reasoning for checking the fields instead of CurrentState: FinishEatingAndReturnToWork/
    // FinishDrinkingAndReturnToPrevious clear cafeteriaBeingUsed/waterRoomBeingUsed FIRST, before their
    // ReturnToPreviousActivity -> RequestNewJobSpot re-entrant call, so CurrentState is still Eating/
    // Drinking for that one call but the field is already null — checking CurrentState would wrongly
    // block that call forever. Without this guard, RequestNewJobSpot used to yank the bunny straight to
    // its work spot mid-meal via MoveAlongPath, which overwrites currentTargetSpot with the work spot —
    // orphaning the cafeteria/water room's own EatingRoutine/DrinkingRoutine coroutine (it has no idea
    // CurrentState changed and keeps ticking hunger/thirst up), and corrupting spot claims once it
    // eventually finished and tried to release currentTargetSpot (by then pointing at the work spot, not
    // the eating spot).
    private bool IsHeadedToOrCurrentlyEatingOrDrinking()
    {
        return cafeteriaBeingUsed != null || waterRoomBeingUsed != null;
    }

    private void HandleIdle()
    {
        // Still queued/awaiting gate approval — MoveToQueueSpot parks a bunny in BunnyState.Idle while
        // it waits, but it has no currentRoom yet and hasn't passed the gate. Without this guard, a
        // queued bunny would immediately claim a Living Room spot and walk straight there, skipping the
        // gate/approval flow entirely.
        if (!HasEnteredBase) return;

        if (assignedJobRoom != null && claimedWorkSpot == null)
        {
            RequestNewJobSpot();
            return;
        }
        if (assignedJobRoom != null) return;

        // Already have a relax spot claimed and a trip toward it in flight — e.g. DisembarkFromLift
        // parks the bunny in Idle for a brief reveal delay before its own delayed walk-out coroutine
        // starts moving it, and Update()'s Idle case has no way to know that pause is transient.
        // TryClaimRelaxSpot has no way to know a claim already exists either — without this guard it
        // happily claims ANOTHER spot on top of the existing one, overwriting claimedRelaxSpot/
        // claimedRelaxRoom with a spot the bunny never actually walked to. That corrupts
        // OnArrivedAtRelaxSpot's eventual currentRoom/currentSpot assignment once the real (separately
        // tracked, via pendingFinalSpot) trip resumes and completes.
        if (claimedRelaxSpot != null) return;

        // No job: always try to claim a Living Room spot this tick (cheap, and lets a bunny grab a
        // spot the moment one frees up rather than waiting out a full wanderPauseDuration first).
        if (TryClaimRelaxSpot()) return;

        // No Living Room anywhere on the base has an open spot — pace within the current room only
        // until one frees up, on the same cadence the warning is throttled to.
        wanderTimer += Time.deltaTime;
        if (wanderTimer >= wanderPauseDuration)
        {
            wanderTimer = 0f;
            NotificationToast.Instance?.Show($"{BunnyName} has no relaxing spot available.");
            PickNewWanderDestination();
        }
    }

    // Finds the nearest Living Room with an open relaxing spot (anywhere on the base, crossing floors
    // via lift if needed — unlike the old wandering system, this is the bunny's actual idle destination,
    // not aimless exploration, so there's no reason to bound it to the current floor). Returns false if
    // no Living Room anywhere has room, so HandleIdle can fall back to same-room pacing.
    private bool TryClaimRelaxSpot()
    {
        LivingRoom room = BaseManager.Instance.FindNearestLivingRoomWithSpot(transform.position);
        if (room == null) return false;

        RoomSpot spot = room.RequestSpot(this);
        if (spot == null) return false; // spot claimed by someone else between the check and this call — retry next tick

        claimedRelaxSpot = spot;
        claimedRelaxRoom = room;

        // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
        if (currentFloorIndex != room.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(room, spot, BunnyState.Relaxing))
            {
                room.ReleaseSpot(spot, this);
                claimedRelaxSpot = null;
                claimedRelaxRoom = null;
                return false;
            }
            return true;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, room, spot, currentFloorIndex);

        DebugLogPath("TryClaimRelaxSpot", path);

        MoveAlongPath(path, spot, BunnyState.Relaxing);
        return true;
    }

    // Fallback for when no Living Room anywhere on the base has an open relax spot (see
    // TryClaimRelaxSpot/HandleIdle) — the bunny paces back and forth within its OWN current room only,
    // never picks a neighboring room. This exists purely so a bunny isn't frozen stiff while waiting for
    // a spot to free up; it deliberately does NOT explore the base the way the old wandering system did.
    private void PickNewWanderDestination()
    {
        if (currentRoom == null) return; // no known room yet — shouldn't normally happen once past first arrival

        List<Transform> wanderPoints = currentRoom.GetWanderPoints();
        if (wanderPoints.Count == 0) return;

        Transform destination = wanderPoints[Random.Range(0, wanderPoints.Count)];

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, currentRoom, destination, currentFloorIndex);

        if (path.Count == 0) return;

        currentTargetSpot = null; // wandering has no RoomSpot destination
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Wandering;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();

        // Remembered for OnArrivedAtWanderPoint — NOT committed to currentRoom/currentWanderPoint yet,
        // since the bunny hasn't actually walked there.
        pendingWanderRoom = currentRoom;
        pendingWanderDestination = destination;
    }

    private void OnArrivedAtWanderPoint()
    {
        currentRoom = pendingWanderRoom;
        currentSpot = null;
        currentWanderPoint = pendingWanderDestination;
        currentFloorIndex = pendingWanderRoom.FloorIndex;
        pendingWanderRoom = null;
        pendingWanderDestination = null;

        CurrentState = BunnyState.Idle; // pause, then HandleIdle retries a Living Room spot / paces again
    }

    private void OnArrivedAtRelaxSpot()
    {
        currentRoom = claimedRelaxRoom;
        currentSpot = claimedRelaxSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
    }

    private void OnArrivedAtDrinkingSpot()
    {
        currentRoom = waterRoomBeingUsed;
        currentSpot = currentTargetSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        waterRoomBeingUsed.NotifyBunnyReadyToDrink(this);
    }

    private void OnArrivedAtSleepingSpot()
    {
        currentRoom = claimedSleepRoom;
        currentSpot = claimedSleepSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
    }

    // ---------- MOVEMENT ----------

    // Temporary — dumps every waypoint's name+position in order, to see whether a path genuinely
    // reverses direction (vs. just having many points, which is normal for an authored route around
    // walls/furniture). Remove once the entrance back-and-forth is root-caused.
    private void DebugLogPath(string label, List<Transform> path)
    {
        if (path.Count == 0)
        {
            Debug.Log($"[PathDebug] {name} {label}: (empty path)");
            return;
        }

        string waypoints = "";
        for (int i = 0; i < path.Count; i++)
            waypoints += $"\n  [{i}] {(path[i] != null ? path[i].name : "NULL")} {(path[i] != null ? path[i].position.ToString() : "")}";

        Debug.Log($"[PathDebug] {name} {label} ({path.Count} points):{waypoints}");
    }

    private void MoveAlongPath(List<Transform> waypoints, RoomSpot spot, BunnyState stateOnArrival)
    {
        // An empty path means BaseLayoutManager couldn't find a physically walkable route (e.g. the
        // start and target aren't actually connected by contiguous floor — see BaseLayoutManager's
        // IsContiguousRun) — bail without changing state rather than instantly "arriving" via an
        // empty waypoint queue, which would fire the arrival callback (claiming the spot as reached,
        // starting work/eating/etc.) without the bunny's position ever actually moving there.
        if (waypoints.Count == 0)
        {
            Debug.LogWarning($"{name}: MoveAlongPath got an empty path (no walkable route to {(spot != null ? spot.name : "target")}) — staying put.");
            return;
        }

        pendingFacingOverride = null;
        currentTargetSpot = spot;
        currentPath = new Queue<Transform>(waypoints);
        pendingStateOnArrival = stateOnArrival;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    private void AdvanceToNextWaypoint()
    {
        currentWaypointTarget = currentPath.Count > 0 ? currentPath.Dequeue() : null;
    }

    private BunnyState pendingStateOnArrival;

    private void HandleMovingToSpot()
    {
        if (currentWaypointTarget == null)
        {
            CurrentState = pendingStateOnArrival;

            Debug.Log($"[PathDebug] {name} ARRIVED pendingStateOnArrival={pendingStateOnArrival} pos={transform.position} currentRoom={(currentRoom != null ? currentRoom.name : "NULL")} currentSpot={(currentSpot != null ? currentSpot.name : "NULL")} currentFloorIndex={currentFloorIndex} currentTargetSpot={(currentTargetSpot != null ? currentTargetSpot.name : "NULL")}");

            if (pendingFacingOverride.HasValue)
            {
                SetFacing(pendingFacingOverride.Value);
                pendingFacingOverride = null;
            }
            else if (currentTargetSpot != null)
            {
                SetFacing(currentTargetSpot.FacesRight);
            }

            if (pendingStateOnArrival == BunnyState.Working)
                OnArrivedAtWorkSpot();
            else if (pendingStateOnArrival == BunnyState.Eating)
                OnArrivedAtEatingSpot();
            else if (pendingStateOnArrival == BunnyState.Drinking)
                OnArrivedAtDrinkingSpot();
            else if (pendingStateOnArrival == BunnyState.Relaxing)
                OnArrivedAtRelaxSpot();
            else if (pendingStateOnArrival == BunnyState.Sleeping)
                OnArrivedAtSleepingSpot();
            else if (pendingStateOnArrival == BunnyState.Wandering)
                OnArrivedAtWanderPoint();
            else if (pendingStateOnArrival == BunnyState.PassingGate)
                OnArrivedAtGateExit();
            else if (pendingStateOnArrival == BunnyState.DepartingThroughGate)
                OnArrivedAtDepartingGate();
            else if (pendingStateOnArrival == BunnyState.Foraging)
                OnArrivedAtForagingStagingPoint();
            else if (pendingStateOnArrival == BunnyState.Despawning)
                OnArrivedAtDespawnPoint();
            else if (pendingStateOnArrival == BunnyState.WaitingForLift)
                OnArrivedAtLiftLanding();
            else if (pendingStateOnArrival == BunnyState.DisembarkingLift)
                OnArrivedAtDisembarkLanding();
            else if (pendingStateOnArrival == BunnyState.Idle)
                OnArrivedIdleAfterCancelledTrip();
            else if (pendingStateOnArrival == BunnyState.Defending)
                OnArrivedAtDefendingSpot();
            else if (pendingStateOnArrival == BunnyState.Recovering)
                OnArrivedAtRecoveringSpot();

            return;
        }

        Vector3 target = currentWaypointTarget.position;
        Vector3 current = transform.position;
        Vector3 toTarget = target - current;

        if (toTarget.magnitude <= arrivalThreshold)
        {
            transform.position = target;
            AdvanceToNextWaypoint(); // move on to the next waypoint in the queue
            return;
        }

        Vector3 direction = toTarget.normalized;
        float currentMoveSpeed = IsWanderPacedLeg() ? moveSpeed * wanderSpeedMultiplier : moveSpeed;
        transform.position += direction * currentMoveSpeed * Time.deltaTime;

        if (Mathf.Abs(direction.x) > 0.01f)
            SetFacing(direction.x < 0f);
    }

    // True while the bunny should move/animate at wander pace rather than full speed. Used to only be
    // more involved than this single check — it also had to cover the WaitingForLift/RidingLift/
    // DisembarkingLift legs of a lift trip whose ultimate purpose was itself a wander — but wandering
    // never touches a lift at all anymore (see PickNewWanderDestination), so a lift leg can now only
    // ever be for a real job or the cafeteria, never wandering.
    private bool IsWanderPacedLeg()
    {
        return pendingStateOnArrival == BunnyState.Wandering;
    }

    public void EnterBaseAndWander(RoomBase startingRoom = null, Transform startingPoint = null)
    {
        HasEnteredBase = true;
        currentRoom = startingRoom;
        currentSpot = null;
        currentWanderPoint = startingPoint;
        currentFloorIndex = startingRoom != null ? startingRoom.FloorIndex : 0;
        CurrentState = BunnyState.Idle; // triggers HandleIdle -> seeks a Living Room spot
    }
    public void MoveToQueueSpot(Transform queueSpot)
    {
        List<Transform> path = new List<Transform> { queueSpot };
        currentTargetSpot = null; // queue spots aren't RoomSpots, just plain waypoints
        pendingFacingOverride = true; // queued bunnies always face right, toward the base interior
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Idle; // just stand there once arrived, waiting in queue
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }
    public void ProceedThroughGate(Transform gateExitPoint, RoomBase arrivalRoom)
    {
        List<Transform> path = new List<Transform> { gateExitPoint };
        currentTargetSpot = null;
        pendingFacingOverride = null; // let facing follow travel direction through the gate
        pendingArrivalRoom = arrivalRoom;
        pendingArrivalPoint = gateExitPoint;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.PassingGate;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }
    public void RejectAndDespawn(Transform exitPoint)
    {
        List<Transform> path = new List<Transform> { exitPoint };
        currentTargetSpot = null;
        pendingFacingOverride = null; // let facing follow travel direction back out
        pendingArrivalRoom = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Despawning;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    // A job or relax trip was cancelled (UnassignFromJob/TryClaimRelaxSpot pulling a spot out from
    // under a bunny) while a same-floor walk toward it was already in progress. The walk itself is
    // still let to finish along its already-valid path (see UnassignFromJob), but the eventual arrival
    // state gets flipped to plain Idle — so currentRoom/currentSpot/currentFloorIndex, which every
    // other arrival state updates via its own OnArrivedAt*, would otherwise stay stale at wherever the
    // bunny was BEFORE this trip started. That stale room then feeds the very next HandleIdle routing
    // call as the (wrong) starting point, producing a straight-line path across the base back to
    // whatever real spot is picked next. currentTargetSpot still correctly identifies the RoomSpot the
    // bunny actually just walked to and is physically standing at (the trip itself wasn't touched, only
    // its claim), so derive the arrival room from that rather than any now-cleared claimed*/assigned*
    // field. currentTargetSpot is null for the (separate, HasEnteredBase-gated) gate-queue arrival —
    // nothing to correct there.
    private void OnArrivedIdleAfterCancelledTrip()
    {
        RoomBase arrivedRoom = currentTargetSpot != null ? currentTargetSpot.GetComponentInParent<RoomBase>() : null;
        if (arrivedRoom == null) return;

        currentRoom = arrivedRoom;
        currentSpot = null; // the spot claim was already released by whichever caller cancelled this trip
        currentWanderPoint = null;
        currentFloorIndex = arrivedRoom.FloorIndex;
    }

    // ---------- WORKING (GARDEN) ----------

    private void OnArrivedAtWorkSpot()
    {
        currentRoom = (RoomBase)assignedJobRoom;
        currentSpot = claimedWorkSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        assignedJobRoom.NotifyBunnyReadyToWork(this);
    }

    private void OnArrivedAtDefendingSpot()
    {
        currentRoom = defendingRoom;
        currentSpot = defendingSpot;
        currentWanderPoint = null;
        currentFloorIndex = defendingRoom.FloorIndex;
    }

    private void OnArrivedAtRecoveringSpot()
    {
        currentRoom = claimedHospitalRoom;
        currentSpot = claimedHospitalBedSpot;
        currentWanderPoint = null;
        currentFloorIndex = claimedHospitalRoom.FloorIndex;
    }

    private void OnArrivedAtEatingSpot()
    {
        currentRoom = cafeteriaBeingUsed;
        currentSpot = currentTargetSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        cafeteriaBeingUsed.NotifyBunnyReadyToEat(this);
    }

    private void OnArrivedAtGateExit()
    {
        GateQueueManager.Instance.NotifyBunnyPassedGate(this);
        EnterBaseAndWander(pendingArrivalRoom, pendingArrivalPoint);
    }

    private void OnArrivedAtDespawnPoint()
    {
        Destroy(gameObject);
    }

    // ---------- FORAGING (see Foraging_DesignDoc.md) ----------

    // Gate for ForagingManager.TryDispatch — a bunny is only safe to pull for a trip while settled into
    // one of the "at rest" states. Mid-transit (MovingToSpot/lift legs) has in-flight path/lift
    // bookkeeping that would be corrupted by yanking the bunny elsewhere mid-leg (same reasoning as
    // RequestNewJobSpot's own transit guards); Eating/Drinking are externally driven by Cafeteria/
    // WaterRoom's own coroutines, which have no idea CurrentState just changed out from under them.
    public bool CanDepartForForaging()
    {
        if (!HasEnteredBase || IsAwaitingApproval) return false;

        switch (CurrentState)
        {
            case BunnyState.Idle:
            case BunnyState.Working:
            case BunnyState.Relaxing:
            case BunnyState.Sleeping:
                return true;
            default:
                return false;
        }
    }

    // Releases whatever the bunny currently holds — unconditional, unlike EvacuateForRoomTransition,
    // since a bunny departing for Foraging has no "away and resumable" nuance to preserve; it's genuinely
    // leaving the base, not being evicted mid-errand elsewhere.
    private void ReleaseAllClaimsForDeparture()
    {
        if (assignedJobRoom != null)
            UnassignFromJob();

        if (claimedRelaxSpot != null)
        {
            claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
            claimedRelaxSpot = null;
            claimedRelaxRoom = null;
        }

        if (claimedSleepSpot != null)
        {
            claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);
            claimedSleepSpot = null;
            claimedSleepRoom = null;
        }

        if (CurrentState == BunnyState.Sleeping || CurrentState == BunnyState.Relaxing)
            CurrentState = BunnyState.Idle;

        // A bunny who was already a resident before this trip otherwise keeps HasEnteredBase == true for
        // the whole trip, since nothing else ever resets it — which means HandleIdle's own "still queued,
        // don't wander off" guard (its "if (!HasEnteredBase) return;") wouldn't apply once she reaches
        // BunnyState.Idle at her return queue spot, and she'd immediately claim a Living Room relax spot
        // instead of actually waiting in line. Clearing it here makes the whole round trip look exactly
        // like a first-time wild arrival to every HasEnteredBase-gated system (needs/mood freeze,
        // HandleIdle, AssignToJob) — EnterBaseAndWander (called from OnArrivedAtGateExit once she's
        // actually let through the gate on return) flips it back to true, same as any other arrival.
        HasEnteredBase = false;
    }

    // Called by ForagingManager to send an already-resident bunny (CanDepartForForaging() checked by the
    // caller) out on a trip. Routes to the gate exit point — same-floor via the existing
    // GetRouteToWanderPoint, cross-floor via TryBeginCrossFloorTripToWanderPoint below — then
    // OnArrivedAtDepartingGate takes over once the bunny physically reaches the gate. Returns false if no
    // walkable/liftable route exists (shouldn't normally happen), letting the caller back out of the
    // dispatch cleanly instead of leaving the bunny stranded mid-release.
    public bool DepartForForaging(RoomBase entranceRoom, Transform gateExitPoint, Transform stagingPoint)
    {
        ReleaseAllClaimsForDeparture();

        pendingForagingStagingPoint = stagingPoint;
        pendingFacingOverride = null;

        if (currentFloorIndex != entranceRoom.FloorIndex)
            return TryBeginCrossFloorTripToWanderPoint(entranceRoom, gateExitPoint, BunnyState.DepartingThroughGate);

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, entranceRoom, gateExitPoint, currentFloorIndex);
        if (path.Count == 0) return false;

        MoveAlongPath(path, null, BunnyState.DepartingThroughGate);
        return true;
    }

    // Mirrors OnArrivedAtGateExit's arrival-direction counterpart: the bunny has physically reached the
    // gate exit point from somewhere inside the base and now needs the gate itself to open before
    // continuing the short final leg out to the offscreen staging point.
    private void OnArrivedAtDepartingGate()
    {
        EntranceGate.Instance.RequestPassage();
        StartCoroutine(WaitForGateThenWalkToStaging());
    }

    private IEnumerator WaitForGateThenWalkToStaging()
    {
        while (EntranceGate.Instance.CurrentState != GateState.Open)
            yield return null;

        List<Transform> path = new List<Transform> { pendingForagingStagingPoint };
        MoveAlongPath(path, null, BunnyState.Foraging);
    }

    // The bunny is now parked offscreen for the whole length of the trip — alive and simulated (see
    // ForagingManager), not despawned. CurrentState is already BunnyState.Foraging by the time this runs
    // (set by HandleMovingToSpot's dispatch, same as every other OnArrivedAt* callback).
    private void OnArrivedAtForagingStagingPoint()
    {
        currentRoom = null;
        currentSpot = null;
        currentWanderPoint = pendingForagingStagingPoint;
        pendingForagingStagingPoint = null;

        EntranceGate.Instance.NotifyPassageComplete();
    }

    // Called by ForagingManager once a trip's return countdown completes. Routes into the exact same
    // gate queue every wild arrival uses (GateQueueManager.Enqueue + MoveToQueueSpot — see
    // WildBunnySpawner.SpawnWildBunny for the mirrored pattern) rather than requesting gate passage
    // directly. That earlier direct-request approach opened the gate the instant the return countdown
    // ended, regardless of whether this bunny — or anyone else — was actually at the front of the queue,
    // which could leave the gate open for the entire walk back from the offscreen staging point. Going
    // through the queue means the gate only opens once this bunny is genuinely next in line, same as
    // every other arrival; GateQueueManager.Update()'s own ProceedThroughGate call (once cleared) handles
    // the rest identically to a wild/quest arrival.
    public void ReturnFromForaging()
    {
        SetArrivalType(BunnyArrivalType.ReturningFromForaging);

        Transform queueSpot = GateQueueManager.Instance.Enqueue(this);
        if (queueSpot != null)
            MoveToQueueSpot(queueSpot);
        // If the queue is full, Enqueue already added this bunny to the waiting backlog — it just stays
        // parked at the staging point until TryAdmitFromWaitingBacklog calls MoveToQueueSpot on it later,
        // same as an over-capacity wild arrival.
    }

    // ---------- CROSS-FLOOR (LIFT) ----------

    // Routes to the nearest floor-appropriate lift, then hands the trip off to it. Returns false
    // if no lift services both floors (or a trip is already in flight), so the caller can back out
    // cleanly (release a claimed spot, etc).
    private bool TryBeginCrossFloorTripToSpot(RoomBase targetRoom, RoomSpot targetSpot, BunnyState finalState)
    {
        if (pendingLift != null)
        {
            // Already mid-trip (waiting for / riding / walking off a lift) from an earlier job/eating
            // assignment — wandering never touches a lift at all now (see PickNewWanderDestination), so
            // this can only mean an equally-important trip is already in flight. Leave it alone:
            // overwriting pendingFinalRoom/Spot/Lift out from under it would make IT resolve against
            // the wrong destination once it finishes — the bunny would end up "arriving" somewhere that
            // doesn't match where it actually is.
            Debug.LogWarning($"{name}: already mid-lift-trip, ignoring new cross-floor request to floor {targetRoom.FloorIndex}.");
            return false;
        }

        int myFloor = currentFloorIndex;
        LiftRoom lift = BaseLayoutManager.Instance.FindLiftServicing(myFloor, targetRoom.FloorIndex, currentRoom, targetRoom);
        if (lift == null)
        {
            Debug.LogWarning($"{name}: no lift services floor {myFloor} -> {targetRoom.FloorIndex}.");
            return false;
        }

        pendingFinalRoom = targetRoom;
        pendingFinalSpot = targetSpot;
        pendingFinalWanderPoint = null;
        pendingFinalState = finalState;
        return BeginTripToLift(lift, myFloor, targetRoom.FloorIndex);
    }

    // Sibling of TryBeginCrossFloorTripToSpot above, for a plain wander-point destination instead of a
    // claimed RoomSpot — used by DepartForForaging to reach the gate exit point from another floor.
    // ResumeTripAfterLift already has a working wander-point branch (its pendingFinalSpot == null case),
    // so this only needs to feed the right pending fields into the same BeginTripToLift/
    // ResumeTripAfterLift machinery; neither of those needs any changes.
    private bool TryBeginCrossFloorTripToWanderPoint(RoomBase targetRoom, Transform destination, BunnyState finalState)
    {
        if (pendingLift != null)
        {
            Debug.LogWarning($"{name}: already mid-lift-trip, ignoring new cross-floor wander-point request to floor {targetRoom.FloorIndex}.");
            return false;
        }

        int myFloor = currentFloorIndex;
        LiftRoom lift = BaseLayoutManager.Instance.FindLiftServicing(myFloor, targetRoom.FloorIndex, currentRoom, targetRoom);
        if (lift == null)
        {
            Debug.LogWarning($"{name}: no lift services floor {myFloor} -> {targetRoom.FloorIndex}.");
            return false;
        }

        pendingFinalRoom = targetRoom;
        pendingFinalSpot = null;
        pendingFinalWanderPoint = destination;
        pendingFinalState = finalState;
        return BeginTripToLift(lift, myFloor, targetRoom.FloorIndex);
    }

    // Returns false if BeginTripToLift's own route lookup fails (see the empty-path guard below) — lets
    // TryBeginCrossFloorTripToSpot's caller (RequestNewJobSpot etc.) find out the trip never actually
    // started, instead of wrongly believing it did and leaving a claimed spot/state permanently stranded
    // with nothing left to ever resolve it.
    private bool BeginTripToLift(LiftRoom lift, int originFloor, int destinationFloor)
    {
        pendingLift = lift;
        pendingLiftOriginFloor = originFloor;

        // Route to the SPECIFIC segment registered on our current floor, not the coordinator directly
        // — a lift's coordinator is just whichever segment happens to own the shared state machine,
        // and it's only actually registered (for pass-through/routing purposes) on its own floor.
        LiftRoom originSegment = lift.GetSegmentForFloor(originFloor);
        Transform landingSpot = lift.GetLandingSpot(originFloor);
        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, originSegment, landingSpot, currentFloorIndex);

        // FindLiftServicing only guarantees a contiguous route when at least one candidate lift has
        // one — its fallback (nearest-overall) can still hand back a lift this bunny genuinely can't
        // walk to (see BaseLayoutManager.IsContiguousRun). Bail rather than fake-arriving via an
        // empty path — same reasoning as MoveAlongPath's own guard.
        if (path.Count == 0)
        {
            Debug.LogWarning($"{name}: no walkable route to {lift.name}'s floor {originFloor} landing spot — aborting lift trip.");
            pendingLift = null;
            pendingFinalRoom = null;
            pendingFinalSpot = null;
            pendingFinalWanderPoint = null;
            return false;
        }

        currentTargetSpot = null;
        pendingFacingOverride = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.WaitingForLift;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();

        lift.RequestLift(this, originFloor, destinationFloor);
        return true;
    }

    private void OnArrivedAtLiftLanding()
    {
        currentRoom = pendingLift.GetSegmentForFloor(pendingLiftOriginFloor);
        currentSpot = null;
        currentWanderPoint = pendingLift.GetLandingSpot(pendingLiftOriginFloor);
        currentFloorIndex = pendingLiftOriginFloor;
        CurrentState = BunnyState.WaitingForLift;
        pendingLift.NotifyArrivedAtLanding(this, pendingLiftOriginFloor);
    }

    // Called by LiftRoom once it's ready to carry this bunny — walks the short distance from the
    // landing/waiting spot to the boarding point (near the shaft), then actually starts riding. The
    // bunny stays visible for the whole ride (see DisembarkFromLift) — there's no door animation yet to
    // mask a hide/reveal, and the scripted hide/reveal was itself the source of more than one
    // "disappears and never comes back" bug (races between this coroutine and a fast/near-zero-time
    // ride — see git history), so it was removed entirely rather than patched again.
    public void BoardLift(Transform boardingSpot)
    {
        if (boardingSpot == null)
        {
            CurrentState = BunnyState.RidingLift;
            return;
        }

        List<Transform> path = new List<Transform> { boardingSpot };
        currentTargetSpot = null;
        pendingFacingOverride = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.RidingLift;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    // Called by LiftRoom once it's arrived at this bunny's destination floor. Teleports to the new
    // floor's boarding point (the bunny was visibly standing at the origin boarding point the whole
    // ride — see BoardLift), then walks the short distance back out to the landing/waiting spot before
    // resuming whatever trip was in progress.
    public void DisembarkFromLift(Transform landingSpot, Transform boardingSpot, int floorIndex)
    {
        currentFloorIndex = floorIndex;

        Transform arrivalPoint = boardingSpot != null ? boardingSpot : landingSpot;
        if (arrivalPoint != null)
            transform.position = arrivalPoint.position;

        currentPath = new Queue<Transform>();
        currentWaypointTarget = null;
        CurrentState = BunnyState.Idle;

        StartCoroutine(WalkOutAfterDelay(landingSpot, boardingSpot, liftDisembarkRevealDelay));
    }

    // Brief settling pause after teleporting onto the new floor before walking out — leaves room for a
    // doors-opening animation later, once one exists.
    private IEnumerator WalkOutAfterDelay(Transform landingSpot, Transform boardingSpot, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (boardingSpot != null && landingSpot != null && boardingSpot != landingSpot)
        {
            pendingDisembarkLandingSpot = landingSpot;
            List<Transform> path = new List<Transform> { landingSpot };
            currentTargetSpot = null;
            pendingFacingOverride = null;
            currentPath = new Queue<Transform>(path);
            pendingStateOnArrival = BunnyState.DisembarkingLift;
            CurrentState = BunnyState.MovingToSpot;
            AdvanceToNextWaypoint();
            yield break;
        }

        ResumeTripAfterLift(landingSpot);
    }

    private void OnArrivedAtDisembarkLanding()
    {
        Transform landingSpot = pendingDisembarkLandingSpot;
        pendingDisembarkLandingSpot = null;
        ResumeTripAfterLift(landingSpot);
    }

    private void ResumeTripAfterLift(Transform landingSpot)
    {
        // Resolve to the SPECIFIC segment registered on the floor we actually landed on — currentFloorIndex
        // was already updated to the destination floor by DisembarkFromLift before this runs. The
        // coordinator itself is only registered (for routing purposes) on its own floor, which may
        // not be this one.
        LiftRoom arrivalSegment = pendingLift.GetSegmentForFloor(currentFloorIndex);
        RoomBase finalRoom = pendingFinalRoom;
        RoomSpot finalSpot = pendingFinalSpot;
        Transform finalWanderPoint = pendingFinalWanderPoint;
        BunnyState finalState = pendingFinalState;

        pendingLift = null;
        pendingFinalRoom = null;
        pendingFinalSpot = null;
        pendingFinalWanderPoint = null;

        currentSpot = null;
        currentWanderPoint = landingSpot;

        // We're now physically on the destination floor at the lift's landing spot — resume the
        // original trip as an ordinary same-floor walk from here.
        if (finalSpot != null)
        {
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(arrivalSegment, null, landingSpot, finalRoom, finalSpot, currentFloorIndex);
            MoveAlongPath(path, finalSpot, finalState);
        }
        else if (finalWanderPoint != null)
        {
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(arrivalSegment, null, landingSpot, finalRoom, finalWanderPoint, currentFloorIndex);
            if (path.Count == 0)
            {
                Debug.LogWarning($"{name}: no walkable route from the lift landing to {finalWanderPoint.name} on floor {currentFloorIndex} — staying put.");
                CurrentState = BunnyState.Idle;
                return;
            }
            currentTargetSpot = null;
            pendingFacingOverride = null;
            currentPath = new Queue<Transform>(path);
            pendingStateOnArrival = finalState;
            CurrentState = BunnyState.MovingToSpot;
            AdvanceToNextWaypoint();

            currentRoom = finalRoom;
            currentWanderPoint = finalWanderPoint;
            currentFloorIndex = finalRoom.FloorIndex;

            // pendingStateOnArrival == Wandering means OnArrivedAtWanderPoint() fires once this leg's
            // path completes, and THAT method reads pendingWanderRoom/pendingWanderDestination (only
            // ever set by the same-floor wander path, PickNewWanderDestination) rather than finalRoom/
            // finalWanderPoint. Without setting them here too, it dereferences a null pendingWanderRoom
            // and throws — which aborts Update() mid-call and leaves the bunny stuck in
            // BunnyState.Wandering (a state Update()'s switch has no case for) permanently. Harmless to
            // set redundantly alongside the eager currentRoom/currentWanderPoint/currentFloorIndex
            // assignment above — OnArrivedAtWanderPoint() just re-applies the same values.
            pendingWanderRoom = finalRoom;
            pendingWanderDestination = finalWanderPoint;
        }
        else
        {
            currentRoom = arrivalSegment;
            CurrentState = BunnyState.Idle;
        }
    }

    // Priority chain: whichever need crosses its threshold first wins, and Update()'s dispatch only
    // calls this again once the bunny is back in Working/Relaxing (satiated) — so within a single leave
    // the highest-priority need in code order (hunger, then thirst, then tired) is the one acted on.
    private void HandleNeedsCheckWhileWorking()
    {
        if (IsHungry)
            LeaveWorkForCafeteria();
        else if (IsThirsty)
            LeaveWorkForWaterRoom();
        else if (IsTired)
            LeaveWorkForBedroom();
    }

    private void HandleNeedsCheckWhileRelaxing()
    {
        if (IsHungry)
            LeaveRelaxingForCafeteria();
        else if (IsThirsty)
            LeaveRelaxingForWaterRoom();
        else if (IsTired)
            LeaveRelaxingForBedroom();
    }

    // Sleeping is only ever interrupted by a CRITICAL hunger/thirst threshold (well below the normal
    // low threshold) — normal hunger/thirst are ignored while asleep, and a job assignment never
    // interrupts it at all (see the Sleeping case in Update()).
    private void HandleCriticalNeedsCheckWhileSleeping()
    {
        if (IsCriticallyHungry)
            LeaveSleepingForCafeteria();
        else if (IsCriticallyThirsty)
            LeaveSleepingForWaterRoom();
    }

    private void WarnNoEatSpot()
    {
        if (Time.time - lastNoEatSpotWarningTime < noSpotWarningCooldown) return;
        lastNoEatSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is hungry, but there are no eating spots available.");
    }

    private void WarnNoDrinkSpot()
    {
        if (Time.time - lastNoDrinkSpotWarningTime < noSpotWarningCooldown) return;
        lastNoDrinkSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is thirsty, but there are no drinking spots available.");
    }

    private void WarnNoSleepSpot()
    {
        if (Time.time - lastNoSleepSpotWarningTime < noSpotWarningCooldown) return;
        lastNoSleepSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is tired, but there are no sleeping spots available.");
    }

    private void LeaveWorkForCafeteria()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        DebugLog.Log($"Cafeteria found: {cafeteria}");
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return; // no cafeteria available — stay working, hunger check retries next frame
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        DebugLog.Log($"Eat spot found: {eatSpot}");
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return; // cafeteria full — stay working, hunger check retries next frame
        }

        // Pause work but keep claimedWorkSpot reserved — otherwise the room could hand
        // this bunny's spot to someone else while it's off eating.
        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        cafeteriaBeingUsed = cafeteria;

        if (departingRoom.FloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                // No lift connects these floors — abandon the eating spot, retry on a later hunger check.
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);

        DebugLog.Log($"Path built with {path.Count} points:");
        foreach (Transform t in path)
            DebugLog.Log($" - {(t != null ? t.name : "NULL")} at {(t != null ? t.position.ToString() : "N/A")}");

        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }
    private void LeaveRelaxingForCafeteria()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return;
        }

        cafeteriaBeingUsed = cafeteria;

        if (departingRoom != null && currentFloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);
        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }

    // ---------- THIRST (WATER ROOM DRINKING SIDE) ----------
    // Structural copies of LeaveWorkForCafeteria/LeaveRelaxingForCafeteria — spot stays reserved while
    // away (paused, not released), same as the hunger pair.

    private void LeaveWorkForWaterRoom()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        // TEMP diagnostic — remove once the "thirsty but no drinking spot" false-warning bug is root-caused.
        Debug.Log($"[ThirstDebug] {BunnyName} LeaveWorkForWaterRoom: currentRoom={(currentRoom != null ? currentRoom.name : "NULL")}, assignedJobRoom={(departingRoom != null ? departingRoom.name : "NULL")}, pos={transform.position}");

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            Debug.Log($"[ThirstDebug] {BunnyName}: no water room with a drinking spot found.");
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            Debug.Log($"[ThirstDebug] {BunnyName}: found {waterRoom.name} but RequestDrinkingSpot returned null (claimed by someone else between the check and this call?).");
            WarnNoDrinkSpot();
            return;
        }

        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        waterRoomBeingUsed = waterRoom;

        if (departingRoom.FloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    private void LeaveRelaxingForWaterRoom()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        if (departingRoom != null && currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    // ---------- ENERGY (BEDROOM) ----------
    // Structural copies of the relax pair, but the claim persists once arrived — nothing auto-returns
    // the bunny (Bedroom has no coroutine; NPCBunny's own Update() Sleeping case decides when to leave).

    private void LeaveWorkForBedroom()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        Bedroom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            return;
        }

        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        if (departingRoom.FloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    private void LeaveRelaxingForBedroom()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        Bedroom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            return;
        }

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        if (departingRoom != null && currentFloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    // ---------- CRITICAL-NEED WAKE (SLEEPING -> EATING/DRINKING) ----------
    // Departing spot is the SLEEP spot, which stays reserved throughout — unconditionally, regardless
    // of any pending job. FinishEatingAndReturnToWork / FinishDrinkingAndReturnToPrevious route back to
    // it via ReturnToPreviousActivity's top-priority sleep check once satiated.

    private void LeaveSleepingForCafeteria()
    {
        RoomBase departingRoom = claimedSleepRoom;
        RoomSpot departingSpot = claimedSleepSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return;
        }

        cafeteriaBeingUsed = cafeteria;

        if (departingRoom != null && currentFloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);
        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }

    private void LeaveSleepingForWaterRoom()
    {
        RoomBase departingRoom = claimedSleepRoom;
        RoomSpot departingSpot = claimedSleepSpot;

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        if (departingRoom != null && currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    // Called by CafeteriaRoom each time a carrot-consumption tick happens
    public void ReceiveCarrotNutrition()
    {
        // Cafeteria/Kitchen's GradeMultiplier — a nicer cafeteria restores more hunger per carrot.
        float multiplier = (cafeteriaBeingUsed != null) ? cafeteriaBeingUsed.GradeMultiplier : 1f;
        hunger = Mathf.Min(100f, hunger + hungerGainPerCarrot * multiplier);
    }

    public bool IsFullyFed()
    {
        return hunger >= hungerThresholdFull;
    }

    // Called by WaterRoom each time a water-consumption tick happens
    public void ReceiveWaterHydration()
    {
        // Water Room's GradeMultiplier — a nicer water room restores more thirst per drink.
        float multiplier = (waterRoomBeingUsed != null) ? waterRoomBeingUsed.GradeMultiplier : 1f;
        thirst = Mathf.Min(100f, thirst + thirstGainPerDrink * multiplier);
    }

    public bool IsFullyHydrated()
    {
        return thirst >= thirstThresholdFull;
    }

    // Called by CafeteriaRoom once bunny is full (or can't get more carrots)
    public void FinishEatingAndReturnToWork()
    {
        if (cafeteriaBeingUsed != null)
        {
            cafeteriaBeingUsed.ReleaseSpot(currentTargetSpot, this);
            cafeteriaBeingUsed = null;
        }

        // If thirst has ALSO crossed its threshold, chain straight to the Water Room from here instead
        // of walking all the way back to work/relaxing just to immediately leave again — saves a
        // pointless round trip, especially on a large base. Sleep-origin trips only chain on the
        // CRITICAL threshold (matching Sleeping's own critical-only interrupt rule — normal thirst is
        // ignored while asleep); Working/Relaxing-origin trips chain on the normal LOW threshold, since
        // that's what would trigger the interrupt in the first place. claimedSleepSpot/claimedWorkSpot/
        // claimedRelaxSpot all stay reserved through the whole chain, so the eventual
        // ReturnToPreviousActivity() at the end still routes back to the SAME spot regardless of how
        // many stops were chained first.
        bool shouldChainToWater = claimedSleepSpot != null ? IsCriticallyThirsty : IsThirsty;
        if (shouldChainToWater)
        {
            ChainToWaterRoomFromCurrentSpot();
            return;
        }

        // Hunger and thirst are both settled — if tiredness is ALSO a problem, chain straight to the
        // Bedroom instead of walking back to work/relaxing first. Only when NOT already mid-sleep
        // (claimedSleepSpot == null): a bunny woken from sleep for a critical need always resumes the
        // SAME sleep spot via ReturnToPreviousActivity's own priority below, never a fresh Bedroom trip
        // from here — tiredness is checked last and only matters for a Working/Relaxing-origin chain.
        if (claimedSleepSpot == null && IsTired)
        {
            ChainToBedroomFromCurrentSpot();
            return;
        }

        ReturnToPreviousActivity();
    }

    // Called by WaterRoom once bunny is fully hydrated (or can't get more water)
    public void FinishDrinkingAndReturnToPrevious()
    {
        if (waterRoomBeingUsed != null)
        {
            waterRoomBeingUsed.ReleaseDrinkingSpot(currentTargetSpot, this);
            waterRoomBeingUsed = null;
        }

        // Reverse of FinishEatingAndReturnToWork's chain, above — same critical-vs-low threshold split
        // by origin.
        bool shouldChainToCafeteria = claimedSleepSpot != null ? IsCriticallyHungry : IsHungry;
        if (shouldChainToCafeteria)
        {
            ChainToCafeteriaFromCurrentSpot();
            return;
        }

        // Same tiredness fallback as FinishEatingAndReturnToWork, above — reached here when Thirst was
        // the originally-triggering need instead of Hunger.
        if (claimedSleepSpot == null && IsTired)
        {
            ChainToBedroomFromCurrentSpot();
            return;
        }

        ReturnToPreviousActivity();
    }

    // Structural copy of the LeaveXForWaterRoom methods, but routes from the bunny's CURRENT position
    // (it just finished eating at the Cafeteria) rather than from whatever spot it left behind — used to
    // chain directly from Eating to Drinking without detouring back to work/relaxing/bed first, whatever
    // the origin was. See FinishEatingAndReturnToWork.
    private void ChainToWaterRoomFromCurrentSpot()
    {
        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            ReturnToPreviousActivity(); // no Water Room available — fall back to sleep; the next critical check retries once actually asleep again
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            ReturnToPreviousActivity();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
        if (currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    // Structural copy of the LeaveXForCafeteria methods, but routes from the bunny's CURRENT position
    // (it just finished drinking at the Water Room) rather than from whatever spot it left behind —
    // reverse of ChainToWaterRoomFromCurrentSpot, see FinishDrinkingAndReturnToPrevious.
    private void ChainToCafeteriaFromCurrentSpot()
    {
        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            ReturnToPreviousActivity();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            ReturnToPreviousActivity();
            return;
        }

        cafeteriaBeingUsed = cafeteria;

        // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
        if (currentFloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);
        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }

    // Structural copy of the LeaveXForBedroom methods, but routes from the bunny's CURRENT position (it
    // just finished eating/drinking) rather than from whatever spot it left behind — chains straight to
    // the Bedroom as the last link in the hunger/thirst/tired chain. Only ever called when
    // claimedSleepSpot == null (see FinishEatingAndReturnToWork/FinishDrinkingAndReturnToPrevious), so
    // this always represents a genuinely NEW sleep claim, never a currently-sleeping bunny.
    private void ChainToBedroomFromCurrentSpot()
    {
        Bedroom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            ReturnToPreviousActivity();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            ReturnToPreviousActivity();
            return;
        }

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
        if (currentFloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    // Called from Update()'s Sleeping case once energy reaches 100. Releases the sleep claim FIRST —
    // this is what distinguishes this wake path from the critical-need wake
    // (LeaveSleepingForCafeteria/WaterRoom), which deliberately never clears claimedSleepSpot so a
    // return trip after eating/drinking always loops back to sleep no matter what else is pending. Here,
    // clearing it first means ReturnToPreviousActivity's own sleep-check correctly falls through to the
    // job/relax/idle chain instead of looping back to sleep.
    private void FinishSleepingAndReturnToPrevious()
    {
        if (claimedSleepRoom != null)
            claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);

        claimedSleepSpot = null;
        claimedSleepRoom = null;

        ReturnToPreviousActivity();
    }

    // Called by InvasionManager (auto-defend, for a resident already Working/Relaxing in the invaded
    // room) or GuardDeployUI (manual deploy, for a Guard Room bunny being sent to reinforce elsewhere).
    // The caller has already claimed `spot` via room.ClaimCombatSpot before calling this. Mirrors
    // RequestNewJobSpot's routing shape but targets an arbitrary room/spot instead of assignedJobRoom.
    public void BeginDefending(RoomSpot spot, RoomBase room)
    {
        defendingSpot = spot;
        defendingRoom = room;

        if (currentFloorIndex != room.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(room, spot, BunnyState.Defending))
            {
                // No lift connects these floors — don't leave the spot reserved for an unreachable bunny.
                room.ReleaseCombatSpot(spot, this);
                defendingSpot = null;
                defendingRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, room, spot, currentFloorIndex);
        MoveAlongPath(path, spot, BunnyState.Defending);
    }

    // Ticked every frame while CurrentState == Defending — targets whichever enemy in defendingRoom is
    // closest and starts this bunny's own attack wind-up once engaged, via the shared CombatEngagement
    // utility (also used by EnemyInstance, so both sides of an invasion use identical targeting/cooldown
    // logic). The actual AttackInstance doesn't spawn here — see ReleasePendingAttack below.
    private void HandleDefending()
    {
        if (InvasionManager.Instance == null || defendingRoom == null) return;

        BunnyTypeDefinition attackSource = ((ICombatant)this).AttackSource;
        if (attackSource != null && attackSource.isMelee)
        {
            HandleMeleeDefending();
            return;
        }

        List<ICombatant> candidatePoolList = InvasionManager.Instance.GetEnemiesInRoom(defendingRoom).Cast<ICombatant>().ToList();
        bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentCombatTarget, ref attackCooldownRemaining, candidatePoolList, out ICombatant attackTarget);

        // TEMP — chasing "second invasion, bunny reaches CombatSpot but never attacks" bug.
        if (Time.frameCount % 30 == 0 || startedWindUp)
            Debug.Log($"[VFXDEBUG] HandleDefending({name}): pool={candidatePoolList.Count} target={(currentCombatTarget != null ? currentCombatTarget.CombatGameObject?.name : "NULL")} cooldown={attackCooldownRemaining:F2} attackSource={((ICombatant)this).AttackSource?.displayName ?? "NULL"} startedWindUp={startedWindUp} animator={(animator != null ? "set" : "NULL")}");

        if (!startedWindUp) return;

        pendingAttackTarget = attackTarget;
        if (animator != null)
            animator.SetTrigger(isAttackingParam);
    }

    // Melee counterpart to HandleDefending above — a melee defender can't just fire from wherever it's
    // standing (its CombatSpot) the way ranged does, it has to physically walk to and hold a flank
    // position beside ONE specific target first. Split into two branches: already flanking someone (fight,
    // or release-and-retarget if that target died), or not yet flanking anyone (find a target with an open
    // FlankSlots slot and start walking there).
    private void HandleMeleeDefending()
    {
        if (claimedFlankSlots != null)
        {
            if (claimedFlankTarget == null || claimedFlankTarget.CombatGameObject == null || !claimedFlankTarget.IsAlive)
            {
                claimedFlankSlots.ReleaseSlot(this);
                claimedFlankSlots = null;
                claimedFlankTarget = null;
                return; // re-evaluate fresh next frame — pick a new target below
            }

            // Pinned single-target pool: a flanking melee attacker must NOT retarget to "whatever's
            // closest now" the way ranged does (CombatEngagement's own header comment) — it's physically
            // committed to the one target it walked up to and claimed a slot on.
            List<ICombatant> pinnedPool = new List<ICombatant> { claimedFlankTarget };
            bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentCombatTarget, ref attackCooldownRemaining, pinnedPool, out ICombatant attackTarget);
            if (!startedWindUp) return;

            pendingAttackTarget = attackTarget;
            if (animator != null)
                animator.SetTrigger(isAttackingParam);
            return;
        }

        List<ICombatant> candidatePool = InvasionManager.Instance.GetEnemiesInRoom(defendingRoom).Cast<ICombatant>().ToList();
        ICombatant chosen = CombatEngagement.FindBestMeleeTarget(transform.position, candidatePool);
        if (chosen == null) return; // nobody in the room has an open flank slot right now — idle at CombatSpot until one frees

        FlankSlots slots = CombatEngagement.GetFlankSlots(chosen);
        if (slots == null || !slots.TryClaimSlot(this, out FlankSide side)) return; // no FlankSlots authored on this target yet, or lost the claim race — retry next frame

        claimedFlankSlots = slots;
        claimedFlankSide = side;
        claimedFlankTarget = chosen;

        Vector3 flankPoint = CombatEngagement.ComputeFlankPosition(chosen, side);
        // Standing left of the target means facing right, toward it, and vice versa.
        MoveToFlankPosition(flankPoint, faceRightOnArrival: side == FlankSide.Left);
    }

    // Walks this bunny to an arbitrary world-space point within its current combat room, rather than a
    // RoomSpot the way every other MoveAlongPath call in this file targets — a flank position is a
    // computed offset from a live target, not an authored room Transform. Reuses MoveAlongPath's own
    // pendingFacingOverride/currentPath/pendingStateOnArrival machinery directly instead of going through
    // MoveAlongPath itself (which requires a RoomSpot parameter) since flankWaypoint is a single reused
    // scratch Transform, not a room-authored one. pendingStateOnArrival stays BunnyState.Defending, so
    // arrival re-dispatches through OnArrivedAtDefendingSpot — verified safe to reuse for this: that method
    // re-derives currentRoom/currentSpot from defendingRoom/defendingSpot directly, never from
    // currentTargetSpot (which this leaves null), so reusing it here is a true no-op for those fields.
    private void MoveToFlankPosition(Vector3 worldPosition, bool faceRightOnArrival)
    {
        if (flankWaypoint == null)
        {
            flankWaypoint = new GameObject($"~{name} FlankWaypoint (runtime)").transform;
        }
        flankWaypoint.position = worldPosition;

        pendingFacingOverride = faceRightOnArrival;
        currentTargetSpot = null; // not a RoomSpot — facing on arrival comes from pendingFacingOverride above instead
        currentPath = new Queue<Transform>(new List<Transform> { flankWaypoint });
        pendingStateOnArrival = BunnyState.Defending;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    // Animation Event receiver — place this on this type's Attacking clip (via its Animator Override
    // Controller) at the frame the attack visually "releases" (e.g. the moment a cast/throw completes),
    // so the AttackInstance VFX spawns in sync with the animation instead of instantly on cooldown.
    // Re-validates the target itself (see CombatEngagement.ReleaseAttack) since a few frames of wind-up
    // may have passed since TryBeginAttack snapshotted it.
    public void ReleasePendingAttack()
    {
        // TEMP — chasing "second invasion, bunny reaches CombatSpot but never attacks" bug. Confirms
        // whether the Animation Event even fires on the second engagement.
        Debug.Log($"[VFXDEBUG] ReleasePendingAttack({name}): pendingAttackTarget={(pendingAttackTarget != null ? pendingAttackTarget.CombatGameObject?.name : "NULL")}");
        if (pendingAttackTarget == null) return;
        CombatEngagement.ReleaseAttack(this, pendingAttackTarget);
        pendingAttackTarget = null;
    }

    // Test-only hook for VFXPreviewHarness — triggers the same wind-up animation HandleDefending does
    // (skipping its InvasionManager/cooldown gating, which the isolated preview scene has no reason to
    // set up), so the Attacking clip's existing Animation Event still fires ReleasePendingAttack above at
    // the real "release" frame, the same path actual combat uses.
    public void TestFireAttack(ICombatant target)
    {
        pendingAttackTarget = target;
        if (animator != null)
            animator.SetTrigger(isAttackingParam);
    }

    // Test-only hook for VFXPreviewHarness — a bunny just placed in a test scene never goes through
    // WildBunnySpawner's SetTypeAndProgression (see that method's own comment), so typeDefinition/Stats/
    // currentHP are all still null/zero, which silently made ICombatant.AttackSource null and
    // CombatEngagement.ReleaseAttack bail with nothing to spawn. Mirrors just enough of
    // SetTypeAndProgression to make this bunny a valid attacker/target for VFX preview — no-ops if this
    // bunny already has real progression (don't clobber a bunny that WAS spawned normally), and skips
    // traits/passives since VFX timing doesn't depend on them.
    public void TestInitializeForPreview(BunnyTypeDefinition def)
    {
        if (def == null || typeDefinition != null) return;
        typeDefinition = def;
        Type = def.type;
        Stats = BunnyStatCalculator.Resolve(def, Level, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (currentHP <= 0) currentHP = Stats.HP;
    }

    // Called once an invasion clears in defendingRoom (InvasionManager.OnInvasionCleared). Releases the
    // CombatSpot and hands off to ReturnToPreviousActivity — already knows how to walk a resident back to
    // its own claimedWorkSpot/claimedRelaxSpot, or a deployed Guard bunny back to its Guard Room post
    // (assignedJobRoom/claimedWorkSpot there, untouched by BeginDefending), with no special-case needed.
    // A Fainted bunny revives to 1 HP here rather than at any point mid-battle — see BunnyState.Fainted's
    // comment — so it can walk itself to a job/relax spot (and, once built, the Hospital) afterward.
    public void StopDefendingAndReturn()
    {
        if (defendingRoom == null || defendingSpot == null) return;

        if (CurrentState == BunnyState.Fainted)
        {
            currentHP = 1;

            // Cancel the faint fade and snap straight back to full opacity — no fade-in, this bunny should
            // read as instantly back to normal the moment combat resolves.
            if (faintFadeRoutine != null)
            {
                StopCoroutine(faintFadeRoutine);
                faintFadeRoutine = null;
            }
            SetSpriteAlpha(1f);
        }

        defendingRoom.ReleaseCombatSpot(defendingSpot, this);
        defendingSpot = null;
        defendingRoom = null;

        // Melee-only cleanup — releases whatever this bunny was flanking, if anything, on recall. In
        // practice the target is almost always already dead/gone by the time an invasion actually clears,
        // but this stays unconditional for correctness/symmetry with the CombatSpot release above.
        if (claimedFlankSlots != null)
        {
            claimedFlankSlots.ReleaseSlot(this);
            claimedFlankSlots = null;
            claimedFlankTarget = null;
        }

        ReturnToPreviousActivity();
    }

    // Called by PatientUI when the player assigns an injured bunny to a Hospital bed. The caller has
    // already claimed `spot` via room.RequestBed before calling this — mirrors BeginDefending's shape
    // exactly, just walking to a bed instead of a CombatSpot. HospitalRoom.HealPatientsRoutine (not this
    // class) is what actually raises currentHP once the bunny arrives; HandleRecovering just watches for
    // full HP.
    public void AssignToHospitalBed(RoomSpot spot, HospitalRoom room)
    {
        claimedHospitalBedSpot = spot;
        claimedHospitalRoom = room;

        if (currentFloorIndex != room.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(room, spot, BunnyState.Recovering))
            {
                // No lift connects these floors — don't leave the bed reserved for an unreachable bunny.
                room.ReleaseBed(spot, this);
                claimedHospitalBedSpot = null;
                claimedHospitalRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, room, spot, currentFloorIndex);
        MoveAlongPath(path, spot, BunnyState.Recovering);
    }

    // Ticked every frame while CurrentState == Recovering — HospitalRoom's own heal tick is what actually
    // moves currentHP; this just recalls the bunny once it's back to full, the same "watch a condition,
    // recall via ReturnToPreviousActivity" shape as Sleeping's own energy>=100 check in Update().
    private void HandleRecovering()
    {
        if (currentHP >= Stats.HP)
            ReturnFromHospital();
    }

    // Also callable directly (not just from HandleRecovering) so a future "manually pull a bunny out of
    // the Hospital early" UI action has something to call.
    public void ReturnFromHospital()
    {
        if (claimedHospitalRoom == null || claimedHospitalBedSpot == null) return;

        claimedHospitalRoom.ReleaseBed(claimedHospitalBedSpot, this);
        claimedHospitalBedSpot = null;
        claimedHospitalRoom = null;

        ReturnToPreviousActivity();
    }

    // Shared by all three "done with an interrupt, resume whatever I was doing" paths above (eating,
    // drinking, and finishing a sleep). Sleep is checked FIRST, ahead of the job check — a bunny that
    // just finished eating/drinking while still mid-sleep (claimedSleepSpot still reserved — see
    // LeaveSleepingForCafeteria/WaterRoom) always walks straight back to sleep regardless of any pending
    // job. Only once claimedSleepSpot has actually been released (by FinishSleepingAndReturnToPrevious)
    // does the job -> relax -> idle chain below get a turn.
    private void ReturnToPreviousActivity()
    {
        if (claimedSleepSpot != null)
        {
            Bedroom sleepRoomBase = claimedSleepRoom;

            // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
            if (currentFloorIndex != sleepRoomBase.FloorIndex)
            {
                if (!TryBeginCrossFloorTripToSpot(sleepRoomBase, claimedSleepSpot, BunnyState.Sleeping))
                {
                    // No lift back to the sleep floor — release the spot and let a later needs-check retry.
                    claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);
                    claimedSleepSpot = null;
                    claimedSleepRoom = null;
                    CurrentState = BunnyState.Idle;
                }
                return;
            }

            // Spot was never released while eating/drinking — walk straight back to it.
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, sleepRoomBase, claimedSleepSpot, currentFloorIndex);
            MoveAlongPath(path, claimedSleepSpot, BunnyState.Sleeping);
            return;
        }

        if (assignedJobRoom != null)
        {
            if (claimedWorkSpot != null)
            {
                RoomBase jobRoomBase = (RoomBase)assignedJobRoom;

                // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
                if (currentFloorIndex != jobRoomBase.FloorIndex)
                {
                    if (!TryBeginCrossFloorTripToSpot(jobRoomBase, claimedWorkSpot, BunnyState.Working))
                    {
                        // No lift back to the job floor — release the spot and let HandleIdle retry later.
                        assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
                        claimedWorkSpot = null;
                        CurrentState = BunnyState.Idle;
                    }
                    return;
                }

                // Spot was never released while eating/drinking/sleeping — walk straight back to it.
                List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, jobRoomBase, claimedWorkSpot, currentFloorIndex);
                MoveAlongPath(path, claimedWorkSpot, BunnyState.Working);
            }
            else
            {
                RequestNewJobSpot();
            }
        }
        else if (claimedRelaxSpot != null)
        {
            RoomBase relaxRoomBase = claimedRelaxRoom;

            // See RequestNewJobSpot's identical check for why currentRoom's nullness must not gate this.
            if (currentFloorIndex != relaxRoomBase.FloorIndex)
            {
                if (!TryBeginCrossFloorTripToSpot(relaxRoomBase, claimedRelaxSpot, BunnyState.Relaxing))
                {
                    // No lift back to the relax floor — release the spot and let HandleIdle claim a new one.
                    claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
                    claimedRelaxSpot = null;
                    claimedRelaxRoom = null;
                    CurrentState = BunnyState.Idle;
                }
                return;
            }

            // Spot was never released while eating/drinking — walk straight back to it.
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, relaxRoomBase, claimedRelaxSpot, currentFloorIndex);
            MoveAlongPath(path, claimedRelaxSpot, BunnyState.Relaxing);
        }
        else
        {
            CurrentState = BunnyState.Idle; // HandleIdle claims a fresh relax spot (or paces) from here
        }
    }
    // ---------- ANIMATION ----------

    private void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetBool(isMovingParam, CurrentState == BunnyState.MovingToSpot);
        animator.SetBool(isWorkingParam, CurrentState == BunnyState.Working);
        animator.SetBool(isEatingParam, CurrentState == BunnyState.Eating);
        animator.SetBool(isDrinkingParam, CurrentState == BunnyState.Drinking);
        animator.SetBool(isSleepingParam, CurrentState == BunnyState.Sleeping);
        animator.SetBool(isFaintedParam, CurrentState == BunnyState.Fainted);

        bool isSlowWander = CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg();
        animator.speed = isSlowWander ? wanderSpeedMultiplier : 1f;
    }

    // ---------- FACING (reused from BunnyMovement) ----------

    private void SetFacing(bool shouldFaceRight)
    {
        if (bunnyScaleRoot == null) return;

        if (!hasInitializedFacing)
        {
            hasInitializedFacing = true;
        }
        else if (shouldFaceRight == facingRight)
        {
            return;
        }

        // TEMP — chasing the "stutter step" visual bug at room-transition waypoints. Only fires when a
        // flip is actually about to happen (not on the early-return no-ops above), so this shows exactly
        // how often/rapidly facing direction is flip-flopping. Remove once root-caused.
        Debug.Log($"[FacingDebug] {name} SetFacing({shouldFaceRight}) applied — was facingRight={facingRight}, pos={transform.position}, CurrentState={CurrentState}, pendingStateOnArrival={pendingStateOnArrival}, currentWaypointTarget={(currentWaypointTarget != null ? currentWaypointTarget.name : "NULL")}, currentTargetSpot={(currentTargetSpot != null ? currentTargetSpot.name : "NULL")}");

        facingRight = shouldFaceRight;
        bool flip = bunnyFacesLeftByDefault ? shouldFaceRight : !shouldFaceRight;

        Vector3 scale = bunnyScaleRoot.localScale;
        float magnitude = Mathf.Abs(scale.x);
        bunnyScaleRoot.localScale = new Vector3(flip ? -magnitude : magnitude, scale.y, scale.z);
    }
}