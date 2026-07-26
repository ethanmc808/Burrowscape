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

    [Header("Worker Production Scaling")]
    // See Docs/DiminishingReturnsProduction_Design.md. All three fields below are edited EXCLUSIVELY via
    // Burrowscape > Work Room Production Tuner, never through this component's own Inspector — rate/bonus
    // are broadcast identically to every work room from there, and recommendedTypes is a per-room grid
    // edited in the same window (kept out of the default Inspector via HideInInspector specifically so a
    // stray edit on the wrong prefab can't happen).
    [SerializeField] private float diminishingReturnsRate = 0.7f;
    [HideInInspector] [SerializeField] private List<BunnyType> recommendedTypes = new List<BunnyType>();
    [SerializeField] private float typeMatchProductionBonus = 0.25f;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Order bunnies became active producers in THIS room — a bunny's live index here is its slot rank
    // (0 = first/best). Recomputed via IndexOf on every production tick rather than cached, so removing a
    // bunny automatically shifts everyone behind it down a rank with no extra bookkeeping.
    private List<NPCBunny> activeWorkerOrder = new List<NPCBunny>();

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
            activeWorkerOrder.Remove(bunny);
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
            // Always appended at the back (lowest current rank) — including a bunny returning from an
            // eating trip. Total room output only depends on active COUNT, never on which bunny holds
            // which rank, so this is simplest-possible and can't be gamed by timing eating trips.
            if (!activeWorkerOrder.Contains(bunny))
                activeWorkerOrder.Add(bunny);

            Coroutine routine = StartCoroutine(ProduceCarrotsRoutine(bunny));
            activeProductionRoutines[bunny] = routine;
        }
    }

    // rank(bunny) is bunny's live index in activeWorkerOrder — 0 = 1st worker (100%), 1 = 2nd (70%), etc.
    private float ComputeProductionMultiplier(NPCBunny bunny)
    {
        int rank = activeWorkerOrder.IndexOf(bunny);
        float slotMultiplier = rank >= 0 ? WorkerProductionScaling.SlotMultiplier(diminishingReturnsRate, rank) : 1f;

        bool typeMatch = recommendedTypes != null && recommendedTypes.Contains(bunny.Type);
        float typeBonusMultiplier = typeMatch ? 1f + typeMatchProductionBonus : 1f;

        return slotMultiplier * typeBonusMultiplier;
    }

    private IEnumerator ProduceCarrotsRoutine(NPCBunny bunny)
    {
        // Fractional production (e.g. a 3rd-ranked worker at 49%) would otherwise round down to 0 every
        // tick and produce nothing at all — this banks the leftover fraction instead of losing it. Local
        // to this coroutine invocation, so it's naturally cleaned up when the coroutine stops.
        float carryover = 0f;

        while (true)
        {
            yield return new WaitForSeconds(productionInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeProductionRoutines.Remove(bunny);
                activeWorkerOrder.Remove(bunny);
                yield break;
            }

            float rawAmount = carrotsPerProduction * GradeMultiplier * bunny.ProductionMultiplier * ComputeProductionMultiplier(bunny);
            carryover = Mathf.Round((carryover + rawAmount) * 100f) / 100f; // keep to 2 decimal places, avoid float drift

            int producedAmount = Mathf.FloorToInt(carryover);
            if (producedAmount > 0)
            {
                carryover -= producedAmount;
                CarrotManager.Instance.AddCarrots(producedAmount);
                CarrotManager.Instance.RecordProduction(producedAmount);
            }
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
        activeWorkerOrder.Clear();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}