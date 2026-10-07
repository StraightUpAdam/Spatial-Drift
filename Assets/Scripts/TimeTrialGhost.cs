using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// Records the player's time-trial run and replays their PERSONAL BEST as a
/// translucent ghost car, so every reset lets you race the movements of your
/// fastest lap.  Fully self-contained: it polls RaceManager's time-trial state
/// (armed / running / finished) rather than depending on event ordering.
///
///   • While the clock runs it samples the car's pose (30 Hz).
///   • When RaceManager reports a new PB it saves that recording to disk
///     (persistentDataPath/Ghosts/<map>.json) — one PB ghost per map.
///   • On each arm/reset it reloads the saved PB and replays it against the run
///     clock (frozen at the start line during the standing start, then in sync
///     once you move), so the comparison is always fair.
[DisallowMultipleComponent]
public class TimeTrialGhost : MonoBehaviour
{
    public CarController player;
    public RaceManager   race;
    public Track         track;
    public string        mapName = "";

    [Serializable] class Sample    { public float t; public Vector3 p; public Quaternion r; }
    [Serializable] class GhostData { public float time; public List<Sample> samples = new List<Sample>(); }

    const float SampleInterval = 1f / 30f;

    readonly List<Sample> recording = new List<Sample>();
    GhostData  ghost;            // loaded personal best (null = none yet this map)
    GameObject ghostCar;
    Transform  ghostT;

    float _recAccum;
    bool  _wasArmed;
    int   _cursor;

    static string Dir => Path.Combine(Application.persistentDataPath, "Ghosts");
    string FilePath() => PathFor(mapName);

    static string PathFor(string map)
    {
        string safe = map ?? "";
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        return Path.Combine(Dir, safe + ".json");
    }

    /// Remove a map's PB ghost (its track was edited — TrackRecords).
    public static void DeleteFor(string map)
    {
        try { var p = PathFor(map); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    void Start()
    {
        if (race != null) race.OnTimeTrialBest += SaveGhost;
        LoadGhost();
    }

    void OnDestroy()
    {
        if (race != null) race.OnTimeTrialBest -= SaveGhost;
    }

    void LateUpdate()
    {
        if (race == null || !race.TimeTrial || player == null) return;

        var   entry   = race.LocalEntry;
        if (entry == null) return;
        float t       = entry.currentLapTime;
        bool  armed   = race.TimeTrialArmed;
        bool  running = race.TimeTrialRunning;

        // Fresh arm/reset (rising edge): drop the old recording, rewind the
        // playback cursor, and pick up any PB saved on the run that just ended.
        if (armed && !_wasArmed)
        {
            recording.Clear();
            _recAccum = 0f;
            _cursor   = 0;
            LoadGhost();
        }
        _wasArmed = armed;

        // Record the live run at a fixed rate.
        if (running)
        {
            _recAccum += Time.deltaTime;
            if (recording.Count == 0 || _recAccum >= SampleInterval)
            {
                _recAccum = 0f;
                recording.Add(new Sample
                {
                    t = t,
                    p = player.transform.position,
                    r = player.transform.rotation,
                });
            }
        }

        // Replay the PB ghost.  Frozen at its start pose while you're still armed
        // (standing start), then evaluated against the run clock once you move.
        if (ghost != null && ghost.samples.Count > 0)
        {
            if (ghostCar == null) BuildGhostCar();
            bool visible = GameSettings.ShowGhost;          // Options ▸ Gameplay ▸ Ghost
            if (ghostCar.activeSelf != visible) ghostCar.SetActive(visible);

            Evaluate(armed ? 0f : t, out Vector3 p, out Quaternion r);
            ghostT.SetPositionAndRotation(p, r);
        }
        else if (ghostCar != null && ghostCar.activeSelf)
        {
            ghostCar.SetActive(false);   // no PB for this map yet
        }
    }

    void Evaluate(float pt, out Vector3 p, out Quaternion r)
    {
        var s = ghost.samples;
        if (pt <= s[0].t)               { p = s[0].p; r = s[0].r; _cursor = 0; return; }
        int last = s.Count - 1;
        if (pt >= s[last].t)            { p = s[last].p; r = s[last].r; return; }

        if (pt < s[_cursor].t) _cursor = 0;                       // clock rewound
        while (_cursor < last - 1 && s[_cursor + 1].t < pt) _cursor++;

        var a = s[_cursor];
        var b = s[_cursor + 1];
        float u = (b.t > a.t) ? Mathf.InverseLerp(a.t, b.t, pt) : 0f;
        p = Vector3.Lerp(a.p, b.p, u);
        r = Quaternion.Slerp(a.r, b.r, u);
    }

    // Called by RaceManager.OnTimeTrialBest — the run just finished as a new PB.
    void SaveGhost()
    {
        if (recording.Count < 2) return;
        var data = new GhostData
        {
            time    = race != null && race.LocalEntry != null ? race.LocalEntry.currentLapTime : 0f,
            samples = new List<Sample>(recording),
        };
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath(), JsonUtility.ToJson(data));
        }
        catch (Exception e) { Debug.LogWarning($"[TimeTrialGhost] save failed: {e.Message}"); }
    }

    void LoadGhost()
    {
        try
        {
            string path = FilePath();
            if (File.Exists(path))
            {
                var g = JsonUtility.FromJson<GhostData>(File.ReadAllText(path));
                ghost = (g != null && g.samples != null && g.samples.Count > 0) ? g : null;
            }
            else ghost = null;
        }
        catch { ghost = null; }
    }

    void BuildGhostCar()
    {
        ghostCar = new GameObject("PB Ghost");
        ghostT   = ghostCar.transform;

        var mat = GhostMaterial();
        AddBox(ghostT, new Vector3(0f, 0.30f, 0f),  new Vector3(1.8f, 0.5f, 3.4f), mat);   // body
        AddBox(ghostT, new Vector3(0f, 0.35f, 1.9f), new Vector3(0.9f, 0.35f, 0.8f), mat);  // nose

        ghostCar.SetActive(false);
    }

    static void AddBox(Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPos;
        box.transform.localScale    = scale;
        Destroy(box.GetComponent<Collider>());
        box.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // Translucent unlit material (URP Unlit is in Always-Included shaders, so it
    // survives the build).  Set the blend state directly so alpha works at runtime.
    static Material GhostMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var m  = new Material(sh);
        var col = new Color(0.55f, 0.85f, 1f, 0.34f);   // ghostly cyan
        m.color = col;
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Surface", 1f);                      // transparent
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }
}
