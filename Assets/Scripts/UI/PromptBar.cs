using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// Button-prompt strip ("Ⓐ SELECT   Ⓑ BACK").  Glyphs follow the last-used
/// device — Xbox face buttons on a pad, keycaps on keyboard — and every
/// prompt is clickable with the mouse.  Entries are cloned from a baked,
/// inactive template child so the look is art-passable in the editor.
[DisallowMultipleComponent]
public class PromptBar : MonoBehaviour
{
    public PromptEntry template;

    readonly List<PromptEntry> live = new List<PromptEntry>();
    readonly List<(MenuAction action, string label)> current = new List<(MenuAction, string)>();
    bool lastPad;

    public void Set(params (MenuAction action, string label)[] prompts)
    {
        current.Clear();
        if (prompts != null) current.AddRange(prompts);
        Rebuild();
    }

    void OnEnable()
    {
        MenuInput.DeviceChanged += Rebuild;
        lastPad = MenuInput.UsingPad;
    }

    void OnDisable() => MenuInput.DeviceChanged -= Rebuild;

    void Update()
    {
        MenuInput.Poll();   // keep device detection live even on idle screens
        if (lastPad != MenuInput.UsingPad) Rebuild();
    }

    void Rebuild()
    {
        lastPad = MenuInput.UsingPad;
        if (template == null) return;
        template.gameObject.SetActive(false);
        while (live.Count < current.Count)
        {
            var e = Instantiate(template, template.transform.parent);
            e.name = "Prompt";
            live.Add(e);
        }
        for (int i = 0; i < live.Count; i++)
        {
            bool on = i < current.Count;
            live[i].gameObject.SetActive(on);
            if (on) live[i].Show(current[i].action, current[i].label, lastPad);
        }
    }
}

/// Glyph art per action and device.
public static class MenuGlyphs
{
    public static void Style(MenuAction a, bool pad, Image bg, TextMeshProUGUI txt, Image arrowA, Image arrowB)
    {
        bool dir = a == MenuAction.Adjust || a == MenuAction.Navigate;
        if (arrowA != null) arrowA.gameObject.SetActive(dir);
        if (arrowB != null) arrowB.gameObject.SetActive(dir);
        if (txt != null) txt.gameObject.SetActive(!dir);

        if (dir)
        {
            float rotA = a == MenuAction.Adjust ? 180f : 90f;     // ◄ or ▲
            float rotB = a == MenuAction.Adjust ? 0f   : -90f;    // ► or ▼
            if (arrowA != null) { arrowA.rectTransform.localRotation = Quaternion.Euler(0, 0, rotA); arrowA.color = UITheme.Text; }
            if (arrowB != null) { arrowB.rectTransform.localRotation = Quaternion.Euler(0, 0, rotB); arrowB.color = UITheme.Text; }
            SetBg(bg, "ui_pill", pad ? new Color(0.20f, 0.19f, 0.26f, 1f) : Color.clear, pad ? 64f : 58f, 34f);
            if (!pad && bg != null) { bg.sprite = UITheme.Sprite("ui_round_line"); bg.type = Image.Type.Sliced; bg.color = UITheme.Alpha(UITheme.Text, 0.8f); }
            return;
        }

        if (pad)
        {
            switch (a)
            {
                case MenuAction.Confirm: Circle(bg, txt, "A", UITheme.PadA); break;
                case MenuAction.Back:    Circle(bg, txt, "B", UITheme.PadB); break;
                case MenuAction.Alt1:    Circle(bg, txt, "X", UITheme.PadX); break;
                case MenuAction.Alt2:    Circle(bg, txt, "Y", UITheme.PadY); break;
                case MenuAction.TabLeft: Pill(bg, txt, "LB"); break;
                case MenuAction.TabRight:Pill(bg, txt, "RB"); break;
                case MenuAction.Start:   Pill(bg, txt, "MENU"); break;
            }
        }
        else
        {
            switch (a)
            {
                case MenuAction.Confirm: Key(bg, txt, "ENTER"); break;
                case MenuAction.Back:    Key(bg, txt, "ESC");   break;
                case MenuAction.Alt1:    Key(bg, txt, "DEL");   break;
                case MenuAction.Alt2:    Key(bg, txt, "TAB");   break;
                case MenuAction.TabLeft: Key(bg, txt, "Q");     break;
                case MenuAction.TabRight:Key(bg, txt, "E");     break;
                case MenuAction.Start:   Key(bg, txt, "ESC");   break;
            }
        }
    }

    static void Circle(Image bg, TextMeshProUGUI t, string s, Color c)
    {
        SetBg(bg, "ui_circle", c, 36f, 36f);
        if (t != null) { t.text = s; t.color = Color.white; t.fontSize = 20; t.fontStyle = FontStyles.Bold; }
    }

    static void Pill(Image bg, TextMeshProUGUI t, string s)
    {
        SetBg(bg, "ui_pill", new Color(0.20f, 0.19f, 0.26f, 1f), s.Length > 2 ? 72f : 50f, 32f);
        if (t != null) { t.text = s; t.color = UITheme.Text; t.fontSize = 16; t.fontStyle = FontStyles.Bold; }
    }

    static void Key(Image bg, TextMeshProUGUI t, string s)
    {
        float w = Mathf.Max(36f, 16f + s.Length * 12f);
        SetBg(bg, "ui_round_line", UITheme.Alpha(UITheme.Text, 0.8f), w, 34f);
        if (t != null) { t.text = s; t.color = UITheme.Text; t.fontSize = 15; t.fontStyle = FontStyles.Bold; }
    }

    static void SetBg(Image bg, string sprite, Color c, float w, float h)
    {
        if (bg == null) return;
        bg.sprite = UITheme.Sprite(sprite);
        bg.type   = sprite == "ui_circle" ? Image.Type.Simple : Image.Type.Sliced;
        bg.color  = c;
        var le = bg.GetComponent<LayoutElement>();
        if (le != null) { le.preferredWidth = w; le.minWidth = w; }
        bg.rectTransform.sizeDelta = new Vector2(w, h);
    }
}
