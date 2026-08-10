using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Text;
using System.IO;
using System.Linq;

// Read-only audit tool: dumps the currently-open scene's full GameObject hierarchy (name, active state,
// component list, prefab-instance source) to a plain text file outside Assets/ (so it doesn't get
// imported as a project asset). Written 2026-08-08 to research Home_Base's hierarchy/naming before any
// cleanup — walking ~800KB of scene YAML by hand for parent/child chains across nested prefab instances
// is exactly the kind of thing that's fast and reliable through Unity's actual object model instead.
//
// Makes no changes to the scene. Safe to keep around for future hierarchy audits — not a one-time tool
// like the sprite fixer was.
public static class HierarchyDumper
{
    [MenuItem("Burrowscape/Dump Active Scene Hierarchy")]
    public static void Dump()
    {
        Scene scene = SceneManager.GetActiveScene();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Scene: {scene.name} ({scene.path})");
        sb.AppendLine($"Root object count: {scene.rootCount}");
        sb.AppendLine(new string('=', 60));

        int totalObjects = 0;
        foreach (GameObject root in scene.GetRootGameObjects().OrderBy(g => g.name))
        {
            totalObjects += WriteNode(sb, root.transform, 0);
        }

        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Total GameObjects: {totalObjects}");

        string outPath = Path.Combine(Application.dataPath, "..", $"{scene.name}_Hierarchy_Dump.txt");
        outPath = Path.GetFullPath(outPath);
        File.WriteAllText(outPath, sb.ToString());

        Debug.Log($"HierarchyDumper: wrote {totalObjects} GameObjects to {outPath}");
        EditorUtility.DisplayDialog("Dump Active Scene Hierarchy",
            $"Wrote {totalObjects} GameObjects from scene '{scene.name}' to:\n\n{outPath}", "OK");
    }

    private static int WriteNode(StringBuilder sb, Transform t, int depth)
    {
        GameObject go = t.gameObject;
        string indent = new string(' ', depth * 2);

        string prefabTag = "";
        PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(go);
        if (status == PrefabInstanceStatus.Connected || status == PrefabInstanceStatus.MissingAsset)
        {
            bool isRootOfInstance = PrefabUtility.GetOutermostPrefabInstanceRoot(go) == go;
            if (isRootOfInstance)
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                prefabTag = src != null ? $"  [PREFAB: {src.name}]" : "  [PREFAB: MISSING SOURCE]";
            }
        }

        string activeTag = go.activeSelf ? "" : "  [INACTIVE]";

        string components = string.Join(", ", go.GetComponents<Component>()
            .Where(c => c != null && !(c is Transform))
            .Select(c => c.GetType().Name));
        string compTag = components.Length > 0 ? $"  <{components}>" : "";

        sb.AppendLine($"{indent}{go.name}{prefabTag}{activeTag}{compTag}");

        int count = 1;
        foreach (Transform child in t)
            count += WriteNode(sb, child, depth + 1);
        return count;
    }
}
