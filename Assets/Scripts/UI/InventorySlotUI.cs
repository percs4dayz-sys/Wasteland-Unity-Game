using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class InventorySlotUI : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public int SlotIndex { get; set; } = -1;

    private static InventorySlotUI _draggedSlot;
    private static GameObject        _dragIcon;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging) return; // Ignore clicks if we just finished a drag
        var (player, item) = Resolve();
        if (item == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (TouchInput.SuppressClick) return;   // tail of a touch long-press (already handled as right-click)
            // The phone's function button (OSRS mobile): Tap-to-drop drops on a tap, Single-tap opens the menu.
            if (FunctionButton.TapToDrop) { Log(player.DropAt(SlotIndex)); return; }
            if (FunctionButton.SingleTap) { OpenMenu(eventData.position, player, item); return; }
            DoDefault(player, item);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
            OpenMenu(eventData.position, player, item);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        var (player, item) = Resolve();
        if (item == null) return;

        _draggedSlot = this;
        if (Application.isMobilePlatform && TouchSettings.VibrateOnDrag) TouchSettings.Haptic();

        // Create a visual copy of the icon to follow the mouse.
        var iconTrans = transform.Find("Icon");
        if (iconTrans == null) return;

        var originalIcon = iconTrans.GetComponent<UnityEngine.UI.Image>();
        if (originalIcon == null || originalIcon.sprite == null) return;

        var canvas = UIUtil.FindOverlayCanvas();
        _dragIcon = new GameObject("DragIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        _dragIcon.transform.SetParent(canvas != null ? canvas.transform : HUDController.Instance.transform, false);
        _dragIcon.transform.SetAsLastSibling();
        
        var dragImg = _dragIcon.GetComponent<UnityEngine.UI.Image>();
        dragImg.sprite = originalIcon.sprite;
        dragImg.raycastTarget = false; // So it doesn't block the drop target
        
        var rt = _dragIcon.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(50, 50);

        // Dim original icon
        originalIcon.color = new Color(1, 1, 1, 0.5f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragIcon != null)
        {
            _dragIcon.transform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_dragIcon != null)
        {
            Destroy(_dragIcon);
            _dragIcon = null;
        }

        if (_draggedSlot != null)
        {
            var iconTrans = _draggedSlot.transform.Find("Icon");
            if (iconTrans != null)
            {
                var img = iconTrans.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = Color.white;
            }
        }

        _draggedSlot = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (_draggedSlot == null || _draggedSlot == this) return;

        var player = PlayerEntity.Instance;
        if (player == null) return;

        player.Inventory.Swap(_draggedSlot.SlotIndex, this.SlotIndex);
        HUDController.Instance.AddChatLine("<color=grey>[INV]: Items swapped.</color>");
    }

    // ── Default left-click action (quick equip / use / deposit) ──────────
    void DoDefault(PlayerEntity player, ItemData item)
    {
        if (BankUI.Instance != null && BankUI.Instance.IsOpen)
        {
            if (player.Bank.Deposit(player.Inventory.GetSlot(SlotIndex)?.Copy(1)))
            {
                player.Inventory.RemoveAt(SlotIndex);
                BankUI.Instance.Refresh();
                Log($"Deposited {item.name}.");
            }
            else
            {
                Log("Bank is full.");
            }
            return;
        }

        string msg;
        if (item.IsEquippable)      msg = player.EquipAt(SlotIndex);
        else if (item.IsConsumable) msg = player.Consume(item.id);
        else if (item.IsBuryable)   msg = player.Bury(item.id);
        else if (item.IsReadable)   { JournalUI.Instance?.Open(item.id); msg = null; }
        else if (ModuleCatalog.IsModule(item.id)) { ModulesPanelUI.Instance?.Open(); msg = null; }
        else                        msg = "You can't use this item.";
        Log(msg);
    }

    // ── Right-click context menu: Examine / Equip(or Use) / Drop ──────────
    void OpenMenu(Vector2 screenPos, PlayerEntity player, ItemData item)
    {
        if (ContextMenuUI.Instance == null) return;
        int id = item.id;

        var options = new List<(string, Action)>
        {
            ("Examine", () =>
                HUDController.Instance?.AddChatLine(
                    $"<color=#88DDFF>[EXAMINE]:</color> {item.name} — {item.description}"))
        };

        if (BankUI.Instance != null && BankUI.Instance.IsOpen)
        {
            options.Add(("Deposit", () => {
                if (player.Bank.Deposit(player.Inventory.GetSlot(SlotIndex)?.Copy(1)))
                {
                    player.Inventory.RemoveAt(SlotIndex);
                    BankUI.Instance.Refresh();
                    Log($"Deposited {item.name}.");
                }
                else Log("Bank is full.");
            }));
        }

        if (item.IsEquippable)
            options.Add(("Equip", () => Log(player.EquipAt(SlotIndex))));
        else if (item.IsConsumable)
            options.Add(("Use", () => Log(player.Consume(id))));
        else if (item.IsBuryable)
            options.Add(("Bury", () => Log(player.Bury(id))));
        else if (item.IsReadable)
            options.Add(("Read", () => JournalUI.Instance?.Open(id)));

        options.Add(("Drop", () => Log(player.DropAt(SlotIndex))));

        ContextMenuUI.Instance.Open(screenPos, item.name, options);
    }

    // ── Hover name → top-left ────────────────────────────────────────────
    public void OnPointerEnter(PointerEventData eventData)
    {
        var (_, item) = Resolve();
        if (item != null) HoverInspector.Instance?.SetHoverName(item.name);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HoverInspector.Instance?.ClearHover();
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    (PlayerEntity player, ItemData item) Resolve()
    {
        var player = PlayerEntity.Instance;
        if (player == null || SlotIndex < 0) return (player, null);
        var stack = player.Inventory.GetSlot(SlotIndex);
        if (stack == null) return (player, null);
        return (player, ItemRegistry.Get(stack.itemId));
    }

    static void Log(string msg)
    {
        if (!string.IsNullOrEmpty(msg))
            HUDController.Instance?.AddChatLine("<color=yellow>[INV]:</color> " + msg);
    }
}
