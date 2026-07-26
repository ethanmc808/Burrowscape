using UnityEngine;

// Shared by GardenRoom/WaterRoom/CoalRoom so the diminishing-returns curve can't silently drift between
// room types — each room keeps its OWN diminishingReturnsRate (Inspector-tunable, broadcast-editable via
// WorkRoomProductionTuner), but all three feed it through this one formula. See
// Docs/DiminishingReturnsProduction_Design.md.
public static class WorkerProductionScaling
{
    // rank is 0 for the first active worker in a room, 1 for the second, etc. — recomputed fresh every
    // production tick from each room's own activeWorkerOrder list, never cached, so a worker leaving
    // automatically shifts everyone behind them down a rank on their very next tick.
    public static float SlotMultiplier(float diminishingReturnsRate, int zeroBasedRank)
    {
        return Mathf.Pow(diminishingReturnsRate, zeroBasedRank);
    }
}
