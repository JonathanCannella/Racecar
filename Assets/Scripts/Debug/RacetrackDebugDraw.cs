

using UnityEngine;
using System.Collections.Generic;

namespace GameAI
{
    /// <summary>
    /// Lightweight, self-contained debug drawing helpers for IRacetrackData.
    /// Drop this into your project; call the static methods from Update() or OnDrawGizmos.
    /// </summary>
    public static class RacetrackDebugDraw
    {
        /// <summary>
        /// Draw the entire Bezier segment chain using control polygons and a sampled curve.
        /// Colors: control polygon gray; P0 red, P1/P2 green, P3 blue; curve yellow.
        /// </summary>
        public static void DrawBezierChain(IRacetrackData racetrack, int samplesPerSeg = 20, float yOffset = 0.25f)
        {
            if (racetrack == null || !racetrack.IsInitialized) return;

            int segCount = racetrack.BezierSegmentCount;
            if (segCount <= 0) return;

            int samples = Mathf.Max(4, samplesPerSeg);
            Vector3 up = Vector3.up * yOffset;

            for (int si = 0; si < segCount; si++)
            {
                Vector3 p0 = racetrack.BezierP0[si] + up;
                Vector3 p1 = racetrack.BezierP1[si] + up;
                Vector3 p2 = racetrack.BezierP2[si] + up;
                Vector3 p3 = racetrack.BezierP3[si] + up;

                // Control polygon
                Debug.DrawLine(p0, p1, Color.gray);
                Debug.DrawLine(p1, p2, Color.gray);
                Debug.DrawLine(p2, p3, Color.gray);

                // Markers
                DrawCross(p0, 0.25f, 0f, Color.red);
                DrawCross(p1, 0.20f, 0f, Color.green);
                DrawCross(p2, 0.20f, 0f, Color.green);
                DrawCross(p3, 0.25f, 0f, Color.blue);

                // Sampled curve
                Vector3 prev = p0;
                for (int i = 1; i <= samples; i++)
                {
                    float t = (float)i / samples;
                    Vector3 pt = CubicBezierPoint(p0, p1, p2, p3, t);
                    Debug.DrawLine(prev, pt, Color.yellow);
                    prev = pt;
                }
            }
        }

        /// <summary>
        /// Draw a polyline through the cached vertex path samples that the vehicle logic uses.
        /// Helpful to contrast with the Bezier control visualization.
        /// </summary>
        public static void DrawVertexPathPolyline(IRacetrackData racetrack, int stride = 4, float yOffset = 0.15f)
        {
            if (racetrack == null || !racetrack.IsInitialized) return;
            if (stride <= 0) stride = 1;
            var verts = racetrack.Vertices;
            if (verts == null || verts.Count < 2) return;

            Vector3 up = Vector3.up * yOffset;
            Vector3 prev = verts[0] + up;
            for (int i = stride; i < verts.Count; i += stride)
            {
                Vector3 curr = verts[i] + up;
                Debug.DrawLine(prev, curr, Color.cyan);
                prev = curr;
            }
        }

        /// <summary>
        /// Draw closest-point frame at the vehicle: center (magenta), forward tangent (white), and a short normal tick.
        /// </summary>
        public static void DrawClosestFrame(IRacetrackData racetrack, float axisLen = 1.0f, float yOffset = 0.05f)
        {
            if (racetrack == null || !racetrack.IsInitialized) return;
            Vector3 p = racetrack.ClosestPointOnPath; p.y += yOffset;
            Vector3 t = racetrack.ClosestPointDirectionOnPath.normalized;
            Vector3 n = new Vector3(-t.z, 0f, t.x); // 2D left normal

            DrawCross(p, 0.15f, 0f, Color.magenta);
            Debug.DrawLine(p, p + t * axisLen, Color.white);
            Debug.DrawLine(p, p + n * (0.5f * axisLen), Color.gray);
        }

        // ---------- helpers ----------

        private static void DrawCross(Vector3 p, float size, float yOffset, Color c)
        {
            p.y += yOffset;
            Vector3 dx = new Vector3(size, 0f, 0f);
            Vector3 dz = new Vector3(0f, 0f, size);
            Debug.DrawLine(p - dx, p + dx, c);
            Debug.DrawLine(p - dz, p + dz, c);
        }

        private static Vector3 CubicBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float uuu = uu * u;
            float tt = t * t;
            float ttt = tt * t;
            return (uuu * p0) + (3f * uu * t * p1) + (3f * u * tt * p2) + (ttt * p3);
        }
    }
}