using UnityEngine;

/// A tear in space — the doorway in and out of Spatial Drift (spawned by
/// SpatialDriftMode).
///
///   ENTRY  rips open on the road AHEAD of the car: a white-hot crack splits
///          the air, then tears wide into a jagged portal showing the drift
///          dimension.  It tracks the car's line so you always drive through
///          it; once the car is past, it slams shut behind you.
///   EXIT   bursts open around the car itself and spits it back out.
///
/// Visual only (shader SpatialDrift/Rift + edge sparks + a magenta light on
/// the road); the gameplay hand-off lives in SpatialDriftMode.
[DisallowMultipleComponent]
public class SpatialRift : MonoBehaviour
{
    public enum Kind { Entry, Exit }

    const float Width = 16f, Height = 12f;

    Kind kind;
    CarController car;
    Transform follow;        // remote (network) cars: follow the transform, not track coords
    Track track;
    float dist;              // wrapped track distance of the rift plane
    float lateral;
    float open, age, fade = 1f;
    bool  collapsing;
    float sparkRate;

    MeshRenderer   mr;
    Material       mat;
    Light          glow;
    ParticleSystem sparks;

    static Shader _shader;
    static Mesh   _quad;

    /// Track distance of the portal plane (entry rifts).
    public float Distance => dist;
    public bool  Collapsing => collapsing;

    // ── Spawning ──────────────────────────────────────────────────────────────
    public static SpatialRift SpawnEntry(CarController car, float ahead)
    {
        if (car == null || car.track == null) return null;
        var r = new GameObject("SpatialRift_Entry").AddComponent<SpatialRift>();
        r.kind    = Kind.Entry;
        r.car     = car;
        r.track   = car.track;
        r.dist    = car.track.WrapDistance(car.DistanceAlongTrack + ahead);
        r.lateral = car.LateralOffset;
        r.Build(1f);
        r.Place(0f);
        return r;
    }

    public static SpatialRift SpawnExit(CarController car)
    {
        if (car == null || car.track == null) return null;
        var r = new GameObject("SpatialRift_Exit").AddComponent<SpatialRift>();
        r.kind    = Kind.Exit;
        r.car     = car;
        r.track   = car.track;
        r.dist    = car.DistanceAlongTrack;
        r.lateral = car.LateralOffset;
        r.Build(0.62f);
        r.Place(0f);
        return r;
    }

    /// A burst around any transform — remote cars online, whose track
    /// coordinates aren't simulated on this machine.
    public static SpatialRift SpawnAround(Transform target)
    {
        if (target == null) return null;
        var r = new GameObject("SpatialRift_Burst").AddComponent<SpatialRift>();
        r.kind   = Kind.Exit;
        r.follow = target;
        r.Build(0.62f);
        r.Place(0f);
        return r;
    }

    /// Slam shut (with a final spray of sparks).
    public void Collapse()
    {
        if (collapsing) return;
        collapsing = true;
        if (sparks != null) sparks.Emit(70);
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    void Build(float scale)
    {
        transform.localScale = Vector3.one * scale;

        if (_shader == null) _shader = Shader.Find("SpatialDrift/Rift");
        if (_quad == null) _quad = MakeQuad();

        var portal = new GameObject("Portal");
        portal.transform.SetParent(transform, false);
        portal.AddComponent<MeshFilter>().sharedMesh = _quad;
        mr = portal.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        if (_shader != null)
        {
            mat = new Material(_shader) { name = "RiftMat" };
            mat.SetFloat("_Aspect", Width / Height);
            mat.SetFloat("_Seed", Random.Range(0f, 100f));
            mr.sharedMaterial = mat;
        }

        // Magenta spill on the road and the car.
        var lgo = new GameObject("RiftLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = new Vector3(0f, 0f, -1.5f);
        glow = lgo.AddComponent<Light>();
        glow.type      = LightType.Point;
        glow.color     = new Color(1f, 0.35f, 0.9f);
        glow.range     = 48f;
        glow.intensity = 0f;
        glow.shadows   = LightShadows.None;

        // Sparks thrown off the torn edge.
        var sgo = new GameObject("RiftSparks");
        sgo.transform.SetParent(transform, false);
        sparks = sgo.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.duration        = 1f;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 16f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor      = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 1f), new Color(1f, 0.3f, 0.85f));
        main.gravityModifier = 0.6f;
        main.maxParticles    = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;
        var em = sparks.emission;
        em.rateOverTime = 0f;
        var shape = sparks.shape;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = 1f;
        shape.radiusThickness = 0f;      // emit from the rim, flying outward
        var col = sparks.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.8f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = sparks.GetComponent<ParticleSystemRenderer>();
        rend.renderMode    = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.045f;
        rend.lengthScale   = 1.5f;
        rend.sharedMaterial = FxMaterials.Additive;
        sparks.Play();
    }

    static Mesh MakeQuad()
    {
        var m = new Mesh { name = "RiftQuad" };
        float w = Width * 0.5f, h = Height * 0.5f;
        m.vertices  = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) };
        m.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, new Vector3(Width, Height, 2f));
        return m;
    }

    // ── Per frame ─────────────────────────────────────────────────────────────
    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        if (kind == Kind.Entry)
        {
            // Crack (0→0.25 fast), then the rip tears wide.
            if (!collapsing) open = Mathf.MoveTowards(open, 1f, dt / (open < 0.25f ? 0.14f : 0.42f));
            else           { open = Mathf.MoveTowards(open, 0f, dt / 0.22f); fade = Mathf.MoveTowards(fade, 0f, dt / 0.3f); }
            if (car != null) lateral = Mathf.Lerp(lateral, car.LateralOffset, 1f - Mathf.Exp(-6f * dt));
        }
        else
        {
            // Burst open around the car, hang for a beat, close.
            if (!collapsing && age > 0.42f) Collapse();
            if (!collapsing) open = Mathf.MoveTowards(open, 1f, dt / 0.12f);
            else           { open = Mathf.MoveTowards(open, 0f, dt / 0.35f); fade = Mathf.MoveTowards(fade, 0f, dt / 0.45f); }
            if (car != null && follow == null) { dist = car.DistanceAlongTrack + 2.5f; lateral = car.LateralOffset; }
        }

        Place(dt);

        if (mat != null) { mat.SetFloat("_Open", open); mat.SetFloat("_Fade", fade); }
        if (glow != null) glow.intensity = open * fade * 9f * (0.85f + 0.15f * Mathf.Sin(age * 40f));

        if (sparks != null)
        {
            var shape = sparks.shape;
            shape.radius = Mathf.Max(0.2f, Height * 0.46f * Mathf.Lerp(0.3f, 1f, open));
            shape.scale  = new Vector3(Mathf.Lerp(0.05f, 1.05f, Mathf.SmoothStep(0.18f, 1f, open)), 1f, 1f);
            sparkRate = collapsing ? 0f : Mathf.Lerp(40f, 260f, open);
            var em = sparks.emission;
            em.rateOverTime = sparkRate;
        }

        if (collapsing && open <= 0.001f && fade <= 0.001f)
        {
            if (mr != null) mr.enabled = false;
            if (glow != null) glow.enabled = false;
            if (sparks == null || sparks.particleCount == 0) Destroy(gameObject);
        }
    }

    void Place(float dt)
    {
        if (follow != null)
        {
            transform.SetPositionAndRotation(
                follow.position + follow.forward * 2.5f + follow.up * (Height * 0.5f - 2.2f) * transform.localScale.y,
                follow.rotation);
            return;
        }
        if (track == null) return;
        dist = track.WrapDistance(dist);
        track.Sample(dist, lateral, out Vector3 pos, out Quaternion rot, out Vector3 fwd, out Vector3 up);
        // Centre high enough that the car passes through the lower half; the
        // bottom of the tear disappears into the road surface.
        float lift = (Height * 0.5f - 2.2f) * transform.localScale.y;
        transform.SetPositionAndRotation(pos + up * lift, Quaternion.LookRotation(fwd, up));
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
