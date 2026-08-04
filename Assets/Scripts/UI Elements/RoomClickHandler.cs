using Unity.VisualScripting;
using UnityEngine;

// Present on every room prefab's root GameObject (see RoomClickHandlerAdder, which backfills it onto any
// room prefab missing it — safe/expected to re-run). jobRoom being null is a legitimate, common case, NOT
// a wiring mistake: Cafeteria/Bedroom/StorageRoom/LivingRoom (and any future non-job room) have no
// IJobRoom at all, since they have nothing to manually assign a bunny to — this component still needs to
// be present on them so GuardDeployUI/PatientUI below can open (Ethan's ask 2026-08-04: slimes spawning
// in the Cafeteria had no way to be fought, since without a click handler at all nothing opened there).
[RequireComponent(typeof(Collider))]
public class RoomClickHandler : MonoBehaviour
{
    private IJobRoom jobRoom;
    private RoomBase roomBase;

    private void Awake()
    {
        jobRoom = GetComponent<IJobRoom>();
        roomBase = GetComponent<RoomBase>();
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

        // Same "no-ops unless relevant" shape as GuardDeployUI above — only actually opens for a
        // HospitalRoom, positioned next to AssignmentUI (which the nurse WorkSpot still uses normally).
        if (roomBase != null && PatientUI.Instance != null)
            PatientUI.Instance.OpenForRoom(roomBase, RoomDisplayName());
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