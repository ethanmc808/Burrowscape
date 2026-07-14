using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class AssignmentUI : MonoBehaviour
{
    public static AssignmentUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // simple prefab: Button + TextMeshProUGUI child
    [SerializeField] private TextMeshProUGUI roomNameLabel;

    private IJobRoom currentRoom;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);
    }

    public void OpenForRoom(IJobRoom room, string roomDisplayName)
    {
        currentRoom = room;
        roomNameLabel.text = roomDisplayName;
        panelRoot.SetActive(true);
        PopulateList();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentRoom = null;
    }

    private void PopulateList()
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
}