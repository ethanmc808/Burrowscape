using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Adds a button to every room's Inspector (any RoomBase-derived component) that WIPES AND REGENERATES
// its spot list(s) and RoomBase.paths from the room's own current hierarchy. Deliberately destructive
// by design, not a merge — every merged/upgraded-size room prefab (e.g. Garden_12x2x6_Grade2) needs its
// spots hand-placed and its waypoints hand-routed around that specific prefab's furniture from scratch,
// since spots move whenever a room widens or changes Grade, so there's rarely anything worth preserving
// from a previous run. Operates ONLY on whichever instance is currently open in the Inspector (a prefab
// in Prefab Mode, or a scene GameObject) — never touches any other prefab, unlike the batch MenuItem
// tools elsewhere in this folder (RoomReferenceFixer, RoomDataGenerator).
//
// Spot list fields are matched to child RoomSpot objects by name prefix, via [SpotNamePrefixAttribute]
// on the field (see that file) — not guessed from the field name, since that guess isn't always right
// (WaterRoom's `productionSpots` field is populated by children named "PottingSpot_XX", not
// "ProductionSpot_XX"). A field with no attribute is left untouched.
[CustomEditor(typeof(RoomBase), true)]
public class RoomSpotPathAutoPopulator : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RoomBase room = (RoomBase)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Wipes this room's own spot list(s) and Spot Paths, then regenerates them from its current " +
            "RoomSpot children and Left/Right Entrance. Spot Paths get one entry per entrance x spot " +
            "pairing with an EMPTY waypoints list — fill those in by hand afterward. Only affects THIS " +
            "instance, not any other room or prefab.",
            MessageType.Info);

        if (GUILayout.Button("Auto-Populate Spots & Paths (Wipe & Regenerate)"))
        {
            if (EditorUtility.DisplayDialog(
                "Auto-Populate Spots & Paths",
                $"This will wipe and regenerate all spot list(s) and Spot Paths on \"{room.name}\", discarding any manually-added waypoints. Continue?",
                "Wipe & Regenerate", "Cancel"))
            {
                AutoPopulate(room, wipePaths: true);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Same spot-list re-scan, but Spot Paths are additive: existing entries (and their hand-routed " +
            "waypoints) are left untouched — only entrance x spot pairings that don't already have a path " +
            "entry get a new one added (with an EMPTY waypoints list). Use this after adding a new spot " +
            "type (e.g. EnemySpot/CombatSpot) to a room that already has hand-routed paths for its other spots.",
            MessageType.Info);

        if (GUILayout.Button("Auto-Populate Spots & Paths (Add Missing Only)"))
        {
            AutoPopulate(room, wipePaths: false);
        }
    }

    private static void AutoPopulate(RoomBase room, bool wipePaths)
    {
        Undo.RecordObject(room, "Auto-Populate Room Spots & Paths");

        List<RoomSpot> spotsUsedForPaths = PopulateSpotFields(room, out List<string> emptyFields);

        FieldInfo pathsField = typeof(RoomBase).GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic);
        List<RoomPath> existingPaths = wipePaths
            ? new List<RoomPath>()
            : ((List<RoomPath>)pathsField.GetValue(room)) ?? new List<RoomPath>();

        List<RoomSpot> combatSpots = room.CombatSpots ?? new List<RoomSpot>();
        List<RoomSpot> enemySpots = room.EnemySpots ?? new List<RoomSpot>();

        List<Transform> entrances = new List<Transform> { room.LeftEntrance, room.RightEntrance };
        List<string> missingEntrances = new List<string>();
        if (room.LeftEntrance == null) missingEntrances.Add("Left Entrance");
        if (room.RightEntrance == null) missingEntrances.Add("Right Entrance");

        List<RoomPath> resultPaths = new List<RoomPath>(existingPaths);
        int addedCount = 0;

        // EnemySpots never get an entrance path — enemies spawn straight onto their spot via
        // InvasionManager.TrySpawnInvasion's Instantiate(prefab, spot.transform.position, ...), no gate
        // walk-in built yet ("a documented future addition, not built here" per that method's own
        // comment), so a RoomPath targeting one would just be dead data nothing ever reads.
        List<RoomSpot> entrancePairableSpots = spotsUsedForPaths.Where(s => !enemySpots.Contains(s)).ToList();

        foreach (Transform entrance in entrances)
        {
            if (entrance == null) continue;
            foreach (RoomSpot spot in entrancePairableSpots)
            {
                bool alreadyExists = resultPaths.Any(p => p.entrance == entrance && p.targetSpot == spot);
                if (alreadyExists) continue;

                resultPaths.Add(new RoomPath
                {
                    entrance = entrance,
                    targetSpot = spot,
                    waypoints = new List<Transform>()
                });
                addedCount++;
            }
        }

        // Every occupied spot type (job spots, relax spots, guard posts — anything NOT itself a
        // CombatSpot/EnemySpot) gets a path to every one of this room's CombatSpots, so a Defending
        // reroute (see NPCBunny.BeginDefending / BaseLayoutManager.GetRouteToSpot's same-room branch)
        // from wherever a bunny was already standing doesn't cut a straight line through walls/furniture.
        // E.g. a room with 2 GuardSpots and 2 CombatSpots gets 4 of these. Enemy-only spots are excluded
        // since a bunny never starts a route from one.
        List<RoomSpot> occupancySpots = spotsUsedForPaths.Where(s => !combatSpots.Contains(s) && !enemySpots.Contains(s)).ToList();
        int addedCombatPathCount = 0;

        if (combatSpots.Count > 0)
        {
            foreach (RoomSpot fromSpot in occupancySpots)
            {
                foreach (RoomSpot toSpot in combatSpots)
                {
                    bool alreadyExists = resultPaths.Any(p => p.entrance == fromSpot.transform && p.targetSpot == toSpot);
                    if (alreadyExists) continue;

                    resultPaths.Add(new RoomPath
                    {
                        entrance = fromSpot.transform,
                        targetSpot = toSpot,
                        waypoints = new List<Transform>()
                    });
                    addedCount++;
                    addedCombatPathCount++;
                }
            }
        }

        pathsField.SetValue(room, resultPaths);

        EditorUtility.SetDirty(room);
        PrefabUtility.RecordPrefabInstancePropertyModifications(room);

        string summary = wipePaths
            ? $"{room.name}: populated {spotsUsedForPaths.Count} spot(s) across matched field(s), and regenerated {resultPaths.Count} Spot Path(s) ({addedCombatPathCount} of them spot-to-CombatSpot) — waypoints left empty for hand-routing."
            : $"{room.name}: populated {spotsUsedForPaths.Count} spot(s) across matched field(s). Added {addedCount} new Spot Path(s) ({addedCombatPathCount} of them spot-to-CombatSpot, waypoints left empty for hand-routing); preserved {existingPaths.Count} existing entr{(existingPaths.Count == 1 ? "y" : "ies")} untouched.";
        if (emptyFields.Count > 0)
            summary += $"\n\nNo matching children found for: {string.Join(", ", emptyFields)}.";
        if (missingEntrances.Count > 0)
            summary += $"\n\nSkipped path generation from: {string.Join(", ", missingEntrances)} (not assigned on this room).";

        Debug.Log($"[RoomSpotPathAutoPopulator] {summary}");
        EditorUtility.DisplayDialog("Auto-Populate Spots & Paths", summary, "OK");
    }

    // Re-scans this room's current RoomSpot children and reassigns every [SpotNamePrefix]-tagged
    // List<RoomSpot> field by name-prefix match — shared by both the wipe-and-regenerate and
    // add-missing-only modes, since re-wiring the spot fields themselves is always safe to redo (it just
    // reflects current children, never deletes a GameObject). Returns every matched spot across all
    // fields, in field-declaration order, for path generation.
    private static List<RoomSpot> PopulateSpotFields(RoomBase room, out List<string> emptyFields)
    {
        List<RoomSpot> allChildSpots = room.GetComponentsInChildren<RoomSpot>(true).ToList();
        List<RoomSpot> spotsUsedForPaths = new List<RoomSpot>();
        emptyFields = new List<string>();

        // DeclaredOnly, walked manually up the inheritance chain (same pattern as RoomReferenceFixer) —
        // otherwise fields declared on a subclass (e.g. GardenRoom.farmingSpots) would be invisible when
        // reflecting starting from a type further down the chain, and GetFields without DeclaredOnly can
        // return duplicate/hidden entries across levels.
        System.Type type = room.GetType();
        while (type != null && type != typeof(MonoBehaviour))
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType != typeof(List<RoomSpot>)) continue;

                SpotNamePrefixAttribute prefixAttr = field.GetCustomAttribute<SpotNamePrefixAttribute>();
                if (prefixAttr == null) continue; // no known naming convention for this field — leave it alone

                List<RoomSpot> matched = allChildSpots
                    .Where(s => s != null && s.name.StartsWith(prefixAttr.Prefix))
                    .OrderBy(s => s.name, System.StringComparer.Ordinal)
                    .ToList();

                if (matched.Count == 0)
                    emptyFields.Add($"{field.Name} (expected children named \"{prefixAttr.Prefix}...\")");

                field.SetValue(room, matched);
                spotsUsedForPaths.AddRange(matched);
            }
            type = type.BaseType;
        }

        return spotsUsedForPaths;
    }
}
