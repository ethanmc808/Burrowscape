using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Retrofitted for breeding (see the Breeding System plan) on top of its original passive-autonomous sleep
// side, which is untouched — RequestSleepSpot/ReleaseSleepSpot/HasAvailableSleepSpot (renamed from
// RequestSpot/ReleaseSpot/HasAvailableSpot in a standalone Phase 0 pass specifically so IJobRoom's
// identically-named methods below could be added without colliding — see that rename's own commit) still
// drive autonomous sleep exactly as before, no coroutine, energy regen still ticks in NPCBunny.Update().
// IJobRoom below is scoped ONLY to the breeding pair — a single male + single female slot per instance
// (multiple pairs per Bedroom is out of scope, see the plan).
public class Bedroom : RoomBase, IJobRoom
{
    [SpotNamePrefix("SleepingSpot")]
    [SerializeField] private List<RoomSpot> sleepingSpots;

    [SpotNamePrefix("BreedingSpotMale")]
    [SerializeField] private List<RoomSpot> maleBreedingSpots;
    [SpotNamePrefix("BreedingSpotFemale")]
    [SerializeField] private List<RoomSpot> femaleBreedingSpots;
    // Shared destination both occupants path to on a successful mating roll — where the visible happy
    // animation plays. Deliberately never TryClaim'd (both bunnies walk to the same Transform at once,
    // see TriggerMatingSequence below) — used purely as a RoomSpot for GetRouteToSpot/MoveAlongPath's API.
    // Both arriving at the exact same point would render their sprites stacked on top of each other —
    // handled with a small sideways position nudge in NPCBunny.OnArrivedAtMatingSpot (male/female offset
    // in opposite X directions) rather than authoring two separate spots, to avoid doubling the
    // hand-routed RoomPath entries this already needs.
    [SpotNamePrefix("MatingSpot")]
    [SerializeField] private List<RoomSpot> matingSpots;
    // Second shared destination, positioned physically behind the prefab's wall art (lower sorting order)
    // — the pair walks here after the happy beat and is occluded by ordinary sprite sorting, the same way
    // any object would be walking behind scenery. They never actually vanish (no SpriteRenderer toggle),
    // they just go out of line of sight. Also never TryClaim'd, same reasoning as matingSpots above.
    [SpotNamePrefix("BehindWallSpot")]
    [SerializeField] private List<RoomSpot> behindWallSpots;

    // This Bedroom's contribution to PopulationManager's population cap — a separately-tunable Inspector
    // value rather than derived from sleepingSpots.Count, same shape as WaterRoom's
    // waterRationingPoolAmount/powerRationingPoolAmount: the spot count still governs how many bunnies can
    // actually sleep here, but the cap contribution is free to differ (e.g. a nicer Grade 2/3 Bedroom
    // could grant more cap than its raw bed count implies). Grade 1 4x2x6 default of 4 matches its current
    // sleeping spot count as a baseline.
    [SerializeField] private int populationCapContribution = 4;
    public int PopulationCapContribution => populationCapContribution;

    // Hearts VFX (see the Breeding System plan's Phase 6) — lives on the Bedroom prefab itself, not on
    // either bunny, since "hearts flow out of the bedroom" reads as a room-level effect. Positioned near
    // the wall cutout/MatingSpot in the prefab. Null-safe: plays only if actually authored, same
    // "not yet authored isn't an error state" philosophy as every other optional-art field in this project.
    [SerializeField] private ParticleSystem heartsVFX;

    private NPCBunny maleOccupant;
    private NPCBunny femaleOccupant;
    private Coroutine breedingRoutine;

    // Throttle for the warnings below — same timestamp-cooldown shape as NPCBunny.WarnNoEatSpot, just
    // per-Bedroom instead of per-bunny (a pair repeatedly re-rolling every 30-60s would otherwise spam a
    // fresh notification every single cycle it stays blocked).
    private float lastHatcheryFullWarningTime = -999f;
    private float lastPopulationCapFullWarningTime = -999f;
    private const float BreedingWarningCooldownSeconds = 60f;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterBedroom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");

        if (PopulationManager.Instance != null)
            PopulationManager.Instance.RegisterBedroomCapacity(this);
        else
            Debug.LogWarning($"{name}: PopulationManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterBedroom(this);

        if (PopulationManager.Instance != null)
            PopulationManager.Instance.UnregisterBedroomCapacity(this);
    }

    // ---------- Sleep (autonomous, unchanged in behavior from before the Phase 0 rename) ----------

    public RoomSpot RequestSleepSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSleepSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableSleepSpot()
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    // ---------- IJobRoom: breeding pair only ----------

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        List<RoomSpot> genderSpots = bunny.Gender == BunnyGender.Male ? maleBreedingSpots : femaleBreedingSpots;
        foreach (RoomSpot spot in genderSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);

        if (maleOccupant == bunny) maleOccupant = null;
        if (femaleOccupant == bunny) femaleOccupant = null;

        StopBreedingRoutine();
    }

    // True if EITHER gender's slot is still free — matches AssignmentUI.AssignAndClose's pre-check shape.
    // The real per-gender correctness backstop is RequestSpot returning null if the specific gender the
    // player picked is actually full, which the rest of this codebase already tolerates gracefully (see
    // RequestNewJobSpot's "spot came back null, stay assigned, retry next Idle tick" handling) — but
    // AssignmentUI's IsEligibleForBreeding filter (below) is the primary defense, hiding an ineligible
    // candidate from the list entirely rather than letting the player click one and silently fail.
    public bool HasAvailableSpot()
    {
        bool maleFree = maleBreedingSpots.Any(s => !s.IsOccupied);
        bool femaleFree = femaleBreedingSpots.Any(s => !s.IsOccupied);
        return maleFree || femaleFree;
    }

    // Used by AssignmentUI.PopulateUnassignedList's Bedroom-specific filter.
    public bool IsEligibleForBreeding(NPCBunny bunny)
    {
        List<RoomSpot> genderSpots = bunny.Gender == BunnyGender.Male ? maleBreedingSpots : femaleBreedingSpots;
        return genderSpots.Any(s => !s.IsOccupied);
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        if (bunny.Gender == BunnyGender.Male) maleOccupant = bunny;
        else femaleOccupant = bunny;

        if (maleOccupant != null && femaleOccupant != null && breedingRoutine == null)
            breedingRoutine = StartCoroutine(BreedingRoutine());
    }

    // Pauses without releasing the reserved spot — same "always restart fresh, never resume leftover
    // time" convention as GardenRoom.NotifyBunnyLeavingToEat/StopProductionRoutine (see its own comment):
    // the departing bunny keeps its claimed breeding spot, only the mating-roll coroutine pauses, and
    // restarts fresh (not resumed) once both occupants are present again via NotifyBunnyReadyToWork.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        StopBreedingRoutine();
    }

    private void StopBreedingRoutine()
    {
        if (breedingRoutine != null)
        {
            StopCoroutine(breedingRoutine);
            breedingRoutine = null;
        }
    }

    public void OnRoomShutdown()
    {
        // Bedroom doesn't set consumesPower/consumesWater today, so RecheckOperational never actually
        // fires this in practice — implemented anyway for IJobRoom contract completeness and
        // future-proofing, same reasoning GardenRoom already documents for its own shutdown handling.
        StopBreedingRoutine();
    }

    public void OnRoomRestored()
    {
        if (maleOccupant != null && femaleOccupant != null && breedingRoutine == null)
            breedingRoutine = StartCoroutine(BreedingRoutine());
    }

    private IEnumerator BreedingRoutine()
    {
        while (true)
        {
            BreedingConfig cfg = BreedingConfig.Instance;
            yield return new WaitForSeconds(Random.Range(cfg.matingIntervalMinSeconds, cfg.matingIntervalMaxSeconds));

            // Defensive — NotifyBunnyLeavingToEat/ReleaseSpot should already have stopped this coroutine
            // the instant either occupant left, but bail cleanly if it somehow didn't.
            if (maleOccupant == null || femaleOccupant == null) yield break;

            if (femaleOccupant.IsPregnant) continue; // stay parked, don't waste a roll

            HatcheryRoom hatchery = BaseManager.Instance != null
                ? BaseManager.Instance.FindNearestHatcheryWithSpot(transform.position)
                : null;
            if (hatchery == null)
            {
                WarnHatcheryFull();
                continue; // romance isn't even attempted without somewhere to eventually put the egg
            }

            if (PopulationManager.Instance == null || !PopulationManager.Instance.HasRoomForNewResident)
            {
                WarnPopulationCapFull();
                continue; // coarse "room for at least one more" gate — see decision #8, a twin/triplet roll can still overflow past this once it succeeds
            }

            float successChance = maleOccupant.Type == femaleOccupant.Type ? cfg.sameTypeMatingChance : cfg.differentTypeMatingChance;
            if (Random.value >= successChance) continue; // failed roll — entirely silent, no animation/VFX, no reservation consumed

            TriggerMatingSequence(hatchery);
        }
    }

    private void WarnHatcheryFull()
    {
        if (Time.time - lastHatcheryFullWarningTime < BreedingWarningCooldownSeconds) return;
        lastHatcheryFullWarningTime = Time.time;
        NotificationManager.Instance?.Show(NotificationType.HatcheryFull, name);
    }

    private void WarnPopulationCapFull()
    {
        if (Time.time - lastPopulationCapFullWarningTime < BreedingWarningCooldownSeconds) return;
        lastPopulationCapFullWarningTime = Time.time;
        NotificationManager.Instance?.Show(NotificationType.PopulationCapFull, name);
    }

    // Reserves Hatchery capacity immediately (before either bunny even starts walking — a Hatchery slot
    // is claimed at conception, not at lay time, see the plan's decision list), rolls and locks in the
    // whole litter, then plays the visible mating sequence on both occupants. This is the ONE place the
    // animation/VFX actually fires — never on a failed roll (see BreedingRoutine above).
    private void TriggerMatingSequence(HatcheryRoom hatchery)
    {
        hatchery.ReserveCapacity();

        // 50/50 shared litter Type — rolled once, not per sibling, so the egg's type-coded sprite stays
        // accurate to everything inside it (decision #6).
        BunnyType litterType = Random.value < 0.5f ? maleOccupant.Type : femaleOccupant.Type;

        // "Touched" stat(s): each parent's own single highest IV stat carries over as-is; if both
        // parents' highest happens to be the same stat, only the higher value is inherited once, not
        // twice (decision #4 — "two single-stat gifts," not a full per-stat max).
        Dictionary<string, int> touchedStats = new Dictionary<string, int>();
        AddTouchedStat(touchedStats, GetHighestIVStat(maleOccupant));
        AddTouchedStat(touchedStats, GetHighestIVStat(femaleOccupant));

        BreedingConfig cfg = BreedingConfig.Instance;
        int litterSize = RollLitterSize(cfg);

        List<LitterMemberData> litterMembers = new List<LitterMemberData>();
        for (int i = 0; i < litterSize; i++)
            litterMembers.Add(RollLitterMember(touchedStats, litterType));

        float pregnancyDuration = Random.Range(cfg.pregnancyDurationMinSeconds, cfg.pregnancyDurationMaxSeconds);
        // PopulationManager.AddNewResident(Egg) fires once per sibling here — the instant pregnancy
        // begins, not at lay time — so total population accounting is correct from conception onward,
        // matching the once-per-sibling MoveResident(Egg, InBase) at hatch (see HatcheryRoom.HatchEgg).
        for (int i = 0; i < litterSize; i++)
            PopulationManager.Instance?.AddNewResident(ResidentCategory.Egg);

        femaleOccupant.BeginPregnancy(litterType, litterMembers, pregnancyDuration, hatchery);

        RoomSpot matingSpot = matingSpots.Count > 0 ? matingSpots[0] : null;
        if (matingSpot == null)
        {
            Debug.LogWarning($"{name}: TriggerMatingSequence has no MatingSpot authored — pregnancy still began, but the pair will stay put instead of playing the mating sequence.");
            return;
        }

        maleOccupant.BeginMatingSequence(matingSpot, femaleOccupant);
        femaleOccupant.BeginMatingSequence(matingSpot, maleOccupant);

        if (heartsVFX != null) heartsVFX.Play();
    }

    // Used by NPCBunny.PlayHappyBeatThenWalkBehindWall — see behindWallSpots' own comment.
    public RoomSpot GetBehindWallSpot() => behindWallSpots.Count > 0 ? behindWallSpots[0] : null;

    // (statName, value) for whichever IV stat is this bunny's own single highest — random tie-break among
    // any stats sharing that max, rather than always favoring whichever stat happens to come first.
    private (string statName, int value) GetHighestIVStat(NPCBunny bunny)
    {
        (string name, int value)[] stats =
        {
            ("HP", bunny.IVHP),
            ("Attack", bunny.IVAttack),
            ("Defense", bunny.IVDefense),
            ("Speed", bunny.IVSpeed),
            ("Luck", bunny.IVLuck),
        };

        int maxValue = stats.Max(s => s.value);
        List<(string name, int value)> topStats = stats.Where(s => s.value == maxValue).ToList();
        return topStats[Random.Range(0, topStats.Count)];
    }

    private void AddTouchedStat(Dictionary<string, int> touchedStats, (string statName, int value) stat)
    {
        // If both parents' highest happens to be the same stat, keep only the higher value (decision #4).
        if (touchedStats.TryGetValue(stat.statName, out int existing))
            touchedStats[stat.statName] = Mathf.Max(existing, stat.value);
        else
            touchedStats[stat.statName] = stat.value;
    }

    // One weighted 3-way roll — triplet / twin / single — not two independent coin flips, so triplets and
    // twins can never both fire off the same conception (decision #6).
    private int RollLitterSize(BreedingConfig cfg)
    {
        float roll = Random.value;
        if (roll < cfg.tripletChance) return 3;
        if (roll < cfg.tripletChance + cfg.twinChance) return 2;
        return 1;
    }

    // Every field on the returned LitterMemberData is fully resolved right now, at conception — nothing
    // here is deferred to hatch time, which is what makes hatching save-scum-proof (decision #4/point 4).
    private LitterMemberData RollLitterMember(Dictionary<string, int> touchedStats, BunnyType litterType)
    {
        // 1-32 inclusive mirrors NPCBunny.RollIndividuality's own MinIV/MaxIV bounds (private to that
        // class, so not reusable directly from here) — every stat NOT inherited from a parent is rolled
        // exactly the same way a wild spawn's own IVs are.
        int ivHP = touchedStats.TryGetValue("HP", out int hp) ? hp : Random.Range(1, 33);
        int ivAttack = touchedStats.TryGetValue("Attack", out int atk) ? atk : Random.Range(1, 33);
        int ivDefense = touchedStats.TryGetValue("Defense", out int def) ? def : Random.Range(1, 33);
        int ivSpeed = touchedStats.TryGetValue("Speed", out int spd) ? spd : Random.Range(1, 33);
        int ivLuck = touchedStats.TryGetValue("Luck", out int luck) ? luck : Random.Range(1, 33);

        BunnyTraitDefinition trait = BunnyTraitCatalog.Instance != null
            ? BunnyTraitCatalog.Instance.RollInheritedTrait(maleOccupant.Traits, femaleOccupant.Traits)
            : null;

        BunnyGender gender = WildBunnyNames.Instance != null ? WildBunnyNames.Instance.GetRandomGender() : BunnyGender.Male;
        // Drawn now, at conception, not at hatch — reserves the no-repeat pool slot immediately even
        // though the name isn't revealed to the player until the hatch notification fires.
        string litterMemberName = WildBunnyNames.Instance != null ? WildBunnyNames.Instance.GetRandomName(litterType, gender) : "Unnamed";

        return new LitterMemberData
        {
            ivHP = ivHP,
            ivAttack = ivAttack,
            ivDefense = ivDefense,
            ivSpeed = ivSpeed,
            ivLuck = ivLuck,
            traitId = trait != null ? trait.id : "",
            gender = gender,
            name = litterMemberName,
        };
    }
}
