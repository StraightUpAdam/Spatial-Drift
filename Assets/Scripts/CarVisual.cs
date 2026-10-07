using UnityEngine;

/// Builds a low-poly arcade-racer body from primitives — replacing the old
/// placeholder cube — under a single animated root (so HoverBody can lean/bob
/// the whole car).  Auto-added by CarController.Awake.
///
/// If a legacy "Body" cube exists, its colour is read and reused, then it's
/// destroyed — so existing scenes/prefabs upgrade at runtime with their
/// per-car colours intact and no rebuild required.
///
/// The body can be re-painted at runtime via SetColor() — multiplayer uses this
/// to apply each player's chosen colour after the car spawns.
[DisallowMultipleComponent]
public class CarVisual : MonoBehaviour
{
    [Tooltip("Main hull colour.  Auto-read from a legacy Body cube if present.")]
    public Color bodyColor = new Color(0.85f, 0.12f, 0.08f);

    /// The animated root holding all the car meshes.  HoverBody moves this.
    public Transform Root { get; private set; }

    // Captured renderers, grouped by how their colour derives from bodyColor,
    // so SetColor() can re-tint the whole car without rebuilding the meshes.
    Renderer _hull, _nose, _tail, _finL, _finR, _accentL, _accentR, _engine;

    static Shader _lit;
    static Shader Lit => _lit != null ? _lit
        : (_lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

    void Awake()
    {
        var old = transform.Find("Body");
        if (old != null)
        {
            var r = old.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) bodyColor = r.sharedMaterial.color;
            Destroy(old.gameObject);
        }
        Build();
    }

    void Build()
    {
        var rootGO = new GameObject("CarBody");
        Root = rootGO.transform;
        Root.SetParent(transform, false);
        Root.localPosition = new Vector3(0f, 0.3f, 0f);   // sit on the track surface

        Color glass = new Color(0.05f, 0.06f, 0.09f, 1f);

        // ── Hull: wide low body + tapered nose + raised tail ─────────────────
        _hull = R(Box("Hull",  new Vector3(0f, 0f,    -0.1f),  new Vector3(1.6f, 0.42f, 3.2f), bodyColor));
        _nose = R(Box("Nose",  new Vector3(0f, -0.04f, 1.75f), new Vector3(1.0f, 0.28f, 1.0f), bodyColor));
        _tail = R(Box("Tail",  new Vector3(0f, 0.08f, -1.7f),  new Vector3(1.7f, 0.5f,  0.5f), Dark(bodyColor)));

        // ── Canopy ───────────────────────────────────────────────────────────
        Box("Canopy", new Vector3(0f, 0.3f, 0.25f), new Vector3(0.9f, 0.4f, 1.3f), glass, 0.2f, 0.7f);

        // ── Side fins (angled out) ───────────────────────────────────────────
        var finL = Box("FinL", new Vector3(-0.9f, 0.12f, -1.15f), new Vector3(0.09f, 0.42f, 1.0f), Dark(bodyColor));
        var finR = Box("FinR", new Vector3( 0.9f, 0.12f, -1.15f), new Vector3(0.09f, 0.42f, 1.0f), Dark(bodyColor));
        finL.localRotation = Quaternion.Euler(0f, 0f,  14f);
        finR.localRotation = Quaternion.Euler(0f, 0f, -14f);
        _finL = R(finL);
        _finR = R(finR);

        // ── Emissive accent strips + engine glow (bloom picks these up) ──────
        _accentL = R(Emissive("AccentL", new Vector3(-0.81f, 0.02f, 0.1f), new Vector3(0.05f, 0.13f, 2.7f), Accent(bodyColor)));
        _accentR = R(Emissive("AccentR", new Vector3( 0.81f, 0.02f, 0.1f), new Vector3(0.05f, 0.13f, 2.7f), Accent(bodyColor)));
        _engine  = R(Emissive("Engine",  new Vector3( 0f,    0.06f, -1.98f), new Vector3(1.15f, 0.22f, 0.08f), Engine(bodyColor)));
    }

    /// Re-paint the whole car.  Safe to call any time after Awake (the meshes
    /// already exist — only material colours change).
    public void SetColor(Color c)
    {
        bodyColor = c;
        Tint(_hull, c);          Tint(_nose, c);
        Tint(_tail, Dark(c));    Tint(_finL, Dark(c));   Tint(_finR, Dark(c));
        TintEmissive(_accentL, Accent(c));
        TintEmissive(_accentR, Accent(c));
        TintEmissive(_engine,  Engine(c));
    }

    // Derived colours — kept in one place so Build() and SetColor() agree.
    static Color Dark(Color c)   { var d = c * 0.55f; d.a = 1f; return d; }
    static Color Accent(Color c) => c * 1.9f;                          // HDR → blooms
    static Color Engine(Color c) => Color.Lerp(c, Color.cyan, 0.4f) * 3f;

    static Renderer R(Transform t) => t != null ? t.GetComponent<Renderer>() : null;

    static void Tint(Renderer r, Color c)
    {
        if (r != null && r.sharedMaterial != null) r.sharedMaterial.color = c;
    }

    static void TintEmissive(Renderer r, Color emission)
    {
        if (r == null || r.sharedMaterial == null) return;
        var mat = r.sharedMaterial;
        mat.color = emission;
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    Transform Box(string name, Vector3 pos, Vector3 scale, Color color,
                  float metallic = 0.1f, float smooth = 0.4f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(Root, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;

        var mat = new Material(Lit) { color = color };
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    Transform Emissive(string name, Vector3 pos, Vector3 scale, Color emission)
    {
        var t = Box(name, pos, scale, emission, 0f, 0.8f);
        TintEmissive(t.GetComponent<Renderer>(), emission);
        return t;
    }
}
