using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Bounding volume hierarchy over world-space triangles, answering "does a ray
    /// from here in this direction hit anything?". Flat arrays and plain float
    /// maths throughout, because the editor runs this on Mono and an avatar needs
    /// on the order of a hundred thousand rays.
    /// </summary>
    internal sealed class TriangleBvh {
        private const int LeafSize = 4;
        private const float MinHitDistance = 1e-5f;

        // Triangles, reordered so each leaf's are contiguous: corner 0 and the two edges.
        private float[] v0x, v0y, v0z, e1x, e1y, e1z, e2x, e2y, e2z;

        // Nodes: bounds, then either children (left >= 0) or a triangle range.
        private readonly List<float> boundsList = new List<float>();
        private readonly List<int> leftList = new List<int>(), rightList = new List<int>(), startList = new List<int>(), countList = new List<int>();
        private float[] bounds;
        private int[] left, right, start, count;
        private int[] stack = new int[64];

        internal int TriangleCount { get; }

        /// <param name="corners">Three corners per triangle, in order.</param>
        internal TriangleBvh(IReadOnlyList<Vector3> corners) {
            var n = corners.Count / 3;
            TriangleCount = n;
            var order = new int[n];
            var cx = new float[n];
            var cy = new float[n];
            var cz = new float[n];
            for (var i = 0; i < n; i++) {
                order[i] = i;
                var c = (corners[i * 3] + corners[i * 3 + 1] + corners[i * 3 + 2]) / 3f;
                cx[i] = c.x;
                cy[i] = c.y;
                cz[i] = c.z;
            }
            if (n > 0) Build(corners, order, 0, n, cx, cy, cz);

            bounds = boundsList.ToArray();
            left = leftList.ToArray();
            right = rightList.ToArray();
            start = startList.ToArray();
            count = countList.ToArray();

            v0x = new float[n]; v0y = new float[n]; v0z = new float[n];
            e1x = new float[n]; e1y = new float[n]; e1z = new float[n];
            e2x = new float[n]; e2y = new float[n]; e2z = new float[n];
            for (var i = 0; i < n; i++) {
                var t = order[i];
                var a = corners[t * 3];
                var b = corners[t * 3 + 1];
                var c = corners[t * 3 + 2];
                v0x[i] = a.x; v0y[i] = a.y; v0z[i] = a.z;
                e1x[i] = b.x - a.x; e1y[i] = b.y - a.y; e1z[i] = b.z - a.z;
                e2x[i] = c.x - a.x; e2y[i] = c.y - a.y; e2z[i] = c.z - a.z;
            }
        }

        private int Build(IReadOnlyList<Vector3> corners, int[] order, int from, int to, float[] cx, float[] cy, float[] cz) {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            var cmin = Vector3.positiveInfinity;
            var cmax = Vector3.negativeInfinity;
            for (var i = from; i < to; i++) {
                var t = order[i];
                for (var k = 0; k < 3; k++) {
                    min = Vector3.Min(min, corners[t * 3 + k]);
                    max = Vector3.Max(max, corners[t * 3 + k]);
                }
                var c = new Vector3(cx[t], cy[t], cz[t]);
                cmin = Vector3.Min(cmin, c);
                cmax = Vector3.Max(cmax, c);
            }

            var index = leftList.Count;
            boundsList.Add(min.x); boundsList.Add(min.y); boundsList.Add(min.z);
            boundsList.Add(max.x); boundsList.Add(max.y); boundsList.Add(max.z);
            leftList.Add(-1);
            rightList.Add(-1);
            startList.Add(from);
            countList.Add(to - from);

            var extent = cmax - cmin;
            if (to - from <= LeafSize || Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)) <= 0f) return index;

            var keys = extent.x >= extent.y && extent.x >= extent.z ? cx : extent.y >= extent.z ? cy : cz;
            Array.Sort(order, from, to - from, Comparer<int>.Create((p, q) => keys[p].CompareTo(keys[q])));
            var mid = (from + to) / 2;
            var l = Build(corners, order, from, mid, cx, cy, cz);
            var r = Build(corners, order, mid, to, cx, cy, cz);
            leftList[index] = l;
            rightList[index] = r;
            countList[index] = 0;
            return index;
        }

        /// <summary>True if the ray hits any triangle further than a hair's breadth away.</summary>
        internal bool Hits(Vector3 origin, Vector3 direction) {
            if (TriangleCount == 0) return false;
            float ox = origin.x, oy = origin.y, oz = origin.z;
            float dx = direction.x, dy = direction.y, dz = direction.z;
            float ix = 1f / dx, iy = 1f / dy, iz = 1f / dz;

            var top = 0;
            stack[top++] = 0;
            while (top > 0) {
                var node = stack[--top];
                if (!HitsBox(node, ox, oy, oz, ix, iy, iz)) continue;
                var l = left[node];
                if (l < 0) {
                    var end = start[node] + count[node];
                    for (var i = start[node]; i < end; i++) {
                        if (HitsTriangle(i, ox, oy, oz, dx, dy, dz)) return true;
                    }
                    continue;
                }
                if (top + 2 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                stack[top++] = right[node];
                stack[top++] = l;
            }
            return false;
        }

        private bool HitsBox(int node, float ox, float oy, float oz, float ix, float iy, float iz) {
            var b = node * 6;
            float tmin = 0f, tmax = float.PositiveInfinity;
            if (!Slab(bounds[b], bounds[b + 3], ox, ix, ref tmin, ref tmax)) return false;
            if (!Slab(bounds[b + 1], bounds[b + 4], oy, iy, ref tmin, ref tmax)) return false;
            return Slab(bounds[b + 2], bounds[b + 5], oz, iz, ref tmin, ref tmax);
        }

        private static bool Slab(float lo, float hi, float o, float inv, ref float tmin, ref float tmax) {
            if (float.IsInfinity(inv)) return o >= lo && o <= hi; // parallel to this slab
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2) {
                var swap = t1;
                t1 = t2;
                t2 = swap;
            }
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            return tmax >= tmin;
        }

        // Möller–Trumbore, both faces.
        private bool HitsTriangle(int i, float ox, float oy, float oz, float dx, float dy, float dz) {
            float ax = e1x[i], ay = e1y[i], az = e1z[i];
            float bx = e2x[i], by = e2y[i], bz = e2z[i];
            var px = dy * bz - dz * by;
            var py = dz * bx - dx * bz;
            var pz = dx * by - dy * bx;
            var det = ax * px + ay * py + az * pz;
            if (det > -1e-12f && det < 1e-12f) return false;
            var invDet = 1f / det;
            var sx = ox - v0x[i];
            var sy = oy - v0y[i];
            var sz = oz - v0z[i];
            var u = (sx * px + sy * py + sz * pz) * invDet;
            if (u < 0f || u > 1f) return false;
            var qx = sy * az - sz * ay;
            var qy = sz * ax - sx * az;
            var qz = sx * ay - sy * ax;
            var v = (dx * qx + dy * qy + dz * qz) * invDet;
            if (v < 0f || u + v > 1f) return false;
            return (bx * qx + by * qy + bz * qz) * invDet > MinHitDistance;
        }
    }
}
