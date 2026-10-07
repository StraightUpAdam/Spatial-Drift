using System.Text;
using UnityEngine;

/// On-screen gamepad diagnostic.  Press F11 in Play mode to toggle.
///
/// Shows:
///   • Names of all joysticks Unity sees (empty = controller not detected)
///   • Live values of every custom axis we configured
///   • Which joystick buttons are currently held (0-19)
///
/// Use this to verify:
///   1. Your controller is detected at all (joystick name appears)
///   2. The custom axes are configured (no "axis missing" errors)
///   3. Which axes/buttons actually fire when you press sticks/triggers
///
/// Auto-attached to the camera by RacingCamera.Awake.
[DisallowMultipleComponent]
public class GamepadDebug : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F11;
    public bool    visible   = false;

    GUIStyle style;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
    }

    void OnGUI()
    {
        if (!visible) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                richText  = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = false,
            };
            style.normal.textColor = Color.white;
        }

        var sb = new StringBuilder(1024);
        sb.AppendLine("<b>=== GAMEPAD DEBUG (F11 to toggle) ===</b>");
        sb.AppendLine();

        // ── Joysticks detected ────────────────────────────────────────────
        var names = Input.GetJoystickNames();
        sb.AppendLine($"<b>Joysticks ({names.Length}):</b>");
        if (names.Length == 0)
        {
            sb.AppendLine("  <color=#ff8080>(none — Unity doesn't see your controller)</color>");
            sb.AppendLine("  • Make sure the controller is plugged in BEFORE Play");
            sb.AppendLine("  • On Windows, generic controllers may need DS4Windows / XOutput");
        }
        for (int i = 0; i < names.Length; i++)
        {
            string n = string.IsNullOrEmpty(names[i]) ? "(empty slot)" : names[i];
            sb.AppendLine($"  [{i}] {n}");
        }
        sb.AppendLine();

        // ── Custom axes ───────────────────────────────────────────────────
        sb.AppendLine("<b>Custom axes (from 'Configure Gamepad Input'):</b>");
        AppendAxis(sb, "PadSteerX");
        AppendAxis(sb, "PadThrottle");
        AppendAxis(sb, "PadBrake");
        AppendAxis(sb, "PadShiftX");
        AppendAxis(sb, "PadShiftY");
        AppendAxis(sb, "PadMoveY");
        sb.AppendLine();

        // ── Held buttons ──────────────────────────────────────────────────
        sb.AppendLine("<b>Held buttons (0-19):</b>");
        bool any = false;
        for (int b = 0; b < 20; b++)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + b))
            {
                sb.Append($"  Button {b}");
                if (b == 0) sb.Append("  (A)");
                if (b == 1) sb.Append("  (B)");
                if (b == 2) sb.Append("  (X)");
                if (b == 3) sb.Append("  (Y)");
                if (b == 4) sb.Append("  (LB)");
                if (b == 5) sb.Append("  (RB)");
                if (b == 6) sb.Append("  (Back/View)");
                if (b == 7) sb.Append("  (Start/Menu)");
                if (b == 8) sb.Append("  (L-stick click)");
                if (b == 9) sb.Append("  (R-stick click)");
                sb.AppendLine();
                any = true;
            }
        }
        if (!any) sb.AppendLine("  (none)");

        GUI.Box(new Rect(12, 12, 420, 460), GUIContent.none);
        GUI.Label(new Rect(20, 18, 400, 450), sb.ToString(), style);
    }

    static void AppendAxis(StringBuilder sb, string name)
    {
        float v;
        bool  exists = true;
        try { v = Input.GetAxis(name); }
        catch { exists = false; v = 0f; }

        if (!exists)
            sb.AppendLine($"  <color=#ff8080>{name,-12} : MISSING — run Configure Gamepad Input</color>");
        else
        {
            string color = Mathf.Abs(v) > 0.05f ? "#80ff80" : "#aaaaaa";
            sb.AppendLine($"  <color={color}>{name,-12} : {v,6:+0.000;-0.000; 0.000}</color>");
        }
    }
}
