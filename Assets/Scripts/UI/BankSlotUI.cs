using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System;

public class BankSlotUI : MonoBehaviour, IPointerClickHandler
{
    public int SlotIndex { get; set; } = -1;

    private Image _icon;
    private TMP_Text _label;
    private Image _bg;

    void Awake()
    {
        _bg = GetComponent<Image>();
        _icon = transform.Find("Icon")?.GetComponent<Image>();
        _label = transform.Find("Label")?.GetComponent<TMP_Text>();
    }

    public void SetItem(ItemStack stack)
    {
        if (_icon == null || _label == null) Awake();

        if (stack != null && stack.itemId > 0)
        {
            var item = ItemRegistry.Get(stack.itemId);
            if (_icon != null)
            {
                _icon.color = Color.white;
                // Icons look bad according to user, so we hide them or set them to placeholder
                _icon.enabled = false; 
            }
            if (_label != null)
            {
                _label.text = item.name + (stack.quantity > 1 ? " x" + stack.quantity : "");
            }
            if (_bg != null) _bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        }
        else
        {
            if (_icon != null) _icon.enabled = false;
            if (_label != null) _label.text = "";
            if (_bg != null) _bg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        var player = PlayerEntity.Instance;
        if (player == null || SlotIndex < 0) return;

        var stack = player.Bank.GetSlot(SlotIndex);
        if (stack == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // Withdraw
            if (player.Inventory.Add(stack.Copy(1)))
            {
                player.Bank.WithdrawAt(SlotIndex);
                BankUI.Instance.Refresh();
                HUDController.Instance.AddChatLine($"<color=green>[BANK]:</color> Withdrew {ItemRegistry.Get(stack.itemId).name}.");
            }
            else
            {
                HUDController.Instance.AddChatLine("<color=red>[BANK]:</color> Inventory full.");
            }
        }
    }
}
