using UnityEngine;

// Deliberately independent of RoomClickHandler (which only opens AssignmentUI, and only on rooms that
// implement IJobRoom) — upgrading applies to every room type, including purely decorative ones like
// LivingRoom/Bedroom that have no job-assignment flow at all. Added to every room prefab except
// Lift segments (a lift never upgrades — see RoomMergeResolver).
[RequireComponent(typeof(Collider))]
public class RoomUpgradeClickHandler : MonoBehaviour
{
    private RoomBase room;

    private void Awake()
    {
        room = GetComponent<RoomBase>();
        if (room == null)
            Debug.LogWarning($"{name}: RoomUpgradeClickHandler requires a RoomBase-derived component on the same object.");
    }

    private void OnMouseDown()
    {
        if (UIPointerGuard.IsPointerOverUI()) return;
        if (BunnyClickPriority.TryOpenInstead()) return;

        if (room != null)
            RoomUpgradeUI.Instance.OpenForRoom(room);
    }
}
