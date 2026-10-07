using UnityEngine;

/// Realistic volumetric clouds — a huge sky disc running the raymarching
/// CloudPlane shader (FBM density, Beer's-law absorption, sun-lit tops).
/// The disc follows the camera horizontally; the noise lives in WORLD space
/// and drifts with the profile's wind, so the clouds feel like a real sky,
/// not a dome decal.
///
/// Coverage / tint / wind come from Atmosphere.Current (F8 panel: CLOUDS
/// section — Coverage, Drift → wind speed, colour).
[DisallowMultipleComponent]
public class CloudLayer : MonoBehaviour
{
    const float DiscRadius = 9000f;
    const float BaseY      = 230f;   // above the tallest track sections
    const float Thickness  = 110f;

    Camera   cam;
    Material mat;

    public void Init(Camera camera) => cam = camera;

    /// Re-push the current Atmosphere profile into the cloud material.
    public void Rebuild() => Rebuild(Atmosphere.Current);

    public void Rebuild(Atmosphere.Profile atmo)
    {
        if (mat == null) Build();
        if (mat == null) return;

        var wind = new Vector4(atmo.cloudDrift * 14f, 0f, atmo.cloudDrift * 5f, 0f);

        mat.SetFloat("_Coverage", Mathf.Clamp01(atmo.cloudiness));
        mat.SetColor("_CloudColor", atmo.cloudColor);
        mat.SetColor("_SunColor",   atmo.sun * Mathf.Max(0.3f, atmo.sunIntensity));
        mat.SetColor("_AmbientSky", atmo.ambientSky);
        // Drift slider = wind strength; direction fixed, speed scaled up so the
        // 0..2 panel range covers calm → storm-racing skies.
        mat.SetVector("_Wind", wind);
        mat.SetVector("_SunDir", Quaternion.Euler(atmo.sunEuler) * Vector3.forward);
        mat.SetFloat("_BaseY",     BaseY);
        mat.SetFloat("_Thickness", Thickness);

        // Drifting cloud shadows on the lit ground (VertexColorLit reads these
        // globals and projects the same shape noise along the sun) — free "sun
        // spots" wandering over the grass, stronger under heavier skies.
        Shader.SetGlobalFloat ("_SD_CloudCoverage", Mathf.Clamp01(atmo.cloudiness));
        Shader.SetGlobalVector("_SD_CloudWind",     wind);
        Shader.SetGlobalFloat ("_SD_CloudShadow",   Mathf.Clamp01(atmo.cloudiness) * 0.55f);
    }

    void Build()
    {
        var shader = Shader.Find("SpatialDrift/CloudPlane");
        if (shader == null)
        {
            Debug.LogWarning("[CloudLayer] CloudPlane shader missing/stripped — run " +
                             "'Racing Game / Fix Build Shaders (Always Include)'.");
            return;
        }
        mat = new Material(shader) { name = "VolumetricCloudPlane" };

        var go = new GameObject("CloudDisc");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Disc();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        // Follow the camera horizontally; the disc sits at the cloud base.  The
        // shader samples world space, so moving the canvas never moves the sky.
        var p = cam.transform.position;
        transform.position = new Vector3(p.x, BaseY, p.z);
    }

    // Flat disc used purely as a raster canvas for the raymarch shader.
    static Mesh _disc;
    static Mesh Disc()
    {
        if (_disc != null) return _disc;
        const int segs = 24;
        var verts = new Vector3[segs + 2];
        var tris  = new int[segs * 6];
        verts[0] = Vector3.zero;
        for (int s = 0; s <= segs; s++)
        {
            float a = s / (float)segs * Mathf.PI * 2f;
            verts[s + 1] = new Vector3(Mathf.Cos(a) * DiscRadius, 0f, Mathf.Sin(a) * DiscRadius);
        }
        for (int s = 0; s < segs; s++)
        {
            tris[s * 6 + 0] = 0; tris[s * 6 + 1] = s + 2; tris[s * 6 + 2] = s + 1;   // seen from below
            tris[s * 6 + 3] = 0; tris[s * 6 + 4] = s + 1; tris[s * 6 + 5] = s + 2;   // and from above
        }
        _disc = new Mesh { name = "CloudDisc" };
        _disc.SetVertices(verts);
        _disc.SetTriangles(tris, 0);
        _disc.bounds = new Bounds(Vector3.zero, Vector3.one * DiscRadius * 3f);   // never culled
        return _disc;
    }
}
