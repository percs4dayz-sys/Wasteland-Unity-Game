using UnityEngine;
using TMPro;

/// <summary>Shared helpers for building UI from script at runtime.</summary>
public static class UIUtil
{
    /// <summary>Finds the root Screen-Space-Overlay canvas (the HUD canvas).</summary>
    public static Canvas FindOverlayCanvas()
    {
        Canvas fallback = null;
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (!c.isRootCanvas) continue;
fallback = c;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) return c;
        }
        return fallback;
    }

    /// <summary>Grabs whatever TMP font the game is already using, so new text matches the HUD.</summary>
    public static TMP_FontAsset FindFont()
    {
        var anyText = Object.FindAnyObjectByType<TMP_Text>();
        if (anyText != null && anyText.font != null) return anyText.font;
        return TMP_Settings.defaultFontAsset;
    }
}
