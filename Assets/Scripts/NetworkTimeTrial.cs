using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// The LOCAL player's time-trial clock in a multiplayer session.  One of these
/// is created (by NetworkRaceDirector) on each client for its own car when the
/// session mode is Time Trial.  Everyone drives the same track at the same time
/// and sees each other (via NetworkCarSync) with NO collision — this component
/// just times YOUR laps and lets you reset, exactly like the solo time trial:
///
///   • Standing start — clock sits at 0 until your car first moves.
///   • Cross the line  — the run freezes; keep driving, or reset for another go.
///   • R / B           — instant restart (teleport to your grid slot, re-arm).
///
/// Purely local: nothing here is networked.  Best time persists per map.
[DisallowMultipleComponent]
public class NetworkTimeTrial : MonoBehaviour
{
    CarController        car;
    NetworkRaceDirector director;
    Track               track;

    float  runTime;
    bool   usedDrift;      // this run entered Spatial Drift (no PURE LAP)
    float  bestTime = float.PositiveInfinity;
    bool   armed    = true;
    bool   finished;
    float  lastDist;
    float  netProgress;   // anti-exploit: a run needs real forward driving
    float  flashTimer;
    string bestKey;

    // UI
    Canvas          canvas;
    TextMeshProUGUI timeText, bestText, hintText, flashText;

    public void Init(CarController localCar, NetworkRaceDirector dir)
    {
        car      = localCar;
        director = dir;
        bestKey  = $"BestLap_TT_{MatchConfig.MapName}";
        TrackRecords.ValidateCurrent();
        bestTime = TrackRecords.BestLap(TrackRecords.TimeTrial, MatchConfig.MapName, false);
        BuildUI();
        Arm();

        // Hidden dev panels (local only): F8 = atmosphere, F9 = drift-zone
        // tuning, F10 = drift feel.
        gameObject.AddComponent<AtmospherePanel>();
        gameObject.AddComponent<DriftTuningPanel>();
        gameObject.AddComponent<DriftFeelPanel>();
    }

    /// Pause menu "Restart run".
    public void Restart() => Arm();

    void Arm()
    {
        armed = true; finished = false; runTime = 0f; netProgress = 0f; usedDrift = false;
        if (car != null)
        {
            car.ResetToStart();
            car.GetComponent<GearSystem>()?.ResetForRace();
            car.GetComponent<GearShifter>()?.ResetToStart();
            lastDist = car.DistanceAlongTrack;
        }
    }

    void Update()
    {
        // Safety: if the session isn't a time trial anymore, remove ourselves.
        if (director != null && !director.IsTimeTrial) { Destroy(gameObject); return; }

        if (car == null) { car = CarController.LocalPlayerOverride; if (car == null) return; }
        if (track == null) track = car.track;

        // Instant restart: R (keyboard) or B (gamepad button 1).  B is ignored
        // while Spatial Drift is active — it doubles as a rhythm-lane input.
        if (!GamePause.InputBlocked &&
            (Input.GetKeyDown(KeyCode.R) ||
             (!car.spatialDriftActive && Input.GetKeyDown(KeyCode.JoystickButton1))))
            Arm();

        if (armed)
        {
            // Standing start: the clock waits for the car to actually roll.
            if (Mathf.Abs(car.Speed) > 0.25f)
            {
                armed       = false;
                runTime     = 0f;
                netProgress = 0f;
                lastDist    = car.DistanceAlongTrack;
            }
        }
        else if (!finished)
        {
            runTime += Time.deltaTime;
            if (car.spatialDriftActive) usedDrift = true;
            CheckLap();
        }

        if (flashTimer > 0f) flashTimer -= Time.deltaTime;
        UpdateUI();
    }

    void CheckLap()
    {
        if (track == null) return;
        float len = track.Length;
        if (len <= 0f) return;

        float curr = car.DistanceAlongTrack;
        // Net forward progress — U-turning over the line banks nothing.
        float d = (curr - lastDist) % len;
        if (d >  len * 0.5f) d -= len;
        if (d < -len * 0.5f) d += len;
        netProgress += d;

        if (track.IsClosed)
        {
            float q = len * 0.25f;
            if (lastDist > len - q && curr < q && netProgress >= len * 0.85f)
            {
                finished = true;
                var (best, pure) = TrackRecords.SubmitLap(TrackRecords.TimeTrial, MatchConfig.MapName, runTime, usedDrift);
                if (best) bestTime = runTime;
                if (flashText != null)
                {
                    flashText.text  = best ? "NEW BEST!" : pure ? "NEW PURE LAP!" : $"FINISH  {RaceManager.FormatTime(runTime)}";
                    flashText.color = best ? new Color(0.30f, 1f, 0.45f, 1f)
                                           : new Color(0.95f, 0.95f, 1f, 1f);
                    flashTimer = 2.0f;
                }

                // Crossed the line while ghost-piloting in Spatial Drift → kick out
                // of it and reset to the start so we don't freeze mid-ghost.
                if (car.spatialDriftActive) { Arm(); return; }
            }
        }
        lastDist = curr;
    }

    // ── UI (small dedicated canvas; RacingHUD hides its MP lap panel in TT) ────
    void BuildUI()
    {
        var go = new GameObject("TimeTrialCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 115;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        timeText  = MakeText(48, new Vector2(0.5f, 1f), new Vector2(0, -40),  new Vector2(520, 66),
                             new Color(1f, 1f, 1f, 0.98f));
        bestText  = MakeText(24, new Vector2(0.5f, 1f), new Vector2(0, -96),  new Vector2(520, 32),
                             new Color(0.55f, 0.95f, 0.6f, 0.95f));
        flashText = MakeText(64, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 120),
                             new Color(1f, 1f, 1f, 0f));
        hintText  = MakeText(24, new Vector2(0.5f, 0f), new Vector2(0, 24),   new Vector2(1400, 34),
                             new Color(0.85f, 0.83f, 0.92f, 0.9f));
        hintText.text = "HIT THE GAS TO START      ·      R / B  RESET      ·      ESC / START  PAUSE";
    }

    TextMeshProUGUI MakeText(int size, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Color color)
    {
        var t = new GameObject("T", typeof(RectTransform));
        t.transform.SetParent(canvas.transform, false);
        var lbl = t.AddComponent<TextMeshProUGUI>();
        lbl.font          = TMP_Settings.defaultFontAsset;
        lbl.fontSize      = size;
        lbl.color         = color;
        lbl.fontStyle     = FontStyles.Bold;
        lbl.alignment     = TextAlignmentOptions.Center;
        lbl.raycastTarget = false;
        lbl.enableWordWrapping = false;
        var rt = lbl.rectTransform;
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        return lbl;
    }

    void UpdateUI()
    {
        if (timeText != null) timeText.text = RaceManager.FormatTime(runTime);
        if (bestText != null)
            bestText.text = bestTime < float.PositiveInfinity
                ? $"BEST  {RaceManager.FormatTime(bestTime)}" : "BEST  -:--.--";
        if (flashText != null)
        {
            var c = flashText.color;
            c.a = Mathf.Clamp01(flashTimer);   // fade out over the last second
            flashText.color = c;
            if (flashTimer <= 0f && !string.IsNullOrEmpty(flashText.text)) flashText.text = "";
        }
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
