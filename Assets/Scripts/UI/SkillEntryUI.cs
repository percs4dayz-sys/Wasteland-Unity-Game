using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Added to each row in the Skills panel by HUDController.
/// Hover  → tooltip showing current XP and XP remaining to next level.
/// Click  → opens the full OSRS-style skill guide popup.
/// </summary>
public class SkillEntryUI : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private Skill _skill;
    private bool  _configured;

    public void Configure(Skill skill)
    {
        _skill      = skill;
        _configured = true;

        // The whole row must be raycastable so hover/click register anywhere on it.
        var graphic = GetComponent<Graphic>();
        if (graphic == null)
        {
            var img = gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // invisible, still receives pointer events
        }
        else
        {
            graphic.raycastTarget = true;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_configured) TooltipUI.Instance?.Show(BuildTooltip());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance?.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_configured || eventData.button != PointerEventData.InputButton.Left) return;
        UnityEngine.Debug.Log("[SKILL] Clicked " + _skill);
        TooltipUI.Instance?.Hide();
        SkillGuideUI.Instance?.Open(_skill);
    }

    private string BuildTooltip()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return "";
        var sd = player.Stats.GetSkillData(_skill);
        if (sd == null) return "";

        int lvl = sd.Level;
        int xp  = sd.xp;

        string train = SkillGuide.TrainedBy(_skill);
        string trainLine = string.IsNullOrEmpty(train) ? "" : $"\n<color=#9FE0FF>Train:</color> {train}";

        if (lvl >= 99)
            return $"<b>{sd.displayName}</b>  (Lv 99)\nXP: {xp:N0}\n<color=#88FF88>MAX LEVEL</color>{trainLine}";

        int nextLevelXP = XPTable.XPForLevel(lvl + 1);
        int remaining   = sd.XPToNext;

        return $"<b>{sd.displayName}</b>  (Lv {lvl})\n" +
               $"XP: {xp:N0}\n" +
               $"Next level at: {nextLevelXP:N0}\n" +
               $"Remaining: <color=#FFD24A>{remaining:N0}</color>" +
               trainLine;
    }
}
