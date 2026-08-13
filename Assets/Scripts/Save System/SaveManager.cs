using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Single entry point for writing/reading the one on-disk save file. Every other system stays ignorant of
// save/load entirely — SaveManager reaches OUT to each manager's own public API (mostly new Set*/Export*/
// Import*/Restore* methods added alongside this system) rather than any manager knowing about SaveData.
//
// Load sequencing matters a lot here and is NOT arbitrary — see LoadGame's own step comments:
//   1. Force-clear any invasion leftovers (defensive only — Save already refuses mid-raid).
//   2. Rooms, since bunnies reference them by instanceId.
//   3. Population / unlocks / game speed (order-independent of everything else).
//   4. Bunnies + their room/job assignment (needs rooms from step 2).
//   5. Resource pool CURRENT values (needs rooms from step 2 already registered so *Max is correct).
//   6. Resume active foraging trips (needs bunnies from step 4).
//   7. Bunny name pools.
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Autosave (off by default — scaffolding for a future 'hard mode' option, not wired to any UI yet)")]
    [SerializeField] private bool autoSaveEnabled = false;
    [SerializeField] private float autoSaveIntervalSeconds = 300f;

    private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Runs after every other singleton's own Awake() (Unity guarantees all Awakes complete before
        // any Start()), so every Instance reference LoadGame touches below is already valid.
        if (HasSaveFile())
            LoadGame();

        if (autoSaveEnabled)
            StartCoroutine(AutoSaveRoutine());
    }

    private System.Collections.IEnumerator AutoSaveRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoSaveIntervalSeconds);
            SaveGame();
        }
    }

    public static bool HasSaveFile() => File.Exists(SavePath);

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    // ================================================================================================
    // SAVE
    // ================================================================================================

    public bool SaveGame()
    {
        if (InvasionManager.Instance != null && InvasionManager.Instance.RaidActive)
        {
            Debug.LogWarning("SaveManager: can't save while an invasion is active — resolve or wait out the raid first.");
            return false;
        }

        SaveData data = new SaveData();

        SaveResources(data);
        SavePopulation(data);
        SaveRooms(data);
        Dictionary<NPCBunny, int> bunnyIndices = SaveBunnies(data);
        SaveGateQueue(data, bunnyIndices); // must run after SaveBunnies — needs bunnyIndices, and every queued bunny to already be in data.bunnies (see SaveBunnies' own isQueued check)
        SaveUnlocks(data);
        SaveForagingInventory(data);
        SaveForagingTrips(data, bunnyIndices);
        SaveEggs(data);
        data.gameSpeed.speedIndex = GameSpeedManager.Instance != null ? GameSpeedManager.Instance.CurrentIndex : 0;
        if (WildBunnyNames.Instance != null)
            data.bunnyNames.usedNames = WildBunnyNames.Instance.ExportUsedNames();

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"SaveManager: saved to {SavePath}");
        return true;
    }

    private void SaveResources(SaveData data)
    {
        data.resources.currentCarrots = CarrotManager.Instance != null ? CarrotManager.Instance.CurrentCarrots : 0;
        data.resources.currentGold = GoldManager.Instance != null ? GoldManager.Instance.CurrentGold : 0;
        data.resources.currentWater = WaterManager.Instance != null ? WaterManager.Instance.CurrentWater : 0;
        data.resources.powerRationingPoolCurrent = PowerManager.Instance != null ? PowerManager.Instance.RationingPoolCurrent : 0f;
        data.resources.waterRationingPoolCurrent = WaterRationingManager.Instance != null ? WaterRationingManager.Instance.RationingPoolCurrent : 0f;
    }

    private void SavePopulation(SaveData data)
    {
        if (PopulationManager.Instance == null) return;
        data.population.inBase = PopulationManager.Instance.GetCount(ResidentCategory.InBase);
        data.population.questing = PopulationManager.Instance.GetCount(ResidentCategory.Questing);
        data.population.foraging = PopulationManager.Instance.GetCount(ResidentCategory.Foraging);
        data.population.egg = PopulationManager.Instance.GetCount(ResidentCategory.Egg);
    }

    private void SaveRooms(SaveData data)
    {
        if (BaseLayoutManager.Instance == null) return;

        foreach (RoomBase room in BaseLayoutManager.Instance.GetAllRooms())
        {
            if (room == null) continue;

            data.rooms.Add(new RoomSaveData
            {
                instanceId = room.InstanceId,
                roomTypeId = room.RoomTypeId,
                footprintWidth = room.FootprintWidth,
                grade = room.Grade,
                gridX = room.GridX,
                worldX = room.transform.position.x,
                floorIndex = room.EffectiveFloorIndex,
            });
        }
    }

    // Returns each saved bunny's index in data.bunnies, keyed by live reference — SaveForagingTrips
    // needs this to record which bunny a trip belongs to (see ForagingTripSaveData.bunnyIndex).
    private Dictionary<NPCBunny, int> SaveBunnies(SaveData data)
    {
        Dictionary<NPCBunny, int> indices = new Dictionary<NPCBunny, int>();
        if (DwellerRoster.Instance == null) return indices;

        foreach (NPCBunny bunny in DwellerRoster.Instance.GetAllBunnies())
        {
            if (bunny == null) continue;

            // HasEnteredBase is deliberately false for the bunny's ENTIRE foraging trip, not just while
            // still queued at the gate (see DepartForForaging's own comment — it's cleared so the whole
            // round trip is treated like a first-time arrival by every HasEnteredBase-gated system).
            // Without this second check, an actively-foraging bunny would be silently dropped from the
            // save entirely: not written here, and therefore never in bunnyIndices for SaveForagingTrips
            // to find either, so its whole trip vanishes on load even though PopulationManager still
            // (correctly) reports one bunny out foraging.
            bool isForaging = ForagingManager.Instance != null && ForagingManager.Instance.IsCurrentlyForaging(bunny);
            // Still queued/awaiting approval at the gate — a real bunny worth persisting (see
            // GateQueueSaveData's own comment for why this is no longer force-admitted instead), but with
            // no room/job of her own yet. Falls through to the generic Idle/no-room branch below exactly
            // like any other unassigned Idle bunny — SaveGateQueue (called right after this method
            // returns) is what actually records her queue membership/position.
            bool isQueued = GateQueueManager.Instance != null && GateQueueManager.Instance.IsInQueue(bunny);
            if (!bunny.HasEnteredBase && !isForaging && !isQueued) continue; // truly nothing yet — not a resident, not away on a trip, not even queued

            SavedBunnyRole role;
            RoomBase room;
            if (bunny.CurrentState == BunnyState.Working) { role = SavedBunnyRole.Working; room = bunny.AssignedJobRoom as RoomBase; }
            // Confirmed real gap this branch fixes: without it, a save landing during the few-second
            // mating animation fell into the generic Idle catch-all below, which does NOT reclaim
            // AssignedJobRoom/claimedWorkSpot on load (only the Working branch above does) — the breeding
            // pair would silently lose their Bedroom assignment on any save landing in that narrow window.
            // Treating Mating as Working here is correct: she's still logically assigned to the Bedroom
            // the whole time, just mid-animation (see NPCBunny.isBreedingBlocked's own comment).
            else if (bunny.CurrentState == BunnyState.Mating) { role = SavedBunnyRole.Working; room = bunny.AssignedJobRoom as RoomBase; }
            else if (bunny.CurrentState == BunnyState.Sleeping) { role = SavedBunnyRole.Sleeping; room = bunny.ClaimedSleepRoom; }
            else if (bunny.CurrentState == BunnyState.Relaxing) { role = SavedBunnyRole.Relaxing; room = bunny.ClaimedRelaxRoom; }
            else
            {
                // Idle, Foraging (handled separately via activeTrips below), and every mid-transit/
                // Eating/Drinking/hospital state all snap down to Idle — see SavedBunnyRole's own comment.
                role = SavedBunnyRole.Idle;
                room = bunny.CurrentRoom;

                // currentRoom is set once on boarding (to the ORIGIN lift segment) and not updated again
                // until the bunny's post-disembark walk-out coroutine finishes — so a bunny saved anywhere
                // from boarding through "doors just opened at the destination" still has currentRoom
                // pointing at the origin segment, nowhere near its actual physical position. Resolve
                // forward to the segment for whichever floor the bunny is ACTUALLY on right now instead:
                // bunny.CurrentFloorIndex is already the destination floor by this exact window (set
                // synchronously in DisembarkFromLift, well before the post-disembark walk-out delay even
                // starts — see its own comment), and stays correctly at the ORIGIN floor for the
                // WaitingForLift/RidingLift states too (where the bunny really is still physically on/at
                // the origin floor — see BoardLift's own comment), so this one lookup covers all three
                // states uniformly. This is exactly the same "arrivalSegment" a live (non-restored)
                // disembark settles into once nothing else claims a room — see ResumeTripAfterLift's own
                // currentRoom = arrivalSegment fallback — just computed early so it survives a save/load
                // round-trip instead of being read fresh off pendingLift, which doesn't survive one.
                if (room is LiftRoom originSegment)
                    room = originSegment.GetSegmentForFloor(bunny.CurrentFloorIndex);
            }

            Vector3 pos = bunny.transform.position;

            BunnySaveData saved = new BunnySaveData
            {
                bunnyName = bunny.BunnyName,
                gender = bunny.Gender,
                bunnyType = bunny.Type,

                level = bunny.Level,
                experience = bunny.Experience,
                nature = bunny.Nature,
                ivHP = bunny.IVHP, ivAttack = bunny.IVAttack, ivDefense = bunny.IVDefense, ivSpeed = bunny.IVSpeed, ivLuck = bunny.IVLuck,
                evHP = bunny.EVHP, evAttack = bunny.EVAttack, evDefense = bunny.EVDefense, evSpeed = bunny.EVSpeed, evLuck = bunny.EVLuck,

                traitIds = bunny.Traits.Select(t => t.id).ToList(),
                passiveIds = bunny.ActivePassives.Select(p => p.id).ToList(),
                equippedAccessoryName = bunny.EquippedAccessory != null ? bunny.EquippedAccessory.displayName : "",

                hunger = bunny.HungerValue,
                thirst = bunny.ThirstValue,
                energy = bunny.EnergyValue,
                mood = bunny.MoodValue,
                currentHP = bunny.HPValue,

                posX = pos.x, posY = pos.y, posZ = pos.z,

                savedState = role,
                roomInstanceId = room != null ? room.InstanceId : "",
                // Independent of role/room above — see BunnySaveData's own comment. Populated whenever a
                // job is currently held, regardless of what the bunny's CurrentState happens to be right now.
                underlyingJobRoomInstanceId = bunny.AssignedJobRoom != null ? ((RoomBase)bunny.AssignedJobRoom).InstanceId : "",

                isPregnant = bunny.IsPregnant,
                pregnancyElapsed = bunny.PregnancyElapsed,
                pregnancyDuration = bunny.PregnancyDuration,
                litterType = bunny.LitterType,
                litterMembers = bunny.LitterMembers.ToList(),
                claimedHatcheryRoomInstanceId = bunny.ClaimedHatcheryRoom != null ? bunny.ClaimedHatcheryRoom.InstanceId : "",

                isKidBunny = bunny.IsKidBunny,
            };

            data.bunnies.Add(saved);
            indices[bunny] = data.bunnies.Count - 1;
        }

        return indices;
    }

    // See GateQueueSaveData's own comment. Must run after SaveBunnies — needs bunnyIndices, and every
    // queued/backlogged bunny to already be written to data.bunnies (SaveBunnies' own isQueued check).
    private void SaveGateQueue(SaveData data, Dictionary<NPCBunny, int> bunnyIndices)
    {
        if (GateQueueManager.Instance == null) return;

        foreach (NPCBunny bunny in GateQueueManager.Instance.QueuedBunnies)
            if (bunny != null && bunnyIndices.TryGetValue(bunny, out int index))
                data.gateQueue.queuedBunnyIndices.Add(index);

        foreach (NPCBunny bunny in GateQueueManager.Instance.WaitingBacklogBunnies)
            if (bunny != null && bunnyIndices.TryGetValue(bunny, out int index))
                data.gateQueue.waitingBunnyIndices.Add(index);
    }

    private void SaveUnlocks(SaveData data)
    {
        if (RoomUnlockTracker.Instance != null)
            data.unlocks.permanentlyUnlockedRoomIds = RoomUnlockTracker.Instance.ExportUnlockedIds().ToList();
        if (BunnyTypeUnlockTracker.Instance != null)
            data.unlocks.unlockedBunnyTypes = BunnyTypeUnlockTracker.Instance.ExportUnlockedTypes().ToList();
        if (ForagingLocationUnlockTracker.Instance != null)
            data.unlocks.unlockedForagingLocationNames = ForagingLocationUnlockTracker.Instance.ExportUnlockedLocationNames().ToList();
        if (LaboratoryRecipeUnlockTracker.Instance != null)
            data.unlocks.discoveredRecipeNames = LaboratoryRecipeUnlockTracker.Instance.ExportDiscoveredRecipeNames().ToList();
    }

    // ForagingInventoryManager's base stockpile was never saved before this — see ForagingInventorySaveData's
    // own comment. Doesn't depend on rooms/bunnies, so it has no ordering requirement relative to those.
    private void SaveForagingInventory(SaveData data)
    {
        if (ForagingInventoryManager.Instance == null) return;

        data.foragingInventory.crystalCarrotStock = ForagingInventoryManager.Instance.CrystalCarrotStock;
        data.foragingInventory.fruitStock = ForagingInventoryManager.Instance.ExportFruitStock();
        data.foragingInventory.trinketStock = ForagingInventoryManager.Instance.ExportTrinketStock();
        data.foragingInventory.materialStock = ForagingInventoryManager.Instance.ExportMaterialStock();
        data.foragingInventory.consumableStock = ForagingInventoryManager.Instance.ExportConsumableStock();
    }

    private void SaveForagingTrips(SaveData data, Dictionary<NPCBunny, int> bunnyIndices)
    {
        if (ForagingManager.Instance == null) return;

        foreach (NPCBunny bunny in ForagingManager.Instance.GetActiveTrips().ToList())
        {
            if (bunny == null || !bunnyIndices.TryGetValue(bunny, out int index)) continue;
            ForagingTripState trip = ForagingManager.Instance.GetTripState(bunny);
            if (trip == null) continue;

            ForagingTripSaveData saved = new ForagingTripSaveData
            {
                bunnyIndex = index,
                locationName = trip.location != null ? trip.location.displayName : "",
                equippedAccessoryName = trip.equippedAccessory != null ? trip.equippedAccessory.displayName : "",
                potionsRemaining = trip.potionsRemaining,
                carryCapacity = trip.carryCapacity,

                carriedCarrots = trip.carriedCarrots,
                carriedGold = trip.carriedGold,
                foundPotionCount = trip.foundPotionCount,
                carriedCrystalCarrots = trip.carriedCrystalCarrots,
                foundAccessoryNames = trip.foundAccessories.Select(a => a.displayName).ToList(),
                foundFruits = trip.foundFruits.Select(kvp => new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value }).ToList(),
                foundTrinkets = trip.foundTrinkets.Select(kvp => new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value }).ToList(),
                foundHerbs = trip.foundHerbs.Select(kvp => new RarityCountEntry { rarity = kvp.Key, count = kvp.Value }).ToList(),

                enemiesSlain = trip.enemiesSlain,
                elapsedTripTime = trip.elapsedTripTime,
                xpAccumulator = trip.xpAccumulator,
            };

            data.activeTrips.Add(saved);
        }
    }

    // See the Breeding System plan. Eggs don't reference live bunny objects (the parents already forgot
    // about them the moment the egg was laid — see NPCBunny.TryLayEgg's ClearPregnancyState), so unlike
    // SaveForagingTrips above this needs no bunnyIndices cross-reference at all.
    private void SaveEggs(SaveData data)
    {
        if (BaseManager.Instance == null) return;

        foreach (HatcheryRoom hatchery in BaseManager.Instance.Hatcheries)
        {
            if (hatchery == null) continue;

            foreach (Egg egg in hatchery.ActiveEggs.ToList())
            {
                if (egg == null) continue;

                data.eggs.Add(new EggSaveData
                {
                    hatcheryRoomInstanceId = hatchery.InstanceId,
                    spotIndex = hatchery.IndexOfSpot(egg.ClaimedSpot),
                    type = egg.Type,
                    litterMembers = egg.LitterMembers.ToList(),
                    incubationElapsed = egg.IncubationElapsed,
                    incubationDuration = egg.IncubationDuration,
                });
            }
        }
    }

    // ================================================================================================
    // LOAD
    // ================================================================================================

    // True for the whole duration of a LoadGame() call. RoomTypeUnlockAnnouncer/ForagingLocationUnlockTracker's
    // own CheckForNewUnlocks (both subscribed to PopulationManager.OnPopulationChanged, active well before
    // any Start() runs) check this and skip entirely while true — without it, LoadPopulation below fires
    // that event SYNCHRONOUSLY with each tracker's bookkeeping still seeded from ITS OWN Start() (which may
    // have run against the still-default population, before this load restored the real one), so the live
    // check would spuriously re-fire a reveal notification for content the player already had, before the
    // explicit SeedAlreadyUnlocked()/SeedAlreadyRevealedTypes() calls further down ever get a chance to
    // correct that bookkeeping. Those explicit calls still fully reconcile state either way; this flag only
    // stops a live notification from firing during the reconciliation itself.
    public static bool IsLoading { get; private set; }

    public void LoadGame()
    {
        string json = File.ReadAllText(SavePath);
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        if (data == null)
        {
            Debug.LogWarning("SaveManager: save file couldn't be parsed — starting fresh instead.");
            return;
        }

        IsLoading = true;
        try
        {
            InvasionManager.Instance?.ForceClearAllInvasions();

            Dictionary<string, RoomBase> roomsByInstanceId = LoadRooms(data);
            LoadPopulation(data);
            LoadUnlocks(data);
            LoadForagingInventory(data);

            // Must run AFTER LoadPopulation, and explicitly (not just left to RoomTypeUnlockAnnouncer's own
            // Start()) — Unity gives no ordering guarantee between two different components' Start() methods,
            // so if that Start() ran before the line above restored the real population, its own silent seed
            // would have run against the still-default population and every already-unlocked room would fire
            // a spurious "New room type unlocked!" the instant LoadPopulation's OnPopulationChanged fires —
            // IsLoading (above) is what actually stops that notification from firing live in the meantime;
            // this call is what makes the tracker's bookkeeping correct once loading finishes.
            // Safe to call twice (idempotent) regardless of whether its own Start() already ran correctly.
            FindAnyObjectByType<RoomTypeUnlockAnnouncer>()?.SeedAlreadyUnlocked();
            // Same reasoning, same ordering race, for the bunny-type equivalent — see
            // WildBunnySpawner.SeedAlreadyRevealedTypes' own comment. Must run after LoadUnlocks (above),
            // which is what actually restores BunnyTypeUnlockTracker's real unlocked-types set this reads.
            FindAnyObjectByType<WildBunnySpawner>()?.SeedAlreadyRevealedTypes();
            // Same reasoning again for foraging locations — see ForagingLocationUnlockTracker.SeedAlreadyUnlocked's
            // own comment. Must also run after LoadUnlocks, which restores its real unlockedLocations set.
            ForagingLocationUnlockTracker.Instance?.SeedAlreadyUnlocked();
            // Same reasoning again for recipes — see LaboratoryRecipeUnlockTracker.SeedAlreadyUnlocked's own
            // comment. Currently unreachable in practice (nothing calls DiscoverRecipe reactively yet), but
            // correct the moment a future foraging/quest/visitor system does.
            LaboratoryRecipeUnlockTracker.Instance?.SeedAlreadyUnlocked();
            if (GameSpeedManager.Instance != null)
                GameSpeedManager.Instance.SetSpeedIndex(data.gameSpeed.speedIndex);

            List<NPCBunny> loadedBunnies = LoadBunnies(data, roomsByInstanceId);
            LoadResources(data); // after rooms so every *Max is already correctly recomputed
            LoadGateQueue(data, loadedBunnies);
            LoadForagingTrips(data, loadedBunnies);
            // Eggs don't reference live bunny objects, so order relative to LoadBunnies doesn't matter —
            // kept adjacent to LoadForagingTrips stylistically (both are "in-flight, non-bunny-keyed
            // state reconstructed after rooms"). See the Breeding System plan.
            LoadEggs(data, roomsByInstanceId);

            if (WildBunnyNames.Instance != null)
                WildBunnyNames.Instance.ImportUsedNames(data.bunnyNames.usedNames);

            Debug.Log("SaveManager: load complete.");
        }
        finally
        {
            // finally, not just a trailing statement — an exception partway through the block above must
            // not leave every room/bunny-type/foraging reveal notification permanently suppressed for the
            // rest of the session.
            IsLoading = false;
        }
    }

    private Dictionary<string, RoomBase> LoadRooms(SaveData data)
    {
        Dictionary<string, RoomBase> roomsByInstanceId = new Dictionary<string, RoomBase>();
        if (BaseLayoutManager.Instance == null) return roomsByInstanceId;

        foreach (RoomSaveData roomData in data.rooms)
        {
            // A room already sitting at this exact grid position is a scene-authored baseline room
            // (e.g. the Entrance) that existed before the save was ever written — two rooms can never
            // legitimately share a grid cell, so this reliably means "don't duplicate it, just adopt its
            // saved id." Anything not already present gets instantiated fresh.
            RoomBase existing = BaseLayoutManager.Instance.GetAllRoomsOnFloor(roomData.floorIndex)
                .FirstOrDefault(r => r.GridX == roomData.gridX);

            RoomBase resolvedRoom;
            if (existing != null)
            {
                existing.SetInstanceId(roomData.instanceId);
                resolvedRoom = existing;
            }
            else
            {
                RoomDefinition definition = RoomCatalogRegistry.Instance != null
                    ? RoomCatalogRegistry.Instance.FindVariant(roomData.roomTypeId, Mathf.RoundToInt(roomData.footprintWidth), roomData.grade)
                    : null;

                if (definition == null || definition.prefab == null)
                {
                    Debug.LogWarning($"SaveManager: no RoomDefinition found for type '{roomData.roomTypeId}' (width {roomData.footprintWidth}, grade {roomData.grade}) — skipping this room.");
                    continue;
                }

                float y = BaseLayoutManager.Instance.GetWorldYForFloor(roomData.floorIndex);
                // worldX over gridX — see RoomSaveData.worldX's own comment; gridX alone would silently
                // shift an odd-footprint room (currently only LiftRoom) by 0.5 units off its true position.
                float x = float.IsNaN(roomData.worldX) ? roomData.gridX : roomData.worldX;
                Vector3 position = new Vector3(x, y, BaseLayoutManager.Instance.EntranceWorldZ);
                GameObject instance = Instantiate(definition.prefab, position, Quaternion.Euler(0f, 180f, 0f));
                resolvedRoom = instance.GetComponent<RoomBase>();
                resolvedRoom?.SetInstanceId(roomData.instanceId);
            }

            if (resolvedRoom != null)
                roomsByInstanceId[roomData.instanceId] = resolvedRoom;

            // LiftRoom is the one room type that doesn't finish registering during Instantiate — it
            // defers floor-detection to its own Start(), and shaft-grouping a further frame beyond that
            // (see LiftRoom.RegisterFloorIfNeeded's own comment for why). Everything else below in this
            // synchronous load — reconstructing bunnies, letting gameplay resume — can't wait that long,
            // so force the floor-registration half right here instead of hoping Start() gets there first.
            if (resolvedRoom is LiftRoom liftRoom)
                liftRoom.RegisterFloorIfNeeded();
        }

        // Now that every lift segment in the save (existing or freshly built) has a correctly-set
        // DetectedFloorIndex, form every shaft synchronously — same call the deferred coroutine would
        // eventually make on its own, just done immediately so bunnies reconstructed right after this
        // (see LoadBunnies) never find an unregistered lift the way the original bug report did.
        foreach (int gridX in roomsByInstanceId.Values.OfType<LiftRoom>().Select(l => l.GridX).Distinct().ToList())
            LiftRoom.RegroupColumn(gridX);

        return roomsByInstanceId;
    }

    private void LoadPopulation(SaveData data)
    {
        PopulationManager.Instance?.SetCounts(data.population.inBase, data.population.questing, data.population.foraging, data.population.egg);
    }

    private void LoadUnlocks(SaveData data)
    {
        RoomUnlockTracker.Instance?.ImportUnlockedIds(data.unlocks.permanentlyUnlockedRoomIds);
        BunnyTypeUnlockTracker.Instance?.ImportUnlockedTypes(data.unlocks.unlockedBunnyTypes);
        ForagingLocationUnlockTracker.Instance?.ImportUnlockedLocationNames(data.unlocks.unlockedForagingLocationNames);
        LaboratoryRecipeUnlockTracker.Instance?.ImportDiscoveredRecipeNames(data.unlocks.discoveredRecipeNames);
    }

    private void LoadForagingInventory(SaveData data)
    {
        if (ForagingInventoryManager.Instance == null) return;

        ForagingInventoryManager.Instance.SetCrystalCarrotStockForLoad(data.foragingInventory.crystalCarrotStock);
        ForagingInventoryManager.Instance.ImportFruitStock(data.foragingInventory.fruitStock);
        ForagingInventoryManager.Instance.ImportTrinketStock(data.foragingInventory.trinketStock);
        ForagingInventoryManager.Instance.ImportMaterialStock(data.foragingInventory.materialStock);
        ForagingInventoryManager.Instance.ImportConsumableStock(data.foragingInventory.consumableStock);
    }

    private void LoadResources(SaveData data)
    {
        CarrotManager.Instance?.SetCurrentCarrots(data.resources.currentCarrots);
        GoldManager.Instance?.SetCurrentGold(data.resources.currentGold);
        WaterManager.Instance?.SetCurrentWater(data.resources.currentWater);
        PowerManager.Instance?.SetRationingPoolCurrent(data.resources.powerRationingPoolCurrent);
        WaterRationingManager.Instance?.SetRationingPoolCurrent(data.resources.waterRationingPoolCurrent);
    }

    private List<NPCBunny> LoadBunnies(SaveData data, Dictionary<string, RoomBase> roomsByInstanceId)
    {
        List<NPCBunny> loaded = new List<NPCBunny>();
        WildBunnySpawner spawner = FindAnyObjectByType<WildBunnySpawner>();

        foreach (BunnySaveData bunnyData in data.bunnies)
        {
            BunnyTypeDefinition typeDef = spawner != null
                ? spawner.BunnyTypes.FirstOrDefault(t => t != null && t.type == bunnyData.bunnyType)
                : null;

            if (typeDef == null || typeDef.prefab == null)
            {
                Debug.LogWarning($"SaveManager: no prefab found for bunny type {bunnyData.bunnyType} — dropping saved bunny '{bunnyData.bunnyName}'.");
                loaded.Add(null);
                continue;
            }

            Vector3 spawnPosition = new Vector3(bunnyData.posX, bunnyData.posY, bunnyData.posZ);
            GameObject spawnedObject = Instantiate(typeDef.prefab, spawnPosition, Quaternion.identity);
            NPCBunny bunny = spawnedObject.GetComponent<NPCBunny>();
            if (bunny == null)
            {
                Debug.LogWarning($"SaveManager: {bunnyData.bunnyType}'s prefab has no NPCBunny component.");
                Destroy(spawnedObject);
                loaded.Add(null);
                continue;
            }

            List<BunnyTraitDefinition> traits = BunnyTraitCatalog.Instance != null
                ? BunnyTraitCatalog.Instance.AllTraits.Where(t => bunnyData.traitIds.Contains(t.id)).ToList()
                : new List<BunnyTraitDefinition>();
            List<BunnyPassiveDefinition> passives = typeDef.passives
                .Where(p => bunnyData.passiveIds.Contains(p.id)).ToList();
            ForagingAccessoryDefinition accessory = ResolveAccessoryByName(bunnyData.equippedAccessoryName);

            bunny.RestoreFromSave(bunnyData.gender, bunnyData.bunnyName, BunnyArrivalType.Wild, typeDef, bunnyData.level, bunnyData.experience,
                bunnyData.nature, bunnyData.ivHP, bunnyData.ivAttack, bunnyData.ivDefense, bunnyData.ivSpeed, bunnyData.ivLuck,
                bunnyData.evHP, bunnyData.evAttack, bunnyData.evDefense, bunnyData.evSpeed, bunnyData.evLuck,
                traits, passives, accessory,
                bunnyData.hunger, bunnyData.thirst, bunnyData.energy, bunnyData.mood, bunnyData.currentHP);

            RoomBase room = !string.IsNullOrEmpty(bunnyData.roomInstanceId) && roomsByInstanceId.TryGetValue(bunnyData.roomInstanceId, out RoomBase r)
                ? r : null;
            bunny.RestoreRoomAssignment(bunnyData.savedState, room);

            // Fixes a real bug: a job-assigned bunny saved mid-Sleep (or Eating/Drinking) previously lost
            // her job entirely on reload, since RestoreRoomAssignment's non-Working branches never touch
            // AssignedJobRoom — see RestoreUnderlyingJobAssignment's own comment. No-ops if the primary
            // role above was already Working (that branch already reclaimed the job the normal way) or if
            // no underlying job was saved.
            if (!string.IsNullOrEmpty(bunnyData.underlyingJobRoomInstanceId)
                && roomsByInstanceId.TryGetValue(bunnyData.underlyingJobRoomInstanceId, out RoomBase jobRoomBase)
                && jobRoomBase is IJobRoom underlyingJobRoom)
            {
                bunny.RestoreUnderlyingJobAssignment(underlyingJobRoom);
            }

            // See the Breeding System plan. Deliberately does NOT call HatcheryRoom.ReserveCapacity again
            // here — reservedCount isn't persisted as a raw number at all (see its own comment); each
            // HatcheryRoom instead recomputes it once, after every bunny in this loop has been restored
            // (see the ResyncReservedCounts call at the end of LoadBunnies below), by counting how many
            // just-restored bunnies are isPregnant and point at it. This avoids a second source of truth
            // that could drift from the actual restored bunny states.
            if (bunnyData.isPregnant
                && !string.IsNullOrEmpty(bunnyData.claimedHatcheryRoomInstanceId)
                && roomsByInstanceId.TryGetValue(bunnyData.claimedHatcheryRoomInstanceId, out RoomBase hatcheryRoomBase)
                && hatcheryRoomBase is HatcheryRoom hatcheryRoom)
            {
                bunny.BeginPregnancy(bunnyData.litterType, bunnyData.litterMembers, bunnyData.pregnancyDuration, hatcheryRoom, bunnyData.pregnancyElapsed);
            }

            if (bunnyData.isKidBunny)
                bunny.SetIsKidBunny(true);

            loaded.Add(bunny);
        }

        ResyncHatcheryReservedCounts(loaded);

        return loaded;
    }

    // See the Breeding System plan / BeginPregnancy's own comment above. Every HatcheryRoom's
    // reservedCount is recomputed from scratch here — after every bunny above has been restored, so this
    // reflects the real, final set of pregnant bunnies rather than a raw saved number that could drift.
    private void ResyncHatcheryReservedCounts(List<NPCBunny> loadedBunnies)
    {
        if (BaseManager.Instance == null) return;

        foreach (HatcheryRoom hatchery in BaseManager.Instance.Hatcheries)
        {
            if (hatchery == null) continue;
            int count = loadedBunnies.Count(b => b != null && b.IsPregnant && b.ClaimedHatcheryRoom == hatchery);
            hatchery.SetReservedCountForLoad(count);
        }
    }

    // See GateQueueSaveData's own comment. Runs right after LoadBunnies so bunnyIndex -> NPCBunny
    // resolution is available, and before LoadForagingTrips (order between the two doesn't actually
    // matter — a bunny can never be in both lists — kept here purely to mirror LoadBunnies' own call
    // order in LoadGame). Each resolved bunny needs SetQueuedStateDirect to undo RestoreFromSave's
    // unconditional HasEnteredBase = true before GateQueueManager.RestoreQueueState re-inserts her into
    // the right queue list — same "generic restore, then a dedicated override pass" shape LoadForagingTrips/
    // SetForagingStateDirect already use for an away-foraging bunny.
    private void LoadGateQueue(SaveData data, List<NPCBunny> loadedBunnies)
    {
        if (GateQueueManager.Instance == null) return;

        List<NPCBunny> queuedBunnies = ResolveGateQueueIndices(data.gateQueue.queuedBunnyIndices, loadedBunnies);
        List<NPCBunny> waitingBunnies = ResolveGateQueueIndices(data.gateQueue.waitingBunnyIndices, loadedBunnies);

        foreach (NPCBunny bunny in queuedBunnies)
            bunny.SetQueuedStateDirect();
        foreach (NPCBunny bunny in waitingBunnies)
            bunny.SetQueuedStateDirect();

        GateQueueManager.Instance.RestoreQueueState(queuedBunnies, waitingBunnies);
    }

    private static List<NPCBunny> ResolveGateQueueIndices(List<int> indices, List<NPCBunny> loadedBunnies)
    {
        List<NPCBunny> resolved = new List<NPCBunny>();
        foreach (int index in indices)
        {
            if (index < 0 || index >= loadedBunnies.Count || loadedBunnies[index] == null) continue;
            resolved.Add(loadedBunnies[index]);
        }
        return resolved;
    }

    private void LoadForagingTrips(SaveData data, List<NPCBunny> loadedBunnies)
    {
        if (ForagingManager.Instance == null) return;

        foreach (ForagingTripSaveData tripData in data.activeTrips)
        {
            if (tripData.bunnyIndex < 0 || tripData.bunnyIndex >= loadedBunnies.Count) continue;
            NPCBunny bunny = loadedBunnies[tripData.bunnyIndex];
            if (bunny == null) continue;

            ForagingLocationDefinition location = ForagingManager.Instance.Locations
                .FirstOrDefault(l => l != null && l.displayName == tripData.locationName);
            if (location == null)
            {
                Debug.LogWarning($"SaveManager: foraging location '{tripData.locationName}' not found — dropping {bunny.BunnyName}'s in-progress trip.");
                continue;
            }

            ForagingTripState trip = new ForagingTripState
            {
                location = location,
                equippedAccessory = ResolveAccessoryByName(tripData.equippedAccessoryName),
                potionsRemaining = tripData.potionsRemaining,
                carryCapacity = tripData.carryCapacity,
                carriedCarrots = tripData.carriedCarrots,
                carriedGold = tripData.carriedGold,
                foundPotionCount = tripData.foundPotionCount,
                carriedCrystalCarrots = tripData.carriedCrystalCarrots,
                enemiesSlain = tripData.enemiesSlain,
                elapsedTripTime = tripData.elapsedTripTime,
                xpAccumulator = tripData.xpAccumulator,
            };

            foreach (string accessoryName in tripData.foundAccessoryNames)
            {
                ForagingAccessoryDefinition a = ResolveAccessoryByName(accessoryName);
                if (a != null) trip.foundAccessories.Add(a);
            }
            foreach (NamedCountEntry entry in tripData.foundFruits)
            {
                ForagingFruitDefinition f = ResolveFruitByName(entry.name);
                if (f != null) trip.foundFruits[f] = entry.count;
            }
            foreach (NamedCountEntry entry in tripData.foundTrinkets)
            {
                ForagingTrinketDefinition t = ResolveTrinketByName(entry.name);
                if (t != null) trip.foundTrinkets[t] = entry.count;
            }
            foreach (RarityCountEntry entry in tripData.foundHerbs)
                trip.foundHerbs[entry.rarity] = entry.count;

            Transform stagingPoint = ForagingManager.Instance.StagingPoint;
            Vector3 stagingPosition = stagingPoint != null ? stagingPoint.position : bunny.transform.position;

            bunny.SetForagingStateDirect(stagingPosition);
            ForagingManager.Instance.ResumeTrip(bunny, trip);
        }
    }

    // See the Breeding System plan. Reconstructs each saved egg at its EXACT saved spot index (via
    // ClaimSpecificSpot, not ClaimAnyFreeSpot) so a reloaded egg doesn't visibly shuffle to a different
    // position for no reason, with incubationElapsed restored rather than reset to 0.
    private void LoadEggs(SaveData data, Dictionary<string, RoomBase> roomsByInstanceId)
    {
        GameObject eggPrefab = BreedingConfig.Instance.eggPrefab;
        if (eggPrefab == null)
        {
            if (data.eggs.Count > 0)
                Debug.LogWarning("SaveManager: save has eggs but BreedingConfig has no eggPrefab assigned — every saved egg dropped.");
            return;
        }

        foreach (EggSaveData eggData in data.eggs)
        {
            if (string.IsNullOrEmpty(eggData.hatcheryRoomInstanceId)
                || !roomsByInstanceId.TryGetValue(eggData.hatcheryRoomInstanceId, out RoomBase roomBase)
                || !(roomBase is HatcheryRoom hatchery))
            {
                Debug.LogWarning($"SaveManager: saved egg's Hatchery ({eggData.hatcheryRoomInstanceId}) no longer exists — egg dropped.");
                continue;
            }

            GameObject eggObject = Instantiate(eggPrefab);
            Egg egg = eggObject.GetComponent<Egg>();
            if (egg == null)
            {
                Debug.LogWarning("SaveManager: eggPrefab has no Egg component — saved egg dropped.");
                Destroy(eggObject);
                continue;
            }

            RoomSpot spot = hatchery.ClaimSpecificSpot(eggData.spotIndex, egg);
            if (spot == null)
            {
                Debug.LogWarning($"SaveManager: saved egg's spot index {eggData.spotIndex} in {hatchery.name} is no longer available — egg dropped.");
                Destroy(eggObject);
                continue;
            }

            egg.Initialize(eggData.type, eggData.litterMembers, hatchery, spot, eggData.incubationDuration, eggData.incubationElapsed);
        }
    }

    // ---------- Asset-by-name resolvers (ScriptableObject asset refs can't round-trip through JSON —
    // every location's lootTable is scanned as the closest thing this project has to a catalog for
    // these three types; see SaveData.cs's file header for why this pattern is needed at all) ----------

    private static ForagingAccessoryDefinition ResolveAccessoryByName(string name)
    {
        if (string.IsNullOrEmpty(name) || ForagingManager.Instance == null) return null;
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            ForagingAccessoryDefinition match = location?.lootTable?
                .Select(e => e.accessory).FirstOrDefault(a => a != null && a.displayName == name);
            if (match != null) return match;
        }
        return null;
    }

    private static ForagingFruitDefinition ResolveFruitByName(string name)
    {
        if (string.IsNullOrEmpty(name) || ForagingManager.Instance == null) return null;
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            ForagingFruitDefinition match = location?.lootTable?
                .Select(e => e.fruit).FirstOrDefault(f => f != null && f.displayName == name);
            if (match != null) return match;
        }
        return null;
    }

    private static ForagingTrinketDefinition ResolveTrinketByName(string name)
    {
        if (string.IsNullOrEmpty(name) || ForagingManager.Instance == null) return null;
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            ForagingTrinketDefinition match = location?.lootTable?
                .Select(e => e.trinket).FirstOrDefault(t => t != null && t.displayName == name);
            if (match != null) return match;
        }
        return null;
    }
}
