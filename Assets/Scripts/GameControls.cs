using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Persistent keyboard bindings plus the shared, frame-based touch input state.</summary>
public static class GameControls
{
    public enum ControlScheme { Keyboard = 0, Touchscreen = 1 }

    public enum Action
    {
        MoveLeft,
        MoveRight,
        Run,
        Jump,
        Sword,
        Bow
    }

    private const string SavePrefix = "Wildrune_Key_";

    // Keep preferences separate per target: trying Touchscreen in the Editor must not make
    // the Windows EXE start in touch mode, while iOS/Android should default to Touchscreen.
    private static string SchemePreferenceKey
    {
        get
        {
#if UNITY_EDITOR
            return "Wildrune_ControlScheme_Editor";
#elif UNITY_IOS
            return "Wildrune_ControlScheme_iOS";
#elif UNITY_ANDROID
            return "Wildrune_ControlScheme_Android";
#elif UNITY_STANDALONE
            return "Wildrune_ControlScheme_Desktop";
#else
            return Application.isMobilePlatform ? "Wildrune_ControlScheme_Mobile" : "Wildrune_ControlScheme_Desktop";
#endif
        }
    }

    private static bool schemeLoaded;
    private static ControlScheme cachedScheme;
    private static float touchHorizontal;
    private static bool touchJumpHeld;
    private static readonly Dictionary<Action, int> touchPressedFrames = new Dictionary<Action, int>();

    public static readonly Action[] AllActions =
    {
        Action.MoveLeft, Action.MoveRight, Action.Run, Action.Jump,
        Action.Sword, Action.Bow
    };

    public static ControlScheme Scheme
    {
        get
        {
            if (!schemeLoaded)
            {
                // A fresh native mobile install defaults to Touchscreen; Editor and desktop builds
                // default to Keyboard. Device Simulator doesn't impersonate an iOS runtime, so
                // choose Touchscreen in Settings when previewing inside the Editor.
                int platformDefault = Application.isMobilePlatform ? (int)ControlScheme.Touchscreen : (int)ControlScheme.Keyboard;
                cachedScheme = (ControlScheme)Mathf.Clamp(PlayerPrefs.GetInt(SchemePreferenceKey, platformDefault), 0, 1);
                schemeLoaded = true;
            }
            return cachedScheme;
        }
    }

    public static bool TouchscreenMode => Scheme == ControlScheme.Touchscreen;

    public static void SetScheme(ControlScheme scheme)
    {
        cachedScheme = scheme;
        schemeLoaded = true;
        PlayerPrefs.SetInt(SchemePreferenceKey, (int)scheme);
        PlayerPrefs.Save();
        ClearTouchInput();
    }

    public static KeyCode DefaultFor(Action action)
    {
        switch (action)
        {
            case Action.MoveLeft: return KeyCode.A;
            case Action.MoveRight: return KeyCode.D;
            case Action.Run: return KeyCode.LeftShift;
            case Action.Jump: return KeyCode.Space;
            case Action.Sword: return KeyCode.J;
            case Action.Bow: return KeyCode.K;
            default: return KeyCode.None;
        }
    }

    public static KeyCode Get(Action action)
    {
        int value = PlayerPrefs.GetInt(SavePrefix + action, (int)DefaultFor(action));
        KeyCode key = (KeyCode)value;
        return Enum.IsDefined(typeof(KeyCode), key) ? key : DefaultFor(action);
    }

    public static bool IsHeld(Action action)
    {
        if (TouchscreenMode)
        {
            switch (action)
            {
                case Action.MoveLeft: return touchHorizontal < -0.05f;
                case Action.MoveRight: return touchHorizontal > 0.05f;
                case Action.Run: return Mathf.Abs(touchHorizontal) > 0.88f;
                case Action.Jump: return touchJumpHeld;
                default: return false;
            }
        }
        return Input.GetKey(Get(action));
    }

    public static bool WasPressed(Action action)
    {
        if (TouchscreenMode)
        {
            int pressedFrame;
            return touchPressedFrames.TryGetValue(action, out pressedFrame) && pressedFrame == Time.frameCount;
        }
        return Input.GetKeyDown(Get(action));
    }

    public static float ReadHorizontal()
    {
        if (TouchscreenMode) return touchHorizontal;
        float value = 0f;
        if (IsHeld(Action.MoveLeft)) value -= 1f;
        if (IsHeld(Action.MoveRight)) value += 1f;
        return Mathf.Clamp(value, -1f, 1f);
    }

    public static void SetTouchHorizontal(float value)
    {
        touchHorizontal = Mathf.Clamp(value, -1f, 1f);
    }

    public static void SetTouchJumpHeld(bool held)
    {
        touchJumpHeld = held;
    }

    public static void PressTouchAction(Action action)
    {
        if (!TouchscreenMode) return;
        touchPressedFrames[action] = Time.frameCount;
    }

    public static void ClearTouchInput()
    {
        touchHorizontal = 0f;
        touchJumpHeld = false;
        touchPressedFrames.Clear();
    }

    public static bool TryRebind(Action action, KeyCode key, out string error)
    {
        if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Return || key == KeyCode.KeypadEnter)
        {
            error = "ESC / ENTER ARE RESERVED FOR MENUS";
            return false;
        }

        string keyName = key.ToString();
        if (keyName.StartsWith("Mouse", StringComparison.Ordinal) ||
            keyName.StartsWith("Joystick", StringComparison.Ordinal))
        {
            error = "CHOOSE A KEYBOARD KEY";
            return false;
        }

        foreach (Action other in AllActions)
        {
            if (other != action && Get(other) == key)
            {
                error = "" + DisplayName(key) + " IS ALREADY USED";
                return false;
            }
        }

        PlayerPrefs.SetInt(SavePrefix + action, (int)key);
        PlayerPrefs.Save();
        error = "BOUND " + ActionName(action) + " TO " + DisplayName(key);
        return true;
    }

    public static void ResetDefaults()
    {
        foreach (Action action in AllActions)
            PlayerPrefs.DeleteKey(SavePrefix + action);
        PlayerPrefs.Save();
    }

    public static string ActionName(Action action)
    {
        switch (action)
        {
            case Action.MoveLeft: return "MOVE LEFT";
            case Action.MoveRight: return "MOVE RIGHT";
            case Action.Run: return "RUN";
            case Action.Jump: return "JUMP / DOUBLE JUMP";
            case Action.Sword: return "SWORD";
            case Action.Bow: return "BOW";
            default: return action.ToString().ToUpperInvariant();
        }
    }

    public static string DisplayName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift: return "LEFT SHIFT";
            case KeyCode.RightShift: return "RIGHT SHIFT";
            case KeyCode.LeftControl: return "LEFT CTRL";
            case KeyCode.RightControl: return "RIGHT CTRL";
            case KeyCode.LeftAlt: return "LEFT ALT";
            case KeyCode.RightAlt: return "RIGHT ALT";
            case KeyCode.Space: return "SPACE";
            case KeyCode.UpArrow: return "UP ARROW";
            case KeyCode.DownArrow: return "DOWN ARROW";
            case KeyCode.LeftArrow: return "LEFT ARROW";
            case KeyCode.RightArrow: return "RIGHT ARROW";
            case KeyCode.BackQuote: return "`";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Slash: return "/";
            case KeyCode.Backslash: return "\\";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            default: return key.ToString().ToUpperInvariant();
        }
    }
}
