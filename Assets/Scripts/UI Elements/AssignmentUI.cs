using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class AssignmentUI : MonoBehaviour
{
    public static AssignmentUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;

    [Header("Unassigned Bunnies (click to assign)")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // simple prefab: Button + TextMeshProUGUI child
    [Tooltip("Small type-symbol icon prefab (TypeIconSmall) plugged in at the start of each bunny's name via BunnyTypeIconHelper. Skipped for types with no icon assigned yet.")]
    [SerializeField] private GameObject typeIconPrefab;

    [Header("Currently Assigned Bunnies (click to select, then Unassign)")]
    [SerializeField] private Transform assignedButtonContainer;
    [SerializeField] private Button unassignButton;

    [Header("Selection Highlight")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalColor = Color.white;

    private IJobRoom currentRoom;
    private NPCBunny selectedAssignedBunny;
    private Button selectedAssignedButton;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        // Defensive: if UnassignButton was duplicated from another button (e.g. CloseButton),
        // it may have carried over an Inspector-configured OnClick() entry pointing at the wrong
        // method. Clearing here guarantees only our code-driven handler is wired, regardless of
        // whatever is (or isn't) sitting in the Inspector's OnClick() list.
        unassignButton.onClick.RemoveAllListeners();
        unassignButton.onClick.AddListener(OnUnassignClicked);
        unassignButton.interactable = false;
    }

    public void OpenForRoom(IJobRoom room, string roomDisplayName)
    {
        currentRoom = room;
        roomNameLabel.text = roomDisplayName;
        panelRoot.SetActive(true);
        AudioManager.EnsureInstance().PlayUIOpen();
        ClearSelection();
        PopulateLists();
    }

    public void Close() => Close(true);

    // playSound=false is used by confirm actions (e.g. AssignAndClose) that close this panel as a side
    // effect of succeeding — PlayUIClose is reserved for an actual Close/Back button press, not layered
    // on top of the confirm action's own PlayButtonClick. Propagated through the RoomUpgradeUI/PatientUI
    // cross-closes too, so assigning a bunny never plays any of the three panels' close cue twice.
    public void Close(bool playSound)
    {
        // Room clicks always open AssignmentUI alongside RoomUpgradeUI (see RoomClickHandler/
        // RoomUpgradeClickHandler) and, for a Hospital specifically, PatientUI too — so any one panel's
        // Close button should close all of them rather than making the player dismiss each separately.
        // PatientUI has no close button of its own for exactly this reason (see its own header comment:
        // "opens alongside AssignmentUI"). The activeSelf guard makes this idempotent, which is what
        // stops the Close() calls from recursing into each other forever.
        if (!panelRoot.activeSelf) return;

        panelRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentRoom = null;
        ClearSelection();
        RoomUpgradeUI.Instance?.Close(playSound);
        PatientUI.Instance?.Close(playSound);
    }

    private void PopulateLists()
    {
        PopulateUnassignedList();
        PopulateAssignedList();
    }

    private void PopulateUnassignedList()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        List<NPCBunny> unassigned = DwellerRoster.Instance.GetUnassignedBunnies();

        // Guard Room specifically wants its strongest candidates surfaced first — a simple re-sort of the
        // same list, no bunnies hidden. Every other room type is unaffected (registration order, as before).
        if (currentRoom is GuardRoom)
            unassigned = unassigned.OrderByDescending(b => b.Level).ToList();

        foreach (NPCBunny bunny in unassigned)
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => AssignAndClose(bunny));
        }
    }

    private void PopulateAssignedList()
    {
        foreach (Transform child in assignedButtonContainer)
            Destroy(child.gameObject);

        List<NPCBunny> assigned = DwellerRoster.Instance.GetBunniesAssignedTo(currentRoom);

        foreach (NPCBunny bunny in assigned)
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, assignedButtonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectAssignedBunny(bunny, btn));
        }
    }

    private void AssignAndClose(NPCBunny bunny)
    {
        if (!currentRoom.HasAvailableSpot())
        {
            NotificationToast.Instance.Show("This room is full.");
            return; // don't assign, don't close the panel — let them pick a different bunny or cancel
        }

        AudioManager.EnsureInstance().PlayButtonClick();
        bunny.AssignToJob(currentRoom);
        Close(false); // click sound already fired above — don't also play the close cue
    }

    private void SelectAssignedBunny(NPCBunny bunny, Button btn)
    {
        DebugLog.Log($"AssignmentUI: selected {bunny.name} for possible unassignment.");

        // Clicking the already-selected bunny again deselects it.
        if (selectedAssignedBunny == bunny)
        {
            ClearSelection();
            return;
        }

        ResetButtonColor(selectedAssignedButton);

        selectedAssignedBunny = bunny;
        selectedAssignedButton = btn;
        unassignButton.interactable = true;

        SetButtonColor(btn, selectedColor);
    }

    private void OnUnassignClicked()
    {
        DebugLog.Log($"AssignmentUI: Unassign button clicked. selectedAssignedBunny={(selectedAssignedBunny != null ? selectedAssignedBunny.name : "NULL")}");

        if (selectedAssignedBunny == null) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        selectedAssignedBunny.UnassignFromJob();
        DebugLog.Log($"AssignmentUI: UnassignFromJob() called on {selectedAssignedBunny.name}.");

        ClearSelection();
        PopulateLists(); // bunny moves from the assigned list back into the unassigned list
    }

    private void ClearSelection()
    {
        ResetButtonColor(selectedAssignedButton);
        selectedAssignedBunny = null;
        selectedAssignedButton = null;
        unassignButton.interactable = false;
    }

    private void SetButtonColor(Button btn, Color color)
    {
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private void ResetButtonColor(Button btn)
    {
        if (btn == null) return;
        SetButtonColor(btn, normalColor);
    }
}