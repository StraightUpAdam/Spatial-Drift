using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// ◄ VALUE ► selector row.  Left/right (or the clickable arrows) cycle the
/// options with a slide animation; A cycles forward unless something listens
/// to Submitted.  Optional baked swatches turn it into a paint picker.
public class MenuStepper : MenuRow
{
    [Header("Stepper visuals")]
    public TextMeshProUGUI valueText;
    public Image arrowLeft, arrowRight;
    [Tooltip("Optional colour chips (paint picker) — one per option.")]
    public Image[] swatches;
    public RectTransform swatchCursor;

    [Header("Stepper behaviour")]
    public bool wrap = true;

    public event System.Action<int> Changed;
    /// If anything subscribes, A/click-on-row fires this instead of cycling.
    public event System.Action Submitted;

    readonly List<string> options = new List<string>();
    public int Index { get; private set; }
    public int Count => options.Count;
    public string Value => Index >= 0 && Index < options.Count ? options[Index] : "";

    float slide, slideDir;
    float nudgeL, nudgeR;

    public override bool WantsHorizontal => true;

    protected override void Awake()
    {
        base.Awake();
        Hook(arrowLeft, -1);
        Hook(arrowRight, +1);
    }

    void Hook(Image arrow, int dir)
    {
        if (arrow == null) return;
        var click = arrow.GetComponent<MenuArrowClick>();
        if (click == null) click = arrow.gameObject.AddComponent<MenuArrowClick>();
        click.owner = this;
        click.dir   = dir;
        arrow.raycastTarget = true;
    }

    public void SetOptions(IList<string> opts, int index)
    {
        options.Clear();
        if (opts != null) options.AddRange(opts);
        Index = options.Count == 0 ? 0 : Mathf.Clamp(index, 0, options.Count - 1);
        RefreshValue();
    }

    public void SetIndex(int i, bool notify = false)
    {
        if (options.Count == 0) return;
        i = Mathf.Clamp(i, 0, options.Count - 1);
        if (i == Index) { RefreshValue(); return; }
        Index = i;
        RefreshValue();
        if (notify) Changed?.Invoke(Index);
    }

    public void SetSwatchColors(IList<Color> colors)
    {
        if (swatches == null) return;
        for (int i = 0; i < swatches.Length; i++)
        {
            if (swatches[i] == null) continue;
            bool has = colors != null && i < colors.Count;
            swatches[i].gameObject.SetActive(has);
            if (has) swatches[i].color = colors[i];
        }
    }

    public override void Adjust(int dir)
    {
        if (!interactable || options.Count < 2) { if (options.Count >= 2) Shake(); return; }
        int n = wrap ? (Index + dir + options.Count) % options.Count
                     : Mathf.Clamp(Index + dir, 0, options.Count - 1);
        if (n == Index) { Shake(); MenuAudio.Play(Sfx.Error, 0.5f); return; }
        Index    = n;
        slide    = 1f;
        slideDir = dir;
        if (dir < 0) nudgeL = 1f; else nudgeR = 1f;
        RefreshValue();
        MenuAudio.Play(Sfx.Adjust);
        Changed?.Invoke(Index);
    }

    public override void Submit()
    {
        if (!interactable) { Shake(); MenuAudio.Play(Sfx.Error); return; }
        if (Submitted != null) { Punch(); MenuAudio.Play(Sfx.Confirm); Submitted.Invoke(); return; }
        Adjust(+1);
    }

    void RefreshValue()
    {
        if (valueText != null) valueText.text = Value;
    }

    protected override void Update()
    {
        base.Update();
        float dt = Time.unscaledDeltaTime;
        slide  = Mathf.MoveTowards(slide, 0f, dt / 0.16f);
        nudgeL = Mathf.MoveTowards(nudgeL, 0f, dt * 6f);
        nudgeR = Mathf.MoveTowards(nudgeR, 0f, dt * 6f);
        float s = UITheme.EaseOutCubic(focusT);

        if (valueText != null)
        {
            float e = UITheme.EaseOutCubic(1f - slide);
            var rt = valueText.rectTransform;
            var p  = rt.anchoredPosition;
            if (!_basePosSet) { _baseX = p.x; _basePosSet = true; }
            rt.anchoredPosition = new Vector2(_baseX + (1f - e) * 26f * slideDir, p.y);
            var c = interactable ? Color.Lerp(UITheme.TextDim, UITheme.Text, Mathf.Max(s, 0.6f))
                                 : UITheme.TextFaint;
            c.a = Mathf.Lerp(0.2f, 1f, e);
            valueText.color = c;
        }

        Color arrowIdle  = UITheme.Alpha(UITheme.TextFaint, 0.8f);
        Color arrowFocus = UITheme.Accent;
        bool showArrows = interactable && options.Count > 1;
        if (arrowLeft != null)
        {
            arrowLeft.enabled = showArrows;
            arrowLeft.color = Color.Lerp(arrowIdle, arrowFocus, Mathf.Max(s, nudgeL));
            arrowLeft.rectTransform.localScale = new Vector3(-1f, 1f, 1f) * (1f + nudgeL * 0.35f);
        }
        if (arrowRight != null)
        {
            arrowRight.enabled = showArrows;
            arrowRight.color = Color.Lerp(arrowIdle, arrowFocus, Mathf.Max(s, nudgeR));
            arrowRight.rectTransform.localScale = Vector3.one * (1f + nudgeR * 0.35f);
        }

        if (swatches != null && swatches.Length > 0)
        {
            for (int i = 0; i < swatches.Length; i++)
            {
                if (swatches[i] == null) continue;
                float target = i == Index ? 1.18f : 0.78f;
                var st = swatches[i].rectTransform;
                float k = Mathf.Lerp(st.localScale.x, target, 1f - Mathf.Exp(-14f * dt));
                st.localScale = new Vector3(k, k, 1f);
            }
            if (swatchCursor != null && Index < swatches.Length && swatches[Index] != null)
            {
                var target = swatches[Index].rectTransform.anchoredPosition;
                swatchCursor.anchoredPosition = Vector2.Lerp(swatchCursor.anchoredPosition, target,
                                                             1f - Mathf.Exp(-18f * dt));
            }
        }
    }

    float _baseX;
    bool  _basePosSet;
}

/// Clickable ◄ / ► on a stepper (added automatically to the baked arrows).
public class MenuArrowClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    public MenuStepper owner;
    public int dir = 1;

    public void OnPointerClick(PointerEventData e)
    {
        if (owner == null || e.button != PointerEventData.InputButton.Left) return;
        if (owner.screen != null && MenuScreen.Top != owner.screen) return;
        owner.screen?.Focus(owner, true);
        owner.Adjust(dir);
    }

    public void OnPointerEnter(PointerEventData e) => owner?.OnPointerEnter(e);
}
