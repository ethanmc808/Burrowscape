using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Shared Evacuate -> Swap -> Resettle implementation behind both room merging and room upgrading — see
// RoomMergeUpgrade_DesignDoc.md at the project root. Both operations are fundamentally "replace N
// existing rooms with 1 new room, preserving occupants"; MergeRooms/UpgradeRoom just differ in what
// they pass to the shared SwapRooms coroutine (N=2-3 rooms with Grade unchanged and width increasing,
// vs. N=1 room with width unchanged and Grade increasing).
public class RoomTransitionService : MonoBehaviour
{
    public static RoomTransitionService Instance { get; private set; }

    // Bookkeeping for one evacuated bunny between the Evacuate and Resettle steps — mirrors LiftRoom's
    // LiftCall class for the same reason (a plain per-item record, not worth a ValueTuple here since
    // it's read across two separate loops in SwapRooms).
    private class Evacuee
    {
        public readonly NPCBunny bunny;
        public readonly RoomTransitionRole role;
        public Evacuee(NPCBunny bunny, RoomTransitionRole role)
        {
            this.bunny = bunny;
            this.role = role;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void MergeRooms(List<RoomBase> rooms, RoomDefinition target)
    {
        if (rooms == null || rooms.Count == 0 || target == null) return;
        StartCoroutine(SwapRooms(rooms, target));
    }

    public void UpgradeRoom(RoomBase room, RoomDefinition target)
    {
        if (room == null || target == null) return;
        StartCoroutine(SwapRooms(new List<RoomBase> { room }, target));
    }

    private IEnumerator SwapRooms(List<RoomBase> rooms, RoomDefinition target)
    {
        // 1. Wait for quiescence. Never forcibly redirect anyone — just defer the whole operation until
        // every bunny associated with these rooms has settled into a claimed RoomSpot (or dropped back
        // to plain Idle), so nothing has to be interrupted mid-walk or mid-lift-trip.
        while (DwellerRoster.Instance != null && DwellerRoster.Instance.IsAnyBunnyTransientlyInRooms(rooms))
            yield return null;

        // 2. Evacuate. Gather everyone tied to any of the rooms being replaced BEFORE anything is
        // destroyed, and release each one's claim on it.
        List<NPCBunny> associated = DwellerRoster.Instance != null
            ? DwellerRoster.Instance.GetBunniesAssociatedWithRooms(rooms)
            : new List<NPCBunny>();

        Debug.Log($"[PathDebug] SwapRooms: rooms=[{string.Join(", ", rooms.ConvertAll(r => r != null ? $"{r.name}({r.GetInstanceID()})" : "NULL"))}], associated bunnies=[{string.Join(", ", associated.ConvertAll(b => b.name))}]");

        List<Evacuee> evacuees = new List<Evacuee>();
        foreach (NPCBunny bunny in associated)
        {
            RoomTransitionRole role = bunny.EvacuateForRoomTransition(rooms);
            Debug.Log($"[PathDebug] SwapRooms: evacuated {bunny.name} with role={role}");
            evacuees.Add(new Evacuee(bunny, role));
        }

        // 3. Swap. Unregister explicitly rather than relying on OnDisable (which Unity defers to
        // end-of-frame) so nothing can query BaseLayoutManager mid-frame and see both the dying room(s)
        // and the new one registered at once. Position: a merge uses the midpoint X of the combined
        // span; an upgrade (single room, unchanged width) keeps the source room's exact position.
        Vector3 position = ComputeSwapPosition(rooms);

        foreach (RoomBase room in rooms)
        {
            if (room == null) continue;
            BaseLayoutManager.Instance?.UnregisterRoom(room);
            Destroy(room.gameObject);
        }

        // Existing RoomBase.OnEnable -> BaseLayoutManager.RegisterRoom handles registration, same as a
        // normal player-placed room (BuildModeController.TryConfirmPlacement).
        GameObject newInstance = Instantiate(target.prefab, position, Quaternion.Euler(0f, 180f, 0f));
        RoomBase newRoom = newInstance.GetComponent<RoomBase>();

        Debug.Log($"[PathDebug] SwapRooms: newRoom={(newRoom != null ? newRoom.name : "NULL")}, isIJobRoom={(newRoom is IJobRoom)}");

        // 4. Resettle. Relocate must run before any resettle call for every evacuee — it's what stops
        // the next path-building call from either dereferencing the just-destroyed old room or treating
        // a null currentRoom as "arriving from the base entrance." It's a no-op for anyone who wasn't
        // physically standing in one of the old rooms to begin with (see its own doc comment).
        foreach (Evacuee evacuee in evacuees)
        {
            if (evacuee.bunny == null || newRoom == null) continue;

            evacuee.bunny.RelocateToRoomAfterTransition(newRoom, rooms);

            switch (evacuee.role)
            {
                case RoomTransitionRole.Working:
                    if (newRoom is IJobRoom jobRoom)
                        evacuee.bunny.ResettleJobWhenSafe(jobRoom);
                    break;

                case RoomTransitionRole.Sleeping:
                    evacuee.bunny.RequestBedroomFromCurrentPosition();
                    break;

                // Relaxing / None: nothing further — TryClaimRelaxSpot already re-polls every Idle tick
                // and will find the new room on its own.
            }
        }

        // 5. Chain-check. The auto-merge check normally only runs right after BuildModeController places
        // a room — but an upgrade can make a room newly merge-eligible too (e.g. upgrade Room B to Grade
        // 2 so it now matches an already-Grade-2 Room A sitting next to it), and that path never went
        // through BuildModeController at all. Re-running the same check here catches that case, and as a
        // side effect also catches a merge leaving behind a third neighbor that's now eligible too.
        // Recursing via MergeRooms (a fresh StartCoroutine) rather than inlining is deliberate — it goes
        // through the exact same Evacuate/Swap/Resettle path a chained merge already needs, including a
        // fresh quiescence wait for whoever was just resettled into newRoom.
        if (newRoom != null && RoomMergeResolver.TryResolveMerge(newRoom, out List<RoomBase> chainRooms, out RoomDefinition chainTarget))
            MergeRooms(chainRooms, chainTarget);
    }

    private Vector3 ComputeSwapPosition(List<RoomBase> rooms)
    {
        RoomBase reference = rooms[0];
        float y = reference.transform.position.y;
        float z = reference.transform.position.z;

        if (rooms.Count == 1)
            return new Vector3(reference.transform.position.x, y, z);

        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (RoomBase room in rooms)
        {
            RoomPlacementValidator.GetInterval(room, out float roomMin, out float roomMax);
            min = Mathf.Min(min, roomMin);
            max = Mathf.Max(max, roomMax);
        }

        return new Vector3((min + max) / 2f, y, z);
    }
}
