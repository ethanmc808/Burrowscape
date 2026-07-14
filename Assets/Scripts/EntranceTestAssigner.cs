using UnityEngine;

public class EntranceTestAssigner : MonoBehaviour
{
    [SerializeField] private NPCBunny bunnyToAssign;
    [SerializeField] private EntranceRoom entranceRoom;
    [SerializeField] private bool assignOnStart = true;

    private void Start()
    {
        if (assignOnStart)
        {
            AssignNow();
        }
    }

    [ContextMenu("Assign Bunny To Entrance")]
    public void AssignNow()
    {
        if (bunnyToAssign == null || entranceRoom == null)
        {
            Debug.LogWarning("EntranceTestAssigner: missing bunny or entrance reference.");
            return;
        }

        bunnyToAssign.AssignToJob(entranceRoom);
        Debug.Log($"Assigned {bunnyToAssign.name} to {entranceRoom.name}");
    }
}