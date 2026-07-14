using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RoomClickHandler : MonoBehaviour
{
    private IJobRoom jobRoom;

    private void Awake()
    {
        jobRoom = GetComponent<IJobRoom>();
        if (jobRoom == null)
            Debug.LogWarning($"{name}: RoomClickHandler requires an IJobRoom component on the same object.");
    }

    private void OnMouseDown()
    {
        if (jobRoom != null)
            AssignmentUI.Instance.OpenForRoom(jobRoom, name);
    }
}