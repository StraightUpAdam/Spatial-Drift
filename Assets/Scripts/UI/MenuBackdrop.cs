using UnityEngine;

/// The living world behind the menus: the same sky, volumetric clouds, fog
/// and grass plains the races use, dressed in the menu's dusk mood (the
/// camera's SceneAmbiance has menuMood on).  Optionally drifts the camera in
/// a slow orbit so the clouds and their shadows keep the frame alive.
///
/// Also the hook for weather PREVIEWS: the race-setup screens call
/// Preview(profile) and the sky cross-fades to it live.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MenuBackdrop : MonoBehaviour
{
    [Header("Camera drift (off when another script drives the camera)")]
    public bool  driftCamera = true;
    public float height      = 9f;
    public float pitch       = -3f;
    public float yawSpeed    = 1.4f;
    public float startYaw    = 20f;

    [Header("World")]
    public bool buildPlains = true;

    SceneAmbiance ambiance;
    float yaw;

    // Weather preview cross-fade.
    Atmosphere.Profile from, to;
    float blend = 1f;
    bool  cycling;
    float cycleTimer;
    int   cycleIndex;

    void Start()
    {
        yaw = startYaw;
        ambiance = GetComponent<SceneAmbiance>();
        if (buildPlains) TrackScenery.BuildPlains(Vector3.zero, 6000f, 0f);
        if (ambiance != null) { from = to = ambiance.Dressing; }
    }

    /// Cross-fade the sky to a race weather (or back to the menu mood).
    public void Preview(Atmosphere.Profile p)
    {
        cycling = false;
        StartBlend(p);
    }

    /// "Random" weather: keep cycling through the presets.
    public void PreviewCycle()
    {
        cycling    = true;
        cycleTimer = 0f;
    }

    public void PreviewMenuMood() => Preview(Atmosphere.Menu);

    void StartBlend(Atmosphere.Profile p)
    {
        if (ambiance == null) return;
        from  = ambiance.Dressing;
        to    = p;
        blend = 0f;
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        if (driftCamera)
        {
            yaw += yawSpeed * dt;
            float bob = Mathf.Sin(Time.unscaledTime * 0.21f) * 0.6f;
            transform.SetPositionAndRotation(new Vector3(0f, height + bob, 0f),
                                             Quaternion.Euler(pitch, yaw, 0f));
        }

        if (cycling)
        {
            cycleTimer -= dt;
            if (cycleTimer <= 0f)
            {
                cycleTimer = 2.6f;
                cycleIndex = (cycleIndex + 1) % Atmosphere.Presets.Length;
                StartBlend(Atmosphere.Presets[cycleIndex]);
            }
        }

        if (blend < 1f && ambiance != null)
        {
            blend = Mathf.Min(1f, blend + dt / 0.9f);
            ambiance.Redress(Atmosphere.Lerp(from, to, UITheme.EaseInOutCubic(blend)));
        }
    }
}
