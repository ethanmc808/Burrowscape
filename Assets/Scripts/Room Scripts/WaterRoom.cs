using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// Unlike the Garden/Cafeteria split, a Water Room is BOTH a job room (bunnies produce Water here,
// mirroring GardenRoom) AND has its own Drinking Spots (mirroring CafeteriaRoom) — so it needs two
// disambiguated spot pools. The production side (RequestSpot/ReleaseSpot/HasAvailableSpot) is what
// RoomClickHandler's GetComponent<IJobRoom>() picks up for manual job assignment via AssignmentUI,
// exactly like GardenRoom; the drinking side is a separate pool with its own names so the two never
// collide.
public class WaterRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("PottingSpot")]
    [SerializeField] private List<RoomSpot> productionSpots;
    [SpotNamePrefix("DrinkingSpot")]
    [SerializeField] private List<RoomSpot> drinkingSpots;

    [SerializeField] private float productionInterval = 10f;
    [SerializeField] private int waterPerProduction = 1;
    [SerializeField] private float waterRationingPoolAmount = 0f; // this room's contribution to the global Water Rationing Pool's max capacity

    [SerializeField] private float drinkingTickInterval = 2f;

    public float ProductionInterval => productionInterval;
    public int WaterPerProduction => waterPerProduction;
    public float WaterRationingPoolAmount => waterRationingPoolAmount;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Bunnies idled because this room lost Power (or Water — a WaterRoom producing water can still be
    // configured to consume Power) while actively working the production side. See
    // RoomBase.RecheckOperational / IJobRoom.OnRoomShutdown.
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    private WaterRationingManager waterRationingManager;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterWaterRoom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");

        waterRationingManager = WaterRationingManager.EnsureInstance();
        waterRationingManager.RegisterProducer(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterWaterRoom(this);

        if (waterRationingManager != null)
        {
            waterRationingManager.UnregisterProducer(this);
            waterRationingManager = null;
        }
    }

    // ---------- PRODUCTION (IJobRoom) ----------

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in productionSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in productionSpots)
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

    // Bunny is heading off to drink/eat but keeps its reserved spot — the room shouldn't
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
        // Arrived while this room is dark (e.g. mid-walk when Power cut out) — idle immediately instead
        // of starting production; OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        if (!activeProductionRoutines.ContainsKey(bunny))
        {
            Coroutine routine = StartCoroutine(ProduceWaterRoutine(bunny));
            activeProductionRoutines[bunny] = routine;
        }
    }

    private IEnumerator ProduceWaterRoutine(NPCBunny bunny)
    {
        while (true)
        {
            yield return new WaitForSeconds(productionInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeProductionRoutines.Remove(bunny);
                yield break;
            }

            WaterManager.Instance.AddWater(Mathf.RoundToInt(waterPerProduction * GradeMultiplier)); // still feeds the simple stockpile bunnies drink from
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

    // ---------- DRINKING ----------

    public RoomSpot RequestDrinkingSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in drinkingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseDrinkingSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableDrinkingSpot()
    {
        foreach (RoomSpot spot in drinkingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    public void NotifyBunnyReadyToDrink(NPCBunny bunny)
    {
        StartCoroutine(DrinkingRoutine(bunny));
    }

    private IEnumerator DrinkingRoutine(NPCBunny bunny)
    {
        while (!bunny.IsFullyHydrated())
        {
            yield return new WaitForSeconds(drinkingTickInterval);

            // Try the normal WaterManager stockpile first, same as always. Only if that's empty does
            // drinking fall back to the Water Rationing Pool — a bunny should never touch that pool
            // while there's still water in the normal one. Instance (not EnsureInstance()) is
            // deliberate: this WaterRoom's own OnEnable already guarantees the manager exists by the
            // time any bunny gets here.
            if (WaterManager.Instance.TryConsumeWater())
                bunny.ReceiveWaterHydration();
            else if (WaterRationingManager.Instance != null && WaterRationingManager.Instance.TryDraw(1f))
                bunny.ReceiveWaterHydration();
        }

        bunny.FinishDrinkingAndReturnToPrevious();
    }
}
