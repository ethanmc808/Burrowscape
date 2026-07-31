using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomSpot : MonoBehaviour
{
    [SerializeField] private bool facesRight = true;
    public bool FacesRight => facesRight;
    public bool IsOccupied { get; private set; }
    private Component occupant;

    // Component rather than NPCBunny so EnemySpots can hold an EnemyInstance occupant too — every
    // existing call site passes an NPCBunny, which upcasts implicitly, so nothing else needed to change.
    public bool TryClaim(Component occupant)
    {
        if (IsOccupied) return false;
        IsOccupied = true;
        this.occupant = occupant;
        return true;
    }

    public void Release(Component occupant)
    {
        if (this.occupant == occupant)
        {
            IsOccupied = false;
            this.occupant = null;
        }
    }

#if UNITY_EDITOR
    // Same reasoning as RoomBase's OnDrawGizmos (see its comment) — a real Gizmos/Handles draw call
    // instead of a custom icon, so it can't go invisible from the icon-overlay Editor bug. The arrow
    // additionally makes a mis-set facesRight visible in-editor without entering Play mode.
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.15f);

        Vector3 facingDir = facesRight ? Vector3.right : Vector3.left;
        Handles.color = Color.red;
        Handles.ArrowHandleCap(0, transform.position, Quaternion.LookRotation(facingDir), 0.4f, EventType.Repaint);
        Handles.Label(transform.position + Vector3.up * 0.25f, name);
    }
#endif
}