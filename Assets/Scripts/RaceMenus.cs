using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// The solo race scene's menus (also used by the editor's test drive), baked
/// into Race.unity / MapTest.unity:
///   PAUSE   (Esc / Start) — time + game audio freeze; RESUME, RESTART,
///           SETTINGS, CHANGE SETUP, QUIT — confirm dialogs where it matters
///   RESULTS (race finish)  — placing slams in, the standings roll in row by
///           row (named rivals, paints, total + best lap), your lap splits
///           with the best lap in gold and a NEW PERSONAL BEST callout;
///           RACE AGAIN / CHANGE SETUP / MAIN MENU
/// Time Trial keeps its instant-restart flow (no results screen).
[DisallowMultipleComponent]
public class RaceMenus : MonoBehaviour
{
    [Header("Pause (baked)")]
    public CanvasGroup     pauseRoot;
    public MenuScreen      pauseScreen;
    public MenuButton      resumeButton, restartButton, settingsButton, setupButton, quitButton;
    public TextMeshProUGUI pauseTitle, pauseSub;
    public PauseSettingsPanel settings;

    [Header("Results (baked)")]
    public CanvasGroup     resultsRoot;
    public MenuScreen      resultsScreen;
    public MenuButton      againButton, resultsSetupButton, resultsMenuButton;
    public TextMeshProUGUI placeText, placeSub, lapsText, totalText, bestText, pbBadge;
    public RectTransform   placeBlock;
    public ResultRow[]     rows;

    [Header("Chrome (baked)")]
    public MenuModal modal;
    public PromptBar prompts;

    public string raceScene  = "Race";
    public string setupScene = "MapSelect";
    public string menuScene  = "MainMenu";

    enum Context { Race, TimeTrial, MapTest, ZoneRace }
    Context ctx;
    bool    resolved;

    RaceManager   race;
    MapTestRunner mapTest;
    ZoneRaceSolo  zone;
    bool paused, resultsUp;
    float pauseFade, resultsFade;

    void Awake()
    {
        RacingHUD.ExternalResults = true;
        MenuSceneRoot.EnsureEventSystem();
        MenuAudio.Ensure();
        UITheme.ApplyFont(gameObject);
        if (pauseRoot != null)   { pauseRoot.alpha = 0f;   pauseRoot.blocksRaycasts = false; }
        if (resultsRoot != null) { resultsRoot.alpha = 0f; resultsRoot.blocksRaycasts = false; }
    }

    void OnDestroy()
    {
        RacingHUD.ExternalResults = false;
        if (paused) GamePause.Clear();
    }

    void Start()
    {
        resumeButton.Clicked   += Resume;
        restartButton.Clicked  += Restart;
        settingsButton.Clicked += OpenSettings;
        setupButton.Clicked    += () => AskLeave(setupScene);
        quitButton.Clicked     += () => AskLeave(ctx == Context.MapTest ? null : menuScene);
        pauseScreen.Backed     += Resume;

        againButton.Clicked        += () => SceneFader.Load(raceScene, MapTitle(), ModeLine());
        resultsSetupButton.Clicked += () => SceneFader.Load(setupScene);
        resultsMenuButton.Clicked  += () => SceneFader.Load(menuScene);
    }

    // The race is built by RaceBootstrap.Start, so resolve lazily.
    void Resolve()
    {
        if (resolved) return;
        mapTest = FindAnyObjectByType<MapTestRunner>();
        race    = FindAnyObjectByType<RaceManager>();
        zone    = FindAnyObjectByType<ZoneRaceSolo>();
        if (mapTest == null && race == null && zone == null) return;
        resolved = true;

        ctx = mapTest != null ? Context.MapTest : zone != null ? Context.ZoneRace
            : race.TimeTrial ? Context.TimeTrial : Context.Race;
        restartButton.SetLabel(ctx == Context.MapTest ? "RESET CAR" : ctx == Context.TimeTrial ? "RESTART RUN" : "RESTART RACE");
        quitButton.SetLabel(ctx == Context.MapTest ? "BACK TO EDITOR" : "QUIT TO MAIN MENU");
        setupButton.gameObject.SetActive(ctx != Context.MapTest);
        restartButton.hint = ctx == Context.MapTest ? "Put the car back on the start line."
                           : ctx == Context.TimeTrial ? "Back to the line — the clock waits until you move."
                           : "Start this race again from the grid.";

        if (race != null && ctx == Context.Race) race.OnRaceFinish += () => StartCoroutine(ShowResults());
        if (zone != null) zone.RaceFinished += () => StartCoroutine(ShowResults());
    }

    string MapTitle() => ctx == Context.MapTest ? "TEST DRIVE"
                       : (MatchConfig.MapName == TrackData.ScratchName ? "EDITOR DRAFT" : MatchConfig.MapName).ToUpperInvariant();

    string ModeLine()
    {
        string w = Atmosphere.Current.name.ToUpperInvariant();
        return ctx switch
        {
            Context.TimeTrial => $"TIME TRIAL  ·  {w}",
            Context.MapTest   => "MAP EDITOR TEST",
            Context.ZoneRace  => $"ZONE RACE  ·  {MatchConfig.Laps} {(MatchConfig.Laps == 1 ? "LAP" : "LAPS")}  ·  {w}",
            _ => $"RACE  ·  {MatchConfig.Laps} {(MatchConfig.Laps == 1 ? "LAP" : "LAPS")}  ·  {w}",
        };
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        Resolve();
        float dt = Time.unscaledDeltaTime;

        if (!resultsUp && !SceneFader.Busy && resolved)
        {
            bool esc   = Input.GetKeyDown(KeyCode.Escape);
            bool start = Input.GetKeyDown(KeyCode.JoystickButton7);
            if (!paused && (esc || start)) Pause();
            else if (paused && start) Resume();   // Start toggles from anywhere in the pause stack
        }

        pauseFade   = Mathf.MoveTowards(pauseFade, paused ? 1f : 0f, dt / (paused ? 0.16f : 0.12f));
        resultsFade = Mathf.MoveTowards(resultsFade, resultsUp ? 1f : 0f, dt / 0.35f);
        if (pauseRoot != null)   { pauseRoot.alpha = UITheme.EaseOutCubic(pauseFade); pauseRoot.blocksRaycasts = paused; }
        if (resultsRoot != null) { resultsRoot.alpha = resultsFade; resultsRoot.blocksRaycasts = resultsUp; }
    }

    // ── Pause ─────────────────────────────────────────────────────────────────
    void Pause()
    {
        paused = true;
        GamePause.Set(true, freezeTime: true);
        HudVisibility.Set(false);
        if (pauseTitle != null) pauseTitle.text = "PAUSED";
        if (pauseSub != null)   pauseSub.text   = $"{MapTitle()}   ·   {ModeLine()}";
        pauseScreen.Show(0);
        MenuAudio.Play(Sfx.Open);
        prompts?.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Confirm, "SELECT"), (MenuAction.Back, "RESUME"));
    }

    void Resume()
    {
        if (!paused) return;
        if (modal != null && modal.IsOpen) return;
        if (settings != null && settings.IsOpen) settings.Close();
        paused = false;
        pauseScreen.Hide();
        GamePause.Set(false, freezeTime: true);
        MenuAudio.Play(Sfx.Back, 0.7f);
        HudVisibility.Set(true);
        prompts?.Set();
    }

    void Restart()
    {
        switch (ctx)
        {
            case Context.MapTest:
                Resume();
                mapTest.ResetCar();
                break;
            case Context.TimeTrial:
                Resume();
                race.RestartTimeTrial();
                break;
            default:
                if (modal == null) { SceneFader.Load(raceScene, MapTitle(), ModeLine()); break; }
                modal.Ask("RESTART RACE?", "You'll lose your current position.", "RESTART", "CANCEL",
                          () => SceneFader.Load(raceScene, MapTitle(), ModeLine()));
                break;
        }
    }

    void OpenSettings()
    {
        if (settings == null) return;
        settings.Open(() => pauseScreen.Show(pauseScreen.items.IndexOf(settingsButton), intro: false));
    }

    void AskLeave(string scene)
    {
        if (ctx == Context.MapTest && scene == null) { mapTest.BackToEditor(); return; }
        if (modal == null) { SceneFader.Load(scene); return; }
        string what = ctx == Context.TimeTrial ? "run" : "race";
        modal.Ask(scene == setupScene ? "CHANGE SETUP?" : "QUIT TO MAIN MENU?",
                  $"This {what} won't be saved.", "LEAVE", "STAY", () => SceneFader.Load(scene));
    }

    // ── Results ───────────────────────────────────────────────────────────────
    IEnumerator ShowResults()
    {
        // Let the finish-line moment breathe before the screen comes up.
        float t = Time.unscaledTime + 1.3f;
        while (Time.unscaledTime < t) yield return null;
        if (paused) { paused = false; pauseScreen.Hide(); GamePause.Set(false, true); }

        resultsUp = true;
        MenuAudio.Play(Sfx.Start, 0.9f);
        FillResults();
        HudVisibility.Set(false);

        // Placing slams in…
        if (placeBlock != null)
        {
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 0.45f)
            {
                float s = Mathf.Lerp(1.8f, 1f, UITheme.EaseOutBack(e));
                placeBlock.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            placeBlock.localScale = Vector3.one;
        }
        // …then the table rolls in.
        foreach (var r in rows)
        {
            if (r == null || r.root == null || !r.root.activeSelf || r.group == null) continue;
            for (float e = 0f; e < 1f; e += Time.unscaledDeltaTime / 0.12f)
            { r.group.alpha = e; yield return null; }
            r.group.alpha = 1f;
            MenuAudio.Play(Sfx.Move, 0.45f);
        }
        resultsScreen.Show(0);
        prompts?.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Confirm, "SELECT"));
    }

    void FillResults()
    {
        if (ctx == Context.ZoneRace) { FillZoneResults(); return; }
        var entry = race.LocalEntry;
        int place = entry != null ? race.GetLivePosition(entry) : 1;
        bool solo = race.RacerCount <= 1;

        if (placeText != null)
        {
            placeText.text  = solo ? "FINISH" : place == 1 ? "VICTORY" : UITheme.Ordinal(place);
            placeText.color = solo ? UITheme.Text : UITheme.PlaceColor(place);
        }
        if (placeSub != null)
            placeSub.text = solo ? MapTitle() : place == 1 ? $"1ST OF {race.RacerCount}  ·  {MapTitle()}"
                                                           : $"PLACE OF {race.RacerCount}  ·  {MapTitle()}";

        // Standings.
        var entries = new List<RaceManager.RacerEntry>();
        foreach (var car in race.racers)
        {
            var e = race.GetEntry(car);
            if (e != null) entries.Add(e);
        }
        entries.Sort((a, b) => race.GetLivePosition(a).CompareTo(race.GetLivePosition(b)));
        ResultRow.FitCard(rows, entries.Count);

        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (r == null || r.root == null) continue;
            bool on = i < entries.Count;
            r.root.SetActive(on);
            if (r.group != null) r.group.alpha = 0f;
            if (!on) continue;

            var e   = entries[i];
            bool me = e.car == race.localPlayer;
            int aiIndex = AiIndex(e.car);
            int pos = race.GetLivePosition(e);
            r.pos.text   = UITheme.Ordinal(pos);
            r.pos.color  = UITheme.PlaceColor(pos);
            r.name.text  = me ? PlayerProfile.Name : PlayerProfile.AIName(aiIndex);
            r.name.color = me ? UITheme.Text : UITheme.TextDim;
            r.paint.color = me ? PlayerProfile.LocalColor : PlayerProfile.ColorById(PlayerProfile.AIColorId(aiIndex));
            r.time.text  = e.finished ? RaceManager.FormatTime(e.totalTime) : "—";
            r.time.color = e.finished ? UITheme.Text : UITheme.TextFaint;
            r.best.text  = e.bestLap < float.PositiveInfinity ? RaceManager.FormatTime(e.bestLap) : "—";
            if (r.highlight != null) r.highlight.color = me ? UITheme.Alpha(UITheme.Accent, 0.28f) : UITheme.Alpha(Color.white, 0.03f);
        }

        // Your laps.
        if (entry != null)
        {
            float best = float.PositiveInfinity;
            foreach (var l in entry.lapTimes) best = Mathf.Min(best, l);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < entry.lapTimes.Count; i++)
            {
                bool isBest = Mathf.Approximately(entry.lapTimes[i], best);
                sb.Append(isBest ? "<color=#FFCC4D>" : "<color=#A6A1BF>");
                sb.Append($"LAP {i + 1,-3}</color>  ");
                sb.Append(isBest ? $"<color=#FFCC4D>{RaceManager.FormatTime(entry.lapTimes[i])}</color>\n"
                                 : $"{RaceManager.FormatTime(entry.lapTimes[i])}\n");
            }
            float pureLap = race.SessionPureLap;
            sb.Append(pureLap < float.PositiveInfinity
                ? $"\n<color=#4DE6FF>PURE    {RaceManager.FormatTime(pureLap)}</color>  <size=70%><color=#8C88A8>NO SPATIAL DRIFT</color></size>\n"
                : "\n<color=#8C88A8>PURE    —  <size=70%>EVERY LAP USED SPATIAL DRIFT</size></color>\n");
            if (lapsText != null)  lapsText.text  = sb.ToString();
            if (totalText != null) totalText.text = RaceManager.FormatTime(entry.totalTime);
            if (bestText != null)  bestText.text  = best < float.PositiveInfinity ? RaceManager.FormatTime(best) : "—";
            bool pb   = best < race.StartingBestLap;
            bool pure = race.SessionPureLap < race.StartingPureLap;
            if (pbBadge != null)
            {
                pbBadge.gameObject.SetActive(pb || pure);
                pbBadge.text = pb
                    ? (race.StartingBestLap < float.PositiveInfinity
                        ? $"NEW PERSONAL BEST  ·  -{(race.StartingBestLap - best):0.00}s"
                        : "FIRST LAP RECORD SET")
                    : "NEW PURE LAP RECORD";
            }
        }
    }

    void FillZoneResults()
    {
        var res = zone.Results;
        if (res == null) return;
        var me = res.Find(s => s.local);
        int place = me != null ? me.place : 1;

        if (placeText != null)
        {
            placeText.text  = place == 1 ? "VICTORY" : UITheme.Ordinal(place);
            placeText.color = UITheme.PlaceColor(place) == UITheme.TextDim ? UITheme.Text : UITheme.PlaceColor(place);
        }
        if (placeSub != null)
            placeSub.text = $"{(place == 1 ? "1ST" : "PLACE")} OF {res.Count}  ·  {MapTitle()}  ·  ZONE RACE";

        // The table's columns mean something else in a zone race.
        ResultRow.FitCard(rows, res.Count);
        if (rows.Length > 0 && rows[0].root != null)
        {
            var card = rows[0].root.transform.parent;
            SetText(card.Find("H_TIME"), "FINISH");
            SetText(card.Find("H_BEST LAP"), "CRASHES");
        }
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (r == null || r.root == null) continue;
            bool on = i < res.Count;
            r.root.SetActive(on);
            if (r.group != null) r.group.alpha = 0f;
            if (!on) continue;
            var s = res[i];
            r.pos.text    = UITheme.Ordinal(s.place);
            r.pos.color   = UITheme.PlaceColor(s.place);
            r.name.text   = s.name;
            r.name.color  = s.local ? UITheme.Text : UITheme.TextDim;
            r.paint.color = s.color;
            r.time.text   = RaceManager.FormatTime(s.time);
            r.time.color  = UITheme.Text;
            r.best.text   = s.hits.ToString();
            if (r.highlight != null)
                r.highlight.color = s.local ? UITheme.Alpha(UITheme.Accent, 0.28f) : UITheme.Alpha(Color.white, 0.03f);
        }

        if (me != null)
        {
            if (lapsText != null)
                lapsText.text =
                    $"<color=#A6A1BF>CRASHES</color>        {me.hits}\n" +
                    $"<color=#A6A1BF>GOLD GATES</color>     {me.bonuses}\n" +
                    $"<color=#A6A1BF>PASSES MADE</color>    {me.passesMade}\n" +
                    $"<color=#A6A1BF>TIMES PASSED</color>   {me.timesPassed}\n" +
                    $"<color=#A6A1BF>SHIELD SAVES</color>   {me.shieldSaves}";
            if (totalText != null) totalText.text = RaceManager.FormatTime(me.time);
            if (bestText != null)
            {
                int net = me.passesMade - me.timesPassed;
                bestText.text = net > 0 ? $"+{net}" : net.ToString();
                SetText(bestText.transform.parent.Find("Caption"), "NET PASSES");
            }
        }
        if (pbBadge != null)
        {
            pbBadge.gameObject.SetActive(zone.NewBestFinish);
            pbBadge.text = zone.PreviousBestFinish == 0 ? "FIRST ZONE FINISH HERE" : "NEW BEST FINISH";
        }
    }

    static void SetText(Transform t, string s)
    {
        var tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        if (tmp != null) tmp.text = s;
    }

    /// AI cars are built as "AI 1".."AI 7" (RaceBootstrap) → roster index.
    static int AiIndex(CarController car)
    {
        if (car == null) return 0;
        string n = car.gameObject.name;
        int sp = n.LastIndexOf(' ');
        return sp >= 0 && int.TryParse(n.Substring(sp + 1), out int k) ? Mathf.Max(0, k - 1) : 0;
    }
}
