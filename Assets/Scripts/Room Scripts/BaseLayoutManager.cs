using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BaseLayoutManager : MonoBehaviour
{
    public static BaseLayoutManager Instance { get; private set; }

    [SerializeField] private Transform baseEntrance;
    public Transform BaseEntrance => baseEntrance;

    private Dictionary<int, List<RoomBase>> roomsByFloor = new Dictionary<int, List<RoomBase>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterRoom(RoomBase room)
    {
        if (!roomsByFloor.ContainsKey(room.FloorIndex))
            roomsByFloor[room.FloorIndex] = new List<RoomBase>();

        if (!roomsByFloor[room.FloorIndex].Contains(room))
            roomsByFloor[room.FloorIndex].Add(room);
    }

    public void UnregisterRoom(RoomBase room)
    {
        if (roomsByFloor.ContainsKey(room.FloorIndex))
            roomsByFloor[room.FloorIndex].Remove(room);
    }

    private List<RoomBase> GetOrderedRooms(int floor)
    {
        if (!roomsByFloor.ContainsKey(floor)) return new List<RoomBase>();
        return roomsByFloor[floor].OrderBy(r => r.GridX).ToList();
    }

    public List<Transform> GetRouteToSpot(RoomBase startRoom, RoomSpot startSpot, RoomBase targetRoom, RoomSpot targetSpot)
    {
        List<Transform> fullPath = new List<Transform>();

        // Starting from the base entrance (outside arrival) rather than a known room
        if (startRoom == null)
        {
            List<RoomBase> orderedFromEntrance = GetOrderedRooms(targetRoom.FloorIndex);
            int targetIndexFromEntrance = orderedFromEntrance.IndexOf(targetRoom);

            if (targetIndexFromEntrance == -1)
            {
                Debug.LogWarning("Target room not registered in BaseLayoutManager.");
                fullPath.Add(targetSpot.transform);
                return fullPath;
            }

            fullPath.Add(baseEntrance);

            // BaseEntrance sits at the visually-leftmost position, which is the LAST index
            // in our ascending-GridX-sorted list (since higher X = visually left in this project).
            for (int i = orderedFromEntrance.Count - 1; i > targetIndexFromEntrance; i--)
            {
                fullPath.AddRange(orderedFromEntrance[i].GetPassThroughPath(enteringFromLeft: true));
            }

            fullPath.AddRange(targetRoom.GetPathBetweenEntranceAndSpot(targetSpot, targetRoom.LeftEntrance));
            return fullPath;
        }

        if (startRoom == targetRoom)
        {
            fullPath.Add(startSpot != null ? startSpot.transform : startRoom.transform);
            fullPath.Add(targetSpot.transform);
            return fullPath;
        }

        if (startRoom.FloorIndex != targetRoom.FloorIndex)
        {
            Debug.LogWarning("Cross-floor pathing (lifts) not yet supported.");
            return fullPath;
        }

        List<RoomBase> ordered = GetOrderedRooms(startRoom.FloorIndex);
        int startIndex = ordered.IndexOf(startRoom);
        int targetIndex = ordered.IndexOf(targetRoom);

        if (startIndex == -1 || targetIndex == -1)
        {
            Debug.LogWarning("Room not registered in BaseLayoutManager.");
            return fullPath;
        }

        bool movingRight = targetIndex > startIndex;
        int step = movingRight ? 1 : -1;

        Transform exitEntrance = movingRight ? startRoom.LeftEntrance : startRoom.RightEntrance;
        if (startSpot != null)
            fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance));
        else
            fullPath.Add(exitEntrance);

        for (int i = startIndex + step; i != targetIndex; i += step)
        {
            fullPath.AddRange(ordered[i].GetPassThroughPath(enteringFromLeft: !movingRight));
        }

        Transform entryEntrance = movingRight ? targetRoom.RightEntrance : targetRoom.LeftEntrance;
        fullPath.AddRange(targetRoom.GetPathBetweenEntranceAndSpot(targetSpot, entryEntrance));

        return fullPath;
    }
}