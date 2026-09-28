using UnityEngine;

/// <summary>
/// Gamepad input that the old Input Manager handles poorly:
///   D-pad, left trigger, right trigger.
///
/// Tries the New Input System first (already installed, v1.19.0) and
/// falls back gracefully if it isn't the active backend.
/// </summary>
public static class SandboxInputHelper
{
    static bool _newSystemChecked;
    static bool _newSystemAvailable;
    static object _gamepad;   // UnityEngine.InputSystem.Gamepad, held as object to avoid hard ref

    /// <summary>D-pad: (-1..1, -1..1). Up=positive Y, Right=positive X.</summary>
    public static Vector2 DPad
    {
        get
        {
            EnsureInit();

            if (_newSystemAvailable && _gamepad != null)
            {
                try
                {
                    // Call gamepad.dpad.ReadValue() via reflection so we don't hard-depend
                    // on the InputSystem assembly at compile time.
                    var dpadProp = _gamepad.GetType().GetProperty("dpad");
                    if (dpadProp != null)
                    {
                        var dpad = dpadProp.GetValue(_gamepad);
                        var readMethod = dpad.GetType().GetMethod("ReadValue", System.Type.EmptyTypes);
                        if (readMethod != null)
                        {
                            var val = readMethod.Invoke(dpad, null);
                            return (Vector2)val;
                        }
                    }
                }
                catch { /* fall through */ }
            }

            // Fallback: try custom DPadX/DPadY axes in old Input Manager.
            float x = 0f, y = 0f;
            try { x = Input.GetAxisRaw("DPadX"); } catch { }
            try { y = Input.GetAxisRaw("DPadY"); } catch { }
            return new Vector2(x, y);
        }
    }

    /// <summary>Left trigger: 0.0 (rest) to 1.0 (fully pressed).</summary>
    public static float LeftTrigger
    {
        get
        {
            EnsureInit();

            if (_newSystemAvailable && _gamepad != null)
            {
                try
                {
                    var ltProp = _gamepad.GetType().GetProperty("leftTrigger");
                    if (ltProp != null)
                    {
                        var trigger = ltProp.GetValue(_gamepad);
                        var readMethod = trigger.GetType().GetMethod("ReadValue", System.Type.EmptyTypes);
                        if (readMethod != null)
                            return (float)readMethod.Invoke(trigger, null);
                    }
                }
                catch { }
            }

            try { return Mathf.Max(0f, Input.GetAxisRaw("LeftTrigger")); } catch { }
            return 0f;
        }
    }

    /// <summary>Right trigger: 0.0 (rest) to 1.0 (fully pressed).</summary>
    public static float RightTrigger
    {
        get
        {
            EnsureInit();

            if (_newSystemAvailable && _gamepad != null)
            {
                try
                {
                    var rtProp = _gamepad.GetType().GetProperty("rightTrigger");
                    if (rtProp != null)
                    {
                        var trigger = rtProp.GetValue(_gamepad);
                        var readMethod = trigger.GetType().GetMethod("ReadValue", System.Type.EmptyTypes);
                        if (readMethod != null)
                            return (float)readMethod.Invoke(trigger, null);
                    }
                }
                catch { }
            }

            try { return Mathf.Max(0f, Input.GetAxisRaw("RightTrigger")); } catch { }
            return 0f;
        }
    }

    /// <summary>True if any gamepad input was detected this frame.</summary>
    public static bool AnyGamepadActivity =>
        DPad.magnitude > 0.1f || LeftTrigger > 0.05f || RightTrigger > 0.05f;

    static void EnsureInit()
    {
        if (_newSystemChecked) return;
        _newSystemChecked = true;

        try
        {
            // Try to find UnityEngine.InputSystem.Gamepad via reflection.
            var asm = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in asm)
            {
                if (a.GetName().Name == "Unity.InputSystem")
                {
                    var gpType = a.GetType("UnityEngine.InputSystem.Gamepad");
                    if (gpType != null)
                    {
                        var currentProp = gpType.GetProperty("current",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (currentProp != null)
                        {
                            _gamepad = currentProp.GetValue(null);
                            _newSystemAvailable = _gamepad != null;
                        }
                    }
                    break;
                }
            }
        }
        catch { _newSystemAvailable = false; }
    }
}
