public interface IJobRoom
{
    RoomSpot RequestSpot(NPCBunny bunny);
    void ReleaseSpot(RoomSpot spot, NPCBunny bunny);
    void NotifyBunnyReadyToWork(NPCBunny bunny);
    bool HasAvailableSpot();   // ADD THIS
}