using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Six-slot join-code entry that works on BOTH keyboard and pad:
///   keyboard — type / paste (Ctrl+V), Backspace, Enter to submit
///   gamepad  — A to start, up/down cycles the letter, left/right moves the
///              cursor, A submits, B stops editing
public class MenuCodeField : MenuRow
{
    [Header("Code field")]
    public TextMeshProUGUI[] slots;
    public Image[]           slotBgs;

    public event System.Action<string> Submitted;

    const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    char[] code;
    int    cursor;
    bool   editing;

    public override bool CapturesInput => editing;
    public string Code => code == null ? "" : new string(code).Replace(" ", "");

    protected override void Awake()
    {
        base.Awake();
        int n = slots != null ? slots.Length : 6;
        code = new char[n];
        for (int i = 0; i < n; i++) code[i] = ' ';
    }

    public void SetCode(string s)
    {
        s = (s ?? "").ToUpperInvariant();
        for (int i = 0; i < code.Length; i++) code[i] = i < s.Length && Chars.IndexOf(s[i]) >= 0 ? s[i] : ' ';
        cursor = Mathf.Clamp(s.Length, 0, code.Length - 1);
    }

    public override void Submit()
    {
        if (!interactable) { Shake(); MenuAudio.Play(Sfx.Error); return; }
        if (!editing) { Begin(); return; }
        Fire();
    }

    void Begin()
    {
        editing = true;
        MenuInput.TextCapture = true;
        cursor = Mathf.Clamp(Code.Length, 0, code.Length - 1);
        MenuAudio.Play(Sfx.Toggle);
    }

    void Stop(bool sound = true)
    {
        if (!editing) return;
        editing = false;
        MenuInput.TextCapture = false;
        if (sound) MenuAudio.Play(Sfx.Back);
    }

    void Fire()
    {
        Stop(false);
        if (Code.Length < code.Length) { Shake(); MenuAudio.Play(Sfx.Error); return; }
        Punch();
        Submitted?.Invoke(Code);
    }

    public override void TickInput()
    {
        if (!editing) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)) { Stop(); return; }
        if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter)) { Fire(); return; }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.V))
        {
            string clip = (GUIUtility.systemCopyBuffer ?? "").Trim().ToUpperInvariant();
            SetCode(clip);
            MenuAudio.Play(Sfx.Type);
            return;
        }

        foreach (char raw in Input.inputString)
        {
            char c = char.ToUpperInvariant(raw);
            if (raw == '\b')
            {
                if (code[cursor] == ' ' && cursor > 0) cursor--;
                code[cursor] = ' ';
                MenuAudio.Play(Sfx.Type, 0.7f);
            }
            else if (Chars.IndexOf(c) >= 0)
            {
                code[cursor] = c;
                if (cursor < code.Length - 1) cursor++;
                MenuAudio.Play(Sfx.Type);
            }
        }

        // Pad: d-pad / stick (MenuInput honours pad even while typing).
        int y = MenuInput.NavY, x = MenuInput.NavX;
        if (y != 0)
        {
            int idx = Chars.IndexOf(code[cursor]);
            idx = idx < 0 ? (y < 0 ? 0 : Chars.Length - 1)
                          : (idx + (y < 0 ? 1 : -1) + Chars.Length) % Chars.Length;
            code[cursor] = Chars[idx];
            MenuAudio.Play(Sfx.Adjust);
        }
        if (x != 0)
        {
            cursor = Mathf.Clamp(cursor + x, 0, code.Length - 1);
            MenuAudio.Play(Sfx.Move);
        }
    }

    protected override void Update()
    {
        base.Update();
        if (slots == null) return;
        float s = UITheme.EaseOutCubic(focusT);
        bool blink = (Time.unscaledTime * 2.2f) % 2f < 1.3f;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                bool cur = editing && i == cursor;
                slots[i].text  = code[i] == ' ' ? (cur && blink ? "_" : "") : code[i].ToString();
                slots[i].color = code[i] == ' ' ? UITheme.Accent : Color.Lerp(UITheme.TextDim, UITheme.Text, Mathf.Max(s, 0.5f));
            }
            if (slotBgs != null && i < slotBgs.Length && slotBgs[i] != null)
            {
                bool cur = editing && i == cursor;
                slotBgs[i].color = cur ? UITheme.Alpha(UITheme.Accent, 0.55f)
                                       : Color.Lerp(UITheme.Alpha(Color.black, 0.45f),
                                                    UITheme.Alpha(UITheme.AccentDeep, 0.6f), s);
            }
        }
    }

    protected override void OnFocusChanged(bool f)
    {
        if (!f) Stop(false);
    }

    void OnDisable()
    {
        if (editing) { editing = false; MenuInput.TextCapture = false; }
    }
}
