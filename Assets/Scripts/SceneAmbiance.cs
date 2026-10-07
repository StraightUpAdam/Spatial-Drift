using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Environment dressing, all procedural: a gradient skydome with a glowing
/// horizon, exponential fog for depth, and a global Bloom volume so the car's
/// emissive accents and the neon track edges actually glow.  Auto-added to the
/// camera by RacingCamera.Awake, so every scene gets it with no setup.
///
/// Sky / fog / ambient all come from Atmosphere.Current — real-life daylight by
/// default (the purple void is the Spatial Drift zone's identity now).
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class SceneAmbiance : MonoBehaviour
{
    [Header("Sky Gradient (overwritten from Atmosphere.Current in Awake)")]
    public Color skyTop     = new Color(0.32f, 0.56f, 0.92f);
    public Color skyHorizon = new Color(0.80f, 0.88f, 0.96f);
    public Color skyBottom  = new Color(0.45f, 0.52f, 0.48f);
    [Tooltip("Kept SMALL on purpose: the dome follows the camera and draws in the " +
             "Background queue (behind everything, ZWrite off), so its physical size " +
             "only matters to FOG — at 1500 the fog swallowed the gradient whole and " +
             "the sky rendered as one flat colour.")]
    public float domeRadius = 80f;

    [Header("Fog (overwritten from Atmosphere.Current in Awake)")]
    public bool  enableFog  = true;
    public Color fogColor   = new Color(0.76f, 0.84f, 0.93f);
    public float fogDensity = 0.0045f;

    [Header("Bloom")]
    [Range(0f, 3f)] public float bloomIntensity = 1.0f;
    [Range(0f, 1.5f)] public float bloomThreshold = 0.80f;

    [Header("Menus")]
    [Tooltip("Menu scenes dress with Atmosphere.Menu (violet dusk) instead of the " +
             "race weather, so browsing menus never changes the next race's sky.")]
    public bool menuMood;

    Transform     dome;
    Camera        cam;
    VolumeProfile profile;
    CloudLayer    clouds;

    /// The profile this scene is currently dressed with.
    public Atmosphere.Profile Dressing { get; private set; }

    void Awake()
    {
        cam = GetComponent<Camera>();
        // The volumetric cloud disc reaches ~9 km; the default 1000 m far plane
        // sliced it to a strip at the top of the screen ("invisible horizon").
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 12000f);
        ApplyAntiAliasing();

        // Make sure sun shadows reach a useful distance (the default URP asset
        // value can be very short, which reads as "no shadows at all").
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
            && urp.shadowDistance < 150f)
            urp.shadowDistance = 150f;

        BuildPostStack();
        Redress();
    }

    /// Options ▸ Display ▸ Anti-aliasing (SMAA on/off), re-applied live.
    public void ApplyAntiAliasing()
    {
        var camData = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        if (camData == null) return;
        camData.renderPostProcessing = true;
        // Clean edges are half of "looks AAA" — SMAA is cheap and sharp.
        camData.antialiasing = GameSettings.AntiAliasing
            ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
            : AntialiasingMode.None;
    }

    /// (Re)dress the live scene from Atmosphere.Current (or the menu mood) —
    /// sky dome colours, fog, ambient, camera background and the sun.  Called
    /// once from Awake and again by the F8 atmosphere panel on every change.
    public void Redress() => Redress(menuMood ? Atmosphere.Menu : Atmosphere.Current);

    /// Dress the live scene with an explicit profile (menu weather previews).
    public void Redress(Atmosphere.Profile atmo)
    {
        Dressing   = atmo;
        skyTop     = atmo.skyTop;
        skyHorizon = atmo.skyHorizon;
        skyBottom  = atmo.skyBottom;
        fogColor   = atmo.fog;
        fogDensity = atmo.fogDensity;

        Atmosphere.ApplyAmbient(atmo);

        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyBottom;   // dome draws over this, but covers any gap

        BuildFog();
        if (dome == null) BuildDome();
        else
        {
            var mf = dome.GetComponent<MeshFilter>();
            if (mf != null)
            {
                if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                mf.sharedMesh = BuildGradientSphere();
            }
        }

        // Restyle whatever directional light the scene has as this profile's sun.
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { Atmosphere.ApplySun(l, atmo); break; }

        // Volumetric cloud layer (coverage / tint / drift from the profile).
        if (clouds == null)
        {
            clouds = new GameObject("CloudLayer").AddComponent<CloudLayer>();
            clouds.Init(cam);
        }
        clouds.Rebuild(atmo);
    }

    void OnEnable()  => GameSettings.Changed += ApplyAntiAliasing;
    void OnDisable() => GameSettings.Changed -= ApplyAntiAliasing;

    void OnDestroy()
    {
        if (profile != null) Destroy(profile);
        if (enableFog) RenderSettings.fog = false;
    }

    // Bloom + filmic tonemapping + a gentle grade + vignette.  ACES tonemapping
    // is the single biggest "stops looking like a raw engine" switch — it rolls
    // highlights off filmically instead of clipping to white.
    void BuildPostStack()
    {
        profile           = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.overrideState = true; bloom.intensity.value = bloomIntensity;
        bloom.threshold.overrideState = true; bloom.threshold.value = bloomThreshold;
        bloom.scatter.overrideState   = true; bloom.scatter.value   = 0.65f;

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.overrideState = true;
        tone.mode.value         = TonemappingMode.ACES;

        var grade = profile.Add<ColorAdjustments>(true);
        grade.contrast.overrideState     = true; grade.contrast.value     = 10f;
        grade.saturation.overrideState   = true; grade.saturation.value   = 8f;
        grade.postExposure.overrideState = true; grade.postExposure.value = 0.25f;   // ACES darkens a touch

        var vig = profile.Add<Vignette>(true);
        vig.intensity.overrideState  = true; vig.intensity.value  = 0.18f;
        vig.smoothness.overrideState = true; vig.smoothness.value = 0.45f;

        // Put the global volume on a child so it doesn't clash with WarpEffect's
        // volume on the camera itself.
        var volGO = new GameObject("AmbianceVolume");
        volGO.transform.SetParent(transform, false);
        var vol           = volGO.AddComponent<Volume>();
        vol.isGlobal      = true;
        vol.priority      = 50f;          // below WarpEffect (100) so warp stacks on top
        vol.sharedProfile = profile;
    }

    void BuildFog()
    {
        if (!enableFog) return;
        RenderSettings.fog        = true;
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        RenderSettings.fogColor   = fogColor;
        RenderSettings.fogDensity = fogDensity;
    }

    void BuildDome()
    {
        var go = new GameObject("SkyDome");
        dome   = go.transform;
        dome.localScale = Vector3.one * domeRadius;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = BuildGradientSphere();

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { color = Color.white };
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)RenderQueue.Background;
        mr.sharedMaterial    = mat;
        mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows     = false;
    }

    // Inverted (inside-out) UV sphere with a vertical colour gradient baked into
    // vertex colours — a horizon glow band around the equator.
    Mesh BuildGradientSphere()
    {
        const int rings = 24, segs = 32;
        var verts  = new System.Collections.Generic.List<Vector3>();
        var cols   = new System.Collections.Generic.List<Color>();
        var tris   = new System.Collections.Generic.List<int>();

        for (int r = 0; r <= rings; r++)
        {
            float v   = (float)r / rings;            // 0 bottom → 1 top
            float phi = v * Mathf.PI;
            float y   = Mathf.Cos(phi);
            float rad = Mathf.Sin(phi);

            float t = (y + 1f) * 0.5f;               // 0 bottom, 0.5 horizon, 1 top
            float d = Mathf.Abs(t - 0.5f) * 2f;      // distance from horizon
            Color c = Color.Lerp(skyHorizon, t > 0.5f ? skyTop : skyBottom, Mathf.Pow(d, 0.7f));

            for (int s = 0; s <= segs; s++)
            {
                float u  = (float)s / segs;
                float th = u * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(th) * rad, y, Mathf.Sin(th) * rad));
                cols.Add(c);
            }
        }

        int stride = segs + 1;
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segs; s++)
        {
            int a = r * stride + s;
            int b = a + 1;
            int cc = a + stride;
            int d2 = cc + 1;
            // Reversed winding so the INSIDE of the dome is front-facing.
            tris.Add(a); tris.Add(cc); tris.Add(b);
            tris.Add(b); tris.Add(cc); tris.Add(d2);
        }

        var mesh = new Mesh { name = "SkyDomeGradient" };
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);   // never culled
        return mesh;
    }

    void LateUpdate()
    {
        if (dome != null) dome.position = transform.position;   // follow camera (no rotation)
    }
}
