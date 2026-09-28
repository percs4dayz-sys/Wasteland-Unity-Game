using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// OSRS-mobile touch controls. One finger:
///   • Tap            — the default action: walk here / attack / talk / take / chop … (ClickToMove3D), or
///                      the tapped button / inventory item's default (Equip, Eat …).
///   • Press and hold — the minimenu: every action for what's under the finger, Walk here, Examine, Cancel.
///                      On an inventory item / skill / pet button it opens that thing's own menu instead.
///                      It opens with its first entry under the finger: keep holding and slide onto an entry,
///                      lift, and that entry is chosen; lift without sliding and it stays open for a tap.
///                      The hold time is Settings ▸ Controls ▸ Minimenu long-press time.
///   • Drag           — on the world it rotates the camera; on an inventory item it moves the item.
/// Two fingers: pinch to zoom, drag together to orbit (OrbitCamera3D).
/// The function button's Single-tap mode makes a plain tap open the minimenu too (FunctionButton).
///
/// World gestures are raised as events (ClickToMove3D and OrbitCamera3D listen). UI stays with the
/// EventSystem; the long-press there becomes a synthetic right-click, so the desktop right-click menus
/// work unchanged. Auto-bootstrapped; no scene setup. Desktop is untouched: with a mouse, touchCount is 0.
/// </summary>
public class TouchInput : MonoBehaviour
{
    public static float HoldSeconds => TouchSettings.LongPressMs / 1000f;   // press still this long and it's a long-press
    const float MoveToleranceInches = 0.08f;    // ~2 mm of drift before a press counts as a drag

    /// <summary>A quick tap on the world (not on UI): do the default action there.</summary>
    public static event Action<Vector2> WorldTap;
    /// <summary>A long-press on the world: open the Choose Option menu there.</summary>
    public static event Action<Vector2> WorldLongPress;
    /// <summary>How far a one-finger drag across the world moved this frame, in pixels (zero when there's
    /// none). The camera orbits by it.</summary>
    public static Vector2 WorldDragDelta { get; private set; }

    // True briefly after a long-press fires, so left-click handlers can ignore the click the EventSystem
    // still raises when the finger lifts (otherwise a hold would fire BOTH right- and left-click).
    public static bool SuppressClick => Time.unscaledTime < _suppressUntil;
    static float _suppressUntil;

    enum Gesture { None, Press, Drag, Held, Ignore }

    Gesture _gesture;
    Vector2 _startPos;
    float   _startTime;
    bool    _onUI;
    bool    _slid;       // after a long-press: the finger has slid onto the menu

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.Object.FindAnyObjectByType<TouchInput>() != null) return;
        var go = new GameObject("TouchInput (auto)");
        go.AddComponent<TouchInput>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        WorldDragDelta = Vector2.zero;
        if (Input.touchCount == 0) { _gesture = Gesture.None; return; }

        float tolerance = Mathf.Max(24f, (Screen.dpi > 0f ? Screen.dpi : 160f) * MoveToleranceInches);
        // Unity's own drag threshold is 10 px — well under a millimetre on a phone — so a tap on an
        // inventory item wobbled into a "drag" and did nothing. Keep it in step with ours.
        var es = EventSystem.current;
        if (es != null) es.pixelDragThreshold = Mathf.RoundToInt(tolerance);

        // A second finger hands the gesture to the camera (pinch / two-finger orbit): nothing fires
        // until every finger has lifted.
        if (Input.touchCount > 1) { _gesture = Gesture.Ignore; return; }

        var t = Input.GetTouch(0);
        switch (t.phase)
        {
            case TouchPhase.Began:
                _startPos = t.position; _startTime = Time.unscaledTime;
                _onUI = OverUI(t.position);
                // A touch that starts while a menu is up belongs to the menu (pick an option or dismiss it).
                _gesture = !_onUI && ContextMenuUI.Blocking ? Gesture.Ignore : Gesture.Press;
                if (!_onUI) ReleaseChatFocus();
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (_gesture == Gesture.Held)                                   // finger still down after the menu opened
                {
                    KeepSuppressed();
                    if (!_slid && (t.position - _startPos).sqrMagnitude > tolerance * tolerance) _slid = true;
                    if (_slid) ContextMenuUI.Instance?.TouchHover(t.position);
                    break;
                }
                if (_gesture == Gesture.Drag) { if (!_onUI) WorldDragDelta = t.deltaPosition; break; }
                if (_gesture != Gesture.Press) break;
                if ((t.position - _startPos).sqrMagnitude > tolerance * tolerance)
                {
                    _gesture = Gesture.Drag;                                    // UI drags (inventory) are the EventSystem's
                    if (!_onUI) WorldDragDelta = t.position - _startPos;        // include the slack so the view keeps up with the finger
                }
                else if (Time.unscaledTime - _startTime >= HoldSeconds)
                {
                    _gesture = Gesture.Held;
                    _slid = false;
                    if (_onUI) FireRightClick(_startPos);
                    else WorldLongPress?.Invoke(_startPos);
                    KeepSuppressed();
                }
                break;

            default:                                                            // Ended / Canceled
                if (_gesture == Gesture.None && t.phase == TouchPhase.Ended)
                {
                    // Down and up inside one frame (a quick tap on a slow frame): its Began never showed up.
                    _startPos = t.position;
                    _onUI = OverUI(t.position);
                    _gesture = !_onUI && ContextMenuUI.Blocking ? Gesture.Ignore : Gesture.Press;
                    if (!_onUI) ReleaseChatFocus();
                }
                if (_gesture == Gesture.Held)
                {
                    KeepSuppressed();                                           // cover the release-frame tap
                    if (_slid && t.phase == TouchPhase.Ended) ContextMenuUI.Instance?.TouchRelease(t.position);
                }
                else if (_gesture == Gesture.Press && t.phase == TouchPhase.Ended && !_onUI)
                {
                    if (FunctionButton.SingleTap) WorldLongPress?.Invoke(t.position);   // Single-tap mode: a tap opens the minimenu
                    else WorldTap?.Invoke(t.position);
                }
                _gesture = Gesture.None;
                break;
        }
    }

    static readonly List<RaycastResult> _hits = new();

    /// <summary>Is there UI under this point? Asked of the EventSystem directly: on the frame a touch begins,
    /// its pointer data may not exist yet, and IsPointerOverGameObject would wrongly say no.</summary>
    static bool OverUI(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        _hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = screenPos }, _hits);
        return _hits.Count > 0;
    }

    /// <summary>Touching the world takes focus off the chat line, like tapping outside a text box in any
    /// phone app. A chat field left focused after the keyboard closes otherwise counts as "typing", and
    /// the world ignores every tap.</summary>
    static void ReleaseChatFocus()
    {
        if (ChatInput.IsTyping && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void FireRightClick(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return;

        var ped = new PointerEventData(es)
        { position = screenPos, button = PointerEventData.InputButton.Right };

        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        if (hits.Count == 0) return;                                     // nothing tappable under the finger

        var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        if (target == null) return;

        ped.pointerPress = target;
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerClickHandler);
        KeepSuppressed();                                               // start swallowing the trailing left tap
    }

    // Hold the suppression window just ahead of "now" so it stays active for as long as the finger
    // is down and a short beat after it lifts — that's when the EventSystem raises its left click.
    static void KeepSuppressed() => _suppressUntil = Time.unscaledTime + 0.25f;
}
