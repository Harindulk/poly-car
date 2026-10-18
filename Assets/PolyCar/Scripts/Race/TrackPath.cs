using System.Collections.Generic;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// The racing line / road centreline as a dense polyline (a point every few metres). Used for race
    /// progress, checkpoints, respawn poses, wrong-way checks, the minimap and the AI drivers.
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        public List<Vector3> points = new List<Vector3>();
        public bool loop;
        [Tooltip("Half the drivable road width in metres (centre to edge).")]
        public float halfWidth = 6f;
        [Tooltip("Optional per-point half width (same count as points); empty = use halfWidth.")]
        public List<float> widths = new List<float>();

        private float[] _cum;
        private float _length;

        public float Length { get { if (_cum == null) Build(); return _length; } }
        public int Count => points.Count;

        private void Awake() => Build();

        public void Build()
        {
            int n = points.Count;
            _cum = new float[Mathf.Max(1, n + 1)];
            _length = 0f;
            for (int i = 1; i < n; i++)
            {
                _length += Vector3.Distance(points[i - 1], points[i]);
                _cum[i] = _length;
            }
            if (loop && n > 1)
            {
                _length += Vector3.Distance(points[n - 1], points[0]);
                _cum[n] = _length;
            }
        }

        public float Wrap(float d)
        {
            if (_cum == null) Build();
            return loop ? Mathf.Repeat(d, _length) : Mathf.Clamp(d, 0f, _length);
        }

        private int SegmentAt(float d, out float t)
        {
            int n = points.Count;
            int segs = loop ? n : n - 1;
            // Binary search on cumulative distance.
            int lo = 0, hi = segs - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_cum[mid] <= d) lo = mid; else hi = mid - 1;
            }
            float segLen = _cum[lo + 1] - _cum[lo];
            t = segLen > 1e-4f ? Mathf.Clamp01((d - _cum[lo]) / segLen) : 0f;
            return lo;
        }

        public Vector3 PointAt(float d)
        {
            if (points.Count == 0) return transform.position;
            if (points.Count == 1) return points[0];
            if (_cum == null) Build();
            d = Wrap(d);
            int i = SegmentAt(d, out float t);
            return Vector3.Lerp(points[i], points[(i + 1) % points.Count], t);
        }

        public float HalfWidthAt(float d)
        {
            if (widths == null || widths.Count != points.Count || points.Count < 2) return halfWidth;
            d = Wrap(d);
            int i = SegmentAt(d, out float t);
            return Mathf.Lerp(widths[i], widths[(i + 1) % points.Count], t);
        }

        public Vector3 DirectionAt(float d)
        {
            Vector3 a = PointAt(d - 3f), b = PointAt(d + 3f);
            Vector3 dir = b - a;
            if (!loop && d < 3f) dir = PointAt(d + 6f) - PointAt(d);
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
        }

        public Quaternion RotationAt(float d)
        {
            Vector3 dir = DirectionAt(d);
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        /// <summary>
        /// Distance along the path of the point nearest to <paramref name="pos"/>. With a hint &gt;= 0 only
        /// segments within <paramref name="window"/> metres of the hint are searched (stops jumps between
        /// parallel stretches of road); lateral is metres to the right of the centreline.
        /// </summary>
        public float Project(Vector3 pos, float hint, float window, out float lateral)
        {
            if (_cum == null) Build();
            int n = points.Count;
            lateral = 0f;
            if (n < 2) return 0f;
            int segs = loop ? n : n - 1;
            float best = float.MaxValue, bestD = 0f;
            Vector3 bestPoint = points[0], bestDir = Vector3.forward;

            if (hint >= 0f && window > 0f && window * 2f < _length)
            {
                int i0 = SegmentAt(Wrap(hint - window), out _);
                int count = Mathf.CeilToInt(window * 2f / Mathf.Max(0.5f, _length / segs)) + 4;
                count = Mathf.Min(count, segs);
                for (int k = 0; k <= count; k++)
                {
                    int i = loop ? (i0 + k) % segs : Mathf.Min(i0 + k, segs - 1);
                    Test(i);
                }
            }
            else
            {
                for (int i = 0; i < segs; i++) Test(i);
            }

            Vector3 right = Vector3.Cross(Vector3.up, bestDir).normalized;
            lateral = Vector3.Dot(pos - bestPoint, right);
            return bestD;

            void Test(int i)
            {
                Vector3 a = points[i], b = points[(i + 1) % n];
                Vector3 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / len2) : 0f;
                Vector3 p = a + ab * t;
                Vector3 diff = pos - p;
                diff.y *= 0.5f; // vertical error matters less than lateral
                float dist = diff.sqrMagnitude;
                if (dist < best)
                {
                    best = dist;
                    bestD = _cum[i] + Mathf.Sqrt(len2) * t;
                    bestPoint = p;
                    bestDir = len2 > 1e-6f ? ab / Mathf.Sqrt(len2) : Vector3.forward;
                }
            }
        }

        /// <summary>Turn sharpness ahead: radians of heading change per metre over the window.</summary>
        public float CurvatureAhead(float d, float window)
        {
            Vector3 a = DirectionAt(d), b = DirectionAt(d + window);
            a.y = 0f; b.y = 0f;
            return Vector3.Angle(a, b) * Mathf.Deg2Rad / Mathf.Max(1f, window);
        }

        /// <summary>Signed turn direction ahead (+ right, - left) in degrees.</summary>
        public float TurnAngleAhead(float d, float window)
        {
            Vector3 a = DirectionAt(d), b = DirectionAt(d + window);
            a.y = 0f; b.y = 0f;
            return Vector3.SignedAngle(a, b, Vector3.up);
        }

        private float[] _profile;
        private float _profileLat, _profileBrake;
        private const float ProfileStep = 4f;

        /// <summary>
        /// Target speed (m/s) at a distance: corner speed from curvature (v = sqrt(aLat / k)), then a
        /// backward pass so every corner can be reached by braking at <paramref name="brake"/> m/s².
        /// Cached per (aLat, brake).
        /// </summary>
        public float SpeedLimitAt(float d, float aLat, float brake)
        {
            if (_profile == null || !Mathf.Approximately(aLat, _profileLat) || !Mathf.Approximately(brake, _profileBrake))
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Length / ProfileStep) + 1);
                _profile = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float k = CurvatureAhead(i * ProfileStep - 5f, 10f);
                    _profile[i] = Mathf.Min(90f, Mathf.Sqrt(aLat / Mathf.Max(k, 1e-4f)));
                }
                // Two backward passes so the braking wrap-around works on loops.
                for (int pass = 0; pass < (loop ? 2 : 1); pass++)
                    for (int i = n - 2 + (loop ? 1 : 0); i >= 0; i--)
                    {
                        int next = (i + 1) % n;
                        _profile[i] = Mathf.Min(_profile[i], Mathf.Sqrt(_profile[next] * _profile[next] + 2f * brake * ProfileStep));
                    }
                _profileLat = aLat; _profileBrake = brake;
            }
            d = Wrap(d);
            int idx = Mathf.Clamp(Mathf.RoundToInt(d / ProfileStep), 0, _profile.Length - 1);
            return _profile[idx];
        }

        public Bounds FlatBounds()
        {
            var b = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return b;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (points.Count < 2) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 1; i < points.Count; i++) Gizmos.DrawLine(points[i - 1] + Vector3.up, points[i] + Vector3.up);
            if (loop) Gizmos.DrawLine(points[points.Count - 1] + Vector3.up, points[0] + Vector3.up);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(points[0] + Vector3.up, 1.5f);
        }
#endif
    }
}
