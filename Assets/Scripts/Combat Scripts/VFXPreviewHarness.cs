using UnityEngine;

// Disposable test harness for previewing AttackInstance VFX prefabs in Play mode, without needing the
// real combat AI/trigger layer (which doesn't exist yet — nothing else in the codebase calls Launch()).
// Drop this on any empty GameObject in a test scene, assign Attacker Point / Target Point (two placeholder
// Transforms — e.g. simple cubes/spheres positioned apart) and an Attack Prefab, then press Play and hit
// the Fire Key (or enable Auto Repeat) to watch the VFX run start-to-finish repeatedly.
//
// NOT part of the shipped combat system. Delete this once all attack VFX prefabs are built and wired the
// real way via BunnyTypeDefinition/EnemyDefinition.attackSource + whatever the eventual combat AI is.
public class VFXPreviewHarness : MonoBehaviour
{
    [SerializeField] private Transform attackerPoint;
    [SerializeField] private Transform targetPoint;
    [SerializeField] private GameObject attackPrefab;
    [SerializeField] private bool isStationary;

    [SerializeField] private KeyCode fireKey = KeyCode.Space;
    [SerializeField] private bool autoRepeat;
    [SerializeField] private float autoRepeatInterval = 1.5f;

    private DummyCombatant attackerCombatant;
    private DummyCombatant targetCombatant;
    private float autoRepeatTimer;

    private void Awake()
    {
        attackerCombatant = new DummyCombatant(attackerPoint);
        targetCombatant = new DummyCombatant(targetPoint);
    }

    private void Update()
    {
        if (Input.GetKeyDown(fireKey))
            Fire();

        if (autoRepeat)
        {
            autoRepeatTimer += Time.deltaTime;
            if (autoRepeatTimer >= autoRepeatInterval)
            {
                autoRepeatTimer = 0f;
                Fire();
            }
        }
    }

    private void Fire()
    {
        if (attackPrefab == null || attackerPoint == null || targetPoint == null)
        {
            Debug.LogWarning("VFXPreviewHarness: assign Attacker Point, Target Point, and Attack Prefab before firing.");
            return;
        }

        GameObject instance = Instantiate(attackPrefab, attackerPoint.position, Quaternion.identity);
        AttackInstance attack = instance.GetComponent<AttackInstance>();
        if (attack == null)
        {
            Debug.LogWarning($"VFXPreviewHarness: '{attackPrefab.name}' has no AttackInstance component on its root.");
            return;
        }

        attack.Launch(attackerCombatant, targetCombatant, isStationary);
    }

    // Minimal stand-in ICombatant — just enough surface for AttackInstance/CombatResolver to run against
    // with fixed placeholder stats. Not meant to model real damage/defeat, only to exercise VFX timing.
    private class DummyCombatant : ICombatant
    {
        private readonly Transform pointTransform;

        public DummyCombatant(Transform pointTransform) { this.pointTransform = pointTransform; }

        public BunnyType Type => BunnyType.Neutral;
        public int Level => 5;
        public BunnyStats Stats => new BunnyStats { HP = 20, Attack = 10, Defense = 10, Speed = 10, Luck = 10 };
        public int CurrentHP => 20;
        public bool IsAlive => true;
        public BunnyTypeDefinition AttackSource => null;
        public Transform CombatTransform => pointTransform;
        public GameObject CombatGameObject => pointTransform != null ? pointTransform.gameObject : null;
        public void TakeCombatDamage(int amount) { }
        public event System.Action OnDefeated { add { } remove { } }
    }
}
