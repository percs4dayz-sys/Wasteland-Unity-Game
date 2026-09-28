using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EquipmentSlotUI : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public string slotName;
    public Image iconImage;

    public void OnPointerClick(PointerEventData eventData)
    {
        UnityEngine.Debug.Log("[EQUIP] Clicked slot: " + slotName);
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            var player = PlayerEntity.Instance;
            if (player == null) return;

            string msg = player.Unequip(slotName);
            UnityEngine.Debug.Log("[EQUIP] Unequip msg: " + msg);
            if (!string.IsNullOrEmpty(msg))
                HUDController.Instance.AddChatLine("<color=cyan>[EQUIP]:</color> " + msg);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var player = PlayerEntity.Instance;
        var item = player?.Equipment.GetItem(slotName);
        // Show the equipped item's name, or the empty slot's name.
        HoverInspector.Instance?.SetHoverName(item != null ? item.name : slotName + " (empty)");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HoverInspector.Instance?.ClearHover();
    }

    public void Refresh(ItemData item, Sprite icon)
    {
        if (iconImage == null) return;

        if (item != null)
        {
            iconImage.sprite = icon;
            iconImage.color = Color.white;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.color = new Color(0, 0, 0, 0);
        }
    }
}
