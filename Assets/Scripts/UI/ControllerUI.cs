using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Makes menus usable with a gamepad. Unity's StandaloneInputModule already reads the stick
/// (Horizontal/Vertical) for navigation, A = Submit and B = Cancel — but two things were missing:
///   1. Nothing got SELECTED when a panel opened, so the controller had no cursor to move. This
///      auto-selects the first button of the top-most open menu.
///   2. The left stick moved the PLAYER while you tried to navigate. While a menu is focused with a
///      controller, <see cref="MenuFocused"/> is true and Player3DController suspends movement so the
///      stick drives the UI instead. (Mouse/keyboard players are unaffected — the gate only engages
///      when a controller is actually in use.)
///   3. B / Cancel closes the focused menu by clicking its Close/Back button.
///
/// Self-bootstrapping; no scene setup. Menus are detected as any active Canvas (that isn't a HUD
/// overlay) containing an interactable button.
/// </summary>
public class ControllerUI : MonoBehaviour
{
    public static ControllerUI Instance { get; private set; }

    /// <summary>A menu is open AND a controller is in use → gameplay movement should stand down so
    /// the stick navigates the UI. Read by Player3DController / ActionCombat3D.</summary>
    public static bool MenuFocused { get; private set; }

    // Canvas name fragments that are HUD/overlays, never "menus" to navigate.
    static readonly string[] Overlay =
        { "hud", "minimap", "chat", "ammo", "companion", "tooltip", "hover", "boundary", "splash", "world3dchat" };

    float _lastControllerUse = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("ControllerUI (auto)");
        go.AddComponent<ControllerUI>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureEventSystem();
    }

    void Update()
    {
        TrackControllerUse();

        var menu = TopMenuCanvas(out var firstSelectable);
        bool controllerRecent = Time.unscaledTime - _lastControllerUse < 4f;
        var es = EventSystem.current;

        if (menu != null && es != null)
        {
            // Keep something selected so stick/d-pad navigation has a cursor — but ONLY while a pad is in
            // use. UI "Submit" is Enter AND Space, so for mouse/keyboard players a standing selection made
            // Enter-to-chat or Space-to-dodge click it (in the editor it toggled the DEV menu; with a panel
            // open it could click that panel's click-off-to-close backdrop). Never on the frame the chat
            // opens, either: that Enter is for the chat.
            var cur = es.currentSelectedGameObject;
            if (controllerRecent && !ChatInput.IsTyping && !Hud3DChat.OpeningThisFrame &&
                (cur == null || !cur.activeInHierarchy) && firstSelectable != null)
                es.SetSelectedGameObject(firstSelectable.gameObject);

            // B / Cancel closes the menu (clicks its Close/Back/X button). Escape while typing just
            // cancels the chat line.
            if (!ChatInput.IsTyping && (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetButtonDown("Cancel")))
                ClickCancelButton(menu);
        }

        // Mouse & keyboard: a button you clicked stays "selected" in Unity, so the next Enter/Space would
        // click it again (e.g. dodging re-toggled the last tab). Let go of it; text boxes keep their focus.
        if (!controllerRecent && es != null) ReleaseButtonSelection(es);

        MenuFocused = menu != null && controllerRecent;
    }

    static void ReleaseButtonSelection(EventSystem es)
    {
        var cur = es.currentSelectedGameObject;
        if (cur == null || Input.GetMouseButton(0)) return;          // don't disturb a click in progress
        if (cur.GetComponent<TMPro.TMP_InputField>() != null || cur.GetComponent<InputField>() != null) return;
        es.SetSelectedGameObject(null);
    }

    void TrackControllerUse()
    {
        // Right stick and face/shoulder buttons are controller-exclusive — enough to know a pad is live.
        for (int b = 0; b <= 7; b++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.JoystickButton0 + b))) { _lastControllerUse = Time.unscaledTime; return; }
        if (Mathf.Abs(SafeAxis("RightStickX")) > 0.3f || Mathf.Abs(SafeAxis("RightStickY")) > 0.3f)
            _lastControllerUse = Time.unscaledTime;
    }

    static float SafeAxis(string a)
    {
        try { return Input.GetAxisRaw(a); } catch (System.ArgumentException) { return 0f; }
    }

    /// <summary>The active, top-most (highest sorting order) menu canvas and its first navigable
    /// button. Skips HUD/overlay canvases and anything with no interactable button.</summary>
    static Canvas TopMenuCanvas(out Selectable first)
    {
        first = null;
        Canvas best = null; int bestOrder = int.MinValue; Selectable bestFirst = null;

        foreach (var c in Object.FindObjectsByType<Canvas>())
        {
            if (!c.isActiveAndEnabled) continue;
            string n = c.name.ToLowerInvariant();
            bool overlay = false;
            foreach (var o in Overlay) if (n.Contains(o)) { overlay = true; break; }
            if (overlay) continue;

            Selectable f = null;
            foreach (var s in c.GetComponentsInChildren<Selectable>(false))
                if (s.interactable && s.gameObject.activeInHierarchy) { f = s; break; }
            if (f == null) continue;                       // not a navigable menu right now

            if (c.sortingOrder >= bestOrder) { bestOrder = c.sortingOrder; best = c; bestFirst = f; }
        }
        first = bestFirst;
        return best;
    }

    /// <summary>Find and click a Close/Back/Cancel/X button in the menu (how B closes a panel).</summary>
    static void ClickCancelButton(Canvas menu)
    {
        foreach (var b in menu.GetComponentsInChildren<Button>(false))
        {
            if (!b.interactable || !b.gameObject.activeInHierarchy) continue;
            string n = b.name.ToLowerInvariant();
            var txt = b.GetComponentInChildren<TMPro.TMP_Text>();
            string label = txt != null ? txt.text.ToLowerInvariant() : "";
            if (n.Contains("close") || n.Contains("back") || n.Contains("cancel") || n == "x"
                || label == "x" || label.Contains("close") || label.Contains("back"))
            { b.onClick.Invoke(); return; }
        }
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}
