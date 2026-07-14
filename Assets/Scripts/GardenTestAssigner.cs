using UnityEngine;

public class GardenTestAssigner : MonoBehaviour
{
    [SerializeField] private NPCBunny bunnyToAssign;
    [SerializeField] private GardenRoom gardenRoom;
    [SerializeField] private bool assignOnStart = true;

    private void Start()
    {
        if (assignOnStart)
        {
            AssignNow();
        }
    }

    [ContextMenu("Assign Bunny To Garden")]
    public void AssignNow()
    {
        if (bunnyToAssign == null || gardenRoom == null)
        {
            Debug.LogWarning("GardenTestAssigner: missing bunny or garden reference.");
            return;
        }

        bunnyToAssign.AssignToJob(gardenRoom);
        Debug.Log($"Assigned {bunnyToAssign.name} to {gardenRoom.name}");
    }
}