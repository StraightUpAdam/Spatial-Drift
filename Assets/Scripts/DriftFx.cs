using UnityEngine;

/// Drift sparks + tyre smoke from the rear wheels.
///
/// Sparks step up through three tiers as the drift charges its release boost
/// (CarController.DriftCharge) so you can SEE the boost you're banking:
///   tier 0  hot white-gold      (just started — little or no boost yet)
///   tier 1  electric cyan       (a solid boost)
///   tier 2  magenta             (maxed)
/// Each tier-up pops a little burst and a "ching"; letting go of a charged
/// drift fires a final burst in the tier colour.  Smoke rolls off the tyres in
/// proportion to the slide angle.  Auto-added by CarController.Awake.
[DisallowMultipleComponent]
public class DriftFx : MonoBehaviour
{
    public Vector3 leftRearLocal  = new Vector3(-0.78f, 0.06f, -1.45f);
    public Vector3 rightRearLocal = new Vector3( 0.78f, 0.06f, -1.45f);

    static readonly Color[] TierColors =
    {
        new Color(1.00f, 0.80f, 0.45f),
        new Color(0.30f, 0.85f, 1.00f),
        new Color(1.00f, 0.30f, 0.95f),
    };
    static readonly float[] TierAt = { 0f, 0.35f, 0.80f };   // DriftCharge thresholds

    CarController car;
    CarAudio      sfx;
    ParticleSystem sparkL, sparkR, smokeL, smokeR;
    int tier = -1;

    void Awake()
    {
        car = GetComponent<CarController>();
        sfx = GetComponent<CarAudio>();
        sparkL = BuildSparks("DriftSparksL", leftRearLocal);
        sparkR = BuildSparks("DriftSparksR", rightRearLocal);
        smokeL = BuildSmoke("DriftSmokeL", leftRearLocal);
        smokeR = BuildSmoke("DriftSmokeR", rightRearLocal);
        if (car != null) car.OnDriftEnd += HandleDriftEnd;
    }

    void OnDestroy()
    {
        if (car != null) car.OnDriftEnd -= HandleDriftEnd;
    }

    bool IsLocal => car != null && CarController.FindLocalPlayer() == car;

    void Update()
    {
        if (car == null) return;
        if (sfx == null) sfx = GetComponent<CarAudio>();

        bool  drift = car.IsDrifting && car.Speed > 8f && !car.spatialDriftActive;
        float slide = Mathf.Clamp01(car.SlideAngle / 32f);
        int   t     = TierOf(car.DriftCharge);

        if (drift)
        {
            if (tier >= 0 && t > tier)
            {
                Burst(t, 26);
                if (IsLocal) sfx?.PlayDriftTier(t);
            }
            tier = t;
        }
        else tier = -1;

        float sparkRate = drift ? Mathf.Lerp(60f, 180f, slide) * (0.7f + 0.3f * t) : 0f;
        float smokeRate = drift ? Mathf.Lerp(10f, 46f, slide) : 0f;
        Color c = TierColors[Mathf.Max(0, t)];
        SetRate(sparkL, sparkRate, c);
        SetRate(sparkR, sparkRate, c);
        SetRate(smokeL, smokeRate, null);
        SetRate(smokeR, smokeRate, null);
    }

    static int TierOf(float charge) => charge >= TierAt[2] ? 2 : charge >= TierAt[1] ? 1 : 0;

    void HandleDriftEnd(float charge)
    {
        if (charge < 0.12f) return;
        Burst(TierOf(charge), Mathf.RoundToInt(Mathf.Lerp(20f, 70f, charge)));
    }

    void Burst(int t, int count)
    {
        var c = TierColors[Mathf.Clamp(t, 0, 2)];
        foreach (var ps in new[] { sparkL, sparkR })
        {
            if (ps == null) continue;
            var ep = new ParticleSystem.EmitParams
            {
                startColor = c,
                applyShapeToPosition = true,
            };
            ps.Emit(ep, count / 2);
        }
    }

    static void SetRate(ParticleSystem ps, float rate, Color? col)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = rate;
        if (col.HasValue)
        {
            var main = ps.main;
            main.startColor = col.Value;
        }
    }

    // ── Builders ──────────────────────────────────────────────────────────────
    ParticleSystem BuildSparks(string name, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(-18f, 180f, 0f);   // spray back + up

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 13f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startColor      = TierColors[0];
        main.gravityModifier = 1.6f;
        main.maxParticles    = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle     = 28f;
        sh.radius    = 0.12f;
        var inh = ps.inheritVelocity;
        inh.enabled = true;
        inh.mode    = ParticleSystemInheritVelocityMode.Initial;
        inh.curveMultiplier = 0.85f;     // ride along with the car at speed, fan out behind
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode     = ParticleSystemRenderMode.Stretch;
        r.velocityScale  = 0.03f;
        r.lengthScale    = 2.2f;
        r.sharedMaterial = FxMaterials.Additive;
        ps.Play();
        return ps;
    }

    ParticleSystem BuildSmoke(string name, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local + new Vector3(0f, 0.15f, -0.2f);
        go.transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);   // drift up

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = new Color(0.88f, 0.86f, 0.92f, 0.45f);
        main.maxParticles    = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle     = 35f;
        sh.radius    = 0.25f;
        var inh = ps.inheritVelocity;
        inh.enabled = true;
        inh.mode    = ParticleSystemInheritVelocityMode.Initial;
        inh.curveMultiplier = 0.6f;      // linger in view instead of vanishing at 80 m/s
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3.2f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode     = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = FxMaterials.AlphaBlend;
        ps.Play();
        return ps;
    }
}
