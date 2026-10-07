using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// Base of every focusable menu row.  The visuals are BAKED into the scene by
/// the builders (background, accent bar, edge, glow, label); this component
/// only animates them:
///   • focus   — background brightens, accent bar sweeps in, edge + glow light,
///               the row nudges right and the label goes full white
///   • intro   — rows slide + fade in with a stagger when a screen opens
///   • punch / shake — confirm pop and "nope" wobble
/// Mouse: hover focuses, click submits.  Keyboard/pad come via MenuScreen.
[DisallowMultipleComponent]
public abstract class MenuRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Baked visuals")]
    public RectTransform   content;      // animated child holding all the visuals
    public Image           background;
    public Image           focusBar;     // gradient sweep (scaled in X on focus)
    public Image           edge;         // thin accent strip at the left edge
    public Image           glow;         // soft halo when focused
    public TextMeshProUGUI label;

    [Header("Behaviour")]
    [TextArea] public string hint = "";
    public bool interactable = true;
    [Tooltip("Primary rows (START etc.) use the filled accent style.")]
    public bool primary;

    [System.NonSerialized] public MenuScreen screen;

    public bool Focused { get; private set; }

    protected float focusT;
    float introT = 1f, introDelay;
    float punch, shake;
    CanvasGroup group;

    public virtual bool Selectable      => interactable && isActiveAndEnabled;
    public virtual bool WantsHorizontal => false;   // steppers/sliders eat left/right
    public virtual bool CapturesInput   => false;   // text entry in progress

    protected virtual void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (content == null) content = (RectTransform)transform;
    }

    public virtual void SetFocused(bool f)
    {
        if (Focused == f) return;
        Focused = f;
        OnFocusChanged(f);
    }

    protected virtual void OnFocusChanged(bool f) { }
    public virtual void Submit() { }
    public virtual void Adjust(int dir) { }
    public virtual void TickInput() { }        // called while CapturesInput

    public void PlayIntro(float delay) { introT = 0f; introDelay = delay; }
    public void Punch() { punch = 1f; }
    public void Shake() { shake = 1f; }

    public void SetInteractable(bool on)
    {
        if (interactable == on) return;
        interactable = on;
        if (!on && screen != null) screen.RefreshFocus();
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    protected virtual void Update()
    {
        float dt = Time.unscaledDeltaTime;
        focusT = Mathf.MoveTowards(focusT, Focused ? 1f : 0f, dt * 7f);
        float s = UITheme.EaseOutCubic(focusT);

        if (introDelay > 0f) introDelay -= dt;
        else if (introT < 1f) introT = Mathf.Min(1f, introT + dt / 0.32f);
        float intro = UITheme.EaseOutCubic(introT);

        punch = Mathf.MoveTowards(punch, 0f, dt * 5f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 3.5f);
        float shakeX = Mathf.Sin(shake * 40f) * shake * 12f;

        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);

        Color idleBg  = primary ? UITheme.Alpha(UITheme.AccentDeep, 0.85f) : UITheme.Row;
        Color focusBg = primary ? UITheme.Accent                           : UITheme.RowFocus;
        if (background != null) background.color = Color.Lerp(idleBg, focusBg, s);

        if (focusBar != null)
        {
            focusBar.rectTransform.localScale = new Vector3(Mathf.Max(0.0001f, s), 1f, 1f);
            focusBar.color = UITheme.Alpha(primary ? UITheme.AccentHot : UITheme.Accent, 0.5f * s);
        }
        if (edge != null) edge.color = UITheme.Alpha(primary ? Color.white : UITheme.AccentHot, s);
        if (glow != null) glow.color = UITheme.Alpha(UITheme.Accent, 0.45f * s * pulse);
        if (label != null)
            label.color = !interactable ? UITheme.TextFaint
                        : Color.Lerp(primary ? UITheme.Text : UITheme.TextDim, UITheme.Text, s);

        if (content != null && content != transform)
        {
            content.anchoredPosition = new Vector2((1f - intro) * -70f + s * 10f + shakeX, 0f);
            float k = 1f + punch * 0.045f;
            content.localScale = new Vector3(k, k, 1f);
        }
        if (group != null) group.alpha = intro * (interactable ? 1f : 0.55f);
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────
    public void OnPointerEnter(PointerEventData e)
    {
        if (screen != null && Selectable) screen.PointerFocus(this);
    }

    public virtual void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (screen != null) screen.PointerSubmit(this);
    }
}
