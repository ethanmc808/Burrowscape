public interface IJobRoom
{
    RoomSpot RequestSpot(NPCBunny bunny);
    void ReleaseSpot(RoomSpot spot, NPCBunny bunny);
    void NotifyBunnyReadyToWork(NPCBunny bunny);
    void NotifyBunnyLeavingToEat(NPCBunny bunny); // pauses work without releasing the reserved spot
    bool HasAvailableSpot();   // ADD THIS

    // Resource-agnostic — fired by RoomBase.RecheckOperational when the room's Power OR Water gate
    // (whichever it depends on) flips off/on. Stops/resumes active workers without releasing their
    // claimed spot, same shape as NotifyBunnyLeavingToEat.
    void OnRoomShutdown();
    void OnRoomRestored();
}