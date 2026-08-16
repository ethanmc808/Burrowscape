using UnityEngine;
using UnityEditor;
using System.IO;

// EDITOR-ONLY. One-time generator for Insect's "Horn Sting" attack VFX (AttackInstance_HornSting), same
// "generator tool" convention as BunnyDataGenerator/EnemyDataGenerator — this hand-authors a Particle
// System + Material + prefab via the scripting API rather than raw YAML, since AttackInstance prefabs in
// this project run 14,000+ serialized lines and are far too risky to hand-edit as text.
//
// Design (per Ethan's spec, 2026-08-15): a single AttackInstance whose ONE child ParticleSystem fires
// exactly 3 particles via 3 staggered Emission bursts (not 3 separate AttackInstance launches — combat
// still resolves exactly one hit per attack, matching every other type). Each particle is a triangle
// "horn" sprite with a per-particle Trails ribbon (light green, shrinking width) riding in Local
// simulation space, i.e. glued to the moving core transform — same reasoning as Sludge Hurl's
// trailFollower system (see project_burrowscape_attackinstance_trail_arc memory) for why Local space is
// safe here: with near-zero particle-own-velocity, Local space just makes every live particle track the
// transform.
//
// Velocity over Lifetime's local +X is deliberately left at 0 (not given its own forward speed) —
// AttackInstance.Resolve() only checks the CORE transform's distance to the target (see
// AttackInstance.Update()'s arrivalThreshold check), so a particle with real velocity of its own visually
// races ahead of that core. Confirmed as a real bug 2026-08-15: with the core traveling at travelSpeed and
// the particle ALSO carrying its own forward velocity on top (Local space adds them), the visible horn
// reached the target and even flew past it while the invisible core was still closing the gap — damage
// landed and impact VFX (ps.Stop() in Resolve()) fired around half a second after the visual hit. Keeping
// the particles at local (0,0,0) relative to the core — no independent velocity — means the visible horn
// position and the logical arrival position are the same thing, exactly like every other AttackInstance's
// sprite/particle. If a "horn overtakes the core" look is wanted again later, it needs its OWN arrival
// signal (not travelSpeed/arrivalThreshold), not just a velocity bump on top of the existing one.
//
// PLACEHOLDER ART: the triangle sprite is procedurally drawn here (flat white fill, hard pixel edges) so
// the whole attack is testable in Play mode immediately. Swap it for real Illustrator art later via the
// same BaseArt pipeline as every other attack (Assets/Art/Attacks/BaseArt) — nothing else needs to change,
// just the Renderer's Material texture.
//
// Safe to re-run: no-ops (logs and returns) if the prefab already exists, so it won't stomp hand-tuning
// done afterward in the Inspector. Delete AttackInstance_HornSting.prefab first to regenerate from scratch.
public static class HornStingVFXGenerator
{
    private const string SpriteFolder = "Assets/Art/Attacks/BaseArt";
    private const string SpritePath = SpriteFolder + "/Sprite_HornSting_Placeholder.png";
    private const string MaterialPath = "Assets/Prefabs/Combat/Materials/M_HornSting.mat";
    private const string PrefabPath = "Assets/Prefabs/Combat/Attacks/AttackInstance_HornSting.prefab";
    private const string InsectTypePath = "Assets/Data/Bunny Types/Insect.asset";

    // Light green — used for both the trail's color and (slightly paler) the horn body itself, so the
    // whole effect reads as one cohesive color instead of two unrelated hues. Easy to retune later: this
    // only sets the STARTING values, not a locked-in constant read anywhere else.
    private static readonly Color HornBodyColor = new Color(0.78f, 1f, 0.72f, 1f);
    private static readonly Color TrailColor = new Color(0.55f, 0.95f, 0.45f, 1f);

    [MenuItem("Burrowscape/Generate Horn Sting VFX")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log($"HornStingVFXGenerator: {PrefabPath} already exists — delete it first to regenerate.");
            return;
        }

        Sprite hornSprite = GetOrCreateTriangleSprite();
        Material material = GetOrCreateMaterial(hornSprite);
        GameObject prefab = CreatePrefab(hornSprite, material);
        WireIntoInsectType(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"HornStingVFXGenerator: created {PrefabPath}, {MaterialPath}, and a placeholder sprite " +
                   $"at {SpritePath}. Insect.asset's attackName/isMelee/attackVFXPrefab were wired " +
                   "automatically. Swap the placeholder sprite for real art whenever it's ready — nothing " +
                   "else needs to change.");
    }

    private static Sprite GetOrCreateTriangleSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(SpriteFolder))
        {
            Debug.LogError($"HornStingVFXGenerator: expected folder {SpriteFolder} to already exist — aborting sprite creation.");
            return null;
        }

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // Isoceles triangle pointing along local +X (right) — matches AttackInstance's
        // rotateToFaceTravelDirection convention (art authored "facing right" at zero rotation, same as
        // this codebase's existing melee-mirroring convention).
        Vector2 tip = new Vector2(size - 4, size / 2f);
        Vector2 baseTop = new Vector2(6, size * 0.22f);
        Vector2 baseBottom = new Vector2(6, size * 0.78f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, tip, baseTop, baseBottom))
                    pixels[y * size + x] = Color.white;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(SpritePath, png);
        AssetDatabase.ImportAsset(SpritePath);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.spritePixelsPerUnit = 100f;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static Material GetOrCreateMaterial(Sprite sprite)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit"); // Built-in RP fallback
        if (shader == null) shader = Shader.Find("Sprites/Default"); // last-resort fallback

        Material mat = new Material(shader) { name = "M_HornSting" };
        if (sprite != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", sprite.texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", sprite.texture);
        }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", HornBodyColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", HornBodyColor);

        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    private static GameObject CreatePrefab(Sprite sprite, Material material)
    {
        GameObject root = new GameObject("AttackInstance_HornSting");
        AttackInstance attackInstance = root.AddComponent<AttackInstance>();

        SerializedObject so = new SerializedObject(attackInstance);
        so.FindProperty("travelSpeed").floatValue = 10f;
        so.FindProperty("arrivalThreshold").floatValue = 0.1f;
        so.FindProperty("rotateToFaceTravelDirection").boolValue = true;
        so.FindProperty("impactLingerSeconds").floatValue = 0.5f; // no impactVFX assigned yet, see class comment
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject travelObj = new GameObject("Horn_Travel");
        travelObj.transform.SetParent(root.transform, false);
        ParticleSystem ps = travelObj.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = true; // travel VFX, not an impact system — see feedback_particle_impact_playonawake
        main.duration = 0.6f;
        main.startLifetime = 0.6f;
        main.startSpeed = 0f; // all motion comes from Velocity over Lifetime below, not Shape/Start Speed
        // Non-uniform on purpose: a Billboard particle quad is square by default regardless of the
        // texture's own shape, so a single Start Size would squish the (long, thin) horn texture to fit
        // it — startSize3D gives the quad itself a long/thin rectangle matching the art instead. X is the
        // long axis (tip-to-base direction the texture was drawn along); Y is the short axis (thickness).
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.6f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.22f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(0.22f);
        // Forced OFF explicitly (not just left at its default) — AttackInstance.UpdateFacingRotation
        // writes the single-axis `startRotation`/`Particle.rotation` every frame; if 3D Start Rotation is
        // ever toggled on (X/Y/Z curves instead), that write becomes a silent no-op with no error.
        main.startRotation3D = false;
        main.startColor = HornBodyColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 8;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        // Exactly 3 horns, one after another (not simultaneous) — 0.15s apart.
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 1),
            new ParticleSystem.Burst(0.15f, 1),
            new ParticleSystem.Burst(0.3f, 1),
        });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false; // exact single spawn point at local origin (AttackOrigin, e.g. the head)

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        // x deliberately 0, not given its own forward speed — see the class-level comment above (2026-08-15
        // fix) for why any nonzero value here desyncs the visible horn from AttackInstance's arrival check.
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 1f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.5f); // fraction of the particle's own lifetime
        trails.minVertexDistance = 0.05f;
        trails.worldSpace = false;
        trails.dieWithParticles = true;
        trails.sizeAffectsWidth = true;
        AnimationCurve widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, widthCurve); // full horn-width at the head, shrinks to 0 at the tail
        Gradient trailGradient = new Gradient();
        trailGradient.SetKeys(
            new[] { new GradientColorKey(TrailColor, 0f), new GradientColorKey(TrailColor, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailGradient);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        // View (camera-facing) is what actually lets Particle.rotation/startRotation drive the in-plane
        // spin the way AttackInstance.UpdateFacingRotation expects — pinned explicitly rather than left
        // at whatever the Inspector happens to show, since Local alignment was tried during tuning and
        // appeared to block rotation from having any visible effect at all.
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.trailMaterial = material;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Object.DestroyImmediate(root);
        return prefabAsset;
    }

    // Mirrors BunnyDataGenerator's own "wire in what we can, leave the rest alone" philosophy — sets only
    // the 3 fields this VFX pass is actually responsible for. Doesn't touch attackIntervalSeconds/
    // attackBasePower (balance, not VFX) or anything already hand-tuned.
    private static void WireIntoInsectType(GameObject prefab)
    {
        BunnyTypeDefinition insect = AssetDatabase.LoadAssetAtPath<BunnyTypeDefinition>(InsectTypePath);
        if (insect == null)
        {
            Debug.LogWarning($"HornStingVFXGenerator: couldn't find {InsectTypePath} — attack fields not wired, assign manually.");
            return;
        }

        SerializedObject so = new SerializedObject(insect);
        so.FindProperty("attackName").stringValue = "Horn Sting";
        so.FindProperty("isMelee").boolValue = false; // Type: Projectile, per spec
        so.FindProperty("attackVFXPrefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(insect);
    }
}
