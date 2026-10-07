using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// One baked driver slot in the lobby's player list.
[System.Serializable]
public class LobbySlot
{
    public GameObject      root;
    public Image           paint;
    public TextMeshProUGUI name, tag;
}

/// The MULTIPLAYER screen (Lobby scene) — the baked UI over LobbyController.
///   Connect page: driver name, paint, HOST, 6-slot JOIN CODE (keyboard or pad),
///                 JOIN, BACK — plus a live online-services status line
///   Room page:    everyone sees the host's MODE / MAP / LAPS / WEATHER / AI
///                 live (only the host can change them), the driver list with
///                 paints, the join code (X copies it) and a map preview.
[DisallowMultipleComponent]
public class LobbyMenu : MonoBehaviour
{
    [Header("Connect page (baked)")]
    public MenuScreen    connectScreen;
    public MenuTextField nameField;
    public MenuStepper   paintRow;
    public MenuButton    hostButton;
    public MenuCodeField codeField;
    public MenuButton    joinButton;
    public MenuButton    backButton;
    public CanvasGroup   connectCards;

    [Header("Room page (baked)")]
    public MenuScreen    roomScreen;
    public MenuStepper   modeRow, mapRow, lapsRow, weatherRow, aiCountRow;
    public MenuStepper[] aiRows;
    public MenuStepper   roomPaintRow;
    public MenuButton    startButton, leaveButton;
    public CanvasGroup   roomCards;

    [Header("Cards (baked)")]
    public TextMeshProUGUI   codeText;
    public TextMeshProUGUI   playersTitle, aiSummary;
    public LobbySlot[]       slots;
    public MapPreviewGraphic preview;
    public TextMeshProUGUI   mapTitle, mapStats;

    [Header("Status (baked)")]
    public TextMeshProUGUI statusText;
    public Image           statusDot;
    public RectTransform   spinner;

    [Header("Chrome (baked)")]
    public MenuModal modal;
    public PromptBar prompts;

    static readonly string[] ModeNames = { "RACE", "TIME TRIAL", "ZONE RACE" };
    static readonly string[] DiffNames = { "EASY", "MEDIUM", "HARD", "ACE" };

    LobbyController lobby;
    List<MapCatalog.Entry> hostMaps;
    bool inRoom;
    bool pushing;           // guard: refresh caused by our own push
    string previewKey;
    float connectFade = 1f, roomFade;

    void Start()
    {
        lobby = LobbyController.Instance != null ? LobbyController.Instance
                                                 : FindAnyObjectByType<LobbyController>();
        hostMaps = MapCatalog.All();

        // ── Connect page ─────────────────────────────────────────────────────
        nameField.SetValue(PlayerProfile.Name);
        nameField.Committed += n => lobby?.SetLocalProfile(n, PlayerProfile.ColorIndex);

        SetupPaint(paintRow);
        SetupPaint(roomPaintRow);

        hostButton.Clicked += () => lobby?.Host();
        joinButton.Clicked += () => lobby?.Join(codeField.Code);
        codeField.Submitted += c => lobby?.Join(c);
        backButton.Clicked += () => lobby?.LeaveToMainMenu();
        connectScreen.Backed += () => lobby?.LeaveToMainMenu();

        // ── Room page ────────────────────────────────────────────────────────
        modeRow.SetOptions(ModeNames, 0);
        var mapNames = new List<string>();
        foreach (var m in hostMaps) mapNames.Add(m.display.ToUpperInvariant());
        mapRow.SetOptions(mapNames, 0);
        var laps = new List<string>();
        for (int i = 1; i <= 20; i++) laps.Add(i == 1 ? "1 LAP" : $"{i} LAPS");
        lapsRow.SetOptions(laps, 2);
        lapsRow.wrap = false;
        weatherRow.SetOptions(LobbyController.WeatherNames, 0);
        var counts = new List<string>();
        for (int i = 0; i <= 7; i++) counts.Add(i == 0 ? "NONE" : i.ToString());
        aiCountRow.SetOptions(counts, 0);
        aiCountRow.wrap = false;
        for (int i = 0; i < aiRows.Length; i++)
        {
            aiRows[i].SetOptions(DiffNames, 1);
            if (aiRows[i].label != null) aiRows[i].label.text = $"RIVAL {i + 1}  ·  {PlayerProfile.AIName(i)}";
            aiRows[i].Changed += _ => PushSettings();
        }

        modeRow.Changed    += _ => PushSettings();
        mapRow.Changed     += _ => PushSettings();
        lapsRow.Changed    += _ => PushSettings();
        weatherRow.Changed += _ => PushSettings();
        aiCountRow.Changed += _ => PushSettings();

        startButton.Clicked += () => lobby?.StartRace();
        leaveButton.Clicked += AskLeave;
        roomScreen.Backed   += AskLeave;
        roomScreen.Alt1     += CopyCode;

        if (lobby != null) lobby.Changed += Refresh;
        else SetStatus("No NetworkManager in this scene — rebuild it with Multiplayer ▸ Build Full Multiplayer.", true, false);

        connectScreen.Show();
        Refresh();
    }

    void OnDestroy()
    {
        if (lobby != null) lobby.Changed -= Refresh;
    }

    void SetupPaint(MenuStepper row)
    {
        if (row == null) return;
        row.SetOptions(PlayerProfile.PaintNames, PlayerProfile.ColorIndex);
        row.SetSwatchColors(PlayerProfile.PlayerPalette);
        row.Changed += i =>
        {
            if (lobby != null) lobby.SetLocalProfile(PlayerProfile.Name, i);
            else PlayerProfile.ColorIndex = i;
            if (paintRow != row)     paintRow.SetIndex(i);
            if (roomPaintRow != row) roomPaintRow.SetIndex(i);
        };
    }

    // ── Actions ───────────────────────────────────────────────────────────────
    void PushSettings()
    {
        if (pushing || lobby == null || !lobby.IsHost) return;
        var s = new LobbySettings
        {
            mode    = modeRow.Index,
            laps    = lapsRow.Index + 1,
            weather = weatherRow.Index,
        };
        if (hostMaps.Count > 0)
        {
            var e = hostMaps[mapRow.Index];
            s.mapName = e.name; s.mapBuiltIn = e.builtIn; s.mapDisplay = e.display;
        }
        int n = aiCountRow.Index;
        var old = lobby.Lobby.settings.ai;
        for (int i = 0; i < n; i++)
        {
            if (i >= old.Count) { s.ai.Add((int)MatchConfig.Difficulty.Medium); continue; }
            var row = i < aiRows.Length ? aiRows[i] : null;
            s.ai.Add(row != null && row.gameObject.activeSelf ? row.Index : old[i]);
        }
        lobby.SetSettings(s);
    }

    void AskLeave()
    {
        if (modal == null) { lobby?.Leave(); return; }
        bool host = lobby != null && lobby.IsHost;
        modal.Ask(host ? "CLOSE LOBBY?" : "LEAVE LOBBY?",
                  host ? "Everyone in the lobby will be disconnected." : "You can rejoin with the same code.",
                  host ? "CLOSE" : "LEAVE", "STAY",
                  () => lobby?.Leave());
    }

    void CopyCode()
    {
        if (lobby == null || string.IsNullOrEmpty(lobby.Lobby.code)) return;
        GUIUtility.systemCopyBuffer = lobby.Lobby.code;
        MenuAudio.Play(Sfx.Toggle);
        roomScreen.FlashHint($"Join code {lobby.Lobby.code} copied to the clipboard.");
    }

    // ── Refresh from the session ─────────────────────────────────────────────
    void Refresh()
    {
        if (lobby == null) return;
        bool room = lobby.State == LobbyController.Phase.Hosting || lobby.State == LobbyController.Phase.Joined;
        if (room != inRoom)
        {
            inRoom = room;
            if (room) { connectScreen.Hide(); roomScreen.Show(); MenuAudio.Play(Sfx.Open); }
            else      { roomScreen.Hide(); connectScreen.Show(); }
        }

        bool busy = lobby.Busy || lobby.State == LobbyController.Phase.Connecting;
        SetStatus(lobby.Status, lobby.StatusIsError, busy);

        bool canConnect = lobby.ServicesReady && !busy && lobby.State == LobbyController.Phase.Offline;
        hostButton.SetInteractable(canConnect);
        joinButton.SetInteractable(canConnect);
        codeField.SetInteractable(canConnect);

        if (room) RefreshRoom();
        UpdatePrompts();
    }

    void RefreshRoom()
    {
        var st   = lobby.Lobby;
        var s    = st.settings;
        bool host = lobby.IsHost;
        bool tt   = s.mode == (int)MatchConfig.Mode.TimeTrial;

        pushing = true;
        modeRow.SetIndex(Mathf.Clamp(s.mode, 0, ModeNames.Length - 1));
        lapsRow.SetIndex(Mathf.Clamp(s.laps - 1, 0, 19));
        weatherRow.SetIndex(Mathf.Clamp(s.weather, 0, LobbyController.WeatherNames.Length - 1));
        aiCountRow.SetIndex(Mathf.Clamp(s.ai.Count, 0, 7));

        if (host)
        {
            int mi = hostMaps.FindIndex(m => m.name == s.mapName && m.builtIn == s.mapBuiltIn);
            if (mapRow.Count != hostMaps.Count)
            {
                var names = new List<string>();
                foreach (var m in hostMaps) names.Add(m.display.ToUpperInvariant());
                mapRow.SetOptions(names, Mathf.Max(0, mi));
            }
            else mapRow.SetIndex(Mathf.Max(0, mi));
        }
        else mapRow.SetOptions(new[] { (s.mapDisplay ?? s.mapName ?? "").ToUpperInvariant() }, 0);

        for (int i = 0; i < aiRows.Length; i++)
        {
            bool on = !tt && i < s.ai.Count;
            aiRows[i].gameObject.SetActive(on);
            if (on) aiRows[i].SetIndex(Mathf.Clamp(s.ai[i], 0, 3));
            aiRows[i].SetInteractable(host);
        }
        pushing = false;

        foreach (var r in new[] { modeRow, mapRow, lapsRow, weatherRow, aiCountRow }) r.SetInteractable(host);
        lapsRow.gameObject.SetActive(!tt);
        aiCountRow.gameObject.SetActive(!tt);
        startButton.gameObject.SetActive(host);
        bool zr = s.mode == (int)MatchConfig.Mode.ZoneRace;
        startButton.SetLabel(tt ? "START TIME TRIAL" : zr ? "START ZONE RACE" : "START RACE");
        modeRow.hint = tt ? "Everyone hot-laps the track alone. No contact, no AI."
                     : zr ? "Everyone in the drift zone, single file. Pass only when the car ahead crashes. Gold gates erase debt."
                          : "Wheel-to-wheel race with contact.";
        leaveButton.SetLabel(host ? "CLOSE LOBBY" : "LEAVE LOBBY");
        roomScreen.RefreshFocus();

        // Code + drivers.
        if (codeText != null) codeText.text = string.IsNullOrEmpty(st.code) ? "------" : st.code;
        int n = st.players.Count;
        if (playersTitle != null) playersTitle.text = $"DRIVERS   <color=#B06BFF>{n}</color> / 8";
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.root == null) continue;
            bool on = i < n;
            slot.root.SetActive(true);
            if (on)
            {
                var p = st.players[i];
                slot.paint.color = PlayerProfile.PlayerPalette[Mathf.Clamp(p.color, 0, PlayerProfile.PlayerPalette.Length - 1)];
                slot.name.text   = p.name;
                slot.name.color  = UITheme.Text;
                bool you = lobby.IsLocal(p);
                slot.tag.text    = p.host && you ? "HOST · YOU" : p.host ? "HOST" : you ? "YOU" : "";
                slot.tag.color   = you ? UITheme.Accent : UITheme.TextDim;
            }
            else
            {
                slot.paint.color = UITheme.Alpha(Color.white, 0.06f);
                slot.name.text   = "OPEN SLOT";
                slot.name.color  = UITheme.TextFaint;
                slot.tag.text    = "";
            }
        }
        if (aiSummary != null)
        {
            if (tt || s.ai.Count == 0) aiSummary.text = tt ? "TIME TRIAL  ·  NO AI  ·  NO CONTACT" : "NO AI RIVALS";
            else
            {
                var parts = new List<string>();
                for (int i = 0; i < s.ai.Count; i++) parts.Add(DiffNames[Mathf.Clamp(s.ai[i], 0, 3)]);
                aiSummary.text = $"+ {s.ai.Count} AI RIVAL{(s.ai.Count == 1 ? "" : "S")}  ·  {string.Join(", ", parts)}";
            }
        }

        // Map card.
        string key = (s.mapBuiltIn ? "B:" : "U:") + s.mapName + ":" + (s.mapJson?.Length ?? 0);
        if (key != previewKey)
        {
            previewKey = key;
            TrackStats stats = null;
            if (!string.IsNullOrEmpty(s.mapJson))
            {
                try { stats = TrackStats.Compute(JsonUtility.FromJson<TrackData>(s.mapJson)); } catch { }
            }
            if (stats == null && host && hostMaps.Count > 0) stats = TrackStats.For(hostMaps[mapRow.Index]);
            if (preview != null) preview.SetTrack(stats);
            if (mapTitle != null) mapTitle.text = (s.mapDisplay ?? s.mapName ?? "").ToUpperInvariant();
            if (mapStats != null)
                mapStats.text = stats != null
                    ? $"{stats.LengthText}   ·   {stats.ClimbText} CLIMB   ·   {stats.WidthText} WIDE"
                    : "";
        }
    }

    void UpdatePrompts()
    {
        if (prompts == null) return;
        if (!inRoom)
            prompts.Set((MenuAction.Navigate, "MOVE"), (MenuAction.Confirm, "SELECT"), (MenuAction.Back, "BACK"));
        else if (lobby != null && lobby.IsHost)
            prompts.Set((MenuAction.Adjust, "CHANGE"), (MenuAction.Confirm, "SELECT"),
                        (MenuAction.Alt1, "COPY CODE"), (MenuAction.Back, "CLOSE LOBBY"));
        else
            prompts.Set((MenuAction.Alt1, "COPY CODE"), (MenuAction.Back, "LEAVE"));
    }

    void SetStatus(string text, bool error, bool busy)
    {
        if (statusText != null) statusText.text = text;
        if (statusDot != null)
            statusDot.color = error ? UITheme.Bad : busy ? UITheme.Gold : UITheme.Good;
        if (spinner != null) spinner.gameObject.SetActive(busy);
    }

    void Update()
    {
        if (spinner != null && spinner.gameObject.activeSelf)
            spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 320f);

        float dt = Time.unscaledDeltaTime;
        connectFade = Mathf.MoveTowards(connectFade, inRoom ? 0f : 1f, dt / 0.25f);
        roomFade    = Mathf.MoveTowards(roomFade,    inRoom ? 1f : 0f, dt / 0.25f);
        if (connectCards != null) { connectCards.alpha = connectFade; connectCards.blocksRaycasts = !inRoom; }
        if (roomCards != null)    { roomCards.alpha    = roomFade;    roomCards.blocksRaycasts    = inRoom; }
    }
}
