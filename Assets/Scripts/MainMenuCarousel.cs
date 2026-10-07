using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// The main menu: a Budokai-style rotating wheel over the Forza-style car
/// showcase.  Three states, all baked into MainMenu.unity:
///   Title  — "PRESS ANY BUTTON" card (first visit per session only)
///   Wheel  — items on a vertical arc at the left edge; the selected one sits
///            on a slanted accent bar with a one-line description under it
///   Garage — in-scene page: driver name + paint, the showcase car repaints
///            live and the camera moves in for a close orbit
/// Scene changes go through the SceneFader wipe.
[DisallowMultipleComponent]
public class MainMenuCarousel : MonoBehaviour
{
    public const string GarageTarget = "#garage";

    [Header("Wheel (baked)")]
    public TextMeshProUGUI[] itemTexts;
    [Tooltip("Scene per item; \"\" = quit, \"#garage\" = open the garage page.")]
    public string[] sceneNames;
    [TextArea] public string[] descriptions;
    public CanvasGroup     wheelGroup;
    public RectTransform   selectBar;
    public TextMeshProUGUI descText;

    [Header("Pages (baked)")]
    public CanvasGroup     titlePage;
    public TextMeshProUGUI pressStart;
    public MenuScreen      garageScreen;
    public MenuTextField   garageName;
    public MenuStepper     garagePaint;
    public MenuButton      garageDone;

    [Header("Chrome (baked)")]
    public TextMeshProUGUI profileName;
    public Image           profileSwatch;
    public PromptBar       prompts;
    public CarShowcase     showcase;

    [Header("Wheel shape")]
    public float angleStep  = 0.56f;   // radians between neighbouring items
    public float arcHeight  = 260f;
    public float arcDepth   = 180f;    // how far non-selected items tuck left
    public float baseX      = 80f;
    public float easeRate   = 11f;

    enum State { Title, Wheel, Garage }
    State state;
    static bool _seenTitle;

    int   target;          // unwrapped selection index (can run past the ends)
    float current;         // eased display position
    float titleFade, wheelFade, descFade = 1f;
    int   shownDesc = -1;
    bool  leaving;

    int Count => itemTexts != null ? itemTexts.Length : 0;
    int SelectedIndex => Count == 0 ? 0 : ((target % Count) + Count) % Count;

    void Awake()
    {
        state = _seenTitle ? State.Wheel : State.Title;
        titleFade = state == State.Title ? 1f : 0f;
        if (titlePage != null)  titlePage.alpha = titleFade;
        if (wheelGroup != null) wheelGroup.alpha = 0f;

        for (int i = 0; i < Count; i++)
        {
            if (itemTexts[i] == null) continue;
            itemTexts[i].raycastTarget = true;
            var click = itemTexts[i].gameObject.GetComponent<WheelItemClick>();
            if (click == null) click = itemTexts[i].gameObject.AddComponent<WheelItemClick>();
            click.owner = this;
            click.index = i;
        }
    }

    void Start()
    {
        // Garage wiring.
        if (garagePaint != null)
        {
            garagePaint.SetOptions(PlayerProfile.PaintNames, PlayerProfile.ColorIndex);
            garagePaint.SetSwatchColors(PlayerProfile.PlayerPalette);
            garagePaint.Changed += i =>
            {
                PlayerProfile.ColorIndex = i;
                if (showcase != null) showcase.SetPaint(PlayerProfile.LocalColor);
                RefreshProfileChip();
            };
        }
        if (garageName != null)
        {
            garageName.SetValue(PlayerProfile.Name);
            garageName.Committed += n => { PlayerProfile.Name = n; RefreshProfileChip(); };
        }
        if (garageDone != null)   garageDone.Clicked  += CloseGarage;
        if (garageScreen != null) garageScreen.Backed += CloseGarage;

        RefreshProfileChip();
        SetPrompts();
    }

    void RefreshProfileChip()
    {
        if (profileName != null)   profileName.text    = PlayerProfile.Name;
        if (profileSwatch != null) profileSwatch.color = PlayerProfile.LocalColor;
    }

    void SetPrompts()
    {
        if (prompts == null) return;
        switch (state)
        {
            case State.Title:  prompts.Set(); break;
            case State.Wheel:  prompts.Set((MenuAction.Navigate, "BROWSE"), (MenuAction.Confirm, "SELECT"),
                                           (MenuAction.Alt2, "GARAGE"), (MenuAction.Back, "EXIT")); break;
            case State.Garage: prompts.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Adjust, "CHANGE"),
                                           (MenuAction.Confirm, "EDIT"), (MenuAction.Back, "DONE")); break;
        }
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (Count == 0) return;
        float dt = Time.unscaledDeltaTime;

        switch (state)
        {
            case State.Title: TickTitle(); break;
            case State.Wheel: if (!leaving && !SceneFader.Busy) TickWheel(); break;
            // Garage: its MenuScreen handles input.
        }

        titleFade = Mathf.MoveTowards(titleFade, state == State.Title ? 1f : 0f, dt / 0.35f);
        wheelFade = Mathf.MoveTowards(wheelFade, state == State.Wheel ? 1f : 0f,
                                      dt / (state == State.Wheel ? 0.4f : 0.2f));
        if (titlePage != null)
        {
            titlePage.alpha          = UITheme.EaseOutCubic(titleFade);
            titlePage.blocksRaycasts = state == State.Title;
        }
        if (wheelGroup != null)
        {
            wheelGroup.alpha          = UITheme.EaseOutCubic(wheelFade);
            wheelGroup.blocksRaycasts = state == State.Wheel;
            var rt = (RectTransform)wheelGroup.transform;
            rt.anchoredPosition = new Vector2((1f - UITheme.EaseOutCubic(wheelFade)) * -120f,
                                              rt.anchoredPosition.y);
        }
        if (pressStart != null)
            pressStart.alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f));

        current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-easeRate * dt));
        Layout(dt);
    }

    void TickTitle()
    {
        MenuInput.Poll();
        if (SceneFader.Busy) return;
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0))
        {
            _seenTitle = true;
            MenuAudio.Play(Sfx.Start, 0.8f);
            state = State.Wheel;
            SetPrompts();
        }
    }

    void TickWheel()
    {
        int move = MenuInput.NavY;
        if (MenuInput.TabRight) move = +1;
        if (MenuInput.TabLeft)  move = -1;
        float wheel = Input.mouseScrollDelta.y;
        if (wheel > 0.1f) move = -1; else if (wheel < -0.1f) move = +1;
        if (move != 0) { target += move; MenuAudio.Play(Sfx.Move); }

        if (MenuInput.Confirm) Choose();
        if (MenuInput.Alt2)    OpenGarage();

        // B / Esc: hop straight to EXIT (last item); press again to quit.
        if (MenuInput.Back)
        {
            MenuAudio.Play(Sfx.Back);
            if (SelectedIndex == Count - 1) Choose();
            else target += ((Count - 1 - SelectedIndex + Count) % Count + Count / 2) % Count - Count / 2;
        }
    }

    /// Mouse: click an item — spin to it, or pick it if it's already selected.
    public void Clicked(int index)
    {
        if (state != State.Wheel || leaving || SceneFader.Busy) return;
        if (index == SelectedIndex) { Choose(); return; }
        int delta = ((index - SelectedIndex) % Count + Count) % Count;
        if (delta > Count / 2) delta -= Count;
        target += delta;
        MenuAudio.Play(Sfx.Move);
    }

    void Choose()
    {
        string scene = SelectedIndex < (sceneNames?.Length ?? 0) ? sceneNames[SelectedIndex] : "";
        if (scene == GarageTarget) { OpenGarage(); return; }

        MenuAudio.Play(Sfx.Confirm);
        leaving = true;
        if (string.IsNullOrEmpty(scene)) SceneFader.Quit();
        else if (Application.CanStreamedLevelBeLoaded(scene)) SceneFader.Load(scene);
        else
        {
            Debug.LogError($"[MainMenu] Scene '{scene}' not in Build Settings — run " +
                           "'Racing Game / Build All Scenes' + 'Fix Build Settings'.");
            MenuAudio.Play(Sfx.Error);
            leaving = false;
        }
    }

    // ── Garage ────────────────────────────────────────────────────────────────
    void OpenGarage()
    {
        if (garageScreen == null || state == State.Garage) return;
        state = State.Garage;
        MenuAudio.Play(Sfx.Open);
        if (garageName  != null) garageName.SetValue(PlayerProfile.Name);
        if (garagePaint != null) garagePaint.SetIndex(PlayerProfile.ColorIndex);
        garageScreen.Show();
        if (showcase != null) showcase.SetGarage(true);
        SetPrompts();
    }

    void CloseGarage()
    {
        if (state != State.Garage) return;
        garageScreen.Hide();
        if (showcase != null) showcase.SetGarage(false);
        state = State.Wheel;
        SetPrompts();
    }

    // ── Layout ────────────────────────────────────────────────────────────────
    void Layout(float dt)
    {
        int sel = SelectedIndex;
        for (int i = 0; i < Count; i++)
        {
            var t = itemTexts[i];
            if (t == null) continue;

            // Wrapped offset from the current wheel position → [-n/2, n/2).
            float rel = Mathf.Repeat(i - current + Count * 0.5f, Count) - Count * 0.5f;
            float a   = rel * angleStep;

            float cos = Mathf.Cos(Mathf.Clamp(a, -Mathf.PI, Mathf.PI));
            float k   = Mathf.Clamp01(cos);
            var rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(baseX + (cos - 1f) * arcDepth + (i == sel ? 18f * k : 0f),
                                              -Mathf.Sin(a) * arcHeight);
            rt.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, k * k);

            t.raycastTarget = k > 0.25f;   // faded-out items shouldn't eat clicks
            t.color = i == sel ? Color.white
                               : new Color(0.66f, 0.62f, 0.78f, Mathf.Lerp(0f, 0.7f, k * k));
        }

        // Accent bar hugging the selected label.
        if (selectBar != null && itemTexts[sel] != null)
        {
            float w = itemTexts[sel].preferredWidth + 150f;
            var s = selectBar.sizeDelta;
            selectBar.sizeDelta = new Vector2(Mathf.Lerp(s.x, w, 1f - Mathf.Exp(-16f * dt)), s.y);
        }

        // Description swaps with a quick fade.
        if (descText != null)
        {
            if (shownDesc != sel)
            {
                descFade = Mathf.MoveTowards(descFade, 0f, dt / 0.08f);
                if (descFade <= 0f)
                {
                    shownDesc = sel;
                    descText.text = sel < (descriptions?.Length ?? 0) ? descriptions[sel] : "";
                }
            }
            else descFade = Mathf.MoveTowards(descFade, 1f, dt / 0.18f);
            descText.alpha = descFade;
        }
    }
}

/// Mouse click on a wheel item.
public class WheelItemClick : MonoBehaviour, IPointerClickHandler
{
    public MainMenuCarousel owner;
    public int index;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && owner != null) owner.Clicked(index);
    }
}
