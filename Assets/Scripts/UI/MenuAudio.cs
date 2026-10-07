using System.Collections.Generic;
using UnityEngine;

public enum Sfx { Move, Adjust, Confirm, Back, Error, Whoosh, Start, Type, Toggle, Open }

/// Interface sounds for every menu — synthesised at startup (no files needed),
/// played through one persistent source that ignores the pause-menu audio
/// freeze.  Drop a real sample at Resources/UI/Audio/<name> (e.g. "confirm")
/// to replace any sound; volume follows Options ▸ Audio ▸ Interface.
public class MenuAudio : MonoBehaviour
{
    static MenuAudio _i;
    AudioSource src;
    readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
    float lastMoveTime;

    const int SR = 44100;

    public static void Play(Sfx s, float volume = 1f, float pitch = 1f)
    {
        Ensure();
        if (_i == null) return;
        // Rapid-repeat moves (held stick) shouldn't machine-gun at full volume.
        if (s == Sfx.Move || s == Sfx.Adjust)
        {
            float now = Time.unscaledTime;
            if (now - _i.lastMoveTime < 0.05f) volume *= 0.55f;
            _i.lastMoveTime = now;
        }
        if (!_i.clips.TryGetValue(s, out var clip) || clip == null) return;
        _i.src.pitch = pitch;
        _i.src.PlayOneShot(clip, Mathf.Clamp01(volume) * GameSettings.UiVolume);
    }

    public static void Ensure()
    {
        if (_i != null) return;
        var go = new GameObject("[MenuAudio]");
        DontDestroyOnLoad(go);
        _i = go.AddComponent<MenuAudio>();
    }

    void Awake()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake         = false;
        src.spatialBlend        = 0f;
        src.ignoreListenerPause = true;
        src.priority            = 0;

        Load(Sfx.Move,    "move",    Move);
        Load(Sfx.Adjust,  "adjust",  Adjust);
        Load(Sfx.Confirm, "confirm", Confirm);
        Load(Sfx.Back,    "back",    Back);
        Load(Sfx.Error,   "error",   Error);
        Load(Sfx.Whoosh,  "whoosh",  Whoosh);
        Load(Sfx.Start,   "start",   StartSting);
        Load(Sfx.Type,    "type",    TypeClick);
        Load(Sfx.Toggle,  "toggle",  Toggle);
        Load(Sfx.Open,    "open",    Open);
    }

    void Load(Sfx s, string file, System.Func<float[]> synth)
    {
        var clip = Resources.Load<AudioClip>("UI/Audio/" + file);
        if (clip == null)
        {
            var data = synth();
            clip = AudioClip.Create("ui_" + file, data.Length, 1, SR, false);
            clip.SetData(data, 0);
        }
        clips[s] = clip;
    }

    // ── Synthesis helpers ────────────────────────────────────────────────────
    static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * SR)];

    /// Sine sweep f0→f1 with a fast attack and exponential decay (tau seconds).
    static void AddTone(float[] b, float start, float dur, float f0, float f1, float amp,
                        float tau, float harm2 = 0f, float harm3 = 0f)
    {
        int s0 = Mathf.FloorToInt(start * SR);
        int n  = Mathf.Min(b.Length - s0, Mathf.CeilToInt(dur * SR));
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / dur));
            phase += 2.0 * Mathf.PI * f / SR;
            float env = Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t / tau);
            float v = Mathf.Sin((float)phase) + harm2 * Mathf.Sin((float)phase * 2f)
                                              + harm3 * Mathf.Sin((float)phase * 3f);
            b[s0 + i] += v * env * amp;
        }
    }

    static void AddNoise(float[] b, float start, float dur, float amp, float tau, float lpCoef)
    {
        int s0 = Mathf.FloorToInt(start * SR);
        int n  = Mathf.Min(b.Length - s0, Mathf.CeilToInt(dur * SR));
        var rng = new System.Random(1234 + s0);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            float w = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (w - lp) * lpCoef;
            b[s0 + i] += lp * amp * Mathf.Exp(-t / tau);
        }
    }

    static float[] Finish(float[] b, float echo = 0f)
    {
        if (echo > 0f)
        {
            int d = Mathf.RoundToInt(0.055f * SR);
            for (int i = d; i < b.Length; i++) b[i] += b[i - d] * echo;
        }
        // Gentle fade-out on the last 8 ms so nothing clicks.
        int fade = Mathf.Min(b.Length, Mathf.RoundToInt(0.008f * SR));
        for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= (float)i / fade;
        for (int i = 0; i < b.Length; i++) b[i] = b[i] / (1f + Mathf.Abs(b[i]));   // soft clip
        return b;
    }

    // ── The sounds ───────────────────────────────────────────────────────────
    static float[] Move()
    {
        var b = Buf(0.07f);
        AddTone(b, 0f, 0.07f, 2300f, 2150f, 0.30f, 0.011f, 0.15f);
        AddNoise(b, 0f, 0.01f, 0.25f, 0.002f, 0.6f);
        return Finish(b);
    }

    static float[] Adjust()
    {
        var b = Buf(0.07f);
        AddTone(b, 0f, 0.07f, 1500f, 1720f, 0.28f, 0.013f, 0.2f);
        AddNoise(b, 0f, 0.008f, 0.18f, 0.002f, 0.5f);
        return Finish(b);
    }

    static float[] Toggle()
    {
        var b = Buf(0.12f);
        AddTone(b, 0f,    0.06f, 1250f, 1250f, 0.25f, 0.012f, 0.2f);
        AddTone(b, 0.04f, 0.07f, 1880f, 1880f, 0.25f, 0.014f, 0.2f);
        return Finish(b);
    }

    static float[] Confirm()
    {
        var b = Buf(0.32f);
        AddTone(b, 0f,    0.09f, 720f,  1380f, 0.32f, 0.05f, 0.35f);
        AddTone(b, 0.05f, 0.25f, 1380f, 1400f, 0.22f, 0.07f, 0.25f, 0.1f);
        AddNoise(b, 0f, 0.03f, 0.22f, 0.006f, 0.35f);
        return Finish(b, 0.28f);
    }

    static float[] Back()
    {
        var b = Buf(0.22f);
        AddTone(b, 0f, 0.16f, 980f, 520f, 0.30f, 0.05f, 0.3f);
        AddNoise(b, 0f, 0.02f, 0.15f, 0.005f, 0.3f);
        return Finish(b, 0.2f);
    }

    static float[] Error()
    {
        var b = Buf(0.24f);
        AddTone(b, 0f,    0.08f, 170f, 160f, 0.35f, 0.05f, 0.5f, 0.35f);
        AddTone(b, 0.11f, 0.10f, 150f, 140f, 0.35f, 0.05f, 0.5f, 0.35f);
        return Finish(b);
    }

    static float[] Whoosh()
    {
        var b = Buf(0.55f);
        var rng = new System.Random(77);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float t = (float)i / b.Length;
            float w = (float)(rng.NextDouble() * 2.0 - 1.0);
            float coef = Mathf.Lerp(0.02f, 0.35f, Mathf.Sin(t * Mathf.PI));   // cutoff sweeps up then down
            lp  += (w - lp) * coef;
            lp2 += (lp - lp2) * coef;
            b[i] = (lp - lp2 * 0.6f) * Mathf.Sin(t * Mathf.PI) * 0.9f;
        }
        return Finish(b);
    }

    static float[] StartSting()
    {
        var b = Buf(1.1f);
        float[] notes = { 659.3f, 880f, 1318.5f, 1760f };
        for (int i = 0; i < notes.Length; i++)
            AddTone(b, i * 0.075f, 0.7f, notes[i], notes[i], 0.20f, 0.16f, 0.35f, 0.1f);
        AddTone(b, 0.30f, 0.8f, 2637f, 2637f, 0.06f, 0.30f);                    // shimmer
        AddNoise(b, 0f, 0.35f, 0.10f, 0.12f, 0.08f);                            // air
        return Finish(b, 0.32f);
    }

    static float[] TypeClick()
    {
        var b = Buf(0.03f);
        AddNoise(b, 0f, 0.03f, 0.35f, 0.004f, 0.8f);
        AddTone(b, 0f, 0.02f, 3200f, 3000f, 0.08f, 0.004f);
        return Finish(b);
    }

    static float[] Open()
    {
        var b = Buf(0.30f);
        AddTone(b, 0f, 0.26f, 320f, 900f, 0.18f, 0.12f, 0.25f);
        AddNoise(b, 0f, 0.25f, 0.10f, 0.08f, 0.12f);
        return Finish(b, 0.2f);
    }
}
