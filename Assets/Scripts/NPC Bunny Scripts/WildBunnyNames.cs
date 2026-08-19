using UnityEngine;
using System.Collections.Generic;

// One name pool per bunny type, authored by Ethan (see TYPE NAMES LIST.txt at the project root).
// Male names stay masculine/neutral; female names have more leeway to be masculine/neutral/feminine,
// same as real-world naming conventions.
[System.Serializable]
public class TypeNamePool
{
    public BunnyType type;
    public string[] maleNames;
    public string[] femaleNames;
}

public class WildBunnyNames : MonoBehaviour
{
    public static WildBunnyNames Instance { get; private set; }

    // Originally only the 5 types with art at launch (see BunnyType's Group 1 comment in NPCBunny.cs)
    // had pools; Insect/Pixie/Toxic joined below as their art/combat work wrapped up 2026-08-16. Add a
    // new entry here when a future type gets its own — GetPool falls back to null (-> "Unnamed") until
    // it does. Names authored by Ethan throughout, not generated here — see WildBunnyNames_DesignDoc.md's
    // Non-goals.
    [SerializeField]
    private List<TypeNamePool> namePools = new List<TypeNamePool>
    {
        new TypeNamePool
        {
            type = BunnyType.Neutral,
            maleNames = new[] { "Buck", "Jack", "Hopper", "Bounder", "Skip", "Leaper", "Dash", "Scamper", "Pip", "Jumper", "Nibbler", "Whiskers", "Twitch", "Flick", "Zoom", "Speedy", "Kit", "Springer", "Warren", "Lop", "Hippity", "Boing", "Stomper", "Cony", "Hutch", "Buttons", "Biscuit", "Chip", "Buster" },
            femaleNames = new[] { "Bunny", "Cottontail", "Hoppy", "Doe", "Fluff", "Bounce", "Pounce", "Nibbles", "Twitchy", "Floppy", "Skippy", "Wiggle", "Powderpuff", "Fluffle", "Marshmallow", "Puff", "Dashie", "Zippy", "Springy", "Jumpette", "Ricochet", "Velvet", "Honeybun", "Bunbun", "Hopscotch", "Twirl", "Tumbles", "Peaches", "Mochi" }
        },
        new TypeNamePool
        {
            type = BunnyType.Fire,
            maleNames = new[] { "Ash", "Blaze", "Ember", "Cinder", "Scorch", "Flint", "Ignis", "Pyro", "Smolder", "Char", "Kindle", "Wildfire", "Sear", "Vulcan", "Torch", "Magma", "Griff", "Zippo", "Brick", "Burn", "Redd", "Furnace", "Brimstone", "Smoky", "Kiln" },
            femaleNames = new[] { "Crimson", "Pyra", "Pele", "Scarlet", "Sienna", "Flare", "Cherry", "Ignatia", "Solstice", "Asher", "Phoenix", "Cayenne", "Wildflame", "Salsa", "Vesta", "Cindra", "Brenna", "Sunburst", "Firefly", "Torrid", "Inferna", "Sunny", "Amber", "Combusta", "Ruby" }
        },
        new TypeNamePool
        {
            type = BunnyType.Water,
            maleNames = new[] { "Marlin", "Finn", "Torrent", "Reef", "Kelp", "Wade", "Brook", "Cove", "Drake", "Riptide", "Squall", "Ripple", "Stingray", "Undertow", "Kai", "Pirate", "Barnacle", "Skipper", "Eel", "Deluge", "Fathom", "Puddle", "Splash", "Abyss", "Whirlpool" },
            femaleNames = new[] { "Marina", "Coral", "Delta", "Misty", "Pearl", "Brooke", "Harbor", "Lagoon", "Wave", "Dew", "Ocean", "Bubbles", "Kelpie", "Siren", "Rain", "Cascade", "Aqua", "Lorelei", "Dory", "Shelly", "River", "Sea", "Dewdrop", "Bayou", "Foam" }
        },
        new TypeNamePool
        {
            type = BunnyType.Plant,
            maleNames = new[] { "Thorne", "Basil", "Bramble", "Cedar", "Oakley", "Sprout", "Moss", "Root", "Bur", "Nettle", "Clover", "Nightshade", "Fern", "Birch", "Thistle", "Cactus", "Ragweed", "Hemlock", "Bramblewood", "Grove", "Sage", "Bracken", "Elm", "Pollen", "Vine" },
            femaleNames = new[] { "Rosette", "Poppy", "Ivy", "Willow", "Marigold", "Blossom", "Petunia", "Dahlia", "Fennel", "Zinnia", "Clementine", "Hazel", "Briar", "Lily", "Saffron", "Peony", "Wisteria", "Sequoia", "Petal", "Meadow", "Foxglove", "Aster", "Juniper", "Bellflower", "Sorrel" }
        },
        new TypeNamePool
        {
            type = BunnyType.Shock,
            maleNames = new[] { "Volt", "Bolt", "Spark", "Ampere", "Ohm", "Watt", "Jolt", "Static", "Surge", "Flash", "Lightning", "Zap", "Storm", "Thunder", "Fuse", "Coil", "Circuit", "Arc", "Ion", "Blitz", "Neon", "Gauge", "Ray", "Shock", "Zeus" },
            femaleNames = new[] { "Electra", "Beam", "Sparkle", "Nova", "Livewire", "Flicker", "Photon", "Bright", "Dazzle", "Galvan", "Glow", "Aurora", "Sizzle", "Tesla", "Corona", "Fusion", "Xenia", "Volta", "Kirlian", "Faraday", "Voltage", "Crackle", "Luma", "Wired", "Shimmer" }
        },
        new TypeNamePool
        {
            type = BunnyType.Insect,
            maleNames = new[] { "Buzz", "Sting", "Beetle", "Locust", "Hornet", "Wasp", "Grub", "Ant", "Stag", "Roach", "Hercules", "Skitter", "Chitin", "Drone", "Pincer", "Mandible", "Termite", "Gnat", "Scarab", "Wriggler", "Chafer", "Skeeter", "Cricket", "Buggy", "Buzzy" },
            femaleNames = new[] { "Cicada", "Weevil", "Mantis", "Bee", "Ladybug", "Luna", "Monarch", "Antenna", "Honey", "Nectar", "Chrysalis", "Silky", "Flutter", "Papillon", "Cecropia", "Cocoon", "Waspina", "Glowworm", "Iris", "Vespa", "Dragonfly", "Beetlelyn", "Mothina", "Lacewing", "Rolipoli" }
        },
        new TypeNamePool
        {
            type = BunnyType.Toxic,
            maleNames = new[] { "Venom", "Toxin", "Sludge", "Fume", "Blight", "Rot", "Gas", "Bane", "Wolfsbane", "Grime", "Ooze", "Vial", "Fester", "Mercury", "Arsen", "Cesium", "Nox", "Acid", "Reek", "Sulfur", "Leech", "Scum", "Basilisk", "Plague", "Cyanide" },
            femaleNames = new[] { "Circe", "Medea", "Hecate", "Locusta", "Belladonna", "Miasma", "Wormwood", "Cobra", "Mamba", "Widow", "Scorpia", "Vipera", "Fatalis", "Corrosia", "Effluvia", "Contagia", "Malaise", "Necra", "Sable", "Adder", "Vex", "Nyxia", "Toxique", "Pestilenza", "Rue" }
        },
        new TypeNamePool
        {
            type = BunnyType.Pixie,
            maleNames = new[] { "Puck", "Oberon", "Robin", "Tamlin", "Doon", "Sprig", "Wisp", "Flit", "Glint", "Sprite", "Elfin", "Corrigan", "Feylan", "Gossamer", "Goodfellow", "Whistlewick", "Dewkin", "Pipkin", "Fenwick", "Lob", "Hob", "Grig", "Bogle", "Peregrine", "Tod" },
            femaleNames = new[] { "Fiona", "Aine", "Nimue", "Melusine", "Faela", "Sylph", "Peri", "Fata", "Brownie", "Glisten", "Twinkle", "Starling", "Moonbeam", "Petalwing", "Rosalind", "Elowen", "Brighid", "Liriel", "Selene", "Fawn", "Wrenna", "Larkspur", "Cloudwisp", "Freya", "Danu" }
        },
    };

    // Names already handed out this session, per (type, gender) pool — once a pool's remaining names
    // run out, its used-set is cleared and it starts drawing from the full pool again (duplicates
    // allowed again from that point). In-memory only for now since the project has no save system yet;
    // Export/ImportUsedNames below exist so a future save system can persist this without touching the
    // draw logic itself.
    private readonly Dictionary<(BunnyType, BunnyGender), HashSet<string>> usedNames =
        new Dictionary<(BunnyType, BunnyGender), HashSet<string>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public BunnyGender GetRandomGender()
    {
        return Random.value < 0.5f ? BunnyGender.Male : BunnyGender.Female;
    }

    public string GetRandomName(BunnyType type, BunnyGender gender)
    {
        string[] pool = GetPool(type, gender);
        if (pool == null || pool.Length == 0) return "Unnamed";

        var key = (type, gender);
        if (!usedNames.TryGetValue(key, out HashSet<string> used))
        {
            used = new HashSet<string>();
            usedNames[key] = used;
        }

        List<string> remaining = new List<string>();
        foreach (string name in pool)
        {
            if (!used.Contains(name)) remaining.Add(name);
        }

        // Pool exhausted this session -> start a fresh cycle (duplicates now allowed again).
        if (remaining.Count == 0)
        {
            used.Clear();
            remaining.AddRange(pool);
        }

        string chosen = remaining[Random.Range(0, remaining.Count)];
        used.Add(chosen);
        return chosen;
    }

    private string[] GetPool(BunnyType type, BunnyGender gender)
    {
        foreach (TypeNamePool entry in namePools)
        {
            if (entry.type != type) continue;
            return gender == BunnyGender.Male ? entry.maleNames : entry.femaleNames;
        }
        return null;
    }

    // --- Save-system scaffolding ---------------------------------------------------------------
    // No save system exists in this project yet. Once one does, call ExportUsedNames() when writing
    // a save and ImportUsedNames() right after Awake() on load, so reloading a save doesn't
    // un-exhaust a pool that was already drawn dry.
    [System.Serializable]
    public struct UsedNameEntry
    {
        public BunnyType type;
        public BunnyGender gender;
        public List<string> names;
    }

    public List<UsedNameEntry> ExportUsedNames()
    {
        List<UsedNameEntry> export = new List<UsedNameEntry>();
        foreach (var kvp in usedNames)
        {
            export.Add(new UsedNameEntry
            {
                type = kvp.Key.Item1,
                gender = kvp.Key.Item2,
                names = new List<string>(kvp.Value)
            });
        }
        return export;
    }

    public void ImportUsedNames(List<UsedNameEntry> entries)
    {
        usedNames.Clear();
        if (entries == null) return;

        foreach (UsedNameEntry entry in entries)
        {
            usedNames[(entry.type, entry.gender)] = new HashSet<string>(entry.names);
        }
    }
}
