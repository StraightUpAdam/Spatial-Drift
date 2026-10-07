using UnityEngine;

/// Twin spot headlights on the car's nose, automatically on in dark weathers
/// (Night / Storm — Atmosphere.HeadlightsOn).  Auto-added to every car by
/// CarController.Awake, so AI and remote multiplayer cars light up too.
///
/// The road/terrain use the custom VertexColorLit shader, which samples URP's
/// additional lights — that's what makes these actually pool on the asphalt.
[DisallowMultipleComponent]
public class Headlights : MonoBehaviour
{
    [Range(10f, 120f)] public float range      = 55f;
    [Range(20f, 90f)]  public float spotAngle  = 52f;
    [Range(0f, 8f)]    public float intensity  = 3.2f;
    public Color beamColor = new Color(1f, 0.94f, 0.78f);

    Light left, right;
    float checkTimer;

    void Start()
    {
        left  = BuildLamp("Headlight_L", new Vector3(-0.55f, 0.50f, 1.9f));
        right = BuildLamp("Headlight_R", new Vector3( 0.55f, 0.50f, 1.9f));
        Apply(Atmosphere.HeadlightsOn);
    }

    Light BuildLamp(string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);   // slight downward throw

        var l = go.AddComponent<Light>();
        l.type       = LightType.Spot;
        l.range      = range;
        l.spotAngle  = spotAngle;
        l.innerSpotAngle = spotAngle * 0.45f;
        l.intensity  = intensity;
        l.color      = beamColor;
        l.shadows    = LightShadows.None;   // spot shadows × many cars = too dear
        return l;
    }

    void Update()
    {
        // Weather can change live (F8 panel) — poll cheaply.
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = 0.5f;
        bool on = Atmosphere.HeadlightsOn;
        if (left != null && left.enabled != on) Apply(on);
    }

    void Apply(bool on)
    {
        if (left  != null) left.enabled  = on;
        if (right != null) right.enabled = on;
    }
}
