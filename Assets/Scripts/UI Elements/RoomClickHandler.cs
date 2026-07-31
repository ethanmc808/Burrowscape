using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RoomClickHandler : MonoBehaviour
{
    private IJobRoom jobRoom;
    private RoomBase roomBase;

    private void Awake()
    {
        jobRoom = GetComponent<IJobRoom>();
        roomBase = GetComponent<RoomBase>();
        if (jobRoom == null)
            Debug.LogWarning($"{name}: RoomClickHandler requires an IJobRoom component on the same object.");
    }

    private void OnMouseDown()
    {
        if (UIPointerGuard.IsPointerOverUI()) return;
        if (BunnyClickPriority.TryOpenInstead()) return;

        if (jobRoom != null)
            AssignmentUI.Instance.OpenForRoom(jobRoom, RoomDisplayName());

        // No-ops unless this room actually has an active invasion right now (see
        // GuardDeployUI.OpenForRoom) — shows up "alongside" AssignmentUI automatically, only when relevant.
        if (roomBase != null && GuardDeployUI.Instance != null)
            GuardDeployUI.Instance.OpenForRoom(roomBase, RoomDisplayName());
    }

    // Falls back to the GameObject's own name (e.g. "Garden_4x2x6_Grade1") if the room's RoomDefinition
    // can't be resolved — same defensive fallback style as RoomUpgradeUI.RefreshUpgradeOption.
    private string RoomDisplayName()
    {
        if (roomBase != null && RoomCatalogRegistry.Instance != null)
        {
            RoomDefinition definition = RoomCatalogRegistry.Instance.FindVariant(roomBase.RoomTypeId, Mathf.RoundToInt(roomBase.FootprintWidth), roomBase.Grade);
            if (definition != null) return definition.displayName;
        }

        return name;
    }
}