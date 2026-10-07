using UnityEditor;
using UnityEngine;

/// One-shot editor utility that registers the gamepad axes the runtime code
/// reads.  Run once after pulling gamepad changes; rerun safely any time —
/// existing entries are left untouched and only missing ones are added.
///
/// Default mappings (Xbox controller via XInput on Windows):
///   PadSteerX   → left stick X         (axis 0)
///   PadThrottle → right trigger (RT)   (axis 9)
///   PadBrake    → left trigger  (LT)   (axis 8)
///   PadShiftX   → right stick X        (axis 3)
///   PadShiftY   → right stick Y        (axis 4, inverted → stick-up = +1)
///   PadMoveY    → left stick Y         (axis 1, inverted → stick-up = +1)
///   PadDpadX/Y  → d-pad                (axes 5 / 6 — menu navigation)
///
/// Buttons (KeyCode.JoystickButton4 / 5 for LB / RB) work without axis
/// entries.  If your controller's triggers behave differently, edit the axis
/// numbers in Edit → Project Settings → Input Manager.
public static class GamepadInputSetup
{
    [MenuItem("Racing Game/Configure Gamepad Input")]
    static void Configure()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[RacingGame] Could not load ProjectSettings/InputManager.asset.");
            return;
        }

        var so   = new SerializedObject(assets[0]);
        var axes = so.FindProperty("m_Axes");

        int added = 0;
        added += EnsureAxis(axes, "PadSteerX",   joystickAxisIndex: 0,  deadzone: 0.10f, invert: false) ? 1 : 0;
        added += EnsureAxis(axes, "PadThrottle", joystickAxisIndex: 9,  deadzone: 0.05f, invert: false) ? 1 : 0;
        added += EnsureAxis(axes, "PadBrake",    joystickAxisIndex: 8,  deadzone: 0.05f, invert: false) ? 1 : 0;
        added += EnsureAxis(axes, "PadShiftX",   joystickAxisIndex: 3,  deadzone: 0.20f, invert: false) ? 1 : 0;
        added += EnsureAxis(axes, "PadShiftY",   joystickAxisIndex: 4,  deadzone: 0.20f, invert: true ) ? 1 : 0;
        added += EnsureAxis(axes, "PadMoveY",    joystickAxisIndex: 1,  deadzone: 0.20f, invert: true ) ? 1 : 0;
        // D-pad (Xbox on Windows: 6th / 7th axes) — menu navigation.
        added += EnsureAxis(axes, "PadDpadX",    joystickAxisIndex: 5,  deadzone: 0.20f, invert: false) ? 1 : 0;
        added += EnsureAxis(axes, "PadDpadY",    joystickAxisIndex: 6,  deadzone: 0.20f, invert: false) ? 1 : 0;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(assets[0]);
        AssetDatabase.SaveAssets();

        Debug.Log(added > 0
            ? $"[RacingGame] Added {added} gamepad axis entries to InputManager. " +
              "Re-enter Play mode for them to take effect."
            : "[RacingGame] Gamepad axes already configured — no changes made.");
    }

    /// Returns true if a new entry was added.  Existing entries are left untouched.
    static bool EnsureAxis(SerializedProperty axes, string name, int joystickAxisIndex,
                           float deadzone, bool invert)
    {
        for (int i = 0; i < axes.arraySize; i++)
        {
            var entry = axes.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("m_Name").stringValue == name)
                return false;     // already present
        }

        axes.arraySize++;
        var ne = axes.GetArrayElementAtIndex(axes.arraySize - 1);

        ne.FindPropertyRelative("m_Name").stringValue                  = name;
        ne.FindPropertyRelative("descriptiveName").stringValue         = "";
        ne.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
        ne.FindPropertyRelative("negativeButton").stringValue          = "";
        ne.FindPropertyRelative("positiveButton").stringValue          = "";
        ne.FindPropertyRelative("altNegativeButton").stringValue       = "";
        ne.FindPropertyRelative("altPositiveButton").stringValue       = "";
        ne.FindPropertyRelative("gravity").floatValue                  = 0f;
        ne.FindPropertyRelative("dead").floatValue                     = deadzone;
        ne.FindPropertyRelative("sensitivity").floatValue              = 1f;
        ne.FindPropertyRelative("snap").boolValue                      = false;
        ne.FindPropertyRelative("invert").boolValue                    = invert;
        ne.FindPropertyRelative("type").intValue                       = 2;   // 2 = Joystick Axis
        ne.FindPropertyRelative("axis").intValue                       = joystickAxisIndex;
        ne.FindPropertyRelative("joyNum").intValue                     = 0;   // all joysticks
        return true;
    }
}
