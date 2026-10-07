using UnityEngine;

/// Camera-attached speed lines: particles spawn in a ring slightly ahead of
/// the camera, simulated in world space.  As the camera flies through them
/// they stretch into streaks via the renderer's camera-velocity scale.
///
/// Intensity ramps from `minSpeed` → `maxSpeed`, so you only see lines when
/// you're genuinely fast.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class SpeedLinesEffect : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Auto-finds the CarController in the scene if left blank.")]
    public CarController car;

    [Header("Speed Range (m/s)")]
    [Tooltip("Below this speed: no streaks.")]
    public float minSpeed = 40f;
    [Tooltip("At or above this speed: streaks at full intensity.")]
    public float maxSpeed = 120f;

    [Header("Visuals")]
    public Color  lineColor          = new Color(1f, 1f, 1f, 0.40f);
    [Range(0f, 600f)]  public float maxEmitRate     = 220f;
    [Range(1f, 20f)]   public float ringRadius      = 7f;
    [Range(0f, 4f)]    public float ringThickness   = 1.5f;
    [Range(1f, 30f)]   public float aheadDistance   = 8f;
    [Range(0.1f, 1f)]  public float particleLifetime = 0.45f;
    [Range(0f, 30f)]   public float stretchLength   = 12f;

    [Header("Origin Offset (camera-local)")]
    [Tooltip("Vertical / horizontal shift of the emitter origin relative to the camera.")]
    public float verticalOffset   = 0f;
    public float horizontalOffset = 0f;

    ParticleSystem ps;

    void Awake()
    {
        if (car == null) car = CarController.FindLocalPlayer();

        var v = VfxTuning.Load();
        if (v != null)
        {
            minSpeed         = v.speedLinesMinSpeed;
            maxSpeed         = v.speedLinesMaxSpeed;
            lineColor        = v.speedLinesColor;
            maxEmitRate      = v.speedLinesMaxEmitRate;
            stretchLength    = v.speedLinesStretch;
            aheadDistance    = v.speedLinesAheadDistance;
            verticalOffset   = v.speedLinesVerticalOffset;
            horizontalOffset = v.speedLinesHorizontalOffset;
            ringRadius       = v.speedLinesRingRadius;
            ringThickness    = v.speedLinesRingThickness;
            particleLifetime = v.speedLinesLifetime;
        }

        Build();
    }

    void Build()
    {
        var go = new GameObject("SpeedLines");
        go.transform.SetParent(transform, false);

        ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startLifetime   = particleLifetime;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor      = lineColor;
        main.maxParticles    = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = true;

        var emission = ps.emission;
        emission.rateOverTime = 0f;     // gated by speed each frame

        var shape = ps.shape;
        shape.shapeType   = ParticleSystemShapeType.Donut;
        shape.radius      = ringRadius;
        shape.donutRadius = ringThickness;
        shape.rotation    = new Vector3(90f, 0f, 0f);   // ring faces camera forward

        // Fade out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(lineColor, 0f), new GradientColorKey(lineColor, 1f) },
            new[] { new GradientAlphaKey(lineColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        // Stretch billboards along the camera's velocity vector — this is what
        // makes static particles read as streaks when the camera moves.
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode          = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 1f;
        renderer.velocityScale       = 0f;
        renderer.lengthScale         = stretchLength;

        var shader   = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Sprites/Default");
        renderer.material = new Material(shader) { color = Color.white };
    }

    float _lastEmitRate = -1f;

    void LateUpdate()
    {
        if (ps == null) return;
        if (car == null) car = CarController.FindLocalPlayer();   // only runs while unresolved

        // Position emitter ahead of the camera (plus the local origin offset)
        // each frame so the camera is constantly flying into fresh particles.
        ps.transform.position = transform.position
                              + transform.forward * aheadDistance
                              + transform.up      * verticalOffset
                              + transform.right   * horizontalOffset;
        ps.transform.rotation = transform.rotation;

        float speed = car != null ? car.Speed : 0f;
        float t     = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
        float rate  = t * maxEmitRate;
        // ParticleSystem.emission is a struct accessor; only write it back when
        // the value meaningfully changed to avoid redundant module updates.
        if (!Mathf.Approximately(rate, _lastEmitRate))
        {
            var emission = ps.emission;
            emission.rateOverTime = rate;
            _lastEmitRate = rate;
        }
    }
}
