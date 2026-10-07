using UnityEngine;

/// Emits a cyan particle trail from behind the car whenever boost is active.
/// Auto-builds its own ParticleSystem child on Awake — no manual setup.
/// Auto-added by CarController.Awake; you can tweak the public fields in the
/// inspector to change colour / speed / spread without touching code.
[DisallowMultipleComponent]
public class BoostEffects : MonoBehaviour
{
    [Header("References")]
    public GearSystem gears;

    [Header("Emitter")]
    [Tooltip("Local offset from the car's origin. Default is roughly the rear bumper.")]
    public Vector3 localEmitOffset = new Vector3(0f, 0.15f, -1.8f);

    [Header("Particles")]
    public Color particleColor   = new Color(0.30f, 0.85f, 1.0f, 1f);
    [Range(0.1f, 2f)]   public float particleLifetime = 0.55f;
    [Range(1f, 40f)]    public float particleSpeed    = 16f;
    [Range(10f, 400f)]  public float emitRate         = 160f;
    [Range(0.05f, 1f)]  public float startSize        = 0.35f;
    [Range(0f, 45f)]    public float coneAngle        = 18f;

    ParticleSystem ps;

    void Awake()
    {
        if (gears == null) gears = GetComponent<GearSystem>();

        var v = VfxTuning.Load();
        if (v != null)
        {
            particleColor    = v.boostParticleColor;
            particleLifetime = v.boostParticleLifetime;
            particleSpeed    = v.boostParticleSpeed;
            emitRate         = v.boostEmitRate;
            startSize        = v.boostStartSize;
        }

        Build();
    }

    void Build()
    {
        var go = new GameObject("BoostParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localEmitOffset;
        // Emit cone shoots "outward" along its local +Z by default, so spin
        // the emitter 180° around Y to shoot backwards relative to the car.
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startLifetime    = particleLifetime;
        main.startSpeed       = particleSpeed;
        main.startSize        = startSize;
        main.startColor       = particleColor;
        main.maxParticles     = 400;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.playOnAwake      = false;

        var emission = ps.emission;
        emission.rateOverTime = emitRate;
        emission.enabled      = false;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = coneAngle;
        shape.radius    = 0.28f;

        // Fade out + shrink near end of lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(particleColor, 0f), new GradientColorKey(particleColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        // Material — URP particle shader preferred, with fallbacks
        // Soft additive glow (FxMaterials) — the default particle material
        // drew hard opaque squares.
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = FxMaterials.Additive;

        ps.Play();   // play so emission can be toggled live without Restart()
    }

    void Update()
    {
        if (gears == null || ps == null) return;
        var emission = ps.emission;
        emission.enabled = gears.IsBoosting;
    }
}
