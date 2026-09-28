using UnityEngine;

/// <summary>
/// The OSRS-mobile control settings (Settings ▸ Controls), saved in PlayerPrefs:
///   • Minimenu long-press time — how long you hold before the menu opens.
///   • Vibrate when the minimenu opens / when hovering its entries / on drag / on interaction.
///   • Show the function button (Tap-to-drop / Single-tap / Keyboard).
/// Haptic() is the short tick those vibrations use — Android's vibrator, a no-op everywhere else.
/// </summary>
public static class TouchSettings
{
    public const int MinLongPressMs = 200, MaxLongPressMs = 1000;

    const string LongPressKey = "touch.longPressMs";
    const string MenuOpenKey = "touch.vibrateMenuOpen", MenuHoverKey = "touch.vibrateMenuHover";
    const string DragKey = "touch.vibrateDrag", InteractKey = "touch.vibrateInteract";
    const string FunctionKey = "touch.showFunctionButton";

    public static int LongPressMs
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(LongPressKey, 500), MinLongPressMs, MaxLongPressMs);
        set => PlayerPrefs.SetInt(LongPressKey, Mathf.Clamp(value, MinLongPressMs, MaxLongPressMs));
    }

    public static bool VibrateOnMenuOpen    { get => Get(MenuOpenKey, true);   set => Set(MenuOpenKey, value); }
    public static bool VibrateOnMenuHover   { get => Get(MenuHoverKey, true);  set => Set(MenuHoverKey, value); }
    public static bool VibrateOnDrag        { get => Get(DragKey, true);       set => Set(DragKey, value); }
    public static bool VibrateOnInteraction { get => Get(InteractKey, false);  set => Set(InteractKey, value); }
    public static bool ShowFunctionButton   { get => Get(FunctionKey, true);   set => Set(FunctionKey, value); }

    static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
    static void Set(string key, bool on) { PlayerPrefs.SetInt(key, on ? 1 : 0); PlayerPrefs.Save(); }

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject _vibrator;
    static bool _neverTrue;   // keeps the Handheld.Vibrate reference below, which is what makes Unity add the VIBRATE permission
#endif

    /// <summary>A short tick (not Handheld.Vibrate's half-second buzz). Silent where there's no vibrator.</summary>
    public static void Haptic(int ms = 12)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_neverTrue) Handheld.Vibrate();
        try
        {
            if (_vibrator == null)
                _vibrator = UnityEngine.Android.AndroidApplication.currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            using var effect = new AndroidJavaClass("android.os.VibrationEffect");
            using var shot = effect.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, -1);   // -1 = DEFAULT_AMPLITUDE
            _vibrator.Call("vibrate", shot);
        }
        catch (System.Exception) { /* no vibrator / no permission: stay silent */ }
#endif
    }
}
