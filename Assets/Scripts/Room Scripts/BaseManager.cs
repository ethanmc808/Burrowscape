using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BaseManager : MonoBehaviour
{
    public static BaseManager Instance { get; private set; }

    private List<CafeteriaRoom> cafeterias = new List<CafeteriaRoom>();
    private List<LivingRoom> livingRooms = new List<LivingRoom>();

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

    public CafeteriaRoom FindNearestCafeteria(Vector3 fromPosition)
    {
        CafeteriaRoom nearest = null;
        float nearestDist = float.MaxValue;

        foreach (CafeteriaRoom cafeteria in cafeterias)
        {
            if (cafeteria == null) continue; // safety check for destroyed rooms

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
}