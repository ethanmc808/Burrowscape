using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DwellerRoster : MonoBehaviour
{
    public static DwellerRoster Instance { get; private set; }

    private List<NPCBunny> allBunnies = new List<NPCBunny>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(NPCBunny bunny)
    {
        if (!allBunnies.Contains(bunny))
            allBunnies.Add(bunny);
    }

    public void Unregister(NPCBunny bunny)
    {
        allBunnies.Remove(bunny);
    }

    // Read by SaveManager — every resident bunny, unfiltered (including ones out Foraging, mid-transit,
    // etc.) since a save needs to capture all of them, not just whichever subset some other query cares
    // about.
    public List<NPCBunny> GetAllBunnies() => new List<NPCBunny>(allBunnies);

    // Candidate pool for the Patient UI — any resident bunny below max HP, excluding ones not currently
    // controllable: still queued/awaiting approval (mirrors GetUnassignedBunnies' HasEnteredBase guard),
    // out Foraging (parked offscreen), already claimed by a Hospital bed, and Fainted (mid-battle, can't
    // walk anywhere until StopDefendingAndReturn revives it — see NPCBunny.HealHP's own Fainted guard for
    // why healing one directly isn't allowed either). Sorted ascending by HP percentage (lowest first) —
    // PatientUI relies on this ordering rather than re-sorting itself.
    public List<NPCBunny> GetInjuredBunnies()
    {
        return allBunnies
            .Where(b => b.HasEnteredBase && b.CurrentState != BunnyState.Foraging
                && b.CurrentState != BunnyState.Fainted && !b.IsHospitalized
                && b.HPValue < b.Stats.HP)
            .OrderBy(b => (float)b.HPValue / b.Stats.HP)
            .ToList();
    }

    // Every bunny currently claimed to a bed in `room` (walking there or already Recovering) — mirrors
    // GetBunniesAssignedTo's claim-based shape, just keyed on ClaimedHospitalRoom instead of
    // AssignedJobRoom, for PatientUI's own assigned-list-with-unassign panel.
    public List<NPCBunny> GetPatientsIn(HospitalRoom room)
    {
        return allBunnies.Where(b => b.ClaimedHospitalRoom == room).ToList();
    }

    // Candidate pool for GuardDeployUI's manual-reinforcement panel — any resident bunny safely pullable
    // mid-activity to go fight: Idle (holds no claim), Working, or Relaxing (both leave their claim
    // intact when interrupted — see NPCBunny.BeginDefending's own comment — so ReturnToPreviousActivity
    // walks them straight back once the invasion clears). Deliberately excludes Sleeping/Eating/Drinking:
    // those are driven by their own room's coroutine, which has no idea CurrentState just changed out
    // from under it (same caution CanDepartForForaging already documents for this exact class of state).
    // Also excludes anyone already Defending/Fainted (already helping, or not fit to), Recovering
    // (hospitalized), Foraging (away), or still queued/awaiting gate approval. Also excludes kid bunnies
    // (see the Kid Bunny Growth plan) — unlike AssignToJob, BeginDefending itself has no IsKidBunny guard
    // of its own (a manual deploy is a direct RoomBase.ClaimCombatSpot + NPCBunny.BeginDefending call, it
    // never goes through AssignToJob), so this filter is the ONLY thing standing between a kid and an
    // active invasion — confirmed bug 2026-08-12: without it, GuardDeployUI would happily let the player
    // send a kid bunny to fight.
    public List<NPCBunny> GetReinforceableBunnies()
    {
        return allBunnies.Where(b => b.HasEnteredBase && !b.IsKidBunny &&
            (b.CurrentState == BunnyState.Idle || b.CurrentState == BunnyState.Working || b.CurrentState == BunnyState.Relaxing)
        ).ToList();
    }

    public List<NPCBunny> GetUnassignedBunnies()
    {
        // Exclude bunnies still spawned-but-queued at the gate (or mid-approval) — they haven't
        // entered the base yet, so assigning them to a job would route them straight from the base
        // entrance to the job room, skipping the gate/queue entirely. Also exclude bunnies currently out
        // Foraging (parked offscreen — see NPCBunny.DepartForForaging) — they have no job and
        // HasEnteredBase is still true, so without this they'd wrongly appear assignable while away.
        // Also exclude kid bunnies (see the Kid Bunny Growth plan) — AssignToJob itself already rejects
        // one (isKidBunny guard), but without this filter AssignmentUI still listed them as clickable and
        // AssignAndClose closed the panel unconditionally regardless of whether the assign succeeded, so
        // clicking a kid bunny silently did nothing while looking like it worked. Same "hide, don't let
        // them click into a silent failure" precedent as Bedroom's IsEligibleForBreeding filter just below.
        return allBunnies.Where(b => !b.IsAssignedToJob && b.HasEnteredBase && !b.IsKidBunny && b.CurrentState != BunnyState.Foraging).ToList();
    }

    // Every resident bunny eligible to be sent Foraging — HasEnteredBase (a real dweller, not still
    // queued/awaiting approval), not already out on a trip, and not mid-transit toward/through the gate
    // for an existing trip. Unlike GetUnassignedBunnies, a bunny with a job/relax/sleep claim IS
    // included — NPCBunny.CanDepartForForaging/ReleaseAllClaimsForDeparture handle releasing that claim
    // at dispatch time, same as sending a working bunny on break.
    public List<NPCBunny> GetForageableBunnies()
    {
        return allBunnies.Where(b => b.HasEnteredBase && b.CanDepartForForaging()).ToList();
    }
    public List<NPCBunny> GetBunniesAssignedTo(IJobRoom room)
    {
        return allBunnies.Where(b => b.AssignedJobRoom == room).ToList();
    }

    // Bunnies physically working or relaxing in `room` right now — the auto-defend trigger set for
    // InvasionManager. Eating/Drinking/Sleeping happen in a different room (Cafeteria/WaterRoom/Bedroom),
    // so those states are deliberately excluded here, unlike GetBunniesAssignedTo which is claim-based.
    public List<NPCBunny> GetBunniesCurrentlyInRoom(RoomBase room)
    {
        List<NPCBunny> matches = allBunnies.Where(b =>
            (b.CurrentState == BunnyState.Working && b.AssignedJobRoom is RoomBase workRoom && workRoom == room) ||
            (b.CurrentState == BunnyState.Relaxing && b.ClaimedRelaxRoom == room)
        ).ToList();

        // TEMP — chasing "second invasion doesn't recruit defenders" bug. Dumps every bunny's state/room
        // so we can see exactly why a bunny that looks "in the room" doesn't match this filter. Remove
        // once root-caused.
        Debug.Log($"[VFXDEBUG] GetBunniesCurrentlyInRoom({room.name}) matched {matches.Count}: {string.Join(", ", matches.Select(b => b.name))}");
        foreach (NPCBunny b in allBunnies)
            Debug.Log($"[VFXDEBUG]   {b.name}: state={b.CurrentState}, jobRoom={(b.AssignedJobRoom as RoomBase)?.name ?? "null"}, relaxRoom={(b.ClaimedRelaxRoom as RoomBase)?.name ?? "null"}");

        return matches;
    }

    // Every bunny currently posted at `room`'s CombatSpots — auto-defenders and deployed guards alike
    // (see NPCBunny.BeginDefending). Used by InvasionManager to recall everyone once a room's invasion clears.
    public List<NPCBunny> GetBunniesDefendingRoom(RoomBase room)
    {
        return allBunnies.Where(b => b.DefendingRoom == room).ToList();
    }

    // Every Fainted bunny still IsDefending ANYWHERE across the whole base, regardless of which room it
    // fainted in — used by InvasionManager's whole-raid-cleared sweep, not the per-room recall. A room's
    // own enemy group can empty out (die, or relocate elsewhere) while the raid as a whole is still very
    // much ongoing, and a fainted bunny must stay down until every enemy across the whole raid is
    // defeated, not just the ones that happened to be in its own room.
    public List<NPCBunny> GetAllFaintedDefenders()
    {
        return allBunnies.Where(b => b.CurrentState == BunnyState.Fainted && b.IsDefending).ToList();
    }

    // Used by RoomTransitionService's Evacuate step to gather everyone tied to any of the rooms being
    // replaced by a merge/upgrade — reuses the same per-bunny check RoomBase.CanBeDeleted relies on
    // (via IsRoomOccupied, below), just collecting names instead of only checking "any at all."
    public List<NPCBunny> GetBunniesAssociatedWithRooms(List<RoomBase> rooms)
    {
        return allBunnies.Where(b => rooms.Any(r => b.IsAssociatedWithRoom(r))).ToList();
    }

    // Used by RoomTransitionService's quiescence wait — true while anyone is still mid-flight into,
    // through, or actively eating/drinking in any of the rooms about to be replaced.
    public bool IsAnyBunnyTransientlyInRooms(List<RoomBase> rooms)
    {
        return allBunnies.Any(b => rooms.Any(r => b.IsTransientlyInRoom(r)));
    }

    // Used by RoomBase.CanBeDeleted to block demolishing a room that's currently in use.
    public bool IsRoomOccupied(RoomBase room)
    {
        // Guarded as a block, not per-line, so the (DebugState()/IsAssociatedWithRoom() per bunny)
        // work itself is skipped when verbose logging is off, not just the resulting Debug.Log call.
        if (DebugLog.Verbose)
        {
            Debug.Log($"[OccupancyCheck] room={room.name} @ {room.transform.position}");
            foreach (NPCBunny b in allBunnies)
                Debug.Log($"[OccupancyCheck]   {b.DebugState()}, associated={b.IsAssociatedWithRoom(room)}");
        }

        return allBunnies.Any(b => b.IsAssociatedWithRoom(room));
    }
}