using UnityEngine;
using TMPro;

/// Editable text row (driver name).  A / Enter / click starts typing; Enter
/// commits, Esc (or B) cancels.  Ctrl+V pastes.  Gamepad-only players keep
/// the value they have — typing needs a keyboard.
public class MenuTextField : MenuRow
{
    [Header("Text field")]
    public TextMeshProUGUI valueText;
    public int  maxLength = 16;
    public bool upperCase = true;
    public string placeholder = "TYPE A NAME";

    public event System.Action<string> Committed;

    public string Value { get; private set; } = "";
    bool   editing;
    string before;

    public override bool CapturesInput => editing;

    public void SetValue(string v)
    {
        Value = Clean(v ?? "");
        Refresh();
    }

    public override void Submit()
    {
        if (!interactable) { Shake(); MenuAudio.Play(Sfx.Error); return; }
        if (MenuInput.UsingPad && !editing)
        {
            // No on-screen keyboard (yet) — make it clear why nothing happens.
            Shake();
            MenuAudio.Play(Sfx.Error, 0.6f);
            screen?.FlashHint("Use a keyboard to type a name.");
            return;
        }
        BeginEdit();
    }

    void BeginEdit()
    {
        if (editing) return;
        editing = true;
        before  = Value;
        MenuInput.TextCapture = true;
        MenuAudio.Play(Sfx.Toggle);
        Refresh();
    }

    void EndEdit(bool commit)
    {
        if (!editing) return;
        editing = false;
        MenuInput.TextCapture = false;
        if (!commit) Value = before;
        Value = Value.Trim();
        if (Value.Length == 0) Value = before;
        MenuAudio.Play(commit ? Sfx.Confirm : Sfx.Back);
        Refresh();
        if (commit) Committed?.Invoke(Value);
    }

    public override void TickInput()
    {
        if (!editing) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)) { EndEdit(false); return; }
        if (Input.GetKeyDown(KeyCode.JoystickButton0)) { EndEdit(true); return; }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.V))
        {
            Value = Clean(Value + GUIUtility.systemCopyBuffer);
            MenuAudio.Play(Sfx.Type);
            Refresh();
            return;
        }

        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (Value.Length > 0) { Value = Value.Substring(0, Value.Length - 1); MenuAudio.Play(Sfx.Type, 0.7f); }
            }
            else if (c == '\n' || c == '\r') { EndEdit(true); return; }
            else if (Allowed(c) && Value.Length < maxLength)
            {
                Value += upperCase ? char.ToUpperInvariant(c) : c;
                MenuAudio.Play(Sfx.Type);
            }
        }
        Refresh();
    }

    bool Allowed(char c) => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.';

    string Clean(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
            if (Allowed(c) && sb.Length < maxLength) sb.Append(upperCase ? char.ToUpperInvariant(c) : c);
        return sb.ToString();
    }

    void Refresh()
    {
        if (valueText == null) return;
        if (editing)
        {
            bool blink = (Time.unscaledTime * 2f) % 2f < 1.2f;
            valueText.text = Value + (blink ? "<color=#B06BFF>_</color>" : "<alpha=#00>_");
        }
        else valueText.text = Value.Length > 0 ? Value : $"<color=#6B6885>{placeholder}</color>";
    }

    protected override void Update()
    {
        base.Update();
        if (editing) Refresh();
        if (valueText != null && !editing)
        {
            float s = UITheme.EaseOutCubic(focusT);
            valueText.color = interactable ? Color.Lerp(UITheme.TextDim, UITheme.Text, s) : UITheme.TextFaint;
        }
        else if (valueText != null) valueText.color = UITheme.Text;
        if (background != null && editing)
            background.color = Color.Lerp(background.color, UITheme.Alpha(UITheme.AccentDeep, 0.95f), 0.5f);
    }

    protected override void OnFocusChanged(bool f)
    {
        if (!f && editing) EndEdit(true);
    }

    void OnDisable()
    {
        if (editing) { editing = false; MenuInput.TextCapture = false; }
    }
}
