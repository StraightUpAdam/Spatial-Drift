using UnityEngine;
using UnityEngine.UI;

/// Forza-style menu background: the player's car on a display platform in the
/// open plains under the menu's dusk sky, the camera panning through a set of
/// framed shots and dipping to black between each.  One car today; when more
/// cars exist this is where the roster rotates.
///
/// Garage mode (main menu ▸ GARAGE): cuts to a slow, close orbit that frames
/// the car on the right of the screen while the garage panel sits on the left;
/// SetPaint() repaints the car live.
[DisallowMultipleComponent]
public class CarShowcase : MonoBehaviour
{
    [Header("Baked references (wired by the scene builder)")]
    public Camera cam;
    public Image  shotFade;      // full-screen black on its own low canvas

    [Header("Shots")]
    public float shotDuration = 6.5f;
    public float fadeTime     = 0.45f;

    // (start angle°, orbit °/s, radius, cam height, look height, dolly m/s)
    static readonly float[,] Shots =
    {
        { 205f,  4.5f,  7.5f, 1.2f, 0.8f, -0.25f },   // low front 3/4, slow push-in
        {  90f, -3.0f,  9.5f, 2.6f, 1.0f,  0.00f },   // side profile drift
        { 330f,  5.5f,  6.0f, 0.8f, 0.9f,  0.15f },   // rear wing, rising pull-out
        { 150f, -4.0f, 11.0f, 3.4f, 0.6f, -0.35f },   // high sweep down the nose
    };

    Transform car;
    CarVisual visual;
    int   shot;
    float shotTimer;
    float spin;

    bool  garage, pendingGarage;
    float garageAngle = 220f;
    float cutFade;          // quick dip used when switching garage ↔ shots (cut at its darkest)

    void Start()
    {
        BuildStage();
        shotTimer = shotDuration;
        Apply(0f);
    }

    void BuildStage()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Platform — a wide dark disc with a glowing accent rim.
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "ShowcasePlatform";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.position   = new Vector3(0f, 0.05f, 0f);
        disc.transform.localScale = new Vector3(11f, 0.10f, 11f);
        var discMat = new Material(lit) { color = new Color(0.10f, 0.10f, 0.13f) };
        if (discMat.HasProperty("_Smoothness")) discMat.SetFloat("_Smoothness", 0.75f);
        if (discMat.HasProperty("_Metallic"))   discMat.SetFloat("_Metallic", 0.4f);
        disc.GetComponent<Renderer>().sharedMaterial = discMat;

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "ShowcaseRim";
        Destroy(rim.GetComponent<Collider>());
        rim.transform.position   = new Vector3(0f, 0.02f, 0f);
        rim.transform.localScale = new Vector3(11.6f, 0.05f, 11.6f);
        var rimMat = new Material(lit) { color = UITheme.Accent };
        if (rimMat.HasProperty("_EmissionColor"))
        {
            rimMat.EnableKeyword("_EMISSION");
            rimMat.SetColor("_EmissionColor", UITheme.Accent * 2.2f);
        }
        rim.GetComponent<Renderer>().sharedMaterial = rimMat;

        // The car — Body cube seed; CarVisual swaps in the procedural model.
        var carGO = new GameObject("ShowcaseCar");
        var body  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(carGO.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.transform.localScale    = new Vector3(1.8f, 0.6f, 3.6f);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial =
            new Material(lit) { color = PlayerProfile.LocalColor };
        visual = carGO.AddComponent<CarVisual>();
        carGO.transform.position = new Vector3(0f, 0.45f, 0f);
        car = carGO.transform;

        // A soft key light on the car so paint reads even at dusk.
        var keyGO = new GameObject("ShowcaseKey");
        keyGO.transform.position = new Vector3(3.5f, 5f, -4f);
        var key = keyGO.AddComponent<Light>();
        key.type      = LightType.Point;
        key.range     = 14f;
        key.intensity = 2.2f;
        key.color     = new Color(1f, 0.9f, 0.95f);
    }

    public void SetPaint(Color c)
    {
        if (visual != null) visual.SetColor(c);
    }

    public void SetGarage(bool on)
    {
        if (pendingGarage == on) return;
        pendingGarage = on;
        cutFade = 1f;           // the actual camera cut happens at the dip's darkest frame
    }

    void LateUpdate()
    {
        if (cam == null || car == null) return;
        float dt = Time.unscaledDeltaTime;

        // The car turns lazily on its platform (slower in the garage).
        spin += (garage ? 3f : 6f) * dt;
        car.rotation = Quaternion.Euler(0f, spin, 0f);

        // Cut between showcase shots and the garage orbit at the darkest point.
        cutFade = Mathf.MoveTowards(cutFade, 0f, dt / 0.4f);
        if (garage != pendingGarage && cutFade <= 0.5f)
        {
            garage = pendingGarage;
            if (!garage) { shot = (shot + 1) % Shots.GetLength(0); shotTimer = shotDuration; }
        }

        float black;
        if (garage)
        {
            garageAngle += 7f * dt;
            float a = garageAngle * Mathf.Deg2Rad;
            // Frame the car right-of-centre: aim a little left of it.
            var pos = new Vector3(Mathf.Cos(a) * 6.6f, 1.7f, Mathf.Sin(a) * 6.6f);
            cam.transform.position = pos;
            var toCar = (new Vector3(0f, 0.7f, 0f) - pos).normalized;
            var right = Vector3.Cross(Vector3.up, toCar).normalized;
            cam.transform.rotation = Quaternion.LookRotation(toCar + right * -0.33f, Vector3.up);
            black = 0f;
        }
        else
        {
            shotTimer -= dt;
            if (shotTimer <= 0f)
            {
                shot      = (shot + 1) % Shots.GetLength(0);
                shotTimer = shotDuration;
            }
            Apply(shotDuration - shotTimer);

            float tIn  = Mathf.Clamp01((shotDuration - shotTimer) / fadeTime);
            float tOut = Mathf.Clamp01(shotTimer / fadeTime);
            black = 1f - Mathf.Min(tIn, tOut);
        }

        // Quick dip when cutting between garage and the showcase shots.
        black = Mathf.Max(black, Mathf.Sin(cutFade * Mathf.PI));

        if (shotFade != null)
        {
            shotFade.color = new Color(0f, 0f, 0f, black);
            shotFade.raycastTarget = false;
        }
    }

    void Apply(float t)
    {
        float angle  = (Shots[shot, 0] + Shots[shot, 1] * t) * Mathf.Deg2Rad;
        float radius = Shots[shot, 2] + Shots[shot, 5] * t;
        float height = Shots[shot, 3];

        Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, height,
                                  Mathf.Sin(angle) * radius);
        cam.transform.position = pos;
        cam.transform.LookAt(new Vector3(0f, Shots[shot, 4], 0f));
    }
}
