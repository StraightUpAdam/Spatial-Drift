using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// Multiplayer race menus, baked into MP_Race.unity (they replace the old
/// IMGUI overlay):
///   PAUSE    (Esc / Start) — the world keeps racing (it's online), your car
///            coasts.  RESUME, RESTART RUN (time trial), END RACE (host),
///            SETTINGS, LEAVE / END MATCH (with confirms)
///   RESULTS  as soon as YOU finish: live standings (names, paints, times)
///            that fill in as others cross.  When everyone's in, the host
///            gets NEXT RACE setup (map / laps / rivals / difficulty /
///            weather) — nobody leaves the session between races.
[DisallowMultipleComponent]
public class MPRaceMenus : MonoBehaviour
{
    [Header("Pause (baked)")]
    public CanvasGroup     pauseRoot;
    public MenuScreen      pauseScreen;
    public MenuButton      resumeButton, restartRunButton, endRaceButton, settingsButton, leaveButton;
    public TextMeshProUGUI pauseTitle, pauseSub;
    public PauseSettingsPanel settings;

    [Header("Results (baked)")]
    public CanvasGroup     resultsRoot;
    public TextMeshProUGUI placeText, placeSub, waitingText;
    public ResultRow[]     rows;
    public CanvasGroup     nextPanel;
    public MenuScreen      hostScreen;
    public MenuStepper     nextMap, nextLaps, nextRivals, nextDifficulty, nextWeather;
    public MenuButton      nextStart, endMatchButton;
    public MenuScreen      clientScreen;
    public MenuButton      clientLeave;

    [Header("Chrome (baked)")]
    public MenuModal modal;
    public PromptBar prompts;

    static readonly string[] DiffNames = { "EASY", "MEDIUM", "HARD", "ACE" };

    NetworkRaceDirector director;
    bool  paused, resultsUp, nextShown;
    float pauseFade, resultsFade, nextFade;
    float refresh;
    List<MapCatalog.Entry> maps;

    void Awake()
    {
        RacingHUD.ExternalResults = true;
        MenuSceneRoot.EnsureEventSystem();
        MenuAudio.Ensure();
        UITheme.ApplyFont(gameObject);
        if (pauseRoot != null)   { pauseRoot.alpha = 0f;   pauseRoot.blocksRaycasts = false; }
        if (resultsRoot != null) { resultsRoot.alpha = 0f; resultsRoot.blocksRaycasts = false; }
        if (nextPanel != null)   nextPanel.alpha = 0f;
    }

    void OnDestroy()
    {
        RacingHUD.ExternalResults = false;
        if (paused) GamePause.Clear();
    }

    void Start()
    {
        resumeButton.Clicked     += Resume;
        restartRunButton.Clicked += () => { Resume(); director?.LocalTimeTrial?.Restart(); };
        endRaceButton.Clicked    += () => Confirm("END THE RACE?", "Everyone jumps straight to the results.", "END RACE",
                                                  () => { Resume(); director?.ForceFinish(); });
        settingsButton.Clicked   += () => settings?.Open(() => pauseScreen.Show(pauseScreen.items.IndexOf(settingsButton), intro: false));
        leaveButton.Clicked      += AskLeave;
        pauseScreen.Backed       += Resume;

        // Next-race setup (host).
        maps = MapCatalog.All();
        var names = new List<string>();
        int mi = 0;
        for (int i = 0; i < maps.Count; i++)
        {
            names.Add(maps[i].display.ToUpperInvariant());
            if (maps[i].name == MatchConfig.MapName && maps[i].builtIn == MatchConfig.MapBuiltIn) mi = i;
        }
        nextMap.SetOptions(names, mi);
        var laps = new List<string>();
        for (int i = 1; i <= 20; i++) laps.Add(i == 1 ? "1 LAP" : $"{i} LAPS");
        nextLaps.SetOptions(laps, Mathf.Clamp(MatchConfig.Laps - 1, 0, 19));
        nextLaps.wrap = false;
        var rivals = new List<string>();
        for (int i = 0; i <= 7; i++) rivals.Add(i == 0 ? "NONE" : i.ToString());
        nextRivals.SetOptions(rivals, Mathf.Clamp(MatchConfig.AIDifficulties.Count, 0, 7));
        nextRivals.wrap = false;
        nextDifficulty.SetOptions(DiffNames, MatchConfig.AIDifficulties.Count > 0 ? (int)MatchConfig.AIDifficulties[0] : 1);
        nextWeather.SetOptions(LobbyController.WeatherNames,
                               MatchConfig.WeatherPreset >= 0 ? MatchConfig.WeatherPreset : 0);
        nextRivals.Changed += _ => nextDifficulty.SetInteractable(nextRivals.Index > 0);

        nextStart.Clicked      += StartNext;
        endMatchButton.Clicked += () => Confirm("END THE MATCH?", "Everyone goes back to the main menu.", "END MATCH",
                                                () => director?.EndMatch());
        hostScreen.Backed      += () => Confirm("END THE MATCH?", "Everyone goes back to the main menu.", "END MATCH",
                                                () => director?.EndMatch());
        clientLeave.Clicked    += AskLeave;
        clientScreen.Backed    += AskLeave;
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (director == null) director = FindAnyObjectByType<NetworkRaceDirector>();
        float dt = Time.unscaledDeltaTime;

        if (director != null && director.IsSpawned)
        {
            if (!SceneFader.Busy)
            {
                bool esc   = Input.GetKeyDown(KeyCode.Escape);
                bool start = Input.GetKeyDown(KeyCode.JoystickButton7);
                if (!paused && !resultsUp && (esc || start)) Pause();
                else if (paused && start) Resume();
            }

            if (!director.IsTimeTrial)
            {
                var local = LocalSync();
                bool meDone  = local != null && local.FinishPos.Value > 0;
                bool allDone = director.CurrentState == NetworkRaceDirector.RaceState.Finished;
                if (!resultsUp && (meDone || allDone)) OpenResults();
                if (resultsUp && allDone && !nextShown) ShowNext();

                if (resultsUp && (refresh -= dt) <= 0f) { refresh = 0.4f; FillStandings(); }
            }
        }

        pauseFade   = Mathf.MoveTowards(pauseFade,   paused ? 1f : 0f,    dt / (paused ? 0.16f : 0.12f));
        resultsFade = Mathf.MoveTowards(resultsFade, resultsUp ? 1f : 0f, dt / 0.35f);
        nextFade    = Mathf.MoveTowards(nextFade,    nextShown ? 1f : 0f, dt / 0.3f);
        if (pauseRoot != null)   { pauseRoot.alpha = UITheme.EaseOutCubic(pauseFade); pauseRoot.blocksRaycasts = paused; }
        if (resultsRoot != null) { resultsRoot.alpha = resultsFade; resultsRoot.blocksRaycasts = resultsUp; }
        if (nextPanel != null)   { nextPanel.alpha = nextFade; nextPanel.blocksRaycasts = nextShown; }
    }

    static NetworkCarSync LocalSync()
    {
        var car = CarController.LocalPlayerOverride;
        return car != null ? car.GetComponent<NetworkCarSync>() : null;
    }

    // ── Pause ─────────────────────────────────────────────────────────────────
    void Pause()
    {
        paused = true;
        GamePause.Set(true, freezeTime: false);   // online: block input, don't stop time
        HudVisibility.Set(false);

        bool host = director.IsHost, tt = director.IsTimeTrial;
        bool racing = director.CurrentState == NetworkRaceDirector.RaceState.Racing;
        restartRunButton.gameObject.SetActive(tt);
        endRaceButton.gameObject.SetActive(host && !tt && racing);
        leaveButton.SetLabel(host ? (tt ? "END SESSION" : "END MATCH") : "LEAVE MATCH");
        leaveButton.hint = host ? "Close the session — everyone returns to the main menu."
                                : "Leave this session and return to the main menu.";
        if (pauseTitle != null) pauseTitle.text = "PAUSED";
        if (pauseSub != null)
            pauseSub.text = $"{MatchConfig.MapName.ToUpperInvariant()}   ·   ONLINE {(tt ? "TIME TRIAL" : director.IsZoneRace ? "ZONE RACE" : "RACE")}   ·   THE RACE CONTINUES";
        pauseScreen.Show(0);
        MenuAudio.Play(Sfx.Open);
        prompts?.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Confirm, "SELECT"), (MenuAction.Back, "RESUME"));
    }

    void Resume()
    {
        if (!paused || (modal != null && modal.IsOpen)) return;
        if (settings != null && settings.IsOpen) settings.Close();
        paused = false;
        pauseScreen.Hide();
        GamePause.Set(false, freezeTime: false);
        MenuAudio.Play(Sfx.Back, 0.7f);
        if (!resultsUp) HudVisibility.Set(true);
        prompts?.Set();
    }

    void AskLeave()
    {
        bool host = director != null && director.IsHost;
        if (host)
            Confirm("END THE MATCH?", "Everyone goes back to the main menu.", "END MATCH", () => director.EndMatch());
        else
            Confirm("LEAVE THE MATCH?", "You'll return to the main menu.", "LEAVE", () => director?.LeaveToMenu());
    }

    void Confirm(string title, string body, string yes, System.Action action)
    {
        if (modal == null) { action(); return; }
        modal.Ask(title, body, yes, "CANCEL", action);
    }

    // ── Results ───────────────────────────────────────────────────────────────
    void OpenResults()
    {
        if (paused) { paused = false; pauseScreen.Hide(); GamePause.Set(false, false); }
        resultsUp = true;
        MenuAudio.Play(Sfx.Start, 0.9f);
        FillStandings();
        HudVisibility.Set(false);
        if (waitingText != null) waitingText.text = "WAITING FOR THE OTHER DRIVERS…";
        prompts?.Set();
    }

    void ShowNext()
    {
        nextShown = true;
        if (waitingText != null) waitingText.text = director.IsHost ? "" : "WAITING FOR THE HOST TO START THE NEXT RACE…";
        nextDifficulty.SetInteractable(nextRivals.Index > 0);
        if (director.IsHost)
        {
            hostScreen.Show(0);
            prompts?.Set((MenuAction.Adjust, "CHANGE"), (MenuAction.Confirm, "SELECT"), (MenuAction.Back, "END MATCH"));
        }
        else
        {
            clientScreen.Show(0);
            prompts?.Set((MenuAction.Confirm, "SELECT"), (MenuAction.Back, "LEAVE"));
        }
        MenuAudio.Play(Sfx.Open, 0.7f);
    }

    void FillStandings()
    {
        var list = director.Standings();
        ResultRow.FitCard(rows, list.Count);
        int myPos = 0;
        foreach (var s in list) if (s.you) myPos = s.pos;

        if (placeText != null)
        {
            placeText.text  = myPos == 1 ? "VICTORY" : UITheme.Ordinal(myPos);
            placeText.color = UITheme.PlaceColor(myPos);
        }
        if (placeSub != null)
            placeSub.text = $"{(myPos == 1 ? "1ST" : "PLACE")} OF {list.Count}  ·  {MatchConfig.MapName.ToUpperInvariant()}{(director.IsZoneRace ? "  ·  ZONE RACE" : "")}";

        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (r == null || r.root == null) continue;
            bool on = i < list.Count;
            r.root.SetActive(on);
            if (!on) continue;
            var s = list[i];
            if (r.group != null) r.group.alpha = Mathf.MoveTowards(r.group.alpha, 1f, 0.5f);
            r.pos.text    = UITheme.Ordinal(s.pos);
            r.pos.color   = UITheme.PlaceColor(s.pos);
            r.name.text   = s.name;
            r.name.color  = s.you ? UITheme.Text : UITheme.TextDim;
            r.paint.color = PlayerProfile.ColorById(s.colorId);
            r.time.text   = s.finished ? RaceManager.FormatTime(s.time) : $"LAP {Mathf.Max(1, s.lap)}";
            r.time.color  = s.finished ? UITheme.Text : UITheme.TextFaint;
            r.best.text   = s.ai ? "AI" : s.you ? "YOU" : "";
            if (r.highlight != null) r.highlight.color = s.you ? UITheme.Alpha(UITheme.Accent, 0.28f) : UITheme.Alpha(Color.white, 0.03f);
        }
    }

    // ── Next race (host) ──────────────────────────────────────────────────────
    void StartNext()
    {
        if (director == null || !director.IsHost) return;
        var lobby = LobbyController.Instance;
        var e = maps.Count > 0 ? maps[nextMap.Index] : new MapCatalog.Entry { name = MatchConfig.MapName, builtIn = MatchConfig.MapBuiltIn, display = MatchConfig.MapName };
        var s = new LobbySettings
        {
            mode       = (int)(MatchConfig.GameMode == MatchConfig.Mode.ZoneRace ? MatchConfig.Mode.ZoneRace : MatchConfig.Mode.Race),
            mapName    = e.name, mapBuiltIn = e.builtIn, mapDisplay = e.display,
            laps       = nextLaps.Index + 1,
            weather    = nextWeather.Index,
        };
        for (int i = 0; i < nextRivals.Index; i++) s.ai.Add(nextDifficulty.Index);

        if (lobby != null)
        {
            lobby.SetSettings(s);
            lobby.StartRace();
        }
        else
        {
            MatchConfig.SelectMap(e);
            MatchConfig.Laps = s.laps;
            MatchConfig.AIDifficulties = new List<MatchConfig.Difficulty>();
            foreach (int d in s.ai) MatchConfig.AIDifficulties.Add((MatchConfig.Difficulty)d);
            director.StartNextRace();
        }
    }
}
