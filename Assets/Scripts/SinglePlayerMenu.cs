using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// SINGLE PLAYER race setup (the MapSelect scene).  Every widget is baked;
/// this wires them:
///   MAP        — built-in circuits, the editor draft and your saved maps; the
///                card on the right draws the circuit (elevation-coloured, with
///                a runner lapping it) + length / climb / width / PB
///   MODE       — Race / Time Trial (race-only rows fold away for TT)
///   WEATHER    — the sky behind the menu previews it live
///   RIVALS / DIFFICULTY / LAPS
///   START      — wipes into the Race scene with a loading card
/// Selections persist for the session so "change setup" returns you here
/// exactly as you left it.
[DisallowMultipleComponent]
public class SinglePlayerMenu : MonoBehaviour
{
    [Header("Rows (baked)")]
    public MenuScreen  screen;
    public MenuStepper mapRow, modeRow, weatherRow, rivalsRow, difficultyRow, lapsRow;
    public MenuButton  startButton;

    [Header("Map card (baked)")]
    public MapPreviewGraphic preview;
    public TextMeshProUGUI   mapTitle, mapTag, mapCounter;
    public TextMeshProUGUI   statLength, statClimb, statWidth, statNodes;
    public TextMeshProUGUI   pbLabel, pbValue, pbPure, modeBlurb;

    [Header("World (baked)")]
    public MenuBackdrop backdrop;
    public PromptBar    prompts;

    public string raceScene = "Race";
    public string menuScene = "MainMenu";

    // Session memory.
    static string _mapKey;
    static int    _mode, _weather = -1, _rivals = 5, _diff = 1, _laps = 3;

    List<MapCatalog.Entry> maps;
    readonly List<string> weatherKeys = new List<string>();

    static readonly string[] ModeNames = { "RACE", "TIME TRIAL", "ZONE RACE" };
    static readonly string[] DiffNames = { "EASY", "MEDIUM", "HARD", "ACE" };

    void Start()
    {
        maps = MapCatalog.All();

        var mapNames = new List<string>();
        int mapIndex = 0;
        for (int i = 0; i < maps.Count; i++)
        {
            mapNames.Add(maps[i].display.ToUpperInvariant());
            if (Key(maps[i]) == _mapKey) mapIndex = i;
        }
        if (_mapKey == null)
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].name == MatchConfig.MapName && maps[i].builtIn == MatchConfig.MapBuiltIn) mapIndex = i;

        mapRow.SetOptions(mapNames, mapIndex);
        modeRow.SetOptions(ModeNames, _mode);

        // Weather: presets, the F8-saved sky if one exists, and Random.
        var weatherNames = new List<string>();
        weatherKeys.Clear();
        foreach (var p in Atmosphere.Presets) { weatherKeys.Add(p.name); weatherNames.Add(p.name.ToUpperInvariant()); }
        if (Atmosphere.HasSavedOverrides) { weatherKeys.Add("Saved"); weatherNames.Add("MY SKY"); }
        weatherKeys.Add("Random"); weatherNames.Add("RANDOM");
        weatherRow.SetOptions(weatherNames, _weather >= 0 ? Mathf.Min(_weather, weatherNames.Count - 1) : 0);

        var rivals = new List<string>();
        for (int i = 0; i <= 7; i++) rivals.Add(i == 0 ? "NONE" : i.ToString());
        rivalsRow.SetOptions(rivals, _rivals);
        difficultyRow.SetOptions(DiffNames, _diff);
        var laps = new List<string>();
        for (int i = 1; i <= 20; i++) laps.Add(i == 1 ? "1 LAP" : $"{i} LAPS");
        lapsRow.SetOptions(laps, _laps - 1);
        lapsRow.wrap = false;
        rivalsRow.wrap = false;

        mapRow.Changed        += _ => { RefreshMap(); Remember(); };
        modeRow.Changed       += _ => { RefreshMode(); RefreshPB(); Remember(); };
        modeRow.hint = "RACE: the classic grid.  TIME TRIAL: you vs the clock.  ZONE RACE: everyone in the drift zone — pass only when the car ahead crashes.";
        weatherRow.Changed    += _ => { PreviewWeather(); Remember(); };
        rivalsRow.Changed     += _ => { RefreshMode(); Remember(); };
        difficultyRow.Changed += _ => Remember();
        lapsRow.Changed       += _ => Remember();
        startButton.Clicked   += StartRace;
        screen.Backed         += () => SceneFader.Load(menuScene);

        prompts?.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Adjust, "CHANGE"),
                     (MenuAction.Confirm, "START"), (MenuAction.Back, "BACK"));

        RefreshMap();
        RefreshMode();
        PreviewWeather();
    }

    static string Key(MapCatalog.Entry e) => (e.builtIn ? "B:" : "U:") + e.name;

    void Remember()
    {
        if (maps.Count > 0) _mapKey = Key(maps[mapRow.Index]);
        _mode    = modeRow.Index;
        _weather = weatherRow.Index;
        _rivals  = rivalsRow.Index;
        _diff    = difficultyRow.Index;
        _laps    = lapsRow.Index + 1;
    }

    bool TimeTrial => modeRow.Index == (int)MatchConfig.Mode.TimeTrial;
    bool Zone      => modeRow.Index == (int)MatchConfig.Mode.ZoneRace;

    // ── Card ──────────────────────────────────────────────────────────────────
    void RefreshMap()
    {
        if (maps.Count == 0)
        {
            if (mapTitle != null) mapTitle.text = "NO MAPS";
            return;
        }
        var e = maps[mapRow.Index];
        var st = TrackStats.For(e);
        if (preview != null) preview.SetTrack(st);
        if (mapTitle != null)   mapTitle.text   = e.display.ToUpperInvariant();
        if (mapTag != null)     mapTag.text     = e.builtIn ? "BUILT-IN CIRCUIT"
                                                 : e.name == TrackData.ScratchName ? "EDITOR DRAFT" : "CUSTOM CIRCUIT";
        if (mapCounter != null) mapCounter.text = $"{mapRow.Index + 1:00} / {maps.Count:00}";
        if (statLength != null) statLength.text = st != null ? st.LengthText : "—";
        if (statClimb != null)  statClimb.text  = st != null ? st.ClimbText  : "—";
        if (statWidth != null)  statWidth.text  = st != null ? st.WidthText  : "—";
        if (statNodes != null)  statNodes.text  = st != null ? st.nodes.ToString() : "—";
        RefreshPB();
    }

    void RefreshPB()
    {
        if (maps.Count == 0 || pbValue == null) return;
        var e = maps[mapRow.Index];
        TrackRecords.Validate(e);                 // edited since? its old records are gone
        if (pbPure != null) pbPure.text = "";
        if (Zone)
        {
            int bestPlace = TrackRecords.BestFinish(e.name);
            if (pbLabel != null) pbLabel.text = "BEST FINISH";
            pbValue.text  = bestPlace > 0 ? UITheme.Ordinal(bestPlace) : "NO FINISH YET";
            pbValue.color = bestPlace > 0 ? UITheme.PlaceColor(bestPlace) == UITheme.TextDim ? UITheme.Text : UITheme.PlaceColor(bestPlace)
                                          : UITheme.TextFaint;
            return;
        }
        string mode = TimeTrial ? TrackRecords.TimeTrial : TrackRecords.Race;
        float best = TrackRecords.BestLap(mode, e.name, false);
        float pure = TrackRecords.BestLap(mode, e.name, true);
        if (pbLabel != null) pbLabel.text = TimeTrial ? "TIME TRIAL BEST" : "BEST LAP";
        bool has = best < float.PositiveInfinity;
        pbValue.text  = has ? RaceManager.FormatTime(best) : "NO TIME SET";
        pbValue.color = has ? UITheme.Gold : UITheme.TextFaint;
        if (pbPure != null)
            pbPure.text = pure < float.PositiveInfinity
                ? $"<color=#8C88A8>PURE LAP · NO SPATIAL DRIFT</color>   {RaceManager.FormatTime(pure)}"
                : has ? "<color=#8C88A8>PURE LAP · NO SPATIAL DRIFT</color>   —" : "";
    }

    void RefreshMode()
    {
        bool tt = TimeTrial;
        rivalsRow.gameObject.SetActive(!tt);
        difficultyRow.gameObject.SetActive(!tt && rivalsRow.Index > 0);
        lapsRow.gameObject.SetActive(!tt);
        screen.RefreshFocus();
        startButton.SetLabel(tt ? "START TIME TRIAL" : Zone ? "START ZONE RACE" : "START RACE");
        if (modeBlurb != null)
            modeBlurb.text = tt
                ? "One flying lap against the clock.  The timer starts when you move — reset any time with R / B and chase your ghost."
                : Zone
                ? "Everyone in the drift zone, single file.  Pass only when the car ahead crashes — your crashes let the car behind close in.  Gold gates erase one."
                : "A grid race against named AI rivals.  Finish order, lap times and your best lap are all recorded.";
    }

    void PreviewWeather()
    {
        if (backdrop == null) return;
        string k = weatherKeys[Mathf.Clamp(weatherRow.Index, 0, weatherKeys.Count - 1)];
        if (k == "Random") backdrop.PreviewCycle();
        else if (k == "Saved") backdrop.Preview(Atmosphere.SavedOrDefault());
        else foreach (var p in Atmosphere.Presets) if (p.name == k) { backdrop.Preview(p); break; }
    }

    // ── Go ────────────────────────────────────────────────────────────────────
    void StartRace()
    {
        if (maps.Count == 0) { MenuAudio.Play(Sfx.Error); return; }
        Remember();
        var e = maps[mapRow.Index];
        MatchConfig.SelectMap(e);
        MatchConfig.GameMode      = (MatchConfig.Mode)modeRow.Index;
        MatchConfig.WeatherPreset = -1;

        string k = weatherKeys[weatherRow.Index];
        if      (k == "Random") Atmosphere.PickRandom();
        else if (k == "Saved")  Atmosphere.ReloadSaved();
        else foreach (var p in Atmosphere.Presets) if (p.name == k) { Atmosphere.Current = p; break; }

        MatchConfig.AIDifficulties = new List<MatchConfig.Difficulty>();
        if (!TimeTrial)
        {
            for (int i = 0; i < rivalsRow.Index; i++)
                MatchConfig.AIDifficulties.Add((MatchConfig.Difficulty)difficultyRow.Index);
            MatchConfig.Laps = lapsRow.Index + 1;
        }
        else MatchConfig.Laps = 1;

        string weather = Atmosphere.Current.name.ToUpperInvariant();
        string sub = TimeTrial
            ? $"TIME TRIAL  ·  {weather}"
            : $"{(Zone ? "ZONE RACE" : "RACE")}  ·  {MatchConfig.Laps} {(MatchConfig.Laps == 1 ? "LAP" : "LAPS")}  ·  " +
              $"{(rivalsRow.Index == 0 ? "SOLO" : rivalsRow.Index + " RIVALS")}  ·  {weather}";
        SceneFader.Load(raceScene, e.display.ToUpperInvariant(), sub);
    }
}
