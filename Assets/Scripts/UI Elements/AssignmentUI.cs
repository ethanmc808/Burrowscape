using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class AssignmentUI : MonoBehaviour
{
    public static AssignmentUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;

    [Header("Unassigned Bunnies (click to assign)")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // simple prefab: Button + TextMeshProUGUI child

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
        ClearSelection();
        PopulateLists();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentRoom = null;
        ClearSelection();
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

        foreach (NPCBunny bunny in unassigned)
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = bunny.name;

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
            buttonObj.GetComponentInChildren<TextMeshProUGUI>().text = bunny.name;

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

        bunny.AssignToJob(currentRoom);
        Close();
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