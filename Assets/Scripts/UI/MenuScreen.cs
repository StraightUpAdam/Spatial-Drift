using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// A page of focusable rows.  Screens stack: only the TOP open screen takes
/// input (a modal over a page, a sub-page over its parent).  Handles
///   • focus movement with wrap + hold-repeat, skipping hidden/disabled rows
///   • left/right to the focused row (steppers/sliders)
///   • confirm / back / tabs / alt buttons → events the owner wires up
///   • open/close fades and the staggered row intro
///   • optional scrolling viewport that keeps the focused row in view
///   • the hint line (focused row's description) and its prompt bar
[DisallowMultipleComponent]
public class MenuScreen : MonoBehaviour
{
    [Header("Baked references")]
    public List<MenuRow> items = new List<MenuRow>();
    public CanvasGroup     group;
    public TextMeshProUGUI hintText;
    public PromptBar       prompts;
    [Tooltip("Optional scrolling: the masked viewport and the content it moves.")]
    public RectTransform   viewport, scrollContent;

    [Header("Behaviour")]
    public bool  showOnStart   = true;
    public bool  wrapFocus     = true;
    public float introStagger  = 0.035f;
    [Tooltip("When false the screen stays visible but ignores input (grid editors etc.).")]
    [SerializeField] bool _inputEnabled = true;
    public bool inputEnabled
    {
        get => _inputEnabled;
        set
        {
            if (_inputEnabled == value) return;
            _inputEnabled = value;
            // A screen that isn't taking input shouldn't show a focused row.
            if (Focused != null) Focused.SetFocused(value && IsOpen);
        }
    }

    /// Mouse touched this screen while it wasn't taking input — owners can
    /// hand it focus (the Shifter Lab switches zones this way).
    public event System.Action InputRequested;

    public event System.Action Backed, TabLeft, TabRight, Alt1, Alt2, StartPressed;
    public event System.Action<MenuRow> FocusChanged;

    public bool IsOpen { get; private set; }
    public MenuRow Focused => focus >= 0 && focus < items.Count ? items[focus] : null;

    static readonly List<MenuScreen> _open = new List<MenuScreen>();
    public static MenuScreen Top => _open.Count > 0 ? _open[_open.Count - 1] : null;

    int   focus = -1;
    int   openedFrame = -1;
    float fade;
    string flash; float flashUntil;
    float scrollY;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        foreach (var it in items) if (it != null) it.screen = this;
        if (!showOnStart) ApplyHidden();
    }

    void Start()
    {
        if (showOnStart && !IsOpen) Show();
    }

    void OnDestroy()
    {
        _open.Remove(this);
        if (IsOpen) Cursor.visible = true;
    }

    // ── Open / close ─────────────────────────────────────────────────────────
    public void Show(int focusIndex = -1, bool intro = true)
    {
        _open.Remove(this);
        _open.Add(this);
        IsOpen = true;
        openedFrame = Time.frameCount;
        gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.interactable   = true;

        foreach (var it in items) if (it != null) it.screen = this;

        if (intro)
        {
            int k = 0;
            foreach (var it in items)
                if (it != null && it.gameObject.activeInHierarchy) it.PlayIntro(k++ * introStagger);
        }

        int want = focusIndex >= 0 ? focusIndex : (focus >= 0 ? focus : 0);
        focus = -1;
        FocusIndex(FirstSelectableFrom(want, +1), silent: true);
        UpdateHint();
    }

    public void Hide()
    {
        _open.Remove(this);
        IsOpen = false;
        group.blocksRaycasts = false;
        group.interactable   = false;
        if (Focused != null) Focused.SetFocused(false);
    }

    void ApplyHidden()
    {
        IsOpen = false;
        fade = 0f;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable   = false;
    }

    // ── Items ─────────────────────────────────────────────────────────────────
    public void SetItems(IList<MenuRow> list)
    {
        if (Focused != null) Focused.SetFocused(false);
        items.Clear();
        items.AddRange(list);
        foreach (var it in items) if (it != null) it.screen = this;
        focus = -1;
        if (IsOpen) FocusIndex(FirstSelectableFrom(0, +1), true);
    }

    public void Focus(MenuRow item, bool silent = false)
    {
        int i = items.IndexOf(item);
        if (i >= 0) FocusIndex(i, silent);
    }

    public void FocusIndex(int i, bool silent = false)
    {
        if (i < 0 || i >= items.Count) return;
        if (i == focus && items[i] != null && items[i].Focused) return;
        if (Focused != null) Focused.SetFocused(false);
        focus = i;
        items[i].SetFocused(_inputEnabled);
        if (!silent) MenuAudio.Play(Sfx.Move);
        UpdateHint();
        FocusChanged?.Invoke(items[i]);
    }

    /// Re-home focus if the focused row became hidden / disabled.
    public void RefreshFocus()
    {
        var f = Focused;
        if (f != null && f.Selectable) return;
        int i = FirstSelectableFrom(Mathf.Max(0, focus), +1);
        if (i >= 0) FocusIndex(i, true);
        else if (f != null) { f.SetFocused(false); focus = -1; }
    }

    int FirstSelectableFrom(int start, int dir)
    {
        int n = items.Count;
        if (n == 0) return -1;
        start = Mathf.Clamp(start, 0, n - 1);
        for (int k = 0; k < n; k++)
        {
            int i = ((start + dir * k) % n + n) % n;
            if (items[i] != null && items[i].Selectable) return i;
        }
        return -1;
    }

    void Move(int dir)
    {
        int n = items.Count;
        if (n == 0) return;
        int i = focus < 0 ? 0 : focus;
        for (int k = 0; k < n; k++)
        {
            i += dir;
            if (i < 0 || i >= n)
            {
                if (!wrapFocus) return;
                i = (i + n) % n;
            }
            if (items[i] != null && items[i].Selectable) { FocusIndex(i); return; }
        }
    }

    // ── Pointer hooks (from MenuRow) ─────────────────────────────────────────
    public void PointerFocus(MenuRow it)
    {
        if (!inputEnabled && Top == this) InputRequested?.Invoke();
        if (Top != this || !inputEnabled || SceneFader.Busy) return;
        if (Focused != it) Focus(it, silent: false);
    }

    public void PointerSubmit(MenuRow it)
    {
        if (!inputEnabled && Top == this) InputRequested?.Invoke();
        if (Top != this || !inputEnabled || SceneFader.Busy) return;
        if (!it.Selectable) { it.Shake(); MenuAudio.Play(Sfx.Error); return; }
        Focus(it, true);
        it.Submit();
    }

    // ── Hint line ─────────────────────────────────────────────────────────────
    public void FlashHint(string text, float seconds = 2.2f)
    {
        flash = text;
        flashUntil = Time.unscaledTime + seconds;
        UpdateHint();
    }

    void UpdateHint()
    {
        if (hintText == null) return;
        if (!string.IsNullOrEmpty(flash) && Time.unscaledTime < flashUntil)
        { hintText.text = $"<color=#FFCC4D>{flash}</color>"; return; }
        var f = Focused;
        hintText.text = f != null ? f.hint : "";
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        fade = Mathf.MoveTowards(fade, IsOpen ? 1f : 0f, dt / (IsOpen ? 0.18f : 0.14f));
        group.alpha = UITheme.EaseOutCubic(fade);
        if (!IsOpen && fade <= 0f && group.blocksRaycasts) group.blocksRaycasts = false;

        if (flash != null && Time.unscaledTime >= flashUntil) { flash = null; UpdateHint(); }

        UpdateScroll(dt);

        if (!IsOpen || Top != this || !inputEnabled) return;
        if (Time.frameCount == openedFrame || SceneFader.Busy) return;

        // Hide the pointer while a controller drives the menus.
        bool pad = MenuInput.UsingPad;
        if (Cursor.visible == pad) Cursor.visible = !pad;

        var f = Focused;
        if (f != null && f.CapturesInput) { f.TickInput(); return; }
        if (f != null && !f.Selectable) RefreshFocus();

        int y = MenuInput.NavY;
        if (y != 0) Move(y);

        int x = MenuInput.NavX;
        if (x != 0 && Focused != null && Focused.WantsHorizontal) Focused.Adjust(x);

        // Each press is handled once: consume it so a screen revealed by this
        // action (e.g. the one under a dialog we just closed) can't reuse it.
        if (MenuInput.Confirm && Focused != null && Focused.Selectable) { MenuInput.Consume(); Focused.Submit(); return; }
        if (MenuInput.Back && Backed != null) { MenuInput.Consume(); MenuAudio.Play(Sfx.Back); Backed.Invoke(); return; }
        if (MenuInput.TabLeft  && TabLeft      != null) { MenuInput.Consume(); TabLeft.Invoke();      return; }
        if (MenuInput.TabRight && TabRight     != null) { MenuInput.Consume(); TabRight.Invoke();     return; }
        if (MenuInput.Alt1     && Alt1         != null) { MenuInput.Consume(); Alt1.Invoke();         return; }
        if (MenuInput.Alt2     && Alt2         != null) { MenuInput.Consume(); Alt2.Invoke();         return; }
        if (MenuInput.Start    && StartPressed != null) { MenuInput.Consume(); StartPressed.Invoke(); return; }
    }

    void UpdateScroll(float dt)
    {
        if (viewport == null || scrollContent == null) return;
        var f = Focused;
        float viewH = viewport.rect.height;
        float contentH = scrollContent.rect.height;
        float target = scrollY;
        if (f != null)
        {
            // Row position relative to the content's top edge (content pivot top).
            var rt = (RectTransform)f.transform;
            Vector3 local = scrollContent.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            float top = -(local.y + scrollContent.rect.yMax) - rt.rect.height * 0.5f;   // distance below content top
            float bottom = top + rt.rect.height;
            const float pad = 24f;
            if (top - pad < target) target = top - pad;
            if (bottom + pad > target + viewH) target = bottom + pad - viewH;
        }
        target = Mathf.Clamp(target, 0f, Mathf.Max(0f, contentH - viewH));
        scrollY = Mathf.Lerp(scrollY, target, 1f - Mathf.Exp(-14f * dt));
        scrollContent.anchoredPosition = new Vector2(scrollContent.anchoredPosition.x, scrollY);
    }
}
