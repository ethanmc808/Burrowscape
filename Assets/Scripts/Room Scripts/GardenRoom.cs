using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class GardenRoom : RoomBase, IJobRoom   // CHANGED from : MonoBehaviour
{
    [SpotNamePrefix("FarmingSpot")]
    [SerializeField] private List<RoomSpot> farmingSpots;

    // REMOVE: paths list (now inherited from RoomBase)
    // REMOVE: GetPathToSpot method entirely (replaced by RoomBase's entrance-specific methods)

    [SerializeField] private float productionInterval = 10f;
    [SerializeField] private int carrotsPerProduction = 1;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Bunnies idled because this room lost Power or Water while they were actively working — resumed
    // automatically by OnRoomRestored. See RoomBase.RecheckOperational / IJobRoom.OnRoomShutdown.
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in farmingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }

        string spotDump = string.Join(", ", farmingSpots.ConvertAll(s => s != null ? $"{s.name}(id={s.GetInstanceID()}, occupied={s.IsOccupied})" : "NULL"));
        Debug.Log($"[PathDebug] {name} (id={GetInstanceID()}) RequestSpot FAILED for {bunny.name}: farmingSpots.Count={farmingSpots.Count}, spots=[{spotDump}]");
        return null;
    }

    public List<Transform> GetPathToSpot(RoomSpot spot, Vector3 fromPosition)
    {
        RoomPath bestPath = null;
        float bestDist = float.MaxValue;

        foreach (RoomPath path in paths)
        {
            if (path.targetSpot != spot) continue;

            float dist = Vector3.Distance(fromPosition, path.entrance.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPath = path;
            }
        }

        List<Transform> fullPath = new List<Transform>();

        if (bestPath == null)
        {
            // Fallback: no path defined, just go straight to the spot (shouldn't happen once set up)
            fullPath.Add(spot.transform);
            return fullPath;
        }

        fullPath.Add(bestPath.entrance);
        fullPath.AddRange(bestPath.waypoints);
        fullPath.Add(spot.transform);
        return fullPath;
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in farmingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        StopProductionRoutine(bunny);
    }

    // Bunny is heading off to eat but keeps its reserved spot — the room shouldn't
    // hand that spot to anyone else while it's gone, just pause production.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        StopProductionRoutine(bunny);
    }

    private void StopProductionRoutine(NPCBunny bunny)
    {
        if (activeProductionRoutines.TryGetValue(bunny, out Coroutine routine))
        {
            StopCoroutine(routine);
            activeProductionRoutines.Remove(bunny);
        }
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived while this room is dark (e.g. mid-walk when Power/Water cut out) — idle immediately
        // instead of starting production; OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        if (!activeProductionRoutines.ContainsKey(bunny))
        {
            Coroutine routine = StartCoroutine(ProduceCarrotsRoutine(bunny));
            activeProductionRoutines[bunny] = routine;
        }
    }

    private IEnumerator ProduceCarrotsRoutine(NPCBunny bunny)
    {
        while (true)
        {
            yield return new WaitForSeconds(productionInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeProductionRoutines.Remove(bunny);
                yield break;
            }

            CarrotManager.Instance.AddCarrots(carrotsPerProduction);
        }
    }

    // ---------- IJobRoom shutdown/restore ----------

    public void OnRoomShutdown()
    {
        foreach (KeyValuePair<NPCBunny, Coroutine> kvp in activeProductionRoutines)
        {
            StopCoroutine(kvp.Value);
            kvp.Key.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(kvp.Key);
        }
        activeProductionRoutines.Clear();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}