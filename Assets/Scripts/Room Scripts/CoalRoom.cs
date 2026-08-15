using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// Structural copy of GardenRoom's production shape, producing Power instead of Carrots. The only real
// difference: instead of adding to a passive stockpile, it reports a WEIGHTED total to the global
// PowerManager every time a bunny starts/stops actively Working here, since Power production is something
// other rooms actively arbitrate against in real time, not just a counter to add to. The weight (not a
// plain headcount) folds in per-worker slot rank (diminishing returns), type-match bonus, and
// bunny.ProductionMultiplier — see Docs/DiminishingReturnsProduction_Design.md and
// WorkerProductionScaling. PowerManager just multiplies this weight by PowerProductionAmount/Interval and
// GradeMultiplier; it has no idea how the weight was computed.
public class CoalRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("ShovelingSpot")]
    [SerializeField] private List<RoomSpot> shovelingSpots;

    [Header("Worker Production Scaling")]
    // See Docs/DiminishingReturnsProduction_Design.md. diminishingReturnsRate/typeMatchProductionBonus
    // are edited EXCLUSIVELY via Burrowscape > Work Room Production Tuner, never through this component's
    // own Inspector — broadcast identically to every work room from there. recommendedTypes itself now
    // lives on RoomBase (consolidated — see its own comment there), but is still also editable via the
    // same Tuner window's per-room grid.
    [SerializeField] private float diminishingReturnsRate = 0.7f;
    [SerializeField] private float typeMatchProductionBonus = 0.25f;

    [Header("Room Ambient (see AudioManager — proximity-based, both audible only near the camera)")]
    [Tooltip("Plays continuously the whole time this room exists, regardless of whether any bunny is working here — e.g. a low machine hum. Kept as the same field/clip assignment this room already had before the occupancy-gated field below was split out.")]
    [SerializeField] private AudioClip workAmbientClip;
    [Tooltip("Only plays while at least one bunny is actively Working here — ref-counted per active worker.")]
    [SerializeField] private AudioClip workingAmbientClip;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Order bunnies became active producers in THIS room — a bunny's live index here is its slot rank
    // (0 = first/best). Recomputed fresh every time the weight is reported, so removing a bunny
    // automatically shifts everyone behind it down a rank with no extra bookkeeping.
    private List<NPCBunny> activeWorkerOrder = new List<NPCBunny>();

    // Bunnies idled because THIS room shut down (lost Power/Water) while they were actively working —
    // resumed automatically by OnRoomRestored. Mirrors GardenRoom/WaterRoom.
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    // Always-on ambient (workAmbientClip) is registered once for this room's whole lifetime, NOT tied to
    // worker presence like workingAmbientClip's ref-counted Increment/Decrement in NotifyBunnyReadyToWork/
    // StopProductionRoutine below — see AudioManager's proximity-loop system for the shared falloff logic.
    protected override void OnEnable()
    {
        base.OnEnable();
        AudioManager.EnsureInstance().IncrementProximityLoop((this, "Ambient"), workAmbientClip, transform, AudioCategory.Ambient);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        AudioManager.EnsureInstance().DecrementProximityLoop((this, "Ambient"));
    }

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in shovelingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in shovelingSpots)
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

    // Bunny is heading off to eat/drink/sleep but keeps its reserved spot — the room shouldn't hand
    // that spot to anyone else while it's gone, just pause production.
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
            ReportWeightToPowerManager();
            StopWorkXPRoutine(bunny);
            AudioManager.EnsureInstance().DecrementProximityLoop((this, "Working"));
        }
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived while this room itself is dark (e.g. mid-walk when it shut down) — idle immediately
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

            Coroutine routine = StartCoroutine(ProducePowerRoutine(bunny));
            activeProductionRoutines[bunny] = routine;
            ReportWeightToPowerManager();
            StartWorkXPRoutine(bunny);
            AudioManager.EnsureInstance().IncrementProximityLoop((this, "Working"), workingAmbientClip, transform, AudioCategory.Ambient);
        }
    }

    private IEnumerator ProducePowerRoutine(NPCBunny bunny)
    {
        while (true)
        {
            yield return new WaitForSeconds(PowerProductionInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeProductionRoutines.Remove(bunny);
                activeWorkerOrder.Remove(bunny);
                ReportWeightToPowerManager();
                StopWorkXPRoutine(bunny);
                AudioManager.EnsureInstance().DecrementProximityLoop((this, "Working"));
                yield break;
            }

            // No resource is consumed to produce Power — PowerManager reads PowerProductionAmount/
            // PowerProductionInterval directly off this room, multiplied by the weighted total this room
            // last reported via ReportWeightToPowerManager, while it's registered as active; this loop
            // just needs to keep ticking for as long as the bunny stays Working.
        }
    }

    // Recomputes this room's full weighted total from scratch (every active worker's slot-rank multiplier
    // × type-match bonus × their own ProductionMultiplier) and pushes it to PowerManager. Called on every
    // start/stop rather than incrementally, same reasoning as CarrotManager/PopulationManager's "recompute
    // from scratch" pattern elsewhere in this codebase — avoids drift and stays correct regardless of
    // which specific bunny started/stopped.
    private void ReportWeightToPowerManager()
    {
        float totalWeight = 0f;
        for (int i = 0; i < activeWorkerOrder.Count; i++)
        {
            NPCBunny worker = activeWorkerOrder[i];
            float slotMultiplier = WorkerProductionScaling.SlotMultiplier(diminishingReturnsRate, i);
            bool typeMatch = recommendedTypes != null && recommendedTypes.Contains(worker.Type);
            float typeBonusMultiplier = typeMatch ? 1f + typeMatchProductionBonus : 1f;
            totalWeight += slotMultiplier * typeBonusMultiplier * worker.ProductionMultiplier;
        }

        PowerManager.EnsureInstance().SetProducerWeight(this, totalWeight);
    }

    // ---------- IJobRoom shutdown/restore ----------
    // Only reachable if this room is ever configured to itself consume Power or Water (not the normal
    // case for a producer, but the interface is generic — every IJobRoom implements these).

    public void OnRoomShutdown()
    {
        foreach (KeyValuePair<NPCBunny, Coroutine> kvp in activeProductionRoutines)
        {
            StopCoroutine(kvp.Value);
            kvp.Key.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(kvp.Key);
            AudioManager.EnsureInstance().DecrementProximityLoop((this, "Working")); // one per worker that was actively producing, matching IncrementProximityLoop in NotifyBunnyReadyToWork
        }
        activeProductionRoutines.Clear();
        activeWorkerOrder.Clear();
        PowerManager.EnsureInstance().SetProducerWeight(this, 0f); // reset this room's weight to zero, regardless of how many were active
        StopAllWorkXPRoutines();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}
