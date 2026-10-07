using UnityEngine;

/// Global pause flag.  The pause menus set it; gameplay input (car, shifter,
/// drift zone, restart keys) checks InputBlocked so nothing leaks through while
/// a menu is up.  Solo play also freezes time + game audio; multiplayer can't
/// stop the world, so there it only blocks input (the car coasts).
public static class GamePause
{
    public static bool Active { get; private set; }
    static bool _froze;
    static int  _resumeFrame = -10;

    /// True while paused AND for a couple of frames after resuming — the button
    /// that closed the menu (B / A / Start) must not also fire a gameplay action
    /// (B = time-trial restart and a drift-zone lane, A = a lane…).
    public static bool InputBlocked => Active || Time.frameCount <= _resumeFrame + 2;

    public static void Set(bool paused, bool freezeTime)
    {
        if (Active && !paused) _resumeFrame = Time.frameCount;
        Active = paused;
        if (paused && freezeTime)
        {
            Time.timeScale       = 0f;
            AudioListener.pause  = true;   // menu audio ignores listener pause
            _froze = true;
        }
        else if (!paused && _froze)
        {
            Time.timeScale      = 1f;
            AudioListener.pause = false;
            _froze = false;
        }
    }

    /// Hard reset — scene loads call this so a quit-from-pause never leaves the
    /// next scene frozen.
    public static void Clear()
    {
        Active = false;
        _froze = false;
        Time.timeScale      = 1f;
        AudioListener.pause = false;
    }
}
