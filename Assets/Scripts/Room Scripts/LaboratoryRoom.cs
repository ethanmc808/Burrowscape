using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Where bunnies brew potions/consumables from herbs (see RecipeDefinition/ConsumableDefinition,
// LaboratoryRecipeUnlockTracker, and the Laboratory Room design plan).
//
// REDESIGNED from an earlier "2 brew spots per recipe, up to BatchCapacity independent batches" model —
// that fell apart on room width, since a 12x2x6 Grade-3 lab would need SIX independent per-batch UI rows
// (one bunny/timer pairing each), which doesn't fit the AssignmentUI panel at any reasonable size. This
// version is a single ROOM-WIDE brew instead: exactly one recipe+quantity in progress at a time, with every
// currently-present bunny pooling their speed onto it — same "all assigned workers contribute diminishing-
// returns speed" convention Garden/Water/Coal already use for their own continuous production, just applied
// to a discrete timed job instead of an ongoing one. The UI only ever needs ONE status display (recipe,
// progress, remaining) regardless of room grade.
//
// Still genuinely different from Garden/Water (continuous, no player choice) and Hatchery (ambient, not
// job-triggered): a bunny reaching a claimed BrewSpot does NOT auto-start anything — the room just sits
// idle until the player picks a recipe AND a quantity (1x/5x/10x, see AssignmentUI) via the room-level
// "Select item to craft" button. No queueing — once brewing, the picker stays closed until that batch
// finishes (confirmed with Ethan rather than assumed, since queueing vs. not is a real behavior fork).
public class LaboratoryRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("BrewSpot")]
    [SerializeField] private List<RoomSpot> brewSpots;

    [Header("Worker Speed Scaling — edited EXCLUSIVELY via Burrowscape > Work Room Production Tuner, same reasoning as GardenRoom.diminishingReturnsRate/typeMatchProductionBonus. recommendedTypes itself now lives on RoomBase (consolidated), still also editable via the same Tuner window's per-room grid.")]
    [SerializeField] private float diminishingReturnsRate = 0.7f;
    [SerializeField] private float typeMatchBrewSpeedBonus = 0.25f;

    [Tooltip("How many completed brews GetRecentlyCrafted() keeps around for the UI's Recently Crafted list.")]
    [SerializeField] private int recentlyCraftedCapacity = 5;

    // Physical spots (brewSpots) — how many bunnies can occupy the room at once, scales with room WIDTH
    // (2 spots per 4-unit-wide segment: 4x2x6=2, 8x2x6=4, 12x2x6=6), same flat-pool RoomSpot.TryClaim
    // mechanism every other job room uses. No longer paired into batches — WorkerCapacity is just the total
    // headcount that can pool speed onto the one active brew. Kept the same spot counts already authored on
    // the Grade 1/2/3 prefabs rather than reworking them — the "2 per recipe" reasoning behind that count is
    // gone, but the numbers themselves are still a perfectly reasonable per-grade headcount.
    public int WorkerCapacity => brewSpots.Count;

    // Tracked as WORK UNITS rather than raw elapsed-vs-duration seconds specifically so a worker joining/
    // leaving mid-brew can change the completion rate without corrupting progress already banked at the old
    // rate — see ComputeSpeedMultiplier/BrewRoutine below. completedWork/requiredWork are deliberately
    // PRESERVED across a pause (no one present, or the room lost Power/Water) — herbs are already spent the
    // moment a brew starts (TryWithdrawIngredients, below), so losing progress to a short eating trip or a
    // power flicker would be a real, un-signposted loss with nothing to show for it. Unlike the old batch
    // model, there's no separate "full unassignment cancels the job" case anymore — the brew isn't owned by
    // any particular bunny, so it simply stalls at 0 progress/second whenever presentBunnies is empty and
    // resumes the moment anyone returns. That already satisfies "no bunny = no potions" without needing a
    // cancel-and-lose-herbs special case.
    private RecipeDefinition currentRecipe;
    private int currentQuantity;
    private float completedWork;
    private float requiredWork;
    private Coroutine brewRoutine;

    // Everyone currently occupying a claimed BrewSpot and in Working state, whether the room is actively
    // brewing or sitting idle awaiting a pick. Doubles as "who's contributing right now" for
    // ComputeSpeedMultiplier.
    private readonly HashSet<NPCBunny> presentBunnies = new HashSet<NPCBunny>();

    // Idled because this room lost Power or Water while a bunny was present — resumed automatically by
    // OnRoomRestored. See RoomBase.RecheckOperational / IJobRoom.OnRoomShutdown, same pattern
    // GardenRoom/HatcheryRoom already use.
    private readonly List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    // Most-recent-first, capped at recentlyCraftedCapacity — read by the UI's Recently Crafted list.
    private readonly List<ConsumableDefinition> recentlyCrafted = new List<ConsumableDefinition>();

    // ---------- IJobRoom ----------

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in brewSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        presentBunnies.Remove(bunny);
        StopWorkXPRoutine(bunny);
        // No batch to cancel anymore — the active brew (if any) just keeps ticking at whatever rate the
        // REMAINING present bunnies provide, correctly stalling to 0 if this was the last one.
    }

    public bool HasAvailableSpot() => brewSpots.Any(s => !s.IsOccupied);

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived (or is resuming after ResumeWorkAfterRoomRestored) while this room is dark — idle
        // immediately; OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        presentBunnies.Add(bunny);
        StartWorkXPRoutine(bunny);

        // Re-entrant call (returning from an eating trip, or resuming after a shutdown) — if a brew is
        // active, make sure its coroutine is running again now that there's at least one present worker.
        if (IsBrewing) RecheckBrewPause();
    }

    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        presentBunnies.Remove(bunny);
        StopWorkXPRoutine(bunny);

        if (IsBrewing) RecheckBrewPause();
    }

    public void OnRoomShutdown()
    {
        foreach (NPCBunny bunny in presentBunnies.ToList())
        {
            bunny.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(bunny);
            StopWorkXPRoutine(bunny);
        }

        // Pause the brew outright regardless of per-bunny presence bookkeeping — the whole room is dark,
        // nothing should progress. presentBunnies is deliberately left untouched here (they're still
        // physically standing here, just forced idle) so OnRoomRestored's per-bunny
        // ResumeWorkAfterRoomRestored -> NotifyBunnyReadyToWork naturally resumes brewing once operational
        // again.
        if (brewRoutine != null)
        {
            StopCoroutine(brewRoutine);
            brewRoutine = null;
        }
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored(); // calls back into NotifyBunnyReadyToWork above
        idledByShutdown.Clear();
    }

    // ---------- Brewing ----------

    public bool IsBrewing => currentRecipe != null;
    public bool CanStartNewBrew => !IsBrewing;

    // Starts a coroutine if there's any currently-present worker and none is running yet; stops it if no
    // worker is currently present. Safe to call any time presence changes — idempotent both ways.
    private void RecheckBrewPause()
    {
        bool anyPresent = presentBunnies.Count > 0;

        if (anyPresent && brewRoutine == null)
            brewRoutine = StartCoroutine(BrewRoutine());
        else if (!anyPresent && brewRoutine != null)
        {
            StopCoroutine(brewRoutine);
            brewRoutine = null;
        }
    }

    // Player-triggered — starts the room's one active brew. Withdraws herbs up front, scaled by quantity
    // (fails clean, nothing deducted, if unaffordable) so they're locked in for the brew's duration rather
    // than risking being spent elsewhere mid-brew. Fails if a brew is already in progress — no queueing
    // (confirmed with Ethan): the picker stays closed until the current one finishes.
    public bool StartBrew(RecipeDefinition recipe, int quantity)
    {
        if (recipe == null || recipe.output == null || quantity <= 0) return false;
        if (IsBrewing) return false;

        if (ForagingInventoryManager.Instance == null) return false;
        if (!ForagingInventoryManager.Instance.TryWithdrawIngredients(recipe, quantity))
        {
            NotificationManager.Instance?.Show(NotificationType.NotEnoughHerbs);
            return false;
        }

        currentRecipe = recipe;
        currentQuantity = quantity;
        completedWork = 0f;
        requiredWork = recipe.baseBrewTime * quantity;

        RecheckBrewPause();
        return true;
    }

    // rank is each currently-PRESENT worker's live index, recomputed fresh every call, never cached — a
    // worker leaving to eat immediately gives whoever's left the full rank-0 rate rather than staying stuck
    // at a diminished rank. Same WorkerProductionScaling.SlotMultiplier curve Garden/Water/Coal use for
    // their own extra-worker diminishing returns, just summed into a duration-reducing rate here instead of
    // an output multiplier.
    private float ComputeSpeedMultiplier()
    {
        float total = 0f;
        int rank = 0;
        foreach (NPCBunny bunny in presentBunnies)
        {
            float slotMultiplier = WorkerProductionScaling.SlotMultiplier(diminishingReturnsRate, rank);
            bool typeMatch = recommendedTypes != null && recommendedTypes.Contains(bunny.Type);
            total += slotMultiplier * (typeMatch ? 1f + typeMatchBrewSpeedBonus : 1f);
            rank++;
        }
        return total;
    }

    private IEnumerator BrewRoutine()
    {
        while (completedWork < requiredWork)
        {
            yield return null;

            float rate = GradeMultiplier * ComputeSpeedMultiplier();
            // Safety net only — RecheckBrewPause already stops this coroutine the moment no worker is
            // present, so rate should never actually reach 0 here; guard anyway rather than spin forever.
            if (rate <= 0f) yield break;

            completedWork += Time.deltaTime * rate;
        }

        ForagingInventoryManager.Instance.AddConsumable(currentRecipe.output, currentRecipe.outputAmount * currentQuantity);
        RecordRecentlyCrafted(currentRecipe.output);
        NotificationManager.Instance?.ShowWithIcon(NotificationType.BrewComplete, currentRecipe.output.icon, $"{currentRecipe.output.displayName} x{currentRecipe.outputAmount * currentQuantity}");

        currentRecipe = null;
        currentQuantity = 0;
        completedWork = 0f;
        requiredWork = 0f;
        brewRoutine = null;
        // No auto-repeat — the room goes back to idle, the player picks again.
    }

    private void RecordRecentlyCrafted(ConsumableDefinition output)
    {
        recentlyCrafted.Insert(0, output);
        while (recentlyCrafted.Count > Mathf.Max(1, recentlyCraftedCapacity))
            recentlyCrafted.RemoveAt(recentlyCrafted.Count - 1);
    }

    // ---------- Read-only state for the UI (AssignmentUI's Laboratory extras block) ----------

    public RecipeDefinition GetActiveRecipe() => currentRecipe;
    public int GetActiveQuantity() => currentQuantity;
    public float GetBrewProgress01() => requiredWork > 0f ? Mathf.Clamp01(completedWork / requiredWork) : 0f;

    public float GetBrewRemainingSeconds()
    {
        if (!IsBrewing) return 0f;
        float remainingWork = Mathf.Max(0f, requiredWork - completedWork);
        float rate = GradeMultiplier * ComputeSpeedMultiplier();
        return rate > 0f ? remainingWork / rate : remainingWork;
    }

    public IReadOnlyList<ConsumableDefinition> GetRecentlyCrafted() => recentlyCrafted;
}
