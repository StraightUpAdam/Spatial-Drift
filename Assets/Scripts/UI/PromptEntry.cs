using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// One prompt: glyph (circle / pill / keycap) + label.  Click = press.
public class PromptEntry : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Image           glyphBg;
    public TextMeshProUGUI glyphText;
    public Image           arrowA, arrowB;    // for directional glyphs
    public TextMeshProUGUI label;

    MenuAction action;
    bool hover;

    public void Show(MenuAction a, string text, bool pad)
    {
        action = a;
        if (label != null) label.text = text;
        MenuGlyphs.Style(a, pad, glyphBg, glyphText, arrowA, arrowB);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (action != MenuAction.Adjust && action != MenuAction.Navigate) MenuInput.Inject(action);
    }

    public void OnPointerEnter(PointerEventData e) => hover = true;
    public void OnPointerExit(PointerEventData e)  => hover = false;

    void Update()
    {
        if (label != null) label.color = hover ? UITheme.Text : UITheme.Alpha(UITheme.TextDim, 0.95f);
    }
}

