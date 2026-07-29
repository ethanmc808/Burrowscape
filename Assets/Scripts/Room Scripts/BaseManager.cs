using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BaseManager : MonoBehaviour
{
    public static BaseManager Instance { get; private set; }

    private List<CafeteriaRoom> cafeterias = new List<CafeteriaRoom>();
    private List<LivingRoom> livingRooms = new List<LivingRoom>();
    private List<WaterRoom> waterRooms = new List<WaterRoom>();
    private List<Bedroom> bedrooms = new List<Bedroom>();
    // Combat scaffolding only (see Combat_DesignDoc.md) — registered the same way as every other room
    // type here so the future deploy-UX "nearest Guard Room" lookup has this list ready to use; no such
    // lookup exists yet, deploy logic itself isn't designed.
    private List<GuardRoom> guardRooms = new List<GuardRoom>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RegisterCafeteria(CafeteriaRoom cafeteria)
    {
        if (!cafeterias.Contains(cafeteria))
            cafeterias.Add(cafeteria);
    }

    public void UnregisterCafeteria(CafeteriaRoom cafeteria)
    {
        cafeterias.Remove(cafeteria);
    }

    // Nearest cafeteria with an actual open eating spot, not just nearest cafeteria — mirrors
    // FindNearestLivingRoomWithSpot's shape. Fixes a pre-existing gap where a hungry bunny stuck near a
    // full cafeteria never checked a farther one with room.
    public CafeteriaRoom FindNearestCafeteriaWithSpot(Vector3 fromPosition)
    {
        CafeteriaRoom nearest = null;
        float nearestDist = float.MaxValue;

        foreach (CafeteriaRoom cafeteria in cafeterias)
        {
            if (cafeteria == null) continue; // safety check for destroyed rooms
            if (!cafeteria.HasAvailableSpot()) continue;

            float dist = Vector3.Distance(fromPosition, cafeteria.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = cafeteria;
            }
        }

        return nearest;
    }

    public void RegisterLivingRoom(LivingRoom room)
    {
        if (!livingRooms.Contains(room))
            livingRooms.Add(room);
    }

    public void UnregisterLivingRoom(LivingRoom room)
    {
        livingRooms.Remove(room);
    }

    public void RegisterGuardRoom(GuardRoom room)
    {
        if (!guardRooms.Contains(room))
            guardRooms.Add(room);
    }

    public void UnregisterGuardRoom(GuardRoom room)
    {
        guardRooms.Remove(room);
    }

    // Nearest room with an actual open spot, not just nearest room — a full living room right next
    // door is useless to an idle bunny, and checking availability here (rather than letting the bunny
    // try-and-fail on the nearest one) lets NPCBunny tell "no spot anywhere on the base" apart from
    // "the closest one happens to be full," which is what triggers the fallback pacing state.
    public LivingRoom FindNearestLivingRoomWithSpot(Vector3 fromPosition)
    {
        LivingRoom nearest = null;
        float nearestDist = float.MaxValue;

        foreach (LivingRoom room in livingRooms)
        {
            if (room == null) continue; // safety check for destroyed rooms
            if (!room.HasAvailableSpot()) continue;

            float dist = Vector3.Distance(fromPosition, room.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = room;
            }
        }

        return nearest;
    }

    public void RegisterWaterRoom(WaterRoom room)
    {
        if (!waterRooms.Contains(room))
            waterRooms.Add(room);
    }

    public void UnregisterWaterRoom(WaterRoom room)
    {
        waterRooms.Remove(room);
    }

    // Nearest Water Room with an open drinking spot — same shape as FindNearestLivingRoomWithSpot.
    public WaterRoom FindNearestWaterRoomWithDrinkingSpot(Vector3 fromPosition)
    {
        WaterRoom nearest = null;
        float nearestDist = float.MaxValue;

        // TEMP diagnostic — remove once the "thirsty but no drinking spot" false-warning bug is root-caused.
        Debug.Log($"[ThirstDebug] FindNearestWaterRoomWithDrinkingSpot from {fromPosition}: {waterRooms.Count} registered water room(s).");
        foreach (WaterRoom room in waterRooms)
        {
            Debug.Log($"[ThirstDebug]   {(room != null ? room.name : "NULL")} hasSpot={(room != null ? room.HasAvailableDrinkingSpot().ToString() : "N/A")} pos={(room != null ? room.transform.position.ToString() : "N/A")}");
        }

        foreach (WaterRoom room in waterRooms)
        {
            if (room == null) continue; // safety check for destroyed rooms
            if (!room.HasAvailableDrinkingSpot()) continue;

            float dist = Vector3.Distance(fromPosition, room.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = room;
            }
        }

        Debug.Log($"[ThirstDebug] result: {(nearest != null ? nearest.name : "NULL")}");
        return nearest;
    }

    public void RegisterBedroom(Bedroom room)
    {
        if (!bedrooms.Contains(room))
            bedrooms.Add(room);
    }

    public void UnregisterBedroom(Bedroom room)
    {
        bedrooms.Remove(room);
    }

    // Nearest Bedroom with an open sleeping spot — same shape as FindNearestLivingRoomWithSpot.
    public Bedroom FindNearestBedroomWithSpot(Vector3 fromPosition)
    {
        Bedroom nearest = null;
        float nearestDist = float.MaxValue;

        foreach (Bedroom room in bedrooms)
        {
            if (room == null) continue; // safety check for destroyed rooms
            if (!room.HasAvailableSpot()) continue;

            float dist = Vector3.Distance(fromPosition, room.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = room;
            }
        }

        return nearest;
    }
}