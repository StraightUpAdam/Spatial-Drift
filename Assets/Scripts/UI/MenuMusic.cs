using UnityEngine;

/// Menu music that follows you through every menu scene and fades out when a
/// race (or the editor) loads.  With no track supplied it plays a procedural
/// synthwave loop (96 BPM, Am9 – Fmaj9 – Cmaj9 – G6/9: detuned pad with a
/// sidechain pump, offbeat bass, a soft kick and a ping-pong-delayed arp that
/// build in over the first bars) generated live on the audio thread.
///
/// Drop your own track at Resources/UI/Audio/menu_music and it plays instead.
/// Volume follows Options ▸ Audio ▸ Music.
public class MenuMusic : MonoBehaviour
{
    static MenuMusic _i;

    public static void Ensure()
    {
        if (_i != null) return;
        var go = new GameObject("[MenuMusic]");
        DontDestroyOnLoad(go);
        _i = go.AddComponent<MenuMusic>();
    }

    /// Menu scenes ask for music; everything else lets it fade away.
    public static void SetWanted(bool on)
    {
        Ensure();
        _i.wanted = on;
    }

    AudioSource src;
    bool  wanted;
    bool  synth;
    float fade;
    volatile float outVol;     // read on the audio thread

    void Awake()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.loop                = true;
        src.playOnAwake         = false;
        src.spatialBlend        = 0f;
        src.ignoreListenerPause = true;
        src.priority            = 0;

        var track = Resources.Load<AudioClip>("UI/Audio/menu_music");
        if (track != null)
        {
            src.clip = track;
            synth    = false;
        }
        else
        {
            synth = true;
            sr    = AudioSettings.outputSampleRate;
            // OnAudioFilterRead generates the music; the source just has to be
            // "playing" something for the filter to run — one second of silence.
            // (Stereo, so the filter is handed interleaved L/R data.)
            var silence = AudioClip.Create("silence", sr, 2, sr, false);
            silence.SetData(new float[sr * 2], 0);
            src.clip = silence;
            InitSynth();
        }
    }

    void Update()
    {
        float target = wanted ? 1f : 0f;
        float rate   = wanted ? 1f / 2.5f : 1f / 0.9f;
        fade = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime * rate);
        float vol = fade * fade * GameSettings.MusicVolume;

        if (synth) { outVol = vol; src.volume = 1f; }
        else         src.volume = vol * 0.7f;

        if (fade > 0f && !src.isPlaying) src.Play();
        else if (fade <= 0f && src.isPlaying) src.Stop();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Procedural synthwave (audio thread)
    // ═════════════════════════════════════════════════════════════════════════
    const int   Table = 2048;
    const double Bpm  = 96.0;

    int      sr = 48000;
    float[]  sawT, triT;
    long     n;
    double   spb;                     // seconds per beat

    static readonly int[]   Bass  = { 45, 41, 48, 43 };
    static readonly int[][] Pads  =
    {
        new[] { 57, 60, 64, 67, 71 },   // Am9
        new[] { 53, 57, 60, 64, 67 },   // Fmaj9
        new[] { 55, 60, 62, 64, 71 },   // Cmaj9
        new[] { 55, 59, 62, 64, 69 },   // G6/9
    };
    static readonly int[] ArpPattern = { 0, 2, 1, 3, 2, 4, 3, 1 };

    int      curChord = -1, prevChord = -1;
    double   chordStart;
    readonly double[] curPh  = new double[10];
    readonly double[] prevPh = new double[10];
    double   bassPh, kickPh, arpPh;
    float    padL, padR, bassLp;
    float[]  dlyL, dlyR;
    int      dlyPos;
    System.Random rng = new System.Random(7);

    void InitSynth()
    {
        spb  = 60.0 / Bpm;
        sawT = new float[Table];
        triT = new float[Table];
        for (int i = 0; i < Table; i++)
        {
            double p = (double)i / Table;
            double s = 0;
            for (int h = 1; h <= 7; h++) s += System.Math.Sin(2 * System.Math.PI * h * p) / h;
            sawT[i] = (float)(s * 0.55);
            triT[i] = (float)(1.0 - 4.0 * System.Math.Abs(p - 0.5));
        }
        int dl = Mathf.Max(1, (int)(spb * 0.75 * sr));   // dotted-eighth ping-pong
        dlyL = new float[dl];
        dlyR = new float[dl];
    }

    // MIDI note → Hz, precomputed (no Math.Pow on the audio thread).
    static readonly double[] _hz = BuildHz();
    static double[] BuildHz()
    {
        var t = new double[128];
        for (int m = 0; m < 128; m++) t[m] = 440.0 * System.Math.Pow(2.0, (m - 69) / 12.0);
        return t;
    }
    static double Hz(int midi) => _hz[midi & 127];
    static float Smooth01(double x) { float t = Mathf.Clamp01((float)x); return t * t * (3f - 2f * t); }

    float Tab(float[] tab, double ph) => tab[(int)((ph - System.Math.Floor(ph)) * Table) & (Table - 1)];

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!synth || sawT == null) return;
        float vol = outVol;
        if (vol <= 0.0001f) { System.Array.Clear(data, 0, data.Length); n += data.Length / channels; return; }

        double invSr   = 1.0 / sr;
        double chordLen = spb * 8.0;                       // two bars per chord
        float  lpCoef  = 0f;

        for (int i = 0; i < data.Length; i += channels)
        {
            double t = n * invSr;
            n++;

            // ── Chord changes (crossfaded) ───────────────────────────────────
            int chord = (int)(t / chordLen) % 4;
            if (chord != curChord)
            {
                System.Array.Copy(curPh, prevPh, 10);
                prevChord  = curChord;
                curChord   = chord;
                chordStart = t;
                for (int k = 0; k < 10; k++) curPh[k] = rng.NextDouble();
            }
            float gCur  = Smooth01((t - chordStart) / 1.8);
            float gPrev = prevChord >= 0 ? 1f - gCur : 0f;

            if ((i & 63) == 0)
            {
                double fc = 950.0 + 650.0 * System.Math.Sin(t * 2 * System.Math.PI / 40.0);
                lpCoef = (float)(1.0 - System.Math.Exp(-2 * System.Math.PI * fc * invSr));
            }

            // ── Pad: 5 notes × 2 detuned soft-saws, wide stereo ──────────────
            float L = 0f, R = 0f;
            PadVoice(Pads[curChord], curPh, gCur, invSr, ref L, ref R);
            if (gPrev > 0.001f) PadVoice(Pads[prevChord], prevPh, gPrev, invSr, ref L, ref R);
            padL += (L * 0.2f - padL) * lpCoef;
            padR += (R * 0.2f - padR) * lpCoef;

            // ── Rhythm clock ─────────────────────────────────────────────────
            double beatT  = t % spb;
            double pump   = 0.62 + 0.38 * (1.0 - System.Math.Exp(-beatT * 7.0));
            float  build  = Smooth01((t - 4.0) / 6.0);        // arp fades in
            float  drums  = Smooth01((t - 9.0) / 3.0);        // then the kick

            // Soft kick on every beat.
            if (beatT < invSr * 1.5) kickPh = 0;
            double kf = 46.0 + 80.0 * System.Math.Exp(-beatT / 0.028);
            kickPh += kf * invSr;
            float kick = (float)(System.Math.Sin(2 * System.Math.PI * kickPh) * System.Math.Exp(-beatT / 0.17)) * 0.42f * drums;

            // Offbeat bass.
            double eighth = spb * 0.5;
            double sinceOff = (t + eighth) % spb;               // 0 at each offbeat
            bassPh += Hz(Bass[curChord]) * invSr;
            float bassRaw = (float)((System.Math.Sin(2 * System.Math.PI * bassPh)
                                   + 0.3 * System.Math.Sin(4 * System.Math.PI * bassPh))
                                   * System.Math.Exp(-sinceOff / 0.24));
            bassLp += (bassRaw - bassLp) * 0.08f;
            float bass = bassLp * 0.30f * (0.35f + 0.65f * build);

            // 16th arp through the chord (octave up), plucky triangle.
            double sixteenth = spb * 0.25;
            long   step      = (long)(t / sixteenth);
            double since16   = t - step * sixteenth;
            int    note      = Pads[curChord][ArpPattern[step % ArpPattern.Length]] + 12;
            arpPh += Hz(note) * invSr;
            float arp = Tab(triT, arpPh) * (float)System.Math.Exp(-since16 / 0.085) * 0.085f * build;

            // Ping-pong delay on the arp.
            float dL = dlyL[dlyPos], dR = dlyR[dlyPos];
            dlyL[dlyPos] = arp + dR * 0.38f;
            dlyR[dlyPos] = dL * 0.38f;
            if (++dlyPos >= dlyL.Length) dlyPos = 0;

            float p = (float)pump;
            float outL = padL * p + bass + kick + arp * p + dL * 0.42f;
            float outR = padR * p + bass + kick + arp * p + dR * 0.42f;

            outL = outL / (1f + Mathf.Abs(outL)) * vol * 0.8f;
            outR = outR / (1f + Mathf.Abs(outR)) * vol * 0.8f;

            data[i] = outL;
            if (channels > 1) data[i + 1] = outR;
            for (int c = 2; c < channels; c++) data[i + c] = (outL + outR) * 0.5f;
        }
    }

    void PadVoice(int[] notes, double[] ph, float gain, double invSr, ref float L, ref float R)
    {
        if (gain <= 0f) return;
        for (int k = 0; k < notes.Length; k++)
        {
            double f = Hz(notes[k]);
            ph[k * 2]     += f * 1.0035 * invSr;
            ph[k * 2 + 1] += f * 0.9965 * invSr;
            float a = Tab(sawT, ph[k * 2]);
            float b = Tab(sawT, ph[k * 2 + 1]);
            L += (a * 0.62f + b * 0.38f) * gain;
            R += (a * 0.38f + b * 0.62f) * gain;
        }
    }
}
