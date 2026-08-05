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

        // No bunny is left mid-queue at save time — see GateQueueManager.ResolveQueueForSave's own comment.
        GateQueueManager.Instance?.ResolveQueueForSave();

        SaveData data = new SaveData();

        SaveResources(data);
        SavePopulation(data);
        SaveRooms(data);
        Dictionary<NPCBunny, int> bunnyIndices = SaveBunnies(data);
        SaveUnlocks(data);
        SaveForagingTrips(data, bunnyIndices);
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
            if (!bunny.HasEnteredBase && !isForaging) continue; // still queued/awaiting approval — not a real resident and not away on a trip either

            SavedBunnyRole role;
            RoomBase room;
            if (bunny.CurrentState == BunnyState.Working) { role = SavedBunnyRole.Working; room = bunny.AssignedJobRoom as RoomBase; }
            else if (bunny.CurrentState == BunnyState.Sleeping) { role = SavedBunnyRole.Sleeping; room = bunny.ClaimedSleepRoom; }
            else if (bunny.CurrentState == BunnyState.Relaxing) { role = SavedBunnyRole.Relaxing; room = bunny.ClaimedRelaxRoom; }
            else
            {
                // Idle, Foraging (handled separately via activeTrips below), and every mid-transit/
                // Eating/Drinking/hospital state all snap down to Idle — see SavedBunnyRole's own comment.
                role = SavedBunnyRole.Idle;
                room = bunny.CurrentRoom;
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
            };

            data.bunnies.Add(saved);
            indices[bunny] = data.bunnies.Count - 1;
        }

        return indices;
    }

    private void SaveUnlocks(SaveData data)
    {
        if (RoomUnlockTracker.Instance != null)
            data.unlocks.permanentlyUnlockedRoomIds = RoomUnlockTracker.Instance.ExportUnlockedIds().ToList();
        if (BunnyTypeUnlockTracker.Instance != null)
            data.unlocks.unlockedBunnyTypes = BunnyTypeUnlockTracker.Instance.ExportUnlockedTypes().ToList();
        if (ForagingLocationUnlockTracker.Instance != null)
            data.unlocks.unlockedForagingLocationNames = ForagingLocationUnlockTracker.Instance.ExportUnlockedLocationNames().ToList();
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

    // ================================================================================================
    // LOAD
    // ================================================================================================

    public void LoadGame()
    {
        string json = File.ReadAllText(SavePath);
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        if (data == null)
        {
            Debug.LogWarning("SaveManager: save file couldn't be parsed — starting fresh instead.");
            return;
        }

        InvasionManager.Instance?.ForceClearAllInvasions();

        Dictionary<string, RoomBase> roomsByInstanceId = LoadRooms(data);
        LoadPopulation(data);
        LoadUnlocks(data);

        // Must run AFTER LoadPopulation, and explicitly (not just left to RoomTypeUnlockAnnouncer's own
        // Start()) — Unity gives no ordering guarantee between two different components' Start() methods,
        // so if that Start() ran before the line above restored the real population, its own silent seed
        // would have run against the still-default population and every already-unlocked room would fire
        // a spurious "New room type unlocked!" the instant LoadPopulation's OnPopulationChanged fires.
        // Safe to call twice (idempotent) regardless of whether its own Start() already ran correctly.
        FindAnyObjectByType<RoomTypeUnlockAnnouncer>()?.SeedAlreadyUnlocked();
        if (GameSpeedManager.Instance != null)
            GameSpeedManager.Instance.SetSpeedIndex(data.gameSpeed.speedIndex);

        List<NPCBunny> loadedBunnies = LoadBunnies(data, roomsByInstanceId);
        LoadResources(data); // after rooms so every *Max is already correctly recomputed
        LoadForagingTrips(data, loadedBunnies);

        if (WildBunnyNames.Instance != null)
            WildBunnyNames.Instance.ImportUsedNames(data.bunnyNames.usedNames);

        Debug.Log("SaveManager: load complete.");
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
                Vector3 position = new Vector3(roomData.gridX, y, BaseLayoutManager.Instance.EntranceWorldZ);
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

            loaded.Add(bunny);
        }

        return loaded;
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
