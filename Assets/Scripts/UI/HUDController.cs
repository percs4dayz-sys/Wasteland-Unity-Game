using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    [Header("Tabs")]
    [SerializeField] private Button combatTabBtn;
    [SerializeField] private Button skillsTabBtn;
    [SerializeField] private Button invTabBtn;
    [SerializeField] private Button equipTabBtn;
    [SerializeField] private Button mapQuestTabBtn;

    [Header("Panels")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject equipPanel;
    [SerializeField] private GameObject skillsPanel;
    [SerializeField] private GameObject combatPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject questPanel;
    [SerializeField] private GameObject panelsContainer;

    private int _currentTabIndex = -1;
    private Transform _autoSkillContainer;

    [Header("Skills Panel")]
    [SerializeField] private Transform skillsList;
    [SerializeField] private GameObject skillEntryPrefab;

    [Header("Chat Terminal")]
    [SerializeField] private TMP_Text chatLogText;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private Button[] channelButtons; // CH, SYS, COMM, LOC, FRG

    [Header("HUD Info")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text locationText;

    [Header("Inventory Panel")]
    [SerializeField] private Transform inventoryGrid;

    private PlayerEntity _player;
    private bool _playerBound;
    private readonly List<string> _chatLines = new();
    private const int MAX_CHAT_LINES = 100;
    private const int MAX_VISIBLE_NO_SCROLL = 14; // when there's no ScrollRect, only show the newest lines
    private ScrollRect _chatScroll;
    private UnityEngine.UI.Image[] _invCells;
    private UnityEngine.UI.Image[] _invIcons;
    private TMP_Text[] _invLabels;
    private InventorySlotUI[] _invSlotScripts;
    private readonly Dictionary<string, EquipmentSlotUI> _equipSlots = new();

    [Header("Item Sprites")]
    [SerializeField] private Sprite[] itemIcons;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Cache inventory cells and icons
        if (inventoryGrid != null)
        {
            _invCells = new UnityEngine.UI.Image[28];
            _invIcons = new UnityEngine.UI.Image[28];
            _invLabels = new TMP_Text[28];
            _invSlotScripts = new InventorySlotUI[28];
            for (int i = 0; i < 28; i++)
            {
                var cell = inventoryGrid.Find("Cell_" + i);
                if (cell != null)
                {
                    _invCells[i] = cell.GetComponent<UnityEngine.UI.Image>();
                    _invSlotScripts[i] = cell.gameObject.GetComponent<InventorySlotUI>();
                    if (_invSlotScripts[i] == null) _invSlotScripts[i] = cell.gameObject.AddComponent<InventorySlotUI>();
                    _invSlotScripts[i].SlotIndex = i;

                    var iconTrans = cell.Find("Icon");
                    if (iconTrans != null) _invIcons[i] = iconTrans.GetComponent<UnityEngine.UI.Image>();
                    
                    var labelTrans = cell.Find("Label");
                    if (labelTrans != null) _invLabels[i] = labelTrans.GetComponent<TMP_Text>();
                }
            }
        }

        // Cache equipment slots
        if (equipPanel != null)
        {
            var slots = equipPanel.GetComponentsInChildren<EquipmentSlotUI>(true);
            foreach (var s in slots)
            {
                if (!string.IsNullOrEmpty(s.slotName))
                    _equipSlots[s.slotName] = s;
            }
        }

        // Tabs
        combatTabBtn?.onClick.AddListener(() => ShowTab(0));
        skillsTabBtn?.onClick.AddListener(() => ShowTab(1));
        invTabBtn?.onClick.AddListener(() => ShowTab(2));
        equipTabBtn?.onClick.AddListener(() => ShowTab(3));
        mapQuestTabBtn?.onClick.AddListener(() => {
            // If map is already open, show quests. Otherwise show map.
            if (_currentTabIndex == 4) ShowTab(5); // Show Quests
            else ShowTab(4); // Show Map
        });

        // Chat Input
        chatInputField?.onEndEdit.AddListener(OnChatSubmit);

        // Locate a ScrollRect around the chat log so new lines auto-scroll into view.
        if (chatLogText != null) _chatScroll = chatLogText.GetComponentInParent<ScrollRect>();

        if (locationText) locationText.text = "SECTOR 7 // RAVENSWOOD PARKING LOT";

        if (SkillingManager.Instance != null)
            SkillingManager.Instance.OnMessage += AddChatLine;

        if (CraftingManager.Instance != null)
            CraftingManager.Instance.OnMessage += AddChatLine;

        LoadIconsIntoRegistry();   // load icons FIRST so the initial inventory refresh isn't iconless (white cells)
        BindPlayer();              // wires player-dependent events if the player already exists
        ShowTab(0);
    }

    private void LoadIconsIntoRegistry()
    {
        int[] ids = { 1, 2, 3, 4, 5, 10, 11, 12, 13, 20, 21, 22, 23, 30, 31, 32, 40 };
        var missing = new List<int>();

        foreach (int id in ids)
        {
            var item = ItemRegistry.Get(id);
            if (item == null) continue;

            // 1) A sprite matched by name from the inspector's itemIcons array, else
            // 2) Assets/Resources/ItemIcons/<id>.png  (drop a PNG named by item ID — easiest way to add icons).
            Sprite icon = MatchFromArray(item, id) ?? Resources.Load<Sprite>($"ItemIcons/{id}");

            item.icon = icon;
            if (icon == null) missing.Add(id);
        }

        // One quiet summary instead of a wall of warnings. Items without an icon show a name label.
        if (missing.Count > 0)
            UnityEngine.Debug.Log($"[HUD] {missing.Count} items still have no icon (showing name labels). " +
                $"To add one, drop a sprite named <id>.png into Assets/Resources/ItemIcons/. Missing IDs: {string.Join(",", missing)}");
    }

    /// <summary>Find an icon for an item from the inspector-assigned itemIcons array by name.</summary>
    private Sprite MatchFromArray(ItemData item, int id)
    {
        if (itemIcons == null || itemIcons.Length == 0) return null;
        string itemName = item.name.ToLower().Replace(" ", "");

        foreach (var sprite in itemIcons)
        {
            if (sprite == null) continue;
            string s = sprite.name.ToLower();
            bool isMatch = id switch
            {
                1  => s.Contains("machete"),
                4  => s.Contains("hatchet") || s.Contains("axe"),
                10 => s.Contains("scrap") || s.Contains("metal"),
                12 => s.Contains("pistol"),
                21 => s.Contains("water"),
                _  => false,
            };
            if (!isMatch) isMatch = s.Contains(itemName);
            if (isMatch) return sprite;
        }
        return null;
    }

    void Update()
    {
        // Player may not exist yet on the first frame — keep trying until it does.
        if (!_playerBound) BindPlayer();

        // Live HP read-out every frame, so it's always accurate no matter what
        // changed it (damage, healing, Endurance level-up, save load, etc.).
        UpdateHPDisplay();

        HandleTabHotkeys();
    }

    /// <summary>
    /// Keyboard fallback for the HUD tabs (F1–F5). This works even if a panel's
    /// background ends up overlapping the on-screen tab buttons and swallowing clicks.
    /// </summary>
    private void HandleTabHotkeys()
    {
        if (IsChatFocused()) return;   // don't hijack keys while typing in chat

        if (Input.GetKeyDown(KeyCode.F1)) ShowTab(0); // Combat
        else if (Input.GetKeyDown(KeyCode.F2)) ShowTab(1); // Skills
        else if (Input.GetKeyDown(KeyCode.F3)) ShowTab(2); // Inventory
        else if (Input.GetKeyDown(KeyCode.F4)) ShowTab(3); // Equipment
        else if (Input.GetKeyDown(KeyCode.F5)) ShowTab(4); // Map
    }

    private void BindPlayer()
{
        if (_playerBound) return;

        _player = PlayerEntity.Instance;
        if (_player == null) return;

        _player.Stats.OnLevelUp += (_, _) => RefreshSkills();
        _player.Inventory.OnChanged += RefreshInventory;
        _player.Equipment.OnChanged += RefreshEquipment;
        _playerBound = true;

        RefreshSkills();
        RefreshInventory();
        RefreshEquipment();
        UpdateHPDisplay();
    }

    private void RefreshInventory()
{
        if (_invCells == null || _player == null)
        {
            UnityEngine.Debug.LogWarning("[HUD] Cannot refresh inventory: _invCells is " + (_invCells == null ? "null" : "ok") + ", _player is " + (_player == null ? "null" : "ok"));
            return;
        }

        UnityEngine.Debug.Log("[HUD] Refreshing Inventory UI... Count: " + _player.Inventory.CountOfAny());

        for (int i = 0; i < 28; i++)
        {
            if (_invCells[i] == null) continue;
            var itemStack = _player.Inventory.GetSlot(i);
            
            if (itemStack != null)
            {
                var item = ItemRegistry.Get(itemStack.itemId);
                bool hasIcon = item != null && item.icon != null;

                // Only tint the cell white when there's an actual icon to show — otherwise an
                // icon-less item rendered a glaring white square (the "white box" on load).
                _invCells[i].color = hasIcon ? Color.white : new Color(0.2f, 0.2f, 0.2f, 1f);

                if (_invIcons != null && _invIcons[i] != null)
                {
                    _invIcons[i].sprite = hasIcon ? item.icon : null;
                    _invIcons[i].color  = hasIcon ? Color.white : new Color(0, 0, 0, 0);
                }

                if (_invLabels != null && _invLabels[i] != null)
                    _invLabels[i].text = item != null ? item.name : "";
            }
            else
            {
                _invCells[i].color = new Color(0.1f, 0.1f, 0.1f, 1f); 
                if (_invIcons != null && _invIcons[i] != null)
                {
                    _invIcons[i].color = new Color(0, 0, 0, 0);
                    _invIcons[i].sprite = null;
                }

                if (_invLabels != null && _invLabels[i] != null)
                {
                    _invLabels[i].text = "";
                }
            }
        }
    }

    private void RefreshEquipment()
    {
        if (_player == null) return;

        foreach (var slotName in Equipment.Slots)
        {
            if (_equipSlots.TryGetValue(slotName, out var slotUI))
            {
                var id = _player.Equipment.GetItemId(slotName);
                if (id.HasValue)
                {
                    var item = ItemRegistry.Get(id.Value);
                    slotUI.Refresh(item, item.icon);
                }
                else
                {
                    slotUI.Refresh(null, null);
                }
            }
        }
    }

    /// <summary>Pushes the player's real current/max HP into the HUD text.</summary>
    private void UpdateHPDisplay()
    {
        if (hpText == null || _player == null) return;

        int cur = _player.Stats.CurrentHP;
        int max = _player.Stats.MaxHP;
        string num = $"{cur}/{max}";

        // Tint red when badly hurt, amber when below half — pure cosmetic feedback.
        if (max > 0 && cur <= max * 0.25f)      hpText.text = $"<color=#FF4040>{num}</color>";
        else if (max > 0 && cur <= max * 0.5f)  hpText.text = $"<color=#FFC040>{num}</color>";
        else                                    hpText.text = num;
    }

    private void ShowTab(int index)
    {
        UnityEngine.Debug.Log("[HUD] ShowTab called with index: " + index);
        
        // If clicking the same tab, toggle the container
        if (index == _currentTabIndex)
        {
            if (panelsContainer != null) 
            {
                panelsContainer.SetActive(!panelsContainer.activeSelf);
                UnityEngine.Debug.Log("[HUD] Toggling PanelsContainer to " + panelsContainer.activeSelf);
            }
            return;
        }

        _currentTabIndex = index;
        if (panelsContainer != null) panelsContainer.SetActive(true);

        if (combatPanel) combatPanel.SetActive(index == 0);
        if (skillsPanel) 
        {
            skillsPanel.SetActive(index == 1);
            if (index == 1) RefreshSkills();
        }
        if (inventoryPanel) inventoryPanel.SetActive(index == 2);
        if (equipPanel) 
        {
            equipPanel.SetActive(index == 3);
            if (index == 3) RefreshEquipment();
        }
        if (mapPanel) mapPanel.SetActive(index == 4);
        if (questPanel) questPanel.SetActive(index == 5);

        UnityEngine.Debug.Log($"[HUD] Tab {index} activated. EquipPanel Active: {(equipPanel != null && equipPanel.activeSelf)}");
    }

    private void RefreshSkills()
    {
        if (_player == null) return;

        // Use the wired list if present; otherwise build our own so the tab is never empty.
        Transform container = skillsList != null ? skillsList : EnsureAutoSkillContainer();
        if (container == null) return;

        foreach (Transform child in container)
            Destroy(child.gameObject);

        foreach (var sd in _player.Stats.AllSkills())
        {
            GameObject entry;
            if (skillEntryPrefab != null)
            {
                entry = Instantiate(skillEntryPrefab, container);
                var texts = entry.GetComponentsInChildren<TMP_Text>();
                if (texts.Length >= 2)
                {
                    texts[0].text = sd.displayName;
                    texts[1].text = $"Lv {sd.Level}";
                }
            }
            else
            {
                entry = BuildSkillRow(container, sd);
            }

            // Hover → XP tooltip, click → OSRS-style skill guide.
            var entryUI = entry.GetComponent<SkillEntryUI>();
            if (entryUI == null) entryUI = entry.AddComponent<SkillEntryUI>();
            entryUI.Configure(sd.skill);
        }
    }

    /// <summary>Builds a vertical list inside the skills panel when none is wired in the inspector.</summary>
    private Transform EnsureAutoSkillContainer()
    {
        if (_autoSkillContainer != null) return _autoSkillContainer;
        if (skillsPanel == null) return null;

        var go = new GameObject("AutoSkillList", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(skillsPanel.transform, false);
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -8);
        rt.sizeDelta = new Vector2(-16, 0);

        var vlg = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vlg.spacing = 3;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var fit = go.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        _autoSkillContainer = rt;
        return rt;
    }

    private GameObject BuildSkillRow(Transform parent, SkillData sd)
    {
        var row = new GameObject("Skill_" + sd.skill, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<UnityEngine.UI.Image>().color = new Color(0.16f, 0.15f, 0.13f, 1f);
        var le = row.AddComponent<UnityEngine.UI.LayoutElement>();
        le.minHeight = 26; le.preferredHeight = 26;

        var nameGo = new GameObject("Name", typeof(RectTransform));
        nameGo.transform.SetParent(row.transform, false);
        var nrt = nameGo.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(0.7f, 1);
        nrt.offsetMin = new Vector2(8, 0); nrt.offsetMax = Vector2.zero;
        var nt = nameGo.AddComponent<TMPro.TextMeshProUGUI>();
        nt.text = sd.displayName; nt.fontSize = 14; nt.color = Color.white;
        nt.alignment = TMPro.TextAlignmentOptions.MidlineLeft; nt.raycastTarget = false;

        var lvlGo = new GameObject("Lvl", typeof(RectTransform));
        lvlGo.transform.SetParent(row.transform, false);
        var lrt = lvlGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0.7f, 0); lrt.anchorMax = new Vector2(1, 1);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(-8, 0);
        var lt = lvlGo.AddComponent<TMPro.TextMeshProUGUI>();
        lt.text = $"Lv {sd.Level}"; lt.fontSize = 14; lt.color = new Color(1f, 0.85f, 0.45f, 1f);
        lt.alignment = TMPro.TextAlignmentOptions.MidlineRight; lt.raycastTarget = false;

        return row;
    }

    /// <summary>Static chat sink: routes a line to the 2D scene HUD (if present) AND fires OnLine
    /// so the self-building 3D chat (Hud3DChat) can show it too. Use this instead of
    /// Instance?.AddChatLine so messages appear in both 2D and 3D.</summary>
    public static event System.Action<string> OnLine;
    public static void Emit(string line)
    {
        Instance?.AddChatLine(line);
        OnLine?.Invoke(line);
    }

    public void AddChatLine(string line)
    {
        _chatLines.Add(line);
        if (_chatLines.Count > MAX_CHAT_LINES) _chatLines.RemoveAt(0);

        if (chatLogText)
        {
            if (_chatScroll != null)
            {
                // Full history is scrollable — show everything and snap to the newest line.
                chatLogText.text = string.Join("\n", _chatLines);
                Canvas.ForceUpdateCanvases();
                _chatScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
            }
            else
            {
                // No ScrollRect — only render the newest lines so text never runs off screen.
                int start = Mathf.Max(0, _chatLines.Count - MAX_VISIBLE_NO_SCROLL);
                chatLogText.text = string.Join("\n", _chatLines.GetRange(start, _chatLines.Count - start));
            }
        }
    }

    private void OnChatSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!ChatCommands.TryHandle(text))            // "/stuck" etc. are handled, not echoed
            AddChatLine($"{PlayerEntity.ChatLabel()}: {text}");
        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }

    public bool IsChatFocused()
    {
        return chatInputField != null && chatInputField.isFocused;
    }
}

