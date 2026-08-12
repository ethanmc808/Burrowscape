using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Where bunnies brew potions/consumables from herbs (see RecipeDefinition/ConsumableDefinition,
// LaboratoryRecipeUnlockTracker, and the Laboratory Room design plan). Unlike every other IJobRoom
// (Garden/Water: continuous background production with no player choice; Hatchery: ambient incubation,
// not job-triggered), a Laboratory job is DISCRETE and requires an explicit player decision: a bunny
// reaching a claimed BrewSpot does NOT auto-start anything — it just stands idle-at-spot until the player
// either starts a new recipe or joins an in-progress one for it (via AssignmentUI's Laboratory extras
// block, StartBrew/JoinBrew below).
//
// Capacity has two independent numbers:
//   - Physical spots (brewSpots) — how many bunnies can occupy the room at once, scales with room WIDTH
//     (2 spots per 4-unit-wide segment: 4x2x6=2, 8x2x6=4, 12x2x6=6), same flat-pool RoomSpot.TryClaim
//     mechanism every other job room uses.
//   - Batch capacity (BatchCapacity = brewSpots.Count / 2) — how many DISTINCT recipes can be brewing at
//     once. Each brew ("batch") can be staffed by 1 or 2 bunnies (same diminishing-returns curve Garden/
//     Water/Coal already use for extra workers, applied here as a speed bonus instead of an output bonus).
//     Spots are NOT rigidly paired into fixed stations — any free, present bunny can join any batch that
//     currently has room for a second worker, regardless of which named spot either of them is standing at.
public class LaboratoryRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("BrewSpot")]
    [SerializeField] private List<RoomSpot> brewSpots;

    [Header("Worker Speed Scaling — edited EXCLUSIVELY via Burrowscape > Work Room Production Tuner, same reasoning as GardenRoom.diminishingReturnsRate/recommendedTypes/typeMatchProductionBonus.")]
    [SerializeField] private float diminishingReturnsRate = 0.7f;
    [HideInInspector] [SerializeField] private List<BunnyType> recommendedTypes = new List<BunnyType>();
    [SerializeField] private float typeMatchBrewSpeedBonus = 0.25f;

    [Tooltip("How many completed brews GetRecentlyCrafted() keeps around for the UI's Recently Crafted list.")]
    [SerializeField] private int recentlyCraftedCapacity = 5;

    public int BatchCapacity => brewSpots.Count / 2;
    public bool CanStartNewBatch => activeBatches.Count < BatchCapacity;

    // One brew in progress, staffed by 1-2 bunnies. Tracked as WORK UNITS rather than raw elapsed-vs-
    // duration seconds specifically so a worker joining/leaving mid-brew can change the completion rate
    // without corrupting progress already banked at the old rate — see ComputeBatchSpeedMultiplier/
    // BrewRoutine below. `routine` is null while paused (every current worker is away eating, or the room
    // lost Power/Water) — completedWork is deliberately PRESERVED across a pause, never discarded, since
    // herbs are already spent the moment a batch starts (TryWithdrawIngredients, below) — losing progress
    // to a short eating trip or a power flicker would be a real, un-signposted loss with nothing to show
    // for it. Only the batch becoming fully unstaffed via a full unassignment (not eating/shutdown)
    // discards it outright — see CancelBatch.
    private class BrewBatch
    {
        public RecipeDefinition recipe;
        public readonly List<NPCBunny> workers = new List<NPCBunny>();
        public Coroutine routine;
        public float completedWork;
        public float requiredWork;
    }

    private readonly List<BrewBatch> activeBatches = new List<BrewBatch>();
    private readonly Dictionary<NPCBunny, BrewBatch> bunnyToBatch = new Dictionary<NPCBunny, BrewBatch>();

    // Everyone currently occupying a claimed BrewSpot and in Working state, whether actively brewing or
    // idle-at-spot awaiting a pick — separate from bunnyToBatch.Keys since an idle-at-spot bunny has no
    // batch at all. Also doubles as "is this worker currently contributing" for
    // ComputeBatchSpeedMultiplier — a batch member who's momentarily away (eating) stays in
    // BrewBatch.workers but drops out of this set, so they contribute nothing to the rate until they
    // return, without losing their seat in the batch.
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

        if (bunnyToBatch.TryGetValue(bunny, out BrewBatch batch))
        {
            batch.workers.Remove(bunny);
            bunnyToBatch.Remove(bunny);

            // Full unassignment is the one thing that discards a batch outright — herbs already
            // withdrawn are NOT refunded (no refund precedent exists anywhere else in this codebase for a
            // started-and-abandoned job). Only fires when this was the batch's LAST worker; a duo batch
            // losing one worker just continues solo, same as ComputeBatchSpeedMultiplier already handles.
            if (batch.workers.Count == 0)
                CancelBatch(batch);
            else
                RecheckBatchPause(batch);
        }
    }

    public bool HasAvailableSpot() => brewSpots.Any(s => !s.IsOccupied);

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived (or is resuming after ResumeWorkAfterRoomRestored) while this room is dark — idle
        // immediately instead of touching any batch state; OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        presentBunnies.Add(bunny);

        // Re-entrant call (e.g. NPCBunny.ResumeWorkAfterRoomRestored, or returning from an eating trip) —
        // if this bunny already belongs to a batch, make sure it's running (idempotent — the other worker
        // may have already resumed it) and restart this bunny's own XP tracking. A bunny with no batch is
        // genuinely idle-at-spot — StartBrew/JoinBrew (player-triggered) are the only things that start one.
        if (bunnyToBatch.TryGetValue(bunny, out BrewBatch batch))
        {
            RecheckBatchPause(batch);
            StartWorkXPRoutine(bunny);
        }
    }

    // Pauses without releasing the reserved spot, discarding brew progress, or dropping this bunny's seat
    // in its batch — see BrewBatch's own comment for why this deliberately differs from GardenRoom's
    // discard-on-pause convention.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        presentBunnies.Remove(bunny);
        StopWorkXPRoutine(bunny);

        if (bunnyToBatch.TryGetValue(bunny, out BrewBatch batch))
            RecheckBatchPause(batch);
    }

    public void OnRoomShutdown()
    {
        foreach (NPCBunny bunny in presentBunnies.ToList())
        {
            bunny.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(bunny);
            StopWorkXPRoutine(bunny);
        }

        // Pause every batch outright regardless of per-bunny presence bookkeeping — the whole room is
        // dark, nothing should progress. presentBunnies is deliberately left untouched (they're still
        // physically standing here, just forced idle) so OnRoomRestored's per-bunny
        // ResumeWorkAfterRoomRestored -> NotifyBunnyReadyToWork naturally resumes each batch via
        // RecheckBatchPause once operational again.
        foreach (BrewBatch batch in activeBatches)
        {
            if (batch.routine != null)
            {
                StopCoroutine(batch.routine);
                batch.routine = null;
            }
        }
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored(); // calls back into NotifyBunnyReadyToWork above
        idledByShutdown.Clear();
    }

    // ---------- Brewing ----------

    // Starts a coroutine if this batch has any currently-present worker and none is running yet; stops it
    // if no worker is currently present. Safe to call any time presence changes — idempotent both ways.
    private void RecheckBatchPause(BrewBatch batch)
    {
        bool anyPresent = batch.workers.Any(w => presentBunnies.Contains(w));

        if (anyPresent && batch.routine == null)
            batch.routine = StartCoroutine(BrewRoutine(batch));
        else if (!anyPresent && batch.routine != null)
        {
            StopCoroutine(batch.routine);
            batch.routine = null;
        }
    }

    private void CancelBatch(BrewBatch batch)
    {
        if (batch.routine != null) StopCoroutine(batch.routine);
        activeBatches.Remove(batch);
    }

    // Player-triggered — starts a NEW batch. Withdraws herbs up front (fails clean, nothing deducted, if
    // unaffordable) so they're locked in for the brew's duration rather than risking being spent elsewhere
    // mid-brew. Fails if the room is already brewing BatchCapacity distinct recipes.
    public bool StartBrew(NPCBunny bunny, RecipeDefinition recipe)
    {
        if (bunny == null || recipe == null || recipe.output == null) return false;
        if (!presentBunnies.Contains(bunny) || bunnyToBatch.ContainsKey(bunny)) return false;

        if (activeBatches.Count >= BatchCapacity)
        {
            NotificationManager.Instance?.Show(NotificationType.RoomFull);
            return false;
        }

        if (ForagingInventoryManager.Instance == null) return false;
        if (!ForagingInventoryManager.Instance.TryWithdrawIngredients(recipe))
        {
            NotificationManager.Instance?.Show(NotificationType.NotEnoughHerbs);
            return false;
        }

        BrewBatch batch = new BrewBatch { recipe = recipe, requiredWork = recipe.baseBrewTime };
        batch.workers.Add(bunny);
        activeBatches.Add(batch);
        bunnyToBatch[bunny] = batch;

        RecheckBatchPause(batch);
        StartWorkXPRoutine(bunny);
        return true;
    }

    // Player-triggered — joins an EXISTING single-worker batch (see AssignmentUI's "Join [Bunny]'s brew"
    // picker option) rather than starting a fresh one. No additional herb cost — the batch already paid
    // for its ingredients at StartBrew time.
    public bool JoinBrew(NPCBunny bunny, NPCBunny existingWorker)
    {
        if (bunny == null || existingWorker == null) return false;
        if (!presentBunnies.Contains(bunny) || bunnyToBatch.ContainsKey(bunny)) return false;
        if (!bunnyToBatch.TryGetValue(existingWorker, out BrewBatch batch)) return false;
        if (batch.workers.Count >= 2) return false;

        batch.workers.Add(bunny);
        bunnyToBatch[bunny] = batch;

        RecheckBatchPause(batch);
        StartWorkXPRoutine(bunny);
        return true;
    }

    // rank is each currently-PRESENT worker's live index within the batch (0 = first/best, 1 = second) —
    // recomputed fresh every call, never cached, so a worker leaving to eat immediately gives whoever's
    // left the full rank-0 rate rather than staying stuck at a diminished rank. Same
    // WorkerProductionScaling.SlotMultiplier curve Garden/Water/Coal use for their own extra-worker
    // diminishing returns, just summed into a duration-reducing rate here instead of an output multiplier.
    private float ComputeBatchSpeedMultiplier(BrewBatch batch)
    {
        List<NPCBunny> present = batch.workers.Where(w => presentBunnies.Contains(w)).ToList();
        float total = 0f;

        for (int rank = 0; rank < present.Count; rank++)
        {
            float slotMultiplier = WorkerProductionScaling.SlotMultiplier(diminishingReturnsRate, rank);
            bool typeMatch = recommendedTypes != null && recommendedTypes.Contains(present[rank].Type);
            total += slotMultiplier * (typeMatch ? 1f + typeMatchBrewSpeedBonus : 1f);
        }

        return total;
    }

    private IEnumerator BrewRoutine(BrewBatch batch)
    {
        while (batch.completedWork < batch.requiredWork)
        {
            yield return null;

            float rate = GradeMultiplier * ComputeBatchSpeedMultiplier(batch);
            // Safety net only — RecheckBatchPause already stops this coroutine the moment no worker is
            // present, so rate should never actually reach 0 here; guard anyway rather than spin forever.
            if (rate <= 0f) yield break;

            batch.completedWork += Time.deltaTime * rate;
        }

        ForagingInventoryManager.Instance.AddConsumable(batch.recipe.output, batch.recipe.outputAmount);
        RecordRecentlyCrafted(batch.recipe.output);
        NotificationManager.Instance?.ShowWithIcon(NotificationType.BrewComplete, batch.recipe.output.icon, batch.recipe.output.displayName);

        foreach (NPCBunny worker in batch.workers)
        {
            bunnyToBatch.Remove(worker);
            StopWorkXPRoutine(worker);
        }
        activeBatches.Remove(batch);
        // Every worker goes back to idle-at-spot — no auto-repeat, the player picks again for each.
    }

    private void RecordRecentlyCrafted(ConsumableDefinition output)
    {
        recentlyCrafted.Insert(0, output);
        while (recentlyCrafted.Count > Mathf.Max(1, recentlyCraftedCapacity))
            recentlyCrafted.RemoveAt(recentlyCrafted.Count - 1);
    }

    // ---------- Read-only state for the UI (AssignmentUI's Laboratory extras block) ----------

    public bool IsBrewing(NPCBunny bunny) => bunnyToBatch.ContainsKey(bunny);
    public RecipeDefinition GetActiveBrewRecipe(NPCBunny bunny) => bunnyToBatch.TryGetValue(bunny, out BrewBatch batch) ? batch.recipe : null;
    public float GetBrewProgress01(NPCBunny bunny) => bunnyToBatch.TryGetValue(bunny, out BrewBatch batch) && batch.requiredWork > 0f ? Mathf.Clamp01(batch.completedWork / batch.requiredWork) : 0f;

    public float GetBrewRemainingSeconds(NPCBunny bunny)
    {
        if (!bunnyToBatch.TryGetValue(bunny, out BrewBatch batch)) return 0f;
        float remainingWork = Mathf.Max(0f, batch.requiredWork - batch.completedWork);
        float rate = GradeMultiplier * ComputeBatchSpeedMultiplier(batch);
        return rate > 0f ? remainingWork / rate : remainingWork;
    }

    // One entry per batch currently short a second worker — the "Join [Bunny]'s brew" options in
    // AssignmentUI's recipe picker (see RefreshRecipePickerList). The yielded bunny is just an identity
    // handle for JoinBrew above, not necessarily who the UI should label the row after if that bunny's own
    // row is what opened the picker — the caller filters that out.
    public IEnumerable<(NPCBunny worker, RecipeDefinition recipe)> GetJoinableBrews()
    {
        foreach (BrewBatch batch in activeBatches)
            if (batch.workers.Count == 1)
                yield return (batch.workers[0], batch.recipe);
    }

    public IReadOnlyList<ConsumableDefinition> GetRecentlyCrafted() => recentlyCrafted;
}
