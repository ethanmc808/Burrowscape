using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// New room type for the Breeding System (see that plan doc). A hybrid: it's a real IJobRoom (bunnies can
// be assigned to TendingSpots — Fire/Light types speed up incubation, see HatchSpeedMultiplier below) AND
// it independently manages egg capacity/placement, which has nothing to do with job assignment at all.
// Two separate spot-list concepts on one room:
//   - tendingSpots: IJobRoom job-assignment spots, claimed by bunnies exactly like any other work room.
//   - hatcherySpots: egg placement/capacity, never claimed by a bunny — see ReserveCapacity/
//     ClaimAnyFreeSpot below, which are entirely independent of the IJobRoom side.
// Eggs hatch on their own over time regardless of whether anyone is tending — tending only ever makes
// that already-happening timer run faster, never gates it.
public class HatcheryRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("HatcherySpot")]
    [SerializeField] private List<RoomSpot> hatcherySpots;

    // Grade 1 4x2x6 default: 2 TendingSpots (per Ethan's spec) — a future higher-Grade Hatchery could
    // author more; HatchSpeedMultiplier's "two or more" tier (see its own comment) covers that gracefully.
    [SpotNamePrefix("TendingSpot")]
    [SerializeField] private List<RoomSpot> tendingSpots;

    // Pregnancies that have claimed capacity but haven't laid yet — see ReserveCapacity's own comment.
    private int reservedCount;

    // Live eggs currently sitting in this room, physically claimed via ClaimAnyFreeSpot — read by
    // SaveManager.SaveEggs (Phase 5) to enumerate every in-flight egg across every registered Hatchery.
    private readonly List<Egg> activeEggs = new List<Egg>();
    public IReadOnlyList<Egg> ActiveEggs => activeEggs;

    // Bunnies actively tending right now (present, not paused for eating/drinking/sleeping) — same
    // "only ACTIVE workers count toward a room-wide bonus" convention as GardenRoom's activeWorkerOrder.
    private readonly List<NPCBunny> activeTenders = new List<NPCBunny>();

    // Idled because this room lost Power or Water while a bunny was actively tending — resumed
    // automatically by OnRoomRestored. See RoomBase.RecheckOperational / IJobRoom.OnRoomShutdown, same
    // pattern GardenRoom already uses.
    private readonly List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterHatchery(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterHatchery(this);
    }

    // ---------- IJobRoom: TendingSpots ----------

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in tendingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        activeTenders.Remove(bunny);
    }

    public bool HasAvailableSpot()
    {
        return tendingSpots.Any(s => !s.IsOccupied);
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        if (!activeTenders.Contains(bunny))
            activeTenders.Add(bunny);
    }

    // Pauses without releasing the reserved spot — same "always restart fresh, never resume leftover
    // time" convention as GardenRoom.NotifyBunnyLeavingToEat/StopProductionRoutine, just for tending
    // instead of production: leaving to eat only ever removes this bunny from the ACTIVE tender count
    // (dropping the room's speed bonus while they're away), not their claimed TendingSpot.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        activeTenders.Remove(bunny);
    }

    public void OnRoomShutdown()
    {
        foreach (NPCBunny bunny in activeTenders)
        {
            bunny.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(bunny);
        }
        activeTenders.Clear();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }

    // Per-tender additive: every actively-tending Fire/Light bunny contributes fireOrLightTenderHatchSpeedBonus,
    // every other type contributes the smaller otherTypeTenderHatchSpeedBonus, summed across everyone
    // currently tending (so a mixed room sums both rates, and this scales cleanly to more TendingSpots on
    // a future higher-Grade Hatchery with no extra tiers to define). Read live by every Egg in this room
    // each frame (Egg.Update), so it updates immediately as tenders arrive/leave/get interrupted, no caching.
    public float HatchSpeedMultiplier
    {
        get
        {
            BreedingConfig cfg = BreedingConfig.Instance;
            float bonus = 0f;

            foreach (NPCBunny tender in activeTenders)
                bonus += (tender.Type == BunnyType.Fire || tender.Type == BunnyType.Light)
                    ? cfg.fireOrLightTenderHatchSpeedBonus
                    : cfg.otherTypeTenderHatchSpeedBonus;

            return 1f + bonus;
        }
    }

    // ---------- Egg capacity/placement — entirely independent of the IJobRoom side above ----------

    // What a mating roll claims at conception — increments a plain counter, no RoomSpot involved, nothing
    // is pretending to physically occupy a space yet. Paired with CancelReservation once the real Egg is
    // placed via ClaimAnyFreeSpot (the reservation converts into that real claim at lay time).
    public void ReserveCapacity() => reservedCount++;

    // TODO(breeding): call this if a pregnant bunny is removed from the colony (death/banishment) before
    // she ever lays — no such interaction exists yet, this is just the hook for when one does.
    public void CancelReservation() => reservedCount = Mathf.Max(0, reservedCount - 1);

    // Load-only — reservedCount is never persisted as a raw number (see the class header comment), so
    // SaveManager.LoadBunnies calls this once after every bunny has been restored, setting it directly
    // from a freshly recomputed count rather than replaying ReserveCapacity() calls one at a time.
    public void SetReservedCountForLoad(int count) => reservedCount = Mathf.Max(0, count);

    // Takes whichever spot is currently free, not a pre-assigned one — eggs are visually interchangeable
    // within a type, so there's no reason a specific egg needs a specific spot. Routes through
    // RoomSpot.TryClaim's own compare-and-set, so simultaneous callers (two Bedrooms laying at once)
    // still serialize safely with no extra coordination needed here.
    public RoomSpot ClaimAnyFreeSpot(Egg egg)
    {
        foreach (RoomSpot spot in hatcherySpots)
        {
            if (spot.TryClaim(egg))
            {
                activeEggs.Add(egg);
                return spot;
            }
        }
        return null;
    }

    // Called once an egg finishes hatching (HatchEgg below) to free its physical spot.
    public void ReleaseSpot(RoomSpot spot, Egg egg)
    {
        spot.Release(egg);
        activeEggs.Remove(egg);
    }

    // True only if there's room for one more RESERVATION — accounts for both pending pregnancies
    // (reservedCount) and already-laid eggs (physically occupied hatcherySpots) against total capacity.
    public bool HasFreeSlot()
    {
        int occupied = hatcherySpots.Count(s => s.IsOccupied);
        return reservedCount + occupied < hatcherySpots.Count;
    }

    // Used purely as a walking destination for NPCBunny.TryLayEgg (GetRouteToSpot/MoveAlongPath both
    // require a RoomSpot, not just a room) — never TryClaim'd, same "shared navigation marker, not an
    // actual reservation" approach Bedroom's MatingSpot already uses. The real placement happens
    // separately via ClaimAnyFreeSpot once she's physically arrived, which may resolve to a different
    // spot than this one if another egg took it in the meantime — a minor, rare cosmetic imprecision,
    // not a correctness issue (capacity itself is already guaranteed via the conception-time reservation).
    public RoomSpot GetAnyHatcherySpot() => hatcherySpots.Count > 0 ? hatcherySpots[0] : null;

    public int IndexOfSpot(RoomSpot spot) => hatcherySpots.IndexOf(spot);

    // Used only by SaveManager.LoadEggs to reconstruct an egg at the exact spot it was saved at (rather
    // than ClaimAnyFreeSpot's "wherever's free," which would be fine functionally but pointlessly
    // shuffles a reloaded egg's visible position for no reason).
    public RoomSpot ClaimSpecificSpot(int index, Egg egg)
    {
        if (index < 0 || index >= hatcherySpots.Count) return null;
        RoomSpot spot = hatcherySpots[index];
        if (!spot.TryClaim(egg)) return null;
        activeEggs.Add(egg);
        return spot;
    }

    // Releases egg's physical spot, then spawns one sibling per LitterMemberData — mirrors
    // WildBunnySpawner.SpawnBunnyOfType step-for-step but substitutes the pre-rolled inherited data
    // instead of fresh rolls. Only the litter-wide steps (resolving which prefab/BunnyTypeDefinition to
    // instantiate) are shared across siblings; everything else is per-member. Entirely independent of the
    // tending speed bonus above — that only ever affects HOW FAST an egg gets here, never what hatches.
    public void HatchEgg(Egg egg)
    {
        ReleaseSpot(egg.ClaimedSpot, egg);

        WildBunnySpawner spawner = FindAnyObjectByType<WildBunnySpawner>();
        BunnyTypeDefinition typeDef = spawner != null
            ? spawner.BunnyTypes.FirstOrDefault(t => t != null && t.type == egg.Type)
            : null;

        if (typeDef == null || typeDef.prefab == null)
        {
            Debug.LogWarning($"{name}: no prefab found for hatched egg's type {egg.Type} — litter lost.");
            return;
        }

        // Small per-sibling offset so multiple hatchlings from one egg don't fully overlap visually —
        // cosmetic only, not load-bearing.
        for (int i = 0; i < egg.LitterMembers.Count; i++)
        {
            LitterMemberData member = egg.LitterMembers[i];
            Vector3 spawnOffset = new Vector3(i * 0.3f, 0f, 0f);
            Vector3 spawnPosition = egg.transform.position + spawnOffset;

            GameObject spawnedObject = Instantiate(typeDef.prefab, spawnPosition, Quaternion.identity);
            NPCBunny newBunny = spawnedObject.GetComponent<NPCBunny>();
            if (newBunny == null)
            {
                Debug.LogWarning($"{name}: {egg.Type}'s prefab has no NPCBunny component — one sibling lost.");
                Destroy(spawnedObject);
                continue;
            }

            newBunny.SetArrivalType(BunnyArrivalType.Wild);
            newBunny.SetIdentity(member.gender, member.name);
            newBunny.SetIndividualityFromInheritance(member.ivHP, member.ivAttack, member.ivDefense, member.ivSpeed, member.ivLuck);

            BunnyStats stats = BunnyStatCalculator.Resolve(typeDef, 1,
                member.ivHP, member.ivAttack, member.ivDefense, member.ivSpeed, member.ivLuck,
                0, 0, 0, 0, 0);

            BunnyTraitDefinition trait = BunnyTraitCatalog.Instance != null
                ? BunnyTraitCatalog.Instance.AllTraits.FirstOrDefault(t => t.id == member.traitId)
                : null;
            List<BunnyTraitDefinition> traits = trait != null
                ? new List<BunnyTraitDefinition> { trait }
                : new List<BunnyTraitDefinition>();

            List<BunnyPassiveDefinition> passives = BunnyPassiveResolver.ResolvePassives(typeDef, 1);
            newBunny.SetTypeAndProgression(typeDef, 1, stats, traits, passives);

            newBunny.RandomizeStartingNeeds(90f, 100f); // a freshly-hatched kid shouldn't spawn already hungry/tired

            newBunny.SetIsKidBunny(true);

            // Fall back to the existing generic idle/relax behavior — no School/Play Room yet (see the
            // plan's out-of-scope section). Same method the gate-arrival flow already calls once a bunny
            // is let through: sets HasEnteredBase = true, CurrentState = Idle, which drives HandleIdle's
            // existing Living Room relax-spot routing with zero new code.
            newBunny.EnterBaseAndWander(this, egg.transform);

            PopulationManager.Instance?.MoveResident(ResidentCategory.Egg, ResidentCategory.InBase);
            NotificationManager.Instance?.ShowWithIcon(NotificationType.EggHatched, typeDef.icon, newBunny.BunnyName);
        }
    }

    // TODO(kid-bunny-growth): no growth-to-adult timer exists yet — every kid hatched here stays a kid
    // indefinitely (job/forage/quest-gated via NPCBunny.IsKidBunny) until that system lands.
}
