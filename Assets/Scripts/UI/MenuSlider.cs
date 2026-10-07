using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// Value bar row (volume, FOV…).  Left/right step it with hold-repeat; the
/// mouse can click or drag anywhere on the baked track.
public class MenuSlider : MenuRow
{
    [Header("Slider visuals")]
    public RectTransform   track;      // the bar's full extent (drag area)
    public RectTransform   fill;       // stretched child: anchorMax.x = fraction
    public RectTransform   knob;       // optional marker at the fill end
    public TextMeshProUGUI valueText;

    [Header("Range")]
    public float min = 0f, max = 1f, step = 0.05f;
    [Tooltip("{0} = value.  Percent shows 0..1 as 0..100%.")]
    public bool  percent = true;
    public string format = "{0:0}";

    public event System.Action<float> Changed;
    public float Value { get; private set; }

    float shown;
    float lastSound;

    public override bool WantsHorizontal => true;

    protected override void Awake()
    {
        base.Awake();
        if (track != null)
        {
            var drag = track.GetComponent<MenuSliderDrag>();
            if (drag == null) drag = track.gameObject.AddComponent<MenuSliderDrag>();
            drag.owner = this;
            var g = track.GetComponent<Graphic>();
            if (g != null) g.raycastTarget = true;
        }
    }

    public void SetValue(float v, bool notify = false)
    {
        v = Mathf.Clamp(v, min, max);
        bool changed = !Mathf.Approximately(v, Value);
        Value = v;
        if (!_init) { shown = Fraction; _init = true; }
        RefreshText();
        if (changed && notify) Changed?.Invoke(Value);
    }
    bool _init;

    float Fraction => max > min ? (Value - min) / (max - min) : 0f;

    public override void Adjust(int dir)
    {
        if (!interactable) { Shake(); return; }
        float v = Mathf.Clamp(Mathf.Round((Value + dir * step) / step) * step, min, max);
        if (Mathf.Approximately(v, Value)) { Shake(); return; }
        SetValue(v, true);
        Sound();
    }

    public override void Submit() => Adjust(Value >= max - 0.0001f ? -1 : 1);

    internal void SetFromPointer(PointerEventData e)
    {
        if (track == null || !interactable) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position,
                                                                     e.pressEventCamera, out var local))
            return;
        var r = track.rect;
        float f = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
        float v = min + f * (max - min);
        v = Mathf.Round(v / step) * step;
        if (Mathf.Approximately(Mathf.Clamp(v, min, max), Value)) return;
        SetValue(v, true);
        Sound();
    }

    void Sound()
    {
        if (Time.unscaledTime - lastSound < 0.05f) return;
        lastSound = Time.unscaledTime;
        MenuAudio.Play(Sfx.Adjust, 0.8f, 0.9f + 0.3f * Fraction);
    }

    void RefreshText()
    {
        if (valueText == null) return;
        valueText.text = percent ? $"{Mathf.RoundToInt(Fraction * 100f)}%"
                                 : string.Format(format, Value);
    }

    protected override void Update()
    {
        base.Update();
        shown = UITheme.Damp(shown, Fraction, 18f);
        if (fill != null) fill.anchorMax = new Vector2(Mathf.Max(0.0001f, shown), 1f);
        if (knob != null) { knob.anchorMin = knob.anchorMax = new Vector2(shown, 0.5f); }
        float s = UITheme.EaseOutCubic(focusT);
        if (valueText != null)
            valueText.color = interactable ? Color.Lerp(UITheme.TextDim, UITheme.Text, s) : UITheme.TextFaint;
        var fillImg = fill != null ? fill.GetComponent<Image>() : null;
        if (fillImg != null) fillImg.color = Color.Lerp(UITheme.Alpha(UITheme.Accent, 0.65f), UITheme.AccentHot, s);
    }
}

/// Mouse click/drag on a slider track.
public class MenuSliderDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public MenuSlider owner;

    public void OnPointerDown(PointerEventData e)
    {
        if (owner == null) return;
        if (owner.screen != null && MenuScreen.Top != owner.screen) return;
        owner.screen?.Focus(owner, true);
        owner.SetFromPointer(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (owner == null) return;
        if (owner.screen != null && MenuScreen.Top != owner.screen) return;
        owner.SetFromPointer(e);
    }
}
