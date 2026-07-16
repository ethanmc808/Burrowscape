using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class BuildMenuUI : MonoBehaviour
{
    public static BuildMenuUI Instance { get; private set; }

    [Header("Catalog")]
    [Tooltip("Only 4x2x6 ('1 Room Wide') RoomDefinitions belong here, including LiftRoom. The 8x2x6/12x2x6 merged-size prefabs are reserved for a future merge system.")]
    [SerializeField] private List<RoomDefinition> catalog = new List<RoomDefinition>();

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform listContainer;
    [SerializeField] private GameObject roomListItemPrefab; // prefab with a RoomListItemUI component

    [Header("Buttons")]
    [SerializeField] private Button buildToggleButton;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        if (buildToggleButton != null)
        {
            buildToggleButton.onClick.RemoveAllListeners();
            buildToggleButton.onClick.AddListener(Open);
        }
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Close);
        }
    }

    private void Start()
    {
        // Start(), not OnEnable() — see BuildModeController for why (singleton Awake ordering).
        if (PlacementModeManager.Instance != null)
            PlacementModeManager.Instance.OnModeChanged += HandleModeChanged;
    }

    private void HandleModeChanged(PlacementMode mode)
    {
        if (mode != PlacementMode.Build)
            panelRoot.SetActive(false);
    }

    public void Open()
    {
        PlacementModeManager.Instance?.RequestMode(PlacementMode.Build);
        panelRoot.SetActive(true);
        RefreshList();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
    }

    // Re-evaluated fresh every open — live-checked unlock conditions (e.g. population) need to reflect
    // current state, not whatever was true the last time the menu opened.
    private void RefreshList()
    {
        foreach (Transform child in listContainer)
            Destroy(child.gameObject);

        foreach (RoomDefinition definition in catalog.Where(d => d != null && d.IsUnlocked()))
        {
            GameObject itemObj = Instantiate(roomListItemPrefab, listContainer);
            RoomListItemUI item = itemObj.GetComponent<RoomListItemUI>();
            item.Setup(definition, () => OnDefinitionChosen(definition));
        }
    }

    private void OnDefinitionChosen(RoomDefinition definition)
    {
        BuildModeController.Instance?.EnterBuildMode(definition);
        Close();
    }
}
