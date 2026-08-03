using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum GateState
{
    Closed,
    Opening,
    Open,
    Closing
}

// Also the siege target for every base invasion (see Combat_DesignDoc.md's Gate Siege section) —
// implements ICombatant so raiders attack it through the exact same CombatEngagement/AttackInstance
// pipeline used everywhere else, rather than a bespoke damage system. Deliberately does NOT attack back
// (AttackSource is always null) — bunnies cannot defend the gate either, it's purely an HP/Defense timer
// raiders must clear before InvasionManager lets them walk further in.
public class EntranceGate : MonoBehaviour, ICombatant
{
    public static EntranceGate Instance { get; private set; }

    public GateState CurrentState { get; private set; } = GateState.Closed;

    [SerializeField] private float openDuration = 3f;
    [SerializeField] private float closeDuration = 3f;

    [Tooltip("Seconds to wait after the last bunny clears the gate before actually starting to close — gives an approaching bunny time to reach it while it's still open. Canceled (gate just stays open) if new passage is requested before this elapses.")]
    [SerializeField] private float closeGracePeriod = 2f;

    [Header("Visual Gate Model")]
    [SerializeField] private Transform gateTransform;
    [SerializeField] private float closedLocalY = 0f;
    [SerializeField] private float openLocalY = 3f;

    [Header("Siege Spots (outside, near the queue — hand-placed, no auto-populator)")]
    [SerializeField] private List<RoomSpot> rangedSiegeSpots;
    [SerializeField] private RoomSpot meleeSiegeSpot;
    [Tooltip("Extra melee siege positions beyond the single front meleeSiegeSpot above — a melee enemy claiming one of these just stands and waits (see EnemyInstance.HandleSiegeUpdate) rather than attacking, since only meleeSiegeSpot is close enough to actually strike the gate. Siege enemies are invulnerable and never die pre-breach, so the front spot never actually opens up mid-siege for a waiting enemy to advance into — they simply ride along and walk in together with everyone else once the gate breaks. Exists purely to raise melee capacity per wave beyond 1 (see InvasionManager.TrySpawnInvasion), same reasoning as rangedSiegeSpots already being a list.")]
    [SerializeField] private List<RoomSpot> meleeWaitingSpots;
    public IReadOnlyList<RoomSpot> RangedSiegeSpots => rangedSiegeSpots;
    public RoomSpot MeleeSiegeSpot => meleeSiegeSpot;
    public IReadOnlyList<RoomSpot> MeleeWaitingSpots => meleeWaitingSpots;
    [Tooltip("Offscreen points new siege enemies spawn at and walk in from, mirroring WildBunnySpawner's own offscreen bunny-arrival point for visual consistency (hand-placed Transforms, same idea — no auto-populator). InvasionManager assigns one per enemy in a wave (wrapping if a wave is bigger than this list), so a multi-enemy wave starts spread out across separate points instead of all stacked on one — simpler than staggering spawn timing with a coroutine, and solves the clumping directly rather than just delaying it. Leave empty to fall back to the old instant-appear-at-spot behavior.")]
    [SerializeField] private List<Transform> enemySpawnPoints;
    public IReadOnlyList<Transform> EnemySpawnPoints => enemySpawnPoints;

    private int activeTraffic = 0; // bunnies currently queued/waiting/passing through
    private Coroutine gateRoutine;
    private Coroutine closeGraceRoutine;

    // Cached reference for reading .Grade (HP/Defense scale per Entrance grade) — set by
    // EntranceRoom.OnEnable alongside its existing BaseLayoutManager/GateQueueManager hookups.
    private RoomBase entranceRoomRef;
    private int maxHP;
    private int currentHP;
    private int defense;
    private Renderer[] visualRenderers;

    public event System.Action OnDefeated;

    // ---------- ICombatant ----------
    BunnyType ICombatant.Type => BunnyType.Metal;
    // Functionally irrelevant to combat math today — CombatResolver/CombatMath only ever read the
    // ATTACKER's Level (e.g. CombatMath.GetBasePower(attacker.Level)), never the defender's. Fixed
    // rather than tied to Grade so there's no misleading indirection implying it matters.
    int ICombatant.Level => 1;
    BunnyStats ICombatant.Stats => new BunnyStats { HP = maxHP, Attack = 0, Defense = defense, Speed = CombatBalanceConfig.Instance.gateSpeed, Luck = 0 };
    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    // The gate never attacks — this is never actually read since ReleaseAttack/TryBeginAttack are only
    // ever called with the gate as ATTACKER never, but keeping it null (rather than omitting) matches the
    // "optional, null until relevant" tolerance the rest of this system already uses.
    BunnyTypeDefinition ICombatant.AttackSource => null;
    // Uses gateTransform, NOT this component's own transform — EntranceGate lives on a manager-style
    // GameObject that can sit anywhere in the scene (confirmed: ~1235 units from the siege spots in
    // testing), while gateTransform is the actual visible gate model this script already animates
    // open/closed. Combat math (range checks, VisualCenter, attack origin) all needs the real position.
    private Transform CombatOrigin => gateTransform != null ? gateTransform : transform;
    Transform ICombatant.CombatTransform => CombatOrigin;
    GameObject ICombatant.CombatGameObject => this == null ? null : gameObject;
    Vector3 ICombatant.AttackOrigin => CombatOrigin.position; // unused, gate never attacks
    Vector3 ICombatant.VisualCenter => CombatEngagement.ComputeVisualCenter(visualRenderers, CombatOrigin.position);
    bool ICombatant.IsFacingRight => true; // unused, gate never attacks

    public void TakeCombatDamage(int amount)
    {
        if (currentHP <= 0) return;
        currentHP = Mathf.Max(0, currentHP - Mathf.Max(0, amount));
        if (currentHP != 0) return;

        // Reuses the existing passage-open animation rather than a distinct "smashed" visual — good
        // enough for scope, revisit as a polish pass later if a broken-gate look is wanted.
        StartTransition(GateState.Opening);
        OnDefeated?.Invoke(); // the breach signal InvasionManager.HandleGateBreached subscribes to
    }

    // Resolves HP/Defense from the Entrance Room's current Grade — called once in Awake and again on
    // repair (see HandleSiegeFullyCleared below), so a mid-game Entrance upgrade is picked up for free
    // the next time the gate heals rather than needing separate upgrade-event wiring.
    private void ResolveStatsForGrade()
    {
        int grade = entranceRoomRef != null ? entranceRoomRef.Grade : 1;
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        maxHP = grade switch { 1 => cfg.gateGrade1MaxHP, 2 => cfg.gateGrade2MaxHP, _ => cfg.gateGrade3MaxHP };
        defense = grade switch { 1 => cfg.gateGrade1Defense, 2 => cfg.gateGrade2Defense, _ => cfg.gateGrade3Defense };
        currentHP = maxHP;
    }

    // Called by EntranceRoom.OnEnable, same pattern as BaseLayoutManager.SetEntranceRoom/
    // GateQueueManager.SetEntranceRoom — stays valid across an Entrance Room grade-upgrade swap.
    public void SetEntranceRoom(RoomBase room)
    {
        entranceRoomRef = room;
        ResolveStatsForGrade();
    }

    // Fires once per fully-resolved raid, regardless of which raid type it was (InvasionManager now also
    // spawns enemies directly into a room, entirely bypassing the gate — see TrySpawnInRoomInvasion).
    // currentHP only ever reaches 0 via an actual breach (TakeCombatDamage), so >0 here means this
    // particular raid never touched the gate at all — nothing to repair, and replaying the
    // opening/closing transition + SFX for a gate that was never broken would be a visible/audible glitch.
    private void HandleSiegeFullyCleared()
    {
        if (currentHP > 0) return;

        ResolveStatsForGrade(); // full repair
        StartTransition(GateState.Closing);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // From gateTransform's own hierarchy, not this manager GameObject's — see CombatOrigin's comment.
        visualRenderers = CombatOrigin.GetComponentsInChildren<Renderer>();
        ResolveStatsForGrade(); // grade-1 default until SetEntranceRoom provides the real Entrance instance
    }

    private void Start()
    {
        if (InvasionManager.Instance != null)
            InvasionManager.Instance.OnSiegeFullyCleared += HandleSiegeFullyCleared;
    }

    // Call this when a bunny needs to pass through (either direction)
    public void RequestPassage()
    {
        activeTraffic++;

        if (CurrentState == GateState.Closed)
        {
            StartTransition(GateState.Opening);
        }
        else if (CurrentState == GateState.Closing)
        {
            // Let the close finish, then it will detect activeTraffic > 0 and reopen automatically
        }
        // If already Opening or Open, nothing extra needed — bunny just waits for Open state
    }

    // Call this once a bunny has fully cleared the gate (finished walking through)
    public void NotifyPassageComplete()
    {
        activeTraffic = Mathf.Max(0, activeTraffic - 1);

        if (activeTraffic <= 0 && CurrentState == GateState.Open)
        {
            if (closeGraceRoutine != null)
                StopCoroutine(closeGraceRoutine);
            closeGraceRoutine = StartCoroutine(CloseGraceRoutine());
        }
    }

    // Waits closeGracePeriod seconds before actually starting to close, bailing out early if a new
    // RequestPassage() comes in during the wait (activeTraffic goes back above 0) — same "wait for
    // stragglers, cancel if resolved early" shape LiftRoom's boardingGracePeriod already uses.
    private IEnumerator CloseGraceRoutine()
    {
        float elapsed = 0f;
        while (elapsed < closeGracePeriod)
        {
            if (activeTraffic > 0) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (activeTraffic <= 0 && CurrentState == GateState.Open)
            StartTransition(GateState.Closing);
    }

    private void StartTransition(GateState newState)
    {
        if (gateRoutine != null)
            StopCoroutine(gateRoutine);

        gateRoutine = StartCoroutine(TransitionRoutine(newState));
    }

    private IEnumerator TransitionRoutine(GateState toState)
    {
        CurrentState = toState;
        DebugLog.Log($"Gate: entering {toState}");

        if (toState == GateState.Opening) AudioManager.EnsureInstance().PlayGateOpen();
        else if (toState == GateState.Closing) AudioManager.EnsureInstance().PlayGateClose();

        float duration = toState == GateState.Opening ? openDuration : closeDuration;
        float startY = gateTransform != null ? gateTransform.localPosition.y : 0f;
        float targetY = toState == GateState.Opening ? openLocalY : closedLocalY;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (gateTransform != null)
            {
                Vector3 pos = gateTransform.localPosition;
                pos.y = Mathf.Lerp(startY, targetY, t);
                gateTransform.localPosition = pos;
            }

            yield return null;
        }

        if (gateTransform != null)
        {
            Vector3 finalPos = gateTransform.localPosition;
            finalPos.y = targetY;
            gateTransform.localPosition = finalPos;
        }

        if (toState == GateState.Opening)
        {
            CurrentState = GateState.Open;
            DebugLog.Log("Gate: now Open");
        }
        else if (toState == GateState.Closing)
        {
            // Re-check: did new traffic arrive during the closing animation?
            if (activeTraffic > 0)
            {
                DebugLog.Log("Gate: traffic arrived during closing, reopening");
                StartTransition(GateState.Opening);
            }
            else
            {
                CurrentState = GateState.Closed;
                DebugLog.Log("Gate: now Closed");
            }
        }
    }

    // ---------- DEBUG/TEST ----------

    [ContextMenu("Debug: Request Passage")]
    private void DebugRequestPassage()
    {
        RequestPassage();
    }

    [ContextMenu("Debug: Notify Passage Complete")]
    private void DebugNotifyComplete()
    {
        NotifyPassageComplete();


    }

    [ContextMenu("Debug: Log Current State")]
    private void DebugLogState()
    {
        Debug.Log($"Gate current state: {CurrentState}, activeTraffic: {activeTraffic}");
    }
}