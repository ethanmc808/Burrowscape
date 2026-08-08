using UnityEngine;

public class RoomDeletionTester : MonoBehaviour
{
    [SerializeField] private RoomBase roomToDelete;

    [ContextMenu("Try Delete Room")]
    public void TryDelete()
    {
        if (roomToDelete == null)
        {
            Debug.LogWarning("RoomDeletionTester: no room assigned.");
            return;
        }

        if (!roomToDelete.CanBeDeleted(out string reason))
        {
            Debug.LogWarning($"Can't delete {roomToDelete.name}: {reason}");
            NotificationManager.Instance.Show(NotificationType.CantDeleteRoom, reason);
            return;
        }

        Debug.Log($"Deleting {roomToDelete.name}.");
        Destroy(roomToDelete.gameObject);
    }
}
