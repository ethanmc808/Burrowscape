using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class RoomPath
{
    public Transform entrance;
    public RoomSpot targetSpot;
    public List<Transform> waypoints; // in order, NOT including entrance or spot itself
}