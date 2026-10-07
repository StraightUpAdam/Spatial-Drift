using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

/// Layered 2D trackside scenery — FOUR parallax "wall" sheets per side, each a
/// continuous ribbon that follows the track at increasing lateral distance and
/// height, like the stacked backdrop strips of a classic 2D racer.  Nearer
/// layers are lighter; farther layers get taller, darker and hazed toward the
/// void purple — depth of field from geometry alone.
///
/// ART: layers look for repeating textures in Resources/Scenery —
///   WallLayer0..WallLayer3   per-depth-layer sheet, else
///   WallLayer                one shared sheet for every layer (used now:
///                            Adam's TreeLine sprite), else flat grey.
/// Textures with transparency are rendered ALPHA-CUTOUT (transparent sky above
/// the tree line just works).  u tiles along the track (~16 m per repeat,
/// snapped so the loop seam is exact); v spans the wall height.
///
/// The style is PER NODE (NodeData.scenery): None sinks the walls into the
/// ground, Forest/Mountains/City tint the sheets, and TUNNEL replaces them with
/// an enclosing tube — near walls at the road edge plus a ceiling.  Styles
/// blend across segments: walls sink / tunnel mouths rise through transitions.
/// Fully deterministic — editor preview, test drive and every online player
/// build identical scenery.
public static class TrackScenery
{
    /// Style ids stored in NodeData.scenery — index into this list.
    public static readonly string[] StyleNames = { "None", "Forest", "Mountains", "City", "Tunnel" };
    public const int StyleCount = 5;
    const int TunnelStyle = 4;

    const int   Layers      = 4;
    const float StepMeters  = 8f;      // ribbon segment length along the track
    const float BaseOffset  = 6f;      // metres beyond the road edge, layer 0
    const float LayerGap    = 9f;      // extra lateral metres per layer
    const float BaseHeight  = 8f;      // wall height, layer 0
    const float LayerHeight = 5f;      // extra height per layer
    const float TileTarget  = 16f;     // aim for one texture repeat per ~16 m

    // Ground skirt profile: from just under the road edge (inner) sloping gently
    // down to the outer rim.  Wall bottoms are anchored EXACTLY onto this
    // surface, so a sprite's bottom pixel row is the visible ground line —
    // nothing pokes below, nothing floats.
    const float SkirtInnerTuck = 1.5f;   // inner edge starts this far UNDER the road
    const float SkirtInnerDrop = 0.35f;  // ...and this far below the surface
    const float SkirtOuterDrop = 2.5f;   // drop at the outer rim (GroundExtent)

    static float SkirtDropAt(float lateralBeyondEdge) =>
        Mathf.Lerp(SkirtInnerDrop, SkirtOuterDrop,
                   (lateralBeyondEdge + SkirtInnerTuck) / (GroundExtent + SkirtInnerTuck));

    // Tunnel shape
    const float TunnelInset   = 1.4f;  // wall distance beyond the road edge
    const float TunnelHeight  = 9f;
    const float TunnelCeilMin = 0.55f; // ceiling appears once the tunnel is this "solid"

    // Horizon haze — far layers fade toward the current weather's fog colour,
    // so the last sheet melts into the atmosphere instead of ending hard.
    static Color Haze => Atmosphere.Current.fog;

    // Placeholder tints per style — near-neutral (real look comes from the
    // sprite sheets); just enough hue to see transitions work.
    static readonly Color[] StyleTint =
    {
        Color.white,                          // None   (walls sunk — unused)
        new Color(0.80f, 0.88f, 0.80f),       // Forest — light green-grey
        new Color(0.78f, 0.82f, 0.92f),       // Mountains — light blue-grey
        new Color(0.92f, 0.86f, 0.82f),       // City — warm grey
        Color.white,                          // Tunnel (own mesh — unused here)
    };

    /// Build the scenery for a track.  Returns the root GameObject, or null when
    /// no node uses a scenery style.
    public static GameObject Build(Track track, TrackData data)
    {
        if (track == null || data == null || data.nodes == null || data.nodes.Count < 3)
            return null;

        float len  = track.Length;
        var   root = new GameObject("TrackScenery");

        // Base ground plane ALWAYS — climbing a hill overlooks land, not void.
        float planeY = PlaneHeight(data);
        BuildBasePlane(root.transform, data, planeY);
        if (len < 20f) return root;

        // ── Sample the track once; walls + tunnel share these ─────────────────
        int steps = Mathf.Clamp(Mathf.CeilToInt(len / StepMeters), 8, 1200);
        int count = steps + 1;                       // +1 wraps the seam closed

        var pos    = new Vector3[count];
        var right  = new Vector3[count];
        var up     = new Vector3[count];
        var half   = new float[count];
        var tint   = new Color[count];
        var wallW  = new float[count];               // wall solidity   (0..1)
        var tunW   = new float[count];               // tunnel solidity (0..1)
        var grnd   = new float[count];               // on-ground weight (0..1)
        bool anyTunnel = false;

        for (int i = 0; i < count; i++)
        {
            float d = (i % steps) * (len / steps);   // last sample = first (seam sealed)
            track.Sample(d, out pos[i], out Vector3 fwd, out up[i]);
            right[i] = Vector3.Cross(up[i], fwd).normalized;
            half[i]  = track.HalfWidthAt(d);

            StyleMixAt(track, data, d, out tint[i], out wallW[i], out tunW[i], out grnd[i]);
            // AIR sections (onGround = false) carry no skirt and no walls — the
            // multiply makes them sink out through the transition.  Tunnels are
            // allowed to fly (an enclosed tube in the sky reads fine).
            // Steep banks, rolls and loops count as AIR for scenery: anything
            // built sideways from the road there would roll with it into
            // twisted vertical sheets.  Full ground ≤ ~30° bank, none past ~50°.
            grnd[i] *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 0.87f, up[i].y));
            if (tunW[i] > 0.02f) anyTunnel = true;
        }
        SmoothLoop(grnd, steps, 4, 2);          // ground ↔ air hand-offs ease in over ~70 m
        for (int i = 0; i < count; i++) wallW[i] *= grnd[i];

        // Ground skirt + terrain apron: grass hugging the road on every
        // on-ground section (styles or not), falling away to the base plane so
        // the track sits IN the terrain instead of floating above it.
        BuildGround(root.transform, pos, right, up, half, tint, grnd, planeY,
                    steps, len / Mathf.Max(1f, Mathf.Round(len / TileTarget)), len);
        BuildTerrain(root.transform, pos, right, up, half, grnd, planeY, steps);

        bool any = false;
        foreach (var n in data.nodes) if (n.scenery > 0) { any = true; break; }
        if (!any) return root;

        for (int layer = 0; layer < Layers; layer++)
        {
            // Depth shading: near = bright (sprite shows true), far = darker + hazed.
            float k = Layers <= 1 ? 0f : layer / (float)(Layers - 1);
            Color shade = Color.Lerp(Color.white, new Color(0.45f, 0.42f, 0.52f), k);
            shade = Color.Lerp(shade, Haze, k * 0.40f);

            float lateral = BaseOffset + layer * LayerGap;
            float height  = BaseHeight + layer * LayerHeight;

            // Tile length preserves the TEXTURE'S ASPECT over this layer's height:
            // taller far layers scale their art up uniformly instead of stretching
            // it vertically.  Snapped so the loop seam lands on an exact repeat.
            var   mat     = WallMaterial(layer);
            float desired = height * MatAspect(mat, 2f);
            float tileLen = len / Mathf.Max(1f, Mathf.Round(len / desired));

            for (int side = -1; side <= 1; side += 2)
                BuildWall(root.transform, layer, side, lateral, height, shade, mat,
                          pos, right, up, half, tint, wallW, steps, tileLen, len);
        }

        if (anyTunnel)
            BuildTunnel(root.transform, pos, right, up, half, tunW, steps, len);

        return root;
    }

    /// Box-blur a looped per-sample array (last entry mirrors the first).
    static void SmoothLoop(float[] a, int steps, int radius, int passes)
    {
        var tmp = new float[steps];
        for (int p = 0; p < passes; p++)
        {
            for (int i = 0; i < steps; i++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += a[((i + k) % steps + steps) % steps];
                tmp[i] = sum / (2 * radius + 1);
            }
            for (int i = 0; i < steps; i++) a[i] = tmp[i];
            a[steps] = a[0];
        }
    }

    // Width/height ratio of a material's sheet (fallback for the flat grey).
    static float MatAspect(Material m, float fallback)
    {
        var t = m != null ? m.mainTexture : null;
        return t != null && t.height > 0 ? (float)t.width / t.height : fallback;
    }

    // ── One continuous wall ribbon (one mesh; one texture slot per layer) ─────
    static void BuildWall(Transform parent, int layer, int side,
                          float lateral, float height, Color shade, Material mat,
                          Vector3[] pos, Vector3[] right, Vector3[] up,
                          float[] half, Color[] tint, float[] solid,
                          int steps, float tileLen, float len)
    {
        int count = steps + 1;
        var verts = new List<Vector3>(count * 2);
        var cols  = new List<Color>(count * 2);
        var uvs   = new List<Vector2>(count * 2);
        var tris  = new List<int>(steps * 12);

        // Bottom edge sits EXACTLY on the ground skirt at this layer's lateral
        // distance — the sprite's bottom pixel row IS the ground line.
        float drop = SkirtDropAt(lateral);
        for (int i = 0; i < count; i++)
        {
            // Transitions SINK the whole sheet through the ground — the sprite
            // stays full and undistorted (no cropped tree tops, no squash); it
            // just slides down out of sight behind the ground skirt.
            float   sink    = (1f - solid[i]) * (height + drop + 1f);
            Vector3 basePos = pos[i] + right[i] * (side * (half[i] + lateral))
                                     - up[i] * (drop + sink);
            Color   c       = new Color(tint[i].r * shade.r,
                                        tint[i].g * shade.g,
                                        tint[i].b * shade.b, 1f);
            float   u       = (i * (len / steps)) / tileLen;

            verts.Add(basePos);                     // bottom = ground line
            verts.Add(basePos + up[i] * height);    // top
            cols.Add(c); cols.Add(c);
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }

        for (int i = 0; i < steps; i++)
        {
            if (solid[i] < 0.02f && solid[i + 1] < 0.02f) continue;   // sunken span
            AddQuadBothSides(tris, i * 2);
        }
        if (tris.Count == 0) return;

        Emit(parent, $"Wall_L{layer}_{(side < 0 ? "Left" : "Right")}",
             verts, cols, uvs, tris, mat);
    }

    // ── Ground skirt: a dark apron from the road edge out past the last layer,
    //    so you can't see through the gaps between the sheets.
    const float GroundExtent = 47f;

    // ── Terrain: a real heightfield under the whole map ─────────────────────
    // Every on-ground road sample raises a plateau just under its skirt and a
    // smooth falloff around it (gentler the higher the road), the field keeps
    // the MAX of all of them, then every road corridor caps it so ground never
    // pokes through a road (a high section passing over a low one leaves a
    // valley under the bridge).  One continuous surface: it can't fold or
    // twist like per-sample strips did, has no cliff edges, and meets the base
    // plane exactly at its rim.
    const float TerrainCell     = 10f;    // grid resolution (m)
    const float TerrainSlope    = 0.40f;  // average rise per metre of falloff (peak ~37°)
    const float TerrainMinFall  = 90f;    // even a ground-level road blends out over this
    const float TerrainUnder    = 0.6f;   // ground sits this far under the skirt
    const float AirClearance    = 8f;     // ...and this far under a flying road

    static void BuildTerrain(Transform parent, Vector3[] pos, Vector3[] right, Vector3[] up,
                             float[] half, float[] grnd, float planeY, int steps)
    {
        int n = steps;                                         // last sample == first
        if (n < 2) return;

        // Per-sample plateau height (under both skirt rims) + falloff reach.
        var plat = new float[n];
        var fall = new float[n];
        float margin = 0f;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
        for (int i = 0; i < n; i++)
        {
            float rimL = (pos[i] - right[i] * (half[i] + GroundExtent) - up[i] * SkirtOuterDrop).y;
            float rimR = (pos[i] + right[i] * (half[i] + GroundExtent) - up[i] * SkirtOuterDrop).y;
            plat[i] = Mathf.Min(pos[i].y - SkirtInnerDrop, Mathf.Min(rimL, rimR)) - TerrainUnder;
            fall[i] = Mathf.Max(TerrainMinFall, (plat[i] - planeY) / TerrainSlope);
            margin  = Mathf.Max(margin, half[i] + GroundExtent + fall[i]);
            min = Vector2.Min(min, new Vector2(pos[i].x, pos[i].z));
            max = Vector2.Max(max, new Vector2(pos[i].x, pos[i].z));
        }
        min -= Vector2.one * (margin + TerrainCell);
        max += Vector2.one * (margin + TerrainCell);

        int nx = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / TerrainCell) + 1, 2, 600);
        int nz = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / TerrainCell) + 1, 2, 600);
        float cx = (max.x - min.x) / (nx - 1), cz = (max.y - min.y) / (nz - 1);

        var h    = new float[nx * nz];
        var cap  = new float[nx * nz];
        var near = new float[nx * nz];                          // 1 at the road → 0 at the plains
        for (int k = 0; k < h.Length; k++) { h[k] = planeY; cap[k] = float.MaxValue; }

        for (int i = 0; i < n; i++)
        {
            float g    = grnd[i] * grnd[i] * (3f - 2f * grnd[i]);
            float core = half[i] + GroundExtent;
            float R    = core + fall[i];
            float px = pos[i].x, pz = pos[i].z;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((px - R - min.x) / cx)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((px + R - min.x) / cx));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((pz - R - min.y) / cz)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((pz + R - min.y) / cz));
            float corridor = half[i] + GroundExtent * 0.85f;
            // Lowest edge of the road's cross-section; upside down, the car also
            // hangs below it.
            float low  = pos[i].y - Mathf.Abs(right[i].y) * half[i] - (up[i].y < 0.5f ? 6f : 0f);
            float capY = g > 0.5f ? plat[i] : low - AirClearance;

            for (int z = z0; z <= z1; z++)
            {
                float dz = min.y + z * cz - pz;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = min.x + x * cx - px;
                    float d  = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > R) continue;
                    int k = z * nx + x;

                    if (d <= corridor && capY < cap[k]) cap[k] = capY;
                    if (g <= 0.001f) continue;

                    float t = d <= core ? 0f : (d - core) / fall[i];
                    float s = t * t * t * (t * (t * 6f - 15f) + 10f);   // smootherstep
                    float y = Mathf.Lerp(plat[i], planeY, s);
                    y = Mathf.Lerp(planeY, y, g);
                    if (y > h[k]) h[k] = y;
                    float w = (1f - s) * g;
                    if (w > near[k]) near[k] = w;
                }
            }
        }

        var verts = new List<Vector3>(nx * nz);
        var cols  = new List<Color>(nx * nz);
        var uvs   = new List<Vector2>(nx * nz);
        Color plains = new Color(0.27f, 0.41f, 0.22f);         // == base plane
        Color hill   = new Color(0.30f, 0.44f, 0.24f);          // == skirt grass
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int k = z * nx + x;
            float y = Mathf.Max(planeY, Mathf.Min(h[k], cap[k]));
            var p = new Vector3(min.x + x * cx, y, min.y + z * cz);
            verts.Add(p);
            cols.Add(Color.Lerp(plains, hill, near[k]));
            uvs.Add(new Vector2(p.x, p.z) / 25f);               // world-space, like the plains
        }

        var tris = new List<int>((nx - 1) * (nz - 1) * 6);
        float flat = planeY + 0.01f;
        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
            // Cells lying flat on the plain are left to the base plane (no z-fight).
            if (verts[a].y <= flat && verts[b].y <= flat && verts[c].y <= flat && verts[d].y <= flat) continue;
            tris.Add(a); tris.Add(c); tris.Add(d);
            tris.Add(a); tris.Add(d); tris.Add(b);
        }
        if (tris.Count == 0) return;
        var go = Emit(parent, "Terrain", verts, cols, uvs, tris, GroundMaterial());
        go.GetComponent<MeshFilter>().sharedMesh.RecalculateNormals();   // hills shade with the sun
    }


    static void BuildGround(Transform parent,
                            Vector3[] pos, Vector3[] right, Vector3[] up,
                            float[] half, Color[] tint, float[] grnd, float planeY,
                            int steps, float tileLen, float len)
    {
        int count = steps + 1;
        var verts = new List<Vector3>(count * 8);
        var cols  = new List<Color>(count * 8);
        var uvs   = new List<Vector2>(count * 8);
        var tris  = new List<int>(steps * 48);

        Color plains = new Color(0.27f, 0.41f, 0.22f);   // matches the base plane

        for (int side = -1; side <= 1; side += 2)
        {
            // ── Strip 1: the near skirt (road edge → wall zone) ───────────────
            int start = verts.Count;
            for (int i = 0; i < count; i++)
            {
                float u = (i * (len / steps)) / tileLen;
                // Grass, faintly tinted by the zone's style colour.  The inner
                // edge starts UNDER the road surface so no gap can ever show.
                Color c = new Color(tint[i].r * 0.30f,
                                    tint[i].g * 0.44f,
                                    tint[i].b * 0.26f, 1f);
                Vector3 vin  = pos[i] + right[i] * (side * (half[i] - SkirtInnerTuck))
                                      - up[i] * SkirtInnerDrop;
                Vector3 vout = pos[i] + right[i] * (side * (half[i] + GroundExtent))
                                      - up[i] * SkirtOuterDrop;
                // Entering an AIR section (or a steep bank / roll) the skirt
                // NARROWS into the road edge: the inner edge stays glued under
                // the road, the outer edge slides in to meet it.  (Dropping it to
                // the plain made tall vertical sheets under high roads.)
                float gOut = grnd[i] * grnd[i] * (3f - 2f * grnd[i]);
                vout = Vector3.Lerp(vin, vout, gOut);
                verts.Add(vin);
                verts.Add(vout);
                cols.Add(c); cols.Add(c);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < steps; i++)
            {
                // Skip only spans that are FULLY air (both ends tapered flat).
                if (grnd[i] < 0.02f && grnd[i + 1] < 0.02f) continue;
                AddQuadBothSides(tris, start + i * 2);
            }

        }
        if (tris.Count == 0) return;
        Emit(parent, "Ground", verts, cols, uvs, tris, GroundMaterial());
    }

    // ── Base ground plane: a huge grass disc under the whole map, so climbing
    //    a hill overlooks land instead of void.  Always built, even when no node
    //    uses a scenery style.  Shares the Ground material (world-space UVs, so
    //    a future grass texture tiles evenly across it).
    /// Height of the base plane: below the lowest node, and always below the
    /// editor's y=0 grid so the grid stays readable while editing.
    static float PlaneHeight(TrackData data)
    {
        float minY = float.PositiveInfinity;
        foreach (var n in data.nodes) minY = Mathf.Min(minY, n.y);
        return Mathf.Min(minY - 4f, -1f);
    }

    static void BuildBasePlane(Transform parent, TrackData data, float planeY)
    {
        if (data.nodes.Count == 0) return;

        Vector3 min = data.nodes[0].Pos, max = min;
        foreach (var n in data.nodes)
        {
            min = Vector3.Min(min, n.Pos);
            max = Vector3.Max(max, n.Pos);
        }
        Vector3 center = (min + max) * 0.5f;
        // Reaches far past the fog/far clip so the land never ends at the horizon.
        float   radius = Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f + 12000f;
        EmitPlains(parent, "BasePlane", center, radius, planeY);
    }

    /// A free-standing grass plain (menu backdrops use this — same material
    /// and colour as the race world's base plane).
    public static GameObject BuildPlains(Vector3 center, float radius, float y)
    {
        var root = new GameObject("Plains");
        EmitPlains(root.transform, "PlainsDisc", center, radius, y);
        return root;
    }

    static void EmitPlains(Transform parent, string name, Vector3 center, float radius, float y)
    {
        // Concentric rings, finer near the middle: fog is evaluated per vertex,
        // so one giant fan triangle reaching kilometres out would fog the grass
        // right under the camera.  Ring spacing grows ~15% per ring.
        const int segs = 64;
        var radii = new List<float> { 0f };
        for (float r = 30f; r < radius; r = Mathf.Max(r + 30f, r * 1.15f)) radii.Add(r);
        radii.Add(radius);

        var verts = new List<Vector3>(radii.Count * segs);
        var cols  = new List<Color>(radii.Count * segs);
        var uvs   = new List<Vector2>(radii.Count * segs);
        var tris  = new List<int>(radii.Count * segs * 12);

        Color grass = new Color(0.27f, 0.41f, 0.22f);
        for (int ring = 0; ring < radii.Count; ring++)
        for (int s = 0; s < segs; s++)
        {
            float a = s / (float)segs * Mathf.PI * 2f;
            var   p = new Vector3(center.x + Mathf.Cos(a) * radii[ring], y,
                                  center.z + Mathf.Sin(a) * radii[ring]);
            verts.Add(p);
            cols.Add(grass);
            uvs.Add(new Vector2(p.x, p.z) / 25f);
        }
        for (int ring = 0; ring < radii.Count - 1; ring++)
        for (int s = 0; s < segs; s++)
        {
            int a0 = ring * segs + s, a1 = ring * segs + (s + 1) % segs;
            int b0 = a0 + segs,       b1 = a1 + segs;
            // both windings — visible from above & below
            tris.Add(a0); tris.Add(b1); tris.Add(b0);  tris.Add(a0); tris.Add(a1); tris.Add(b1);
            tris.Add(a0); tris.Add(b0); tris.Add(b1);  tris.Add(a0); tris.Add(b1); tris.Add(a1);
        }
        Emit(parent, name, verts, cols, uvs, tris, GroundMaterial());
    }

    // ── Tunnel: near walls both sides + a ceiling slab over the road ──────────
    static void BuildTunnel(Transform parent,
                            Vector3[] pos, Vector3[] right, Vector3[] up,
                            float[] half, float[] solid,
                            int steps, float len)
    {
        int count = steps + 1;
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        Color wallCol = new Color(0.42f, 0.40f, 0.48f);
        Color ceilCol = new Color(0.26f, 0.24f, 0.32f);

        // Aspect-preserving tiling for the (future) tunnel sheet, seam-snapped.
        var   mat     = TunnelMaterial();
        float desired = TunnelHeight * MatAspect(mat, 2f);
        float tileLen = len / Mathf.Max(1f, Mathf.Round(len / desired));

        // Wall bottoms anchored to the ground skirt at the tunnel's lateral.
        float drop  = SkirtDropAt(TunnelInset);
        float fullH = TunnelHeight + drop;          // top still TunnelHeight above road

        // Side walls (left then right), sharing the vertex layout of BuildWall.
        for (int side = -1; side <= 1; side += 2)
        {
            int start = verts.Count;
            for (int i = 0; i < count; i++)
            {
                float   sink    = (1f - solid[i]) * (fullH + 1f);   // sink, don't distort
                Vector3 basePos = pos[i] + right[i] * (side * (half[i] + TunnelInset))
                                         - up[i] * (drop + sink);
                float u = (i * (len / steps)) / tileLen;

                verts.Add(basePos);
                verts.Add(basePos + up[i] * fullH);
                cols.Add(wallCol); cols.Add(wallCol);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < steps; i++)
            {
                if (solid[i] < 0.02f && solid[i + 1] < 0.02f) continue;
                AddQuadBothSides(tris, start + i * 2);
            }
        }

        // Ceiling — spans between the two wall tops once the tunnel is solid
        // enough (the mouth reads as walls first, then the roof closes over).
        int ceilStart = verts.Count;
        for (int i = 0; i < count; i++)
        {
            // Track the (sinking) wall tops so the roof rides them exactly.
            float sink = (1f - solid[i]) * (fullH + 1f);
            float u = (i * (len / steps)) / tileLen;
            Vector3 top = up[i] * (TunnelHeight - sink);
            verts.Add(pos[i] - right[i] * (half[i] + TunnelInset) + top);   // left top
            verts.Add(pos[i] + right[i] * (half[i] + TunnelInset) + top);   // right top
            cols.Add(ceilCol); cols.Add(ceilCol);
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }
        for (int i = 0; i < steps; i++)
        {
            if (solid[i] < TunnelCeilMin || solid[i + 1] < TunnelCeilMin) continue;
            AddQuadBothSides(tris, ceilStart + i * 2);
        }

        if (tris.Count == 0) return;
        Emit(parent, "Tunnel", verts, cols, uvs, tris, mat);
    }

    // Two triangles between sample i (verts b0,b0+1) and i+1 (b0+2,b0+3),
    // emitted both windings so the strip reads from inside and outside.
    static void AddQuadBothSides(List<int> tris, int b0)
    {
        int t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
        tris.Add(b0); tris.Add(t0); tris.Add(t1);
        tris.Add(b0); tris.Add(t1); tris.Add(b1);
        tris.Add(b0); tris.Add(t1); tris.Add(t0);
        tris.Add(b0); tris.Add(b1); tris.Add(t1);
    }

    static GameObject Emit(Transform parent, string name,
                           List<Vector3> verts, List<Color> cols, List<Vector2> uvs,
                           List<int> tris, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        // Lit vertex-colour shader → the world takes part in real shadows:
        // tree-line sprites cast tree-shaped shadows, the terrain receives them.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows    = true;
        return go;
    }

    // ── Materials (cached across builds; textures hot-load from Resources) ────
    static readonly Material[] _wallMats = new Material[Layers];
    static Material _tunnelMat;

    static Material WallMaterial(int layer)
    {
        var m = _wallMats[layer];
        if (m != null) return m;

        m = TrackMeshBuilder.MakeVertexColorMaterial();
        if (m == null) return null;
        m.name = "SceneryLayer" + layer;

        // Per-layer sheet, else the shared sheet, else flat grey placeholder.
        var tex = Resources.Load<Texture2D>("Scenery/WallLayer" + layer)
               ?? Resources.Load<Texture2D>("Scenery/WallLayer");
        if (tex != null) ApplyTexture(m, tex);

        _wallMats[layer] = m;
        return m;
    }

    static Material TunnelMaterial()
    {
        if (_tunnelMat != null) return _tunnelMat;
        _tunnelMat = TrackMeshBuilder.MakeVertexColorMaterial();
        if (_tunnelMat == null) return null;
        _tunnelMat.name = "SceneryTunnel";
        var tex = Resources.Load<Texture2D>("Scenery/Tunnel");
        if (tex != null) ApplyTexture(_tunnelMat, tex);
        return _tunnelMat;
    }

    static Material _groundMat;
    static Material GroundMaterial()
    {
        if (_groundMat != null) return _groundMat;
        _groundMat = TrackMeshBuilder.MakeVertexColorMaterial();
        if (_groundMat == null) return null;
        _groundMat.name = "SceneryGround";
        var tex = Resources.Load<Texture2D>("Scenery/Ground");   // optional art slot
        if (tex != null) ApplyTexture(_groundMat, tex);
        return _groundMat;
    }

    // Assign a repeating sheet + switch the material to ALPHA CUTOUT so
    // transparent sky in the sprite (e.g. above a tree line) draws nothing.
    static void ApplyTexture(Material m, Texture2D tex)
    {
        tex.wrapMode  = TextureWrapMode.Repeat;
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);

        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
        if (m.HasProperty("_Cutoff"))    m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    // ── Per-distance style mix ────────────────────────────────────────────────
    // wallSolid — weight of the parallax-wall styles (Forest/Mountains/City).
    // tunnelSolid — weight of the Tunnel style.  None contributes to neither,
    // so walls sink and tunnels open/close smoothly through transitions.
    static void StyleMixAt(Track track, TrackData data, float distance,
                           out Color tint, out float wallSolid, out float tunnelSolid,
                           out float groundW)
    {
        tint = Color.white; wallSolid = 0f; tunnelSolid = 0f; groundW = 1f;

        var spline = track.Container != null ? track.Container.Spline : null;
        int n = data.nodes.Count;
        if (spline == null || n == 0) return;

        int segCount = spline.Closed ? n : n - 1;
        if (segCount < 1)
        {
            int only = Mathf.Clamp(data.nodes[0].scenery, 0, StyleCount - 1);
            if (only == TunnelStyle) tunnelSolid = 1f;
            else if (only > 0)     { wallSolid = 1f; tint = StyleTint[only]; }
            groundW = data.nodes[0].onGround ? 1f : 0f;
            return;
        }

        // KNOT units (see Track.WidthAt): per-node values must map by knot, not
        // by normalized arc length, or they land on the wrong nodes.
        float tk   = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance,
                                             PathIndexUnit.Knot);
        int   seg  = Mathf.Clamp(Mathf.FloorToInt(tk), 0, segCount - 1);
        float frac = Mathf.Clamp01(tk - seg);
        var   ndA  = data.nodes[seg];
        var   ndB  = data.nodes[spline.Closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1)];
        int   a    = Mathf.Clamp(ndA.scenery, 0, StyleCount - 1);
        int   b    = Mathf.Clamp(ndB.scenery, 0, StyleCount - 1);

        float wallA = a is > 0 and < TunnelStyle ? 1f - frac : 0f;
        float wallB = b is > 0 and < TunnelStyle ? frac      : 0f;
        wallSolid   = wallA + wallB;
        tunnelSolid = (a == TunnelStyle ? 1f - frac : 0f) + (b == TunnelStyle ? frac : 0f);
        groundW     = (ndA.onGround ? 1f - frac : 0f) + (ndB.onGround ? frac : 0f);

        if (wallSolid > 0f)
            tint = (StyleTint[a] * wallA + StyleTint[b] * wallB) / wallSolid;
    }
}
