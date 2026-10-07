using System.Collections.Generic;
using UnityEngine;

/// Generates a procedural ribbon mesh from a Track's spline.  Sampled densely
/// enough that corkscrews and loops look smooth.  Includes a centre line and
/// optional coloured edge stripes.  Regenerate by calling Rebuild() — happens
/// automatically in Awake and any time you call it from the editor.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TrackMeshBuilder : MonoBehaviour
{
    [Header("Source")]
    public Track track;

    [Header("Tessellation")]
    [Tooltip("Distance between cross-sections in metres. Smaller = smoother but more triangles.")]
    [Range(0.25f, 5f)] public float segmentLength = 1.0f;

    [Header("Visuals")]
    public Color surfaceColor    = new Color(0.18f, 0.18f, 0.22f);
    public Color centerLineColor = new Color(0.92f, 0.92f, 0.95f);
    public Color edgeStripeColor = new Color(0.95f, 0.55f, 0.10f);

    [Tooltip("Colour of the road's underside (so it isn't invisible from below).")]
    public Color undersideColor  = new Color(0.12f, 0.08f, 0.05f);
    [Tooltip("How far below the surface the underside sits (gives the road thickness).")]
    public float undersideDrop   = 0.15f;

    [Tooltip("Width of the centre line stripe.")]
    public float centerLineWidth = 0.25f;

    [Tooltip("Width of each edge stripe (drawn just inside the track edge).")]
    public float edgeStripeWidth = 0.6f;

    [Tooltip("Tiny vertical offset for stripes so they don't z-fight with surface.")]
    public float stripeLift = 0.02f;

    MeshFilter   mf;
    MeshRenderer mr;

    void Reset()
    {
        if (track == null) track = GetComponentInParent<Track>();
    }

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        if (track == null) track = GetComponentInParent<Track>();
        Rebuild();
    }

    public void Rebuild()
    {
        if (track == null) return;
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();

        var combined = new Mesh { name = "TrackMesh" };
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // long tracks → lots of verts

        var combine = new List<CombineInstance>(3);

        // Underside — same outline, dropped + reversed winding so it's visible
        // (and coloured) when you look at the track from below.
        combine.Add(new CombineInstance {
            mesh = BuildRibbon(d => track.WidthAt(d) * 0.5f, d => 0f, -undersideDrop, undersideColor, flip: true),
            transform = Matrix4x4.identity
        });

        // Main surface — half-width sampled per cross-section (per-node aware).
        combine.Add(new CombineInstance {
            mesh = BuildRibbon(d => track.WidthAt(d) * 0.5f, d => 0f, 0f, surfaceColor),
            transform = Matrix4x4.identity
        });

        // Centre line stripe (constant thin width, centred).
        if (centerLineWidth > 0f)
            combine.Add(new CombineInstance {
                mesh = BuildRibbon(d => centerLineWidth * 0.5f, d => 0f, stripeLift, centerLineColor),
                transform = Matrix4x4.identity
            });

        // Edge stripes — ride just inside the (possibly varying) track edges.
        if (edgeStripeWidth > 0f)
        {
            float esh = edgeStripeWidth * 0.5f;
            combine.Add(new CombineInstance {
                mesh = BuildRibbon(d => esh, d => track.WidthAt(d) * 0.5f - esh, stripeLift, edgeStripeColor),
                transform = Matrix4x4.identity
            });
            combine.Add(new CombineInstance {
                mesh = BuildRibbon(d => esh, d => -(track.WidthAt(d) * 0.5f - esh), stripeLift, edgeStripeColor),
                transform = Matrix4x4.identity
            });
        }

        combined.CombineMeshes(combine.ToArray(), true, false);
        combined.RecalculateBounds();
        combined.RecalculateNormals();

        mf.sharedMesh = combined;

        // Apply a vertex-coloured material so the procedural colours show up
        if (mr.sharedMaterial == null || mr.sharedMaterial.name != "TrackVertexColor")
            mr.sharedMaterial = MakeVertexColorMaterial();
    }

    // Build a ribbon raised by `lift` along the surface up.  `halfAt` gives the
    // ribbon half-width and `centerAt` its lateral centre offset, both sampled
    // per cross-section by distance — so the road can taper and the edge stripes
    // can track a varying edge.
    Mesh BuildRibbon(System.Func<float, float> halfAt, System.Func<float, float> centerAt,
                     float lift, Color color, bool flip = false)
    {
        float total = track.Length;
        if (total <= 0f) return new Mesh();

        int   segments = Mathf.Max(2, Mathf.CeilToInt(total / segmentLength));
        bool  closed   = track.IsClosed;

        var verts = new Vector3[(segments + 1) * 2];
        var cols  = new Color[verts.Length];
        var uvs   = new Vector2[verts.Length];

        for (int i = 0; i <= segments; i++)
        {
            float d = (float)i / segments * total;
            // For closed loops the last vertex coincides with the first — skip
            // duplicates by sampling slightly before total on the last step.
            if (closed && i == segments) d = total - 0.001f;

            track.Sample(d, out Vector3 centre, out Vector3 fwd, out Vector3 up);
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            float   half  = Mathf.Max(0.01f, halfAt(d));
            Vector3 base_ = centre + up * lift + right * centerAt(d);

            int vi = i * 2;
            verts[vi    ] = base_ - right * half;
            verts[vi + 1] = base_ + right * half;
            cols [vi    ] = color;
            cols [vi + 1] = color;
            uvs  [vi    ] = new Vector2(0f, d * 0.1f);
            uvs  [vi + 1] = new Vector2(1f, d * 0.1f);
        }

        var tris = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int vi = i * 2;
            int ti = i * 6;
            if (!flip)
            {
                tris[ti    ] = vi;     tris[ti + 1] = vi + 2; tris[ti + 2] = vi + 1;
                tris[ti + 3] = vi + 1; tris[ti + 4] = vi + 2; tris[ti + 5] = vi + 3;
            }
            else   // reversed winding → faces the other way (downward)
            {
                tris[ti    ] = vi;     tris[ti + 1] = vi + 1; tris[ti + 2] = vi + 2;
                tris[ti + 3] = vi + 1; tris[ti + 4] = vi + 3; tris[ti + 5] = vi + 2;
            }
        }

        var m = new Mesh();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices    = verts;
        m.colors      = cols;
        m.uv          = uvs;
        m.triangles   = tris;
        return m;
    }

    public static Material MakeVertexColorMaterial()
    {
        // Custom lit vertex-colour shader: keeps the vertex-colour art pipeline
        // AND receives/casts real main-light shadows (see VertexColorLit.shader).
        // Falls back to unlit vertex-colour shaders if it's missing/stripped.

        var shader = Shader.Find("SpatialDrift/VertexColorLit")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Sprites/Default")
                  ?? Shader.Find("Standard");
        if (shader == null)
        {
            // In a BUILD, runtime Shader.Find can return null if the shader was
            // stripped — run "Racing Game / Fix Build Shaders".  Fail soft (no
            // throw) so the rest of the race scene still loads.
            Debug.LogWarning("[TrackMeshBuilder] No vertex-colour shader found — run " +
                             "'Racing Game / Fix Build Shaders (Always Include)' and rebuild.");
            return null;
        }
        var mat = new Material(shader) { name = "TrackVertexColor" };
        mat.enableInstancing = false;
        return mat;
    }
}
