using UnityEngine;

public class RoomSpot : MonoBehaviour
{
    [SerializeField] private bool facesRight = true;
    public bool FacesRight => facesRight;
    public bool IsOccupied { get; private set; }
    private NPCBunny occupant;

    public bool TryClaim(NPCBunny bunny)
    {
        if (IsOccupied) return false;
        IsOccupied = true;
        occupant = bunny;
        return true;
    }

    public void Release(NPCBunny bunny)
    {
        if (occupant == bunny)
        {
            IsOccupied = false;
            occupant = null;
        }
    }
}