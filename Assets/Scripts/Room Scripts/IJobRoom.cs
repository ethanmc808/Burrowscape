public interface IJobRoom
{
    RoomSpot RequestSpot(NPCBunny bunny);
    void ReleaseSpot(RoomSpot spot, NPCBunny bunny);
    void NotifyBunnyReadyToWork(NPCBunny bunny);
    void NotifyBunnyLeavingToEat(NPCBunny bunny); // pauses work without releasing the reserved spot
    bool HasAvailableSpot();   // ADD THIS
}