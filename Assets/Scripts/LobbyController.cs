using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// One driver in the lobby (synced to every client).
[Serializable]
public class LobbyPlayer
{
    public long   id;
    public string name  = "DRIVER";
    public int    color;
    public bool   host;
}

/// The host's race settings (synced so clients see them live).
[Serializable]
public class LobbySettings
{
    public int    mode;                 // MatchConfig.Mode
    public string mapName = "Speed Circuit";
    public bool   mapBuiltIn = true;
    public string mapDisplay = "Speed Circuit";
    public string mapJson = "";         // so clients can preview custom maps
    public int    laps = 3;
    public int    weather;              // 0..3 = Atmosphere.Presets, 4 = Random
    public List<int> ai = new List<int>();   // per-AI MatchConfig.Difficulty
}

[Serializable]
public class LobbyState
{
    public LobbySettings     settings = new LobbySettings();
    public List<LobbyPlayer> players  = new List<LobbyPlayer>();
    public string            code     = "";
}

/// The multiplayer SESSION over Unity Relay — internet play with no port
/// forwarding.  Pure logic (the Lobby scene's baked LobbyMenu is the UI):
///   • services sign-in, host (Relay allocation + join code) / join by code
///   • a synced lobby state: every driver's name + paint, and the host's map /
///     mode / laps / weather / AI settings — broadcast with NGO named messages
///   • race loads wrapped in the SceneFader wipe on host AND clients
///   • clean teardown back to the main menu
/// Lives on the NetworkManager GameObject, so it persists into the race and
/// keeps the session alive between races.
[RequireComponent(typeof(NetworkManager))]
public class LobbyController : MonoBehaviour
{
    [Tooltip("The ONE networked race scene every map loads into.")]
    public string raceScene = "MP_Race";
    [Tooltip("Max human peers that can join the host (AI don't count).")]
    public int maxConnections = 7;

    public enum Phase { Offline, Connecting, Hosting, Joined }

    public static LobbyController Instance { get; private set; }

    public Phase  State          { get; private set; } = Phase.Offline;
    public string Status         { get; private set; } = "Connecting to online services…";
    public bool   StatusIsError  { get; private set; }
    public bool   ServicesReady  { get; private set; }
    public bool   Busy           { get; private set; }
    public bool   IsHost         => net != null && net.IsServer;
    public LobbyState Lobby      { get; private set; } = new LobbyState();

    /// Anything changed (status, players, settings) — UI refreshes.
    public event Action Changed;

    public const int WeatherRandom = 4;
    public static readonly string[] WeatherNames = { "DAY", "SUNSET", "NIGHT", "STORM", "RANDOM" };

    NetworkManager net;
    UnityTransport transport;
    bool hooked;

    const string MsgState = "SD_LobbyState";
    const string MsgHello = "SD_LobbyHello";

    async void Awake()
    {
        Instance  = this;
        net       = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
        SeedSettingsFromMatchConfig();
        await InitServices();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unhook();
    }

    void SeedSettingsFromMatchConfig()
    {
        var s = Lobby.settings;
        s.mode       = (int)MatchConfig.GameMode;
        s.mapName    = MatchConfig.MapName;
        s.mapBuiltIn = MatchConfig.MapBuiltIn;
        s.mapDisplay = MatchConfig.MapName == TrackData.ScratchName ? "Editor draft" : MatchConfig.MapName;
        s.laps       = Mathf.Clamp(MatchConfig.Laps, 1, 20);
        s.weather    = MatchConfig.WeatherPreset >= 0 ? MatchConfig.WeatherPreset : 0;
        s.ai.Clear();
        foreach (var d in MatchConfig.AIDifficulties) s.ai.Add((int)d);
    }

    async Task InitServices()
    {
        SetStatus("Connecting to online services…", false);
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            // Services persist for the whole process, so a SECOND lobby visit
            // must NOT blindly sign in again — a sign-in left in progress throws
            // "already signing in".  Tolerate it and wait for it to settle.
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
                catch (Exception) { /* in progress / already done — wait below */ }
            }

            float t = 0f;
            while (!AuthenticationService.Instance.IsSignedIn && t < 6f)
            { await Task.Delay(150); t += 0.15f; }

            ServicesReady = AuthenticationService.Instance.IsSignedIn;
            SetStatus(ServicesReady ? "Online — host a lobby or join with a code."
                                    : "Online services unavailable — check your connection and restart.",
                      !ServicesReady);
        }
        catch (Exception e)
        {
            SetStatus("Services error: " + e.Message, true);
        }
    }

    void SetStatus(string s, bool error)
    {
        Status = s;
        StatusIsError = error;
        Changed?.Invoke();
    }

    // ── Host / join / leave ───────────────────────────────────────────────────
    public async void Host()
    {
        if (Busy || !ServicesReady || State != Phase.Offline) return;
        Busy = true;
        SetStatus("Creating lobby…", false);
        try
        {
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string code      = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            transport.SetRelayServerData(alloc.ToRelayServerData("dtls"));
            if (!net.StartHost()) throw new Exception("could not start host");

            Hook();
            Lobby.code = code;
            Lobby.players.Clear();
            Lobby.players.Add(new LobbyPlayer
            {
                id = (long)net.LocalClientId, name = PlayerProfile.Name,
                color = PlayerProfile.ColorIndex, host = true,
            });
            RefreshMapJson();
            State = Phase.Hosting;
            Busy  = false;
            SetStatus("Lobby open — share the code.", false);
            Broadcast();
        }
        catch (Exception e)
        {
            Busy = false;
            SetStatus("Couldn't host: " + e.Message, true);
        }
    }

    public async void Join(string code)
    {
        if (Busy || !ServicesReady || State != Phase.Offline) return;
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length < 6) { SetStatus("Enter the 6-character join code first.", true); return; }

        Busy = true;
        SetStatus($"Joining {code}…", false);
        try
        {
            JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(code);
            transport.SetRelayServerData(join.ToRelayServerData("dtls"));
            if (!net.StartClient()) throw new Exception("could not start client");
            Hook();
            Lobby.code = code;
            State = Phase.Connecting;
            SetStatus("Connecting to the host…", false);
        }
        catch (Exception e)
        {
            Busy = false;
            SetStatus("Couldn't join: " + e.Message, true);
        }
    }

    /// Leave the lobby but stay in the Lobby scene (back to host/join).
    public void Leave()
    {
        if (net != null && (net.IsServer || net.IsClient)) net.Shutdown();
        Unhook();
        Lobby.players.Clear();
        Lobby.code = "";
        State = Phase.Offline;
        Busy  = false;
        SetStatus("Left the lobby.", false);
    }

    /// Full teardown to the main menu (wipe → shutdown → destroy NM → load).
    public void LeaveToMainMenu()
    {
        SceneFader.Load("MainMenu", null, null, () =>
        {
            Unhook();
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.Shutdown();
                if (nm.gameObject != null) Destroy(nm.gameObject);
            }
        });
    }

    // ── Settings / profile ────────────────────────────────────────────────────
    /// Host: publish new race settings to everyone.
    public void SetSettings(LobbySettings s)
    {
        if (!IsHost) return;
        bool mapChanged = s.mapName != Lobby.settings.mapName || s.mapBuiltIn != Lobby.settings.mapBuiltIn;
        Lobby.settings = s;
        if (mapChanged || string.IsNullOrEmpty(s.mapJson)) RefreshMapJson();
        Broadcast();
    }

    /// Anyone: my name / paint changed.
    public void SetLocalProfile(string name, int color)
    {
        PlayerProfile.Name       = name;
        PlayerProfile.ColorIndex = color;
        if (State == Phase.Hosting)
        {
            var me = Find((long)net.LocalClientId);
            if (me != null) { me.name = PlayerProfile.Name; me.color = color; }
            Broadcast();
        }
        else if (State == Phase.Joined) SendHello();
        else Changed?.Invoke();
    }

    void RefreshMapJson()
    {
        var s = Lobby.settings;
        TrackData d = s.mapBuiltIn ? MapCatalog.GetBuiltIn(s.mapName) : TrackData.Load(s.mapName);
        string json = d != null ? JsonUtility.ToJson(d) : "";
        s.mapJson = json.Length <= 16000 ? json : "";
    }

    // ── Start ─────────────────────────────────────────────────────────────────
    /// Host: lock in the settings and load everyone into the race.
    public void StartRace()
    {
        if (!IsHost || Busy) return;
        var s = Lobby.settings;

        MatchConfig.SelectMap(new MapCatalog.Entry { name = s.mapName, builtIn = s.mapBuiltIn, display = s.mapDisplay });
        MatchConfig.GameMode = (MatchConfig.Mode)s.mode;
        MatchConfig.Laps     = Mathf.Clamp(s.laps, 1, 20);
        bool tt = MatchConfig.GameMode == MatchConfig.Mode.TimeTrial;
        MatchConfig.AIDifficulties = new List<MatchConfig.Difficulty>();
        if (!tt) foreach (int d in s.ai) MatchConfig.AIDifficulties.Add((MatchConfig.Difficulty)d);

        int w = s.weather == WeatherRandom ? UnityEngine.Random.Range(0, Atmosphere.Presets.Length)
                                           : Mathf.Clamp(s.weather, 0, Atmosphere.Presets.Length - 1);
        MatchConfig.WeatherPreset = w;
        Atmosphere.Current        = Atmosphere.Presets[w];

        HostLoadRace();
    }

    /// Host: wipe everyone out and load the race scene (lobby start AND
    /// "next race" from the results screen both come through here).
    public void HostLoadRace()
    {
        if (!IsHost) return;
        Broadcast();
        StartCoroutine(HostLoadRoutine());
    }

    IEnumerator HostLoadRoutine()
    {
        Busy = true;
        LoadCard(out string title, out string sub);
        SceneFader.CoverForExternalLoad(title, sub);
        float t = Time.unscaledTime + 0.45f;
        while (Time.unscaledTime < t) yield return null;
        net.SceneManager.LoadScene(raceScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
        Busy = false;
    }

    void LoadCard(out string title, out string sub)
    {
        var s = Lobby.settings;
        title = (s.mapDisplay ?? s.mapName ?? "RACE").ToUpperInvariant();
        bool tt = s.mode == (int)MatchConfig.Mode.TimeTrial;
        string w = MatchConfig.WeatherPreset >= 0 && MatchConfig.WeatherPreset < WeatherNames.Length
                 ? WeatherNames[MatchConfig.WeatherPreset] : WeatherNames[Mathf.Clamp(s.weather, 0, 4)];
        bool zr = s.mode == (int)MatchConfig.Mode.ZoneRace;
        sub = tt ? $"ONLINE TIME TRIAL  ·  {w}"
                 : $"ONLINE {(zr ? "ZONE RACE" : "RACE")}  ·  {s.laps} {(s.laps == 1 ? "LAP" : "LAPS")}  ·  {w}";
    }

    // ── NGO hooks ─────────────────────────────────────────────────────────────
    void Hook()
    {
        if (hooked) return;
        hooked = true;
        net.OnClientConnectedCallback  += OnClientConnected;
        net.OnClientDisconnectCallback += OnClientDisconnected;
        if (net.SceneManager != null) net.SceneManager.OnSceneEvent += OnSceneEvent;
        var cm = net.CustomMessagingManager;
        if (cm != null)
        {
            cm.RegisterNamedMessageHandler(MsgState, OnStateMessage);
            cm.RegisterNamedMessageHandler(MsgHello, OnHelloMessage);
        }
    }

    void Unhook()
    {
        if (!hooked || net == null) { hooked = false; return; }
        hooked = false;
        net.OnClientConnectedCallback  -= OnClientConnected;
        net.OnClientDisconnectCallback -= OnClientDisconnected;
        if (net.SceneManager != null) net.SceneManager.OnSceneEvent -= OnSceneEvent;
        var cm = net.CustomMessagingManager;
        if (cm != null)
        {
            cm.UnregisterNamedMessageHandler(MsgState);
            cm.UnregisterNamedMessageHandler(MsgHello);
        }
    }

    void OnClientConnected(ulong id)
    {
        if (net.IsServer)
        {
            if (id != net.LocalClientId && Find((long)id) == null)
                Lobby.players.Add(new LobbyPlayer { id = (long)id, name = "DRIVER", color = 0 });
            Broadcast();
        }
        else if (id == net.LocalClientId)
        {
            State = Phase.Joined;
            Busy  = false;
            SetStatus("Joined — waiting for the host to start.", false);
            SendHello();
        }
    }

    void OnClientDisconnected(ulong id)
    {
        if (net.IsServer)
        {
            Lobby.players.RemoveAll(p => p.id == (long)id);
            Broadcast();
            return;
        }
        // We're a client: the host went away (or we were dropped).
        if (id == net.LocalClientId || !net.IsConnectedClient)
        {
            bool wasInRace = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == raceScene;
            Unhook();
            Lobby.players.Clear();
            State = Phase.Offline;
            Busy  = false;
            SetStatus("Disconnected from the host.", true);
            if (wasInRace) LeaveToMainMenu();
        }
    }

    void OnSceneEvent(SceneEvent e)
    {
        // Clients: the host started a load — wipe the screen while it happens.
        if (net.IsServer) return;
        if (e.SceneEventType == SceneEventType.Load && e.ClientId == net.LocalClientId)
        {
            LoadCard(out string title, out string sub);
            SceneFader.CoverForExternalLoad(title, sub);
        }
    }

    // ── Messages ─────────────────────────────────────────────────────────────
    void Broadcast()
    {
        Changed?.Invoke();
        if (net == null || !net.IsServer || net.CustomMessagingManager == null) return;
        string json = JsonUtility.ToJson(Lobby);
        foreach (var id in net.ConnectedClientsIds)
        {
            if (id == net.LocalClientId) continue;
            using var w = new FastBufferWriter(1024, Allocator.Temp, 65536);
            w.WriteValueSafe(json);
            net.CustomMessagingManager.SendNamedMessage(MsgState, id, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    void SendHello()
    {
        if (net == null || net.IsServer || net.CustomMessagingManager == null) return;
        var hello = new LobbyPlayer { id = (long)net.LocalClientId, name = PlayerProfile.Name, color = PlayerProfile.ColorIndex };
        using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
        w.WriteValueSafe(JsonUtility.ToJson(hello));
        net.CustomMessagingManager.SendNamedMessage(MsgHello, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        Changed?.Invoke();
    }

    void OnStateMessage(ulong sender, FastBufferReader reader)
    {
        if (net.IsServer) return;
        reader.ReadValueSafe(out string json);
        try
        {
            var st = JsonUtility.FromJson<LobbyState>(json);
            if (st != null) Lobby = st;
        }
        catch { return; }
        Changed?.Invoke();
    }

    void OnHelloMessage(ulong sender, FastBufferReader reader)
    {
        if (!net.IsServer) return;
        reader.ReadValueSafe(out string json);
        LobbyPlayer hello;
        try { hello = JsonUtility.FromJson<LobbyPlayer>(json); } catch { return; }
        if (hello == null) return;
        var p = Find((long)sender);
        if (p == null) { p = new LobbyPlayer { id = (long)sender }; Lobby.players.Add(p); }
        p.name  = string.IsNullOrWhiteSpace(hello.name) ? "DRIVER" : hello.name.ToUpperInvariant();
        p.color = Mathf.Clamp(hello.color, 0, PlayerProfile.PlayerPalette.Length - 1);
        Broadcast();
    }

    LobbyPlayer Find(long id) => Lobby.players.Find(p => p.id == id);

    public bool IsLocal(LobbyPlayer p) => net != null && p != null && p.id == (long)net.LocalClientId;
}
