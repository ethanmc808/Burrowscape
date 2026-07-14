using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class GardenRoom : RoomBase, IJobRoom   // CHANGED from : MonoBehaviour
{
    [SerializeField] private List<RoomSpot> farmingSpots;

    // REMOVE: paths list (now inherited from RoomBase)
    // REMOVE: GetPathToSpot method entirely (replaced by RoomBase's entrance-specific methods)

    [SerializeField] private float productionInterval = 10f;
    [SerializeField] private int carrotsPerProduction = 1;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in farmingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
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

        if (activeProductionRoutines.TryGetValue(bunny, out Coroutine routine))
        {
            StopCoroutine(routine);
            activeProductionRoutines.Remove(bunny);
        }
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
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
}