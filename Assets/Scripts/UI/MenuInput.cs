using System.Collections.Generic;
using UnityEngine;

/// The menu actions every screen understands.  Prompt glyphs are drawn per
/// action and swap between gamepad and keyboard art automatically.
public enum MenuAction { None, Confirm, Back, Alt1, Alt2, TabLeft, TabRight, Start, Adjust, Navigate }

/// One input layer for every menu: keyboard, mouse and gamepad fold into the
/// same actions, with console-style hold-to-repeat on directions.  Queried by
/// MenuScreen (and a few custom screens) — values are computed once per frame
/// on first access, so any number of readers agree.
///
///   Navigate : arrows / WASD, d-pad, left stick      (repeat when held)
///   Confirm  : Enter / Space, A          Back : Esc, B
///   Alt1     : Delete / Backspace, X     Alt2 : Tab, Y
///   Tabs     : Q / E, LB / RB            Start: gamepad Menu
///
/// Tracks the last-used device (UsingPad) so prompts show the right glyphs and
/// the mouse cursor hides while a controller is driving.
public static class MenuInput
{
    public static bool UsingPad { get; private set; }
    public static event System.Action DeviceChanged;

    /// Set by text fields while they're capturing typed characters — keyboard
    /// navigation / confirm / back are suppressed so typing never moves focus.
    public static bool TextCapture;

    const float RepeatDelay = 0.34f, RepeatRate = 0.072f;

    static int   _frame = -1;
    static int   _navX, _navY;
    static bool  _confirm, _back, _alt1, _alt2, _tabL, _tabR, _start;
    static int   _heldX, _heldY;
    static float _nextX, _nextY;
    static bool  _stickX, _stickY;          // analog hysteresis latches
    static MenuAction _inject;
    static int   _injectFrame = -1;
    static Vector3 _lastMouse;

    public static int  NavX     { get { Refresh(); return _navX; } }     // -1 left / +1 right
    public static int  NavY     { get { Refresh(); return _navY; } }     // -1 up   / +1 down
    public static bool Confirm  { get { Refresh(); return _confirm; } }
    public static bool Back     { get { Refresh(); return _back; } }
    public static bool Alt1     { get { Refresh(); return _alt1; } }
    public static bool Alt2     { get { Refresh(); return _alt2; } }
    public static bool TabLeft  { get { Refresh(); return _tabL; } }
    public static bool TabRight { get { Refresh(); return _tabR; } }
    public static bool Start    { get { Refresh(); return _start; } }

    /// Fire an action next frame as if pressed (clickable prompt glyphs).
    public static void Inject(MenuAction a)
    {
        _inject      = a;
        _injectFrame = Time.frameCount + 1;
    }

    /// Swallow this frame's button presses once something has acted on them, so
    /// a press that closes a dialog can't also fire the screen underneath it
    /// (which used to re-open the same dialog forever).  Directions are kept.
    public static void Consume()
    {
        Refresh();
        _confirm = _back = _alt1 = _alt2 = _tabL = _tabR = _start = false;
    }

    /// Force a refresh this frame (e.g. to keep device detection live on a
    /// screen that doesn't otherwise read menu input).
    public static void Poll() => Refresh();

    static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        DetectDevice();

        bool kb = !TextCapture;

        // ── Directions (digital sources + analog with hysteresis) ─────────────
        int rawY = 0, rawX = 0;
        if (kb)
        {
            if (Input.GetKey(KeyCode.UpArrow)   || Input.GetKey(KeyCode.W)) rawY = -1;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) rawY = +1;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) rawX = -1;
            if (Input.GetKey(KeyCode.RightArrow)|| Input.GetKey(KeyCode.D)) rawX = +1;
        }
        float dpx = Axis("PadDpadX"), dpy = Axis("PadDpadY");
        if (rawY == 0 && Mathf.Abs(dpy) > 0.5f) rawY = dpy > 0f ? -1 : 1;
        if (rawX == 0 && Mathf.Abs(dpx) > 0.5f) rawX = dpx > 0f ? 1 : -1;

        float sy = Axis("PadMoveY"), sx = Axis("PadSteerX");
        _stickY = _stickY ? Mathf.Abs(sy) > 0.32f : Mathf.Abs(sy) > 0.55f;
        _stickX = _stickX ? Mathf.Abs(sx) > 0.32f : Mathf.Abs(sx) > 0.55f;
        if (rawY == 0 && _stickY) rawY = sy > 0f ? -1 : 1;     // stick up = +1
        if (rawX == 0 && _stickX) rawX = sx > 0f ? 1 : -1;

        _navY = Repeat(rawY, ref _heldY, ref _nextY);
        _navX = Repeat(rawX, ref _heldX, ref _nextX);

        // ── Buttons ───────────────────────────────────────────────────────────
        _confirm = (kb && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                                                            || Input.GetKeyDown(KeyCode.Space)))
                   || Input.GetKeyDown(KeyCode.JoystickButton0);
        _back    = (kb && Input.GetKeyDown(KeyCode.Escape)) || Input.GetKeyDown(KeyCode.JoystickButton1);
        _alt1    = (kb && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
                   || Input.GetKeyDown(KeyCode.JoystickButton2);
        _alt2    = (kb && Input.GetKeyDown(KeyCode.Tab)) || Input.GetKeyDown(KeyCode.JoystickButton3);
        _tabL    = (kb && Input.GetKeyDown(KeyCode.Q)) || Input.GetKeyDown(KeyCode.JoystickButton4);
        _tabR    = (kb && Input.GetKeyDown(KeyCode.E)) || Input.GetKeyDown(KeyCode.JoystickButton5);
        _start   = Input.GetKeyDown(KeyCode.JoystickButton7);

        if (_injectFrame == Time.frameCount)
        {
            switch (_inject)
            {
                case MenuAction.Confirm:  _confirm = true; break;
                case MenuAction.Back:     _back    = true; break;
                case MenuAction.Alt1:     _alt1    = true; break;
                case MenuAction.Alt2:     _alt2    = true; break;
                case MenuAction.TabLeft:  _tabL    = true; break;
                case MenuAction.TabRight: _tabR    = true; break;
                case MenuAction.Start:    _start   = true; break;
            }
            _injectFrame = -1;
        }
    }

    static int Repeat(int raw, ref int held, ref float next)
    {
        float now = Time.unscaledTime;
        if (raw == 0) { held = 0; return 0; }
        if (raw != held) { held = raw; next = now + RepeatDelay; return raw; }
        if (now >= next) { next = now + RepeatRate; return raw; }
        return 0;
    }

    static void DetectDevice()
    {
        bool pad = false;
        for (int b = 0; b < 16 && !pad; b++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.JoystickButton0 + b))) pad = true;
        if (!pad)
        {
            pad = Mathf.Abs(Axis("PadSteerX")) > 0.5f || Mathf.Abs(Axis("PadMoveY")) > 0.5f
               || Mathf.Abs(Axis("PadDpadX"))  > 0.5f || Mathf.Abs(Axis("PadDpadY")) > 0.5f
               || Mathf.Abs(Axis("PadShiftX")) > 0.5f || Mathf.Abs(Axis("PadShiftY")) > 0.5f;
        }

        bool kbm = false;
        if (!pad)
        {
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) kbm = true;
            var m = Input.mousePosition;
            if (_lastMouse != Vector3.zero && (m - _lastMouse).sqrMagnitude > 36f) kbm = true;
            _lastMouse = m;
        }

        if (pad && !UsingPad)      { UsingPad = true;  DeviceChanged?.Invoke(); }
        else if (kbm && UsingPad)  { UsingPad = false; DeviceChanged?.Invoke(); }
    }

    // Input.GetAxis throws on an unregistered axis — cache the misses so a
    // project without the pad axes doesn't spam exceptions every frame.
    static HashSet<string> _bad;
    public static float Axis(string name)
    {
        if (_bad != null && _bad.Contains(name)) return 0f;
        try { return Input.GetAxis(name); }
        catch { (_bad ??= new HashSet<string>()).Add(name); return 0f; }
    }
}
