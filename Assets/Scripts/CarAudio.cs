using UnityEngine;

/// Fully procedural car audio — no .wav files.  Synthesises every clip at
/// startup and drives them from the car's state:
///   • Engine   — a looping harmonic tone, pitch-shifted by RPM, volume by throttle.
///   • Shift    — a short percussive clunk on every gear change (OnShift).
///   • Boost    — a noise whoosh on the rising edge of IsBoosting.
///   • Screech  — a looping tyre whine while drifting, volume scaled by slide angle.
///
/// Auto-added by CarController.Awake.  All clips are mono, 2D (spatialBlend 0)
/// since it's the local player's own car.  Swap any clip for a real sample later
/// by assigning the matching AudioClip field in the inspector — a non-null
/// clip overrides the synthesised one.
[DisallowMultipleComponent]
public class CarAudio : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Per-car master on top of the global AudioListener volume (GameSettings).")]
    [Range(0f, 1f)] public float masterVolume = 0.8f;
    // Per-car master × Options ▸ Audio ▸ Effects.
    float Master => masterVolume * GameSettings.EffectsVolume;
    public bool enableEngine  = true;
    public bool enableShift   = true;
    public bool enableBoost   = true;
    public bool enableScreech = true;

    [Header("Engine")]
    [Tooltip("Engine is continuous so it masks transients — keep it well below the others.")]
    [Range(0f, 1f)] public float engineVolume = 0.12f;
    [Tooltip("Playback pitch at idle (RPM 0) and at redline (RPM 1).")]
    [Range(0.1f, 2f)] public float idlePitch    = 0.70f;
    [Range(1f, 3f)]   public float redlinePitch = 2.05f;
    [Tooltip("Engine volume floor (heard even off-throttle) → full on throttle.")]
    [Range(0f, 1f)] public float engineIdleVolFrac = 0.35f;
    [Range(1f, 30f)] public float engineSmooth = 12f;

    [Header("Shift / Move Click")]
    [Range(0f, 1f)] public float shiftVolume = 0.45f;
    [Tooltip("Click for moving the shifter onto an EMPTY cell — lighter than a gear clunk.")]
    [Range(0f, 1f)] public float moveClickVolume = 0.28f;

    [Header("Boost")]
    [Range(0f, 1f)] public float boostVolume = 0.45f;

    [Header("Screech")]
    [Range(0f, 1f)] public float screechVolume = 0.35f;
    [Tooltip("Slide angle (deg) at which the screech reaches full volume.")]
    [Range(5f, 70f)] public float screechFullSlideAngle = 35f;
    [Range(1f, 30f)] public float screechSmooth = 14f;

    [Header("Spatial Drift Zone Tone")]
    [Tooltip("In ghost mode the engine is silenced and this soft low oscillating " +
             "drone plays instead (placeholder for the eventual music tracks).")]
    [Range(0f, 1f)] public float voidVolume = 0.60f;
    [Range(1f, 20f)] public float voidFade  = 5f;

    [Header("Drift Zone Hit Sounds (osu!-style)")]
    [Range(0f, 1f)] public float hitSoundVolume = 0.55f;

    [Header("Optional sample overrides (leave empty for synth)")]
    public AudioClip engineClipOverride;
    public AudioClip shiftClipOverride;
    public AudioClip boostClipOverride;
    public AudioClip screechClipOverride;

    const int   SR             = 44100;
    const float ENGINE_BASE_HZ = 100f;

    CarController    car;
    GearSystem       gears;
    GearShifter      shifter;
    SpatialDriftMode sdMode;

    AudioSource engineSrc, screechSrc, voidSrc, oneShotSrc;
    AudioClip   shiftClip, boostClip;                 // cached one-shot clips (built once)
    AudioClip   hitTickClip, perfectClip, missClip;   // rhythm-game hit sounds
    AudioClip   riftTearClip, riftCrossClip, riftExitClip, tierClip;   // Spatial Drift rifts / drift sparks
    bool   subscribed;
    bool   wasBoosting;
    float  curEnginePitch = 1f, curEngineVol, curScreechVol, curVoidVol;

    void Awake()
    {
        car     = GetComponent<CarController>();
        gears   = GetComponent<GearSystem>();
        shifter = GetComponent<GearShifter>();
        sdMode  = GetComponent<SpatialDriftMode>();

        // Apply the player's saved global volume to the AudioListener.
        GameSettings.Apply();

        engineSrc  = MakeSource("EngineSource",  loop: true);
        screechSrc = MakeSource("ScreechSource", loop: true);
        voidSrc    = MakeSource("VoidToneSource", loop: true);
        oneShotSrc = MakeSource("OneShotSource", loop: false);

        // Build every clip ONCE up front — never synthesise during play.
        engineSrc.clip  = engineClipOverride  != null ? engineClipOverride  : BuildEngineClip();
        screechSrc.clip = screechClipOverride != null ? screechClipOverride : BuildScreechClip();
        voidSrc.clip    = BuildVoidClip();
        shiftClip       = shiftClipOverride   != null ? shiftClipOverride   : BuildShiftClip();
        boostClip       = boostClipOverride   != null ? boostClipOverride   : BuildBoostClip();
        hitTickClip     = BuildHitTick();
        perfectClip     = BuildPerfectDing();
        missClip        = BuildMissThud();
        riftTearClip    = BuildRiftTear();
        riftCrossClip   = BuildRiftCross();
        riftExitClip    = BuildRiftExit();
        tierClip        = BuildTierChing();

        if (enableEngine)  { engineSrc.volume = 0f; engineSrc.Play(); }
        if (enableScreech) { screechSrc.volume = 0f; screechSrc.Play(); }
        voidSrc.volume = 0f; voidSrc.Play();
    }

    void OnEnable()
    {
        if (subscribed) return;
        if (shifter != null) shifter.OnMove += HandleMove;
        if (sdMode  != null)
        {
            sdMode.OnHit        += HandleRhythmHit;
            sdMode.OnEnterBegin += HandleRiftOpen;
            sdMode.OnActivate   += HandleRiftCross;
            sdMode.OnExit       += HandleRiftExit;
        }
        subscribed = true;
    }

    void OnDisable()
    {
        if (!subscribed) return;
        if (shifter != null) shifter.OnMove -= HandleMove;
        if (sdMode  != null)
        {
            sdMode.OnHit        -= HandleRhythmHit;
            sdMode.OnEnterBegin -= HandleRiftOpen;
            sdMode.OnActivate   -= HandleRiftCross;
            sdMode.OnExit       -= HandleRiftExit;
        }
        subscribed = false;
    }

    // osu!-style audio feedback in the drift zone: tick / ding / thunk.
    void HandleRhythmHit(int tier, KeyCode lane)
    {
        switch (tier)
        {
            case 2:  PlayOneShot(perfectClip, hitSoundVolume,        1f);   break;
            case 1:  PlayOneShot(hitTickClip, hitSoundVolume,        1f);   break;
            default: PlayOneShot(missClip,    hitSoundVolume * 0.8f, 1f);   break;
        }
    }

    AudioSource MakeSource(string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake   = false;
        src.loop          = loop;
        src.spatialBlend  = 0f;     // 2D — it's the player's own car
        src.dopplerLevel  = 0f;
        return src;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        bool inDriftZone = car != null && car.spatialDriftActive;

        // ── Engine: pitch from RPM, volume from throttle ─────────────────────
        //     Silenced in the drift zone — the void tone takes over there.
        if (enableEngine && engineSrc != null && gears != null && car != null)
        {
            float rpm      = gears.RPM;
            float throttle = car.LocalInput.accel;

            float targetPitch = Mathf.Lerp(idlePitch, redlinePitch, rpm);
            float targetVol    = inDriftZone
                ? 0f
                : engineVolume * Master * Mathf.Lerp(engineIdleVolFrac, 1f, throttle);

            curEnginePitch = Mathf.Lerp(curEnginePitch, targetPitch, 1f - Mathf.Exp(-engineSmooth * dt));
            curEngineVol   = Mathf.Lerp(curEngineVol,   targetVol,   1f - Mathf.Exp(-engineSmooth * dt));

            engineSrc.pitch  = curEnginePitch;
            engineSrc.volume = curEngineVol;
        }

        // ── Void tone: soft low drone that fades in only in the drift zone ───
        if (voidSrc != null)
        {
            float targetVoid = inDriftZone ? voidVolume * Master : 0f;
            curVoidVol = Mathf.Lerp(curVoidVol, targetVoid, 1f - Mathf.Exp(-voidFade * dt));
            voidSrc.volume = curVoidVol;
        }

        // ── Screech: volume from drift slide angle ───────────────────────────
        if (enableScreech && screechSrc != null && car != null)
        {
            float target = 0f;
            if (car.IsDrifting)
                target = screechVolume * Master
                       * Mathf.Clamp01(car.SlideAngle / Mathf.Max(1f, screechFullSlideAngle));

            curScreechVol = Mathf.Lerp(curScreechVol, target, 1f - Mathf.Exp(-screechSmooth * dt));
            screechSrc.volume = curScreechVol;
            // Subtle pitch wobble with slide for life.
            screechSrc.pitch  = 1f + Mathf.Clamp01(car.SlideAngle / 60f) * 0.25f;
        }

        // ── Boost: whoosh on rising edge ─────────────────────────────────────
        if (gears != null)
        {
            bool boosting = gears.IsBoosting;
            if (enableBoost && boosting && !wasBoosting)
                PlayOneShot(boostClip, boostVolume, 1f);
            wasBoosting = boosting;
        }
    }

    // Fires on EVERY shifter-ball move (gear cell or empty cell).  A gear cell
    // gets a fuller, lower clunk; an empty cell gets a lighter, higher tick.
    void HandleMove(bool landedOnGear)
    {
        if (!enableShift) return;
        if (landedOnGear) PlayOneShot(shiftClip, shiftVolume,     1f);
        else              PlayOneShot(shiftClip, moveClickVolume, 1.5f);
    }

    // Spatial Drift rifts: the air tears open ahead, you punch through, you're spat back out.
    void HandleRiftOpen()          => PlayOneShot(riftTearClip,  0.85f, 1f);
    void HandleRiftCross()         => PlayOneShot(riftCrossClip, 1.0f,  1f);
    void HandleRiftExit(int score) => PlayOneShot(riftExitClip,  0.9f,  1f);

    /// Drift sparks stepped up a tier (DriftFx).  Higher tier = higher ching.
    public void PlayDriftTier(int tier) =>
        PlayOneShot(tierClip, 0.45f, tier >= 2 ? 1.5f : 1.2f);

    // Zone Race call-outs (reuse the drift-zone hit sounds).
    public void PlayZoneHit()   => PlayOneShot(missClip,    hitSoundVolume,        1f);
    public void PlayZoneBonus() => PlayOneShot(perfectClip, hitSoundVolume,        1.15f);
    public void PlayZoneClean() => PlayOneShot(hitTickClip, hitSoundVolume * 0.35f, 1f);

    void PlayOneShot(AudioClip clip, float vol, float pitch)
    {
        if (oneShotSrc == null || clip == null) return;
        oneShotSrc.pitch = pitch;
        oneShotSrc.PlayOneShot(clip, vol * Master);
    }

    /// Car-contact thud, called by CarBumpSystem.  Reuses the shift clunk
    /// pitched way down — reads as a body impact.  Intensity 0..1 scales volume.
    public void PlayBump(float intensity)
    {
        PlayOneShot(shiftClip, Mathf.Lerp(0.25f, 0.65f, Mathf.Clamp01(intensity)), 0.55f);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Procedural clip synthesis
    // ════════════════════════════════════════════════════════════════════════
    static float Frac(float x) => x - Mathf.Floor(x);

    static void Normalize(float[] d, float peak)
    {
        float max = 0f;
        for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
        if (max < 1e-5f) return;
        float k = peak / max;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }

    static AudioClip MakeClip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Looping engine drone: saw fundamental + sub-octave + square, all completing
    // an integer number of cycles over the buffer so the loop is seamless.
    static AudioClip BuildEngineClip()
    {
        const int cycles = 8;
        int len = Mathf.RoundToInt(SR * cycles / ENGINE_BASE_HZ);   // 3528
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float ph  = i * ENGINE_BASE_HZ / SR;     // cycles elapsed
            float p1  = Frac(ph);
            float saw = 2f * p1 - 1f;
            float sub = 2f * Frac(ph * 0.5f) - 1f;   // sub-octave (even cycles → seamless)
            float sq  = p1 < 0.5f ? 0.5f : -0.5f;
            d[i] = saw * 0.45f + sub * 0.30f + sq * 0.15f;
        }
        Normalize(d, 0.6f);
        return MakeClip("EngineSynth", d);
    }

    // Short percussive clunk: low thud + lowpassed noise, fast exponential decay.
    static AudioClip BuildShiftClip()
    {
        int len = Mathf.RoundToInt(SR * 0.09f);
        var d = new float[len];
        float lp = 0f;
        for (int i = 0; i < len; i++)
        {
            float t   = i / (float)SR;
            float env = Mathf.Exp(-t * 48f);
            float thud = Mathf.Sin(2f * Mathf.PI * 130f * t) * env;
            float n    = Random.value * 2f - 1f;
            lp += (n - lp) * 0.25f;                 // one-pole lowpass
            d[i] = thud * 0.6f + lp * env * 0.5f;
        }
        Normalize(d, 0.9f);
        return MakeClip("ShiftSynth", d);
    }

    // Boost whoosh: noise through a lowpass that opens then closes, swell envelope.
    static AudioClip BuildBoostClip()
    {
        int len = Mathf.RoundToInt(SR * 0.5f);
        var d = new float[len];
        float lp = 0f;
        for (int i = 0; i < len; i++)
        {
            float t    = i / (float)SR;
            float frac = t / 0.5f;                              // 0..1 over the clip
            float env  = Mathf.Clamp01(frac / 0.12f) * Mathf.Clamp01((1f - frac) / 0.5f);
            float cut  = Mathf.Lerp(0.04f, 0.40f, Mathf.Sin(frac * Mathf.PI)); // open→close
            float n    = Random.value * 2f - 1f;
            lp += (n - lp) * cut;
            d[i] = lp * env;
        }
        Normalize(d, 0.85f);
        return MakeClip("BoostSynth", d);
    }

    // Looping tyre screech: detuned high sines + slow amplitude beat, all seamless.
    static AudioClip BuildScreechClip()
    {
        int len = Mathf.RoundToInt(SR * 0.3f);   // 13230 → integer cycles for the freqs below
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR;
            float s = Mathf.Sin(2f * Mathf.PI * 1700f * t) * 0.40f
                    + Mathf.Sin(2f * Mathf.PI * 1950f * t) * 0.32f
                    + Mathf.Sin(2f * Mathf.PI * 2300f * t) * 0.22f;
            float beat = 0.82f + 0.18f * Mathf.Sin(2f * Mathf.PI * 10f * t);
            d[i] = s * beat;
        }
        Normalize(d, 0.5f);
        return MakeClip("ScreechSynth", d);
    }

    // Soft low oscillating drone for the Spatial Drift zone.  Fundamental sits
    // at 110 Hz — the original 55 Hz version was below what most desktop /
    // laptop speakers can reproduce, which made the whole zone sound "muted".
    // Pitch wobble is phase modulation with integer carrier + modulator
    // frequencies, so the 1-second loop stays seamless.  Placeholder for the
    // eventual beat-driven music tracks.
    static AudioClip BuildVoidClip()
    {
        int len = SR;     // 1.0 s
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t  = i / (float)SR;
            float pm = 0.9f * Mathf.Sin(2f * Mathf.PI * 3f * t);   // gentle 3 Hz wobble
            float tone = Mathf.Sin(2f * Mathf.PI * 110f * t + pm) * 0.55f
                       + Mathf.Sin(2f * Mathf.PI * 165f * t) * 0.20f    // fifth
                       + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.28f    // octave
                       + Mathf.Sin(2f * Mathf.PI * 330f * t) * 0.10f;   // shimmer
            float lfo  = 0.70f + 0.30f * Mathf.Sin(2f * Mathf.PI * 2f * t);  // slow swell
            d[i] = tone * lfo;
        }
        Normalize(d, 0.6f);
        return MakeClip("VoidSynth", d);
    }

    // ── Rift sounds ───────────────────────────────────────────────────────────
    // TEAR: the air splitting — a fizz of crackle impulses over a rising
    // band of noise and a sub rumble that swells into the opening.
    static AudioClip BuildRiftTear()
    {
        int len = Mathf.RoundToInt(SR * 0.75f);
        var d = new float[len];
        float lp = 0f, ph = 0f;
        var rng = new System.Random(7);
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR, f = t / 0.75f;
            float env = Mathf.Clamp01(f / 0.08f) * Mathf.Clamp01((1f - f) / 0.35f);
            float n = (float)rng.NextDouble() * 2f - 1f;
            lp += (n - lp) * Mathf.Lerp(0.05f, 0.5f, f);                    // band rises
            float crackle = rng.NextDouble() < Mathf.Lerp(0.002f, 0.02f, f) ? (float)(rng.NextDouble() * 2 - 1) * 3f : 0f;
            ph += 2f * Mathf.PI * Mathf.Lerp(38f, 70f, f) / SR;
            float rumble = Mathf.Sin(ph) * 0.8f * Mathf.Clamp01(f * 1.5f);
            d[i] = (lp * 0.9f + crackle + rumble) * env;
        }
        Normalize(d, 0.8f);
        return MakeClip("RiftTear", d);
    }

    // CROSS: punching through — a downward-sweeping whoosh with a sub boom.
    static AudioClip BuildRiftCross()
    {
        int len = Mathf.RoundToInt(SR * 0.9f);
        var d = new float[len];
        float lp = 0f, ph = 0f;
        var rng = new System.Random(11);
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR, f = t / 0.9f;
            float n = (float)rng.NextDouble() * 2f - 1f;
            lp += (n - lp) * Mathf.Lerp(0.6f, 0.03f, Mathf.Sqrt(f));         // bright → dark
            float wenv = Mathf.Clamp01(f / 0.03f) * Mathf.Exp(-f * 3.2f);
            ph += 2f * Mathf.PI * Mathf.Lerp(90f, 32f, f) / SR;              // boom drops
            float boom = Mathf.Sin(ph) * Mathf.Exp(-f * 4.5f) * 1.2f;
            float shimmer = Mathf.Sin(2f * Mathf.PI * 660f * t) * Mathf.Exp(-f * 6f) * 0.25f;
            d[i] = lp * wenv + boom + shimmer;
        }
        Normalize(d, 0.95f);
        return MakeClip("RiftCross", d);
    }

    // EXIT: reverse swell into a sharp pop.
    static AudioClip BuildRiftExit()
    {
        int len = Mathf.RoundToInt(SR * 0.55f);
        var d = new float[len];
        float lp = 0f, ph = 0f;
        var rng = new System.Random(23);
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR, f = t / 0.55f;
            float n = (float)rng.NextDouble() * 2f - 1f;
            lp += (n - lp) * Mathf.Lerp(0.04f, 0.45f, Mathf.Clamp01(f / 0.4f));
            float swell = Mathf.Pow(Mathf.Clamp01(f / 0.4f), 2f) * (f < 0.4f ? 1f : Mathf.Exp(-(f - 0.4f) * 18f));
            ph += 2f * Mathf.PI * 55f / SR;
            float pop = f >= 0.4f ? Mathf.Sin(ph) * Mathf.Exp(-(f - 0.4f) * 20f) * 1.4f : 0f;
            d[i] = lp * swell + pop;
        }
        Normalize(d, 0.85f);
        return MakeClip("RiftExit", d);
    }

    // Drift-spark tier-up: a short bright ching (two inharmonic partials).
    static AudioClip BuildTierChing()
    {
        int len = Mathf.RoundToInt(SR * 0.28f);
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 16f) * Mathf.Clamp01(t / 0.003f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 1480f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2210f * t) * 0.4f) * env;
        }
        Normalize(d, 0.6f);
        return MakeClip("TierChing", d);
    }

    // ── Rhythm-game hit sounds (osu!-style) ───────────────────────────────────
    // Sharp tick for a good hit — short high sine + a pinch of noise click.
    static AudioClip BuildHitTick()
    {
        int len = Mathf.RoundToInt(SR * 0.035f);
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t   = i / (float)SR;
            float env = Mathf.Exp(-t * 220f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 1900f * t) * 0.8f
                  + (Random.value * 2f - 1f) * 0.25f) * env;
        }
        Normalize(d, 0.8f);
        return MakeClip("HitTick", d);
    }

    // Brighter two-tone ding for a PERFECT (G6 + C7 — a pleasant fourth).
    static AudioClip BuildPerfectDing()
    {
        int len = Mathf.RoundToInt(SR * 0.12f);
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t   = i / (float)SR;
            float env = Mathf.Exp(-t * 60f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 1568f * t) * 0.60f
                  + Mathf.Sin(2f * Mathf.PI * 2093f * t) * 0.45f) * env;
        }
        Normalize(d, 0.8f);
        return MakeClip("PerfectDing", d);
    }

    // Dull low thunk for a miss / bad press.
    static AudioClip BuildMissThud()
    {
        int len = Mathf.RoundToInt(SR * 0.10f);
        var d = new float[len];
        float lp = 0f;
        for (int i = 0; i < len; i++)
        {
            float t   = i / (float)SR;
            float env = Mathf.Exp(-t * 50f);
            float n   = Random.value * 2f - 1f;
            lp += (n - lp) * 0.12f;
            d[i] = (Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.7f + lp * 0.5f) * env;
        }
        Normalize(d, 0.85f);
        return MakeClip("MissThud", d);
    }
}
