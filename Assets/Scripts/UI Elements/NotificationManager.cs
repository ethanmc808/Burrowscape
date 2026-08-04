using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// One authored entry per NotificationType — this is the single source of truth for that notification's
// tier, exact wording, icon, sound, and duration, all editable in the Inspector with no code changes.
// messageTemplate is a string.Format template ("{0} has fainted!") — the code call site only ever
// supplies the dynamic values (bunny name, count, etc.) via Show()'s params, never the wording itself.
[System.Serializable]
public class NotificationDefinition
{
    public NotificationType type;
    public NotificationTier tier;
    [Tooltip("string.Format template. Use {0}, {1}, etc. for dynamic values the code call site supplies (e.g. a bunny's name). Leave with no placeholders for a fixed message.")]
    [TextArea]
    public string messageTemplate;
    public Sprite icon;
    public AudioClip sfxClip;
    [Tooltip("Ignored for Special entries with an animationTrigger set — those wait on the animation instead.")]
    public float displayDuration = 2f;
    [Tooltip("Special tier only — Animator trigger name played on the Special slot's Animator. Leave blank to just hold for displayDuration like any other tier.")]
    public string animationTrigger;
}

// One slot = one tier's on-screen presence: its own root/text/icon/CanvasGroup, its own FIFO queue, its
// own coroutine draining that queue one entry at a time. Three of these (Special/Standard/Alert) run
// fully independently so a burst of Standard toasts never blocks or gets blocked by an Alert.
[System.Serializable]
public class NotificationSlot
{
    public GameObject root;
    public TextMeshProUGUI text;
    public Image icon;
    [Tooltip("Special slot only — receives the per-definition animationTrigger. Leave unassigned for Standard/Alert.")]
    public Animator animator;

    [System.NonSerialized] public Queue<(NotificationDefinition definition, string message, Sprite iconOverride)> queue
        = new Queue<(NotificationDefinition, string, Sprite)>();
    [System.NonSerialized] public bool draining;
}

// Consolidated replacement for the old NotificationToast / RoomDeletionNotification /
// NewBunnyTypeNotification trio (all three deleted — every call site now goes through Show() here
// instead). RoomTransitionNotice is deliberately NOT folded in here — it's an indefinite, ref-counted
// "operation in progress" banner, not a fire-and-forget event, so it keeps its own separate mechanism.
//
// Tier is authored PER NotificationDefinition, not fixed by NotificationType — see NotificationDefinition
// above. All three tiers queue (not cut off) so a burst of same-tier events all get shown in sequence
// instead of the newest one silently replacing whatever was mid-display.
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    [SerializeField] private NotificationSlot specialSlot;
    [SerializeField] private NotificationSlot standardSlot;
    [SerializeField] private NotificationSlot alertSlot;

    [Tooltip("One entry per NotificationType actually used in code. A type with no entry here logs a warning and shows nothing — there's no sensible fallback tier/wording to guess.")]
    [SerializeField] private List<NotificationDefinition> definitions = new List<NotificationDefinition>();

    private readonly Dictionary<NotificationType, NotificationDefinition> definitionsByType = new Dictionary<NotificationType, NotificationDefinition>();

    public static NotificationManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Same scene-aware bootstrap as AudioManager/PowerManager.EnsureInstance — a hand-placed scene
        // instance (with Inspector-authored slots/definitions) must never be silently replaced.
        NotificationManager existing = FindAnyObjectByType<NotificationManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        Debug.LogWarning("NotificationManager: no scene instance found — notifications require Inspector-wired slots/definitions, so auto-creating an empty one will show nothing. Place a NotificationManager in the scene.");
        GameObject go = new GameObject("NotificationManager (Global)");
        return go.AddComponent<NotificationManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        foreach (NotificationDefinition definition in definitions)
        {
            if (definition == null) continue;
            definitionsByType[definition.type] = definition;
        }

        InitSlot(specialSlot);
        InitSlot(standardSlot);
        InitSlot(alertSlot);
    }

    private void InitSlot(NotificationSlot slot)
    {
        if (slot?.root == null) return;
        slot.root.SetActive(false);

        // Purely informational — no button, nothing to dismiss — so it must never intercept a click
        // meant for whatever's underneath (e.g. a build-placement click on a floor a slot happens to be
        // covering). Same reasoning as the old NotificationToast/RoomDeletionNotification/
        // NewBunnyTypeNotification CanvasGroups this replaced.
        CanvasGroup group = slot.root.GetComponent<CanvasGroup>();
        if (group == null) group = slot.root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private NotificationSlot SlotFor(NotificationTier tier)
    {
        switch (tier)
        {
            case NotificationTier.Special: return specialSlot;
            case NotificationTier.Alert: return alertSlot;
            default: return standardSlot;
        }
    }

    // Common case — tier, wording, icon, sound, and duration all come from the Inspector-authored
    // NotificationDefinition for `type`. args fill messageTemplate's {0}/{1}/etc placeholders.
    public void Show(NotificationType type, params object[] args) => ShowInternal(type, args, null);

    // Rare case — NewBunnyType's icon is the specific BunnyTypeDefinition's own sprite, not a fixed
    // per-type one, so it needs to override the definition's icon for this call only.
    public void ShowWithIcon(NotificationType type, Sprite iconOverride, params object[] args) => ShowInternal(type, args, iconOverride);

    private void ShowInternal(NotificationType type, object[] args, Sprite iconOverride)
    {
        if (!definitionsByType.TryGetValue(type, out NotificationDefinition definition))
        {
            Debug.LogWarning($"NotificationManager: no NotificationDefinition authored for {type} — nothing shown.");
            return;
        }

        NotificationSlot slot = SlotFor(definition.tier);
        if (slot?.root == null)
        {
            Debug.LogWarning($"NotificationManager: no slot wired for tier {definition.tier} (type {type}) — notification dropped.");
            return;
        }

        string message = (args != null && args.Length > 0)
            ? string.Format(definition.messageTemplate, args)
            : definition.messageTemplate;

        slot.queue.Enqueue((definition, message, iconOverride));
        if (!slot.draining)
            StartCoroutine(DrainSlot(slot));
    }

    private IEnumerator DrainSlot(NotificationSlot slot)
    {
        slot.draining = true;

        while (slot.queue.Count > 0)
        {
            (NotificationDefinition definition, string message, Sprite iconOverride) = slot.queue.Dequeue();

            if (slot.text != null) slot.text.text = message;

            Sprite iconToShow = iconOverride != null ? iconOverride : definition.icon;
            if (slot.icon != null)
            {
                slot.icon.sprite = iconToShow;
                slot.icon.enabled = iconToShow != null;
            }

            if (definition.sfxClip != null)
                AudioManager.EnsureInstance().PlaySFX2D(definition.sfxClip);

            slot.root.SetActive(true);

            // Duration still governs how long the reveal holds on screen even when an animation trigger
            // also plays — the Animator drives the visual, this coroutine just owns the timing so every
            // tier goes through the same queue-drain shape.
            if (slot.animator != null && !string.IsNullOrEmpty(definition.animationTrigger))
                slot.animator.SetTrigger(definition.animationTrigger);

            yield return new WaitForSeconds(definition.displayDuration);

            slot.root.SetActive(false);
        }

        slot.draining = false;
    }
}
