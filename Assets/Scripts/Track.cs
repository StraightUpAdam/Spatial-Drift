using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// Wraps a SplineContainer with arc-length-based sampling and a configurable
/// track width.  Cars use Sample() to position themselves along the track —
/// the spline gives world-space position + tangent (forward) + up vectors,
/// which together let the car follow corkscrews and loops naturally.
[RequireComponent(typeof(SplineContainer))]
public class Track : MonoBehaviour
{
    [Header("Track Geometry")]
    [Tooltip("Drivable width of the track in metres.")]
    public float width = 12f;

    [Tooltip("How far the car sits above the spline (track surface offset).")]
    public float surfaceOffset = 0f;

    [Tooltip("Per-node widths, one per spline knot (set by the map editor).  When " +
             "null/empty the track is a uniform `width`.  Interpolated along each " +
             "segment so the road can taper from node to node.")]
    public float[] nodeWidths;

    SplineContainer container;

    void Awake()
    {
        container = GetComponent<SplineContainer>();
    }

    public SplineContainer Container
    {
        get
        {
            if (container == null) container = GetComponent<SplineContainer>();
            return container;
        }
    }

    /// True total length of the spline in world units (metres).
    public float Length
    {
        get
        {
            var c = Container;
            return c != null && c.Spline != null ? c.Spline.GetLength() : 0f;
        }
    }

    /// Whether the spline forms a closed loop — drivers wrap around.
    public bool IsClosed
    {
        get
        {
            var c = Container;
            return c != null && c.Spline != null && c.Spline.Closed;
        }
    }

    /// Wrap or clamp a distance value depending on whether the spline is closed.
    public float WrapDistance(float distance)
    {
        float len = Length;
        if (len <= 0f) return 0f;

        if (IsClosed)
        {
            distance %= len;
            if (distance < 0f) distance += len;
            return distance;
        }
        return Mathf.Clamp(distance, 0f, len);
    }

    /// Sample the spline at a world-distance offset along it.
    /// Returns world-space position, forward direction (tangent), and surface up.
    public void Sample(float distance,
                       out Vector3 worldPos,
                       out Vector3 forward,
                       out Vector3 up)
    {
        var c = Container;
        if (c == null || c.Spline == null)
        {
            worldPos = transform.position;
            forward  = transform.forward;
            up       = transform.up;
            return;
        }

        var spline = c.Spline;
        float len  = spline.GetLength();
        if (len <= 0f)
        {
            worldPos = transform.position;
            forward  = transform.forward;
            up       = transform.up;
            return;
        }

        // Distance → normalised t (arc-length parameterised).
        float t = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);

        // Evaluate in spline-container local space.
        float3 localPos, localTan, localUp;
        spline.Evaluate(t, out localPos, out localTan, out localUp);

        // Transform into world space — SplineContainer respects its own transform.
        worldPos = c.transform.TransformPoint((Vector3)localPos);

        Vector3 worldTan = c.transform.TransformDirection((Vector3)localTan);
        Vector3 worldUp  = c.transform.TransformDirection((Vector3)localUp);

        forward = worldTan.sqrMagnitude > 1e-6f ? worldTan.normalized : transform.forward;
        up      = worldUp.sqrMagnitude  > 1e-6f ? worldUp.normalized  : transform.up;
    }

    /// Convenience: get the position a vehicle should be at, given its distance
    /// along the spline AND a lateral offset (left/right of centreline).
    public void Sample(float distance, float lateralOffset,
                       out Vector3 worldPos,
                       out Quaternion worldRot,
                       out Vector3 forward,
                       out Vector3 up)
    {
        Sample(distance, out Vector3 centrePos, out forward, out up);

        // Right vector = up × forward (left-handed Unity coord system).
        Vector3 right = Vector3.Cross(up, forward).normalized;

        worldPos = centrePos + right * lateralOffset + up * surfaceOffset;
        worldRot = Quaternion.LookRotation(forward, up);
    }

    /// Half-width of the drivable surface (for steering clamps).  Uniform fallback.
    public float HalfWidth => width * 0.5f;

    /// Drivable width at a given distance along the track.  Honours per-node
    /// widths (linearly across each segment) when they're set; otherwise the
    /// uniform `width`.  Mirrors how the spline parameterises t per segment.
    public float WidthAt(float distance)
    {
        if (nodeWidths == null || nodeWidths.Length == 0) return width;
        if (nodeWidths.Length == 1) return nodeWidths[0];

        var c = Container;
        if (c == null || c.Spline == null) return width;
        var spline = c.Spline;
        if (spline.GetLength() <= 0f) return nodeWidths[0];

        // KNOT units, not Normalized: normalized t is arc-length across the WHOLE
        // spline, so dividing it by segment count assumes equal-length segments —
        // per-node values landed on the wrong nodes on uneven tracks.
        int   n        = nodeWidths.Length;
        int   segCount = spline.Closed ? n : n - 1;
        if (segCount < 1) return nodeWidths[0];

        float tk   = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Knot);
        int   seg  = Mathf.Clamp(Mathf.FloorToInt(tk), 0, segCount - 1);
        float frac = Mathf.Clamp01(tk - seg);
        int   a    = seg;
        int   b    = spline.Closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
        return Mathf.Lerp(nodeWidths[a], nodeWidths[b], frac);
    }

    /// Half-width at a distance (per-node aware) — for wall clamps / steering.
    public float HalfWidthAt(float distance) => WidthAt(distance) * 0.5f;
}
