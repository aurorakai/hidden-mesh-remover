using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Works out which triangles the hide blendshapes keep out of sight for good,
    /// and builds a copy of the mesh without them. A blendshape qualifies if its
    /// name matches the filter, its editor weight is at least minWeight, and either
    /// nothing animates it or removeAnimated is on.
    ///
    /// A triangle is removed when a qualifying blendshape moves all three of its
    /// vertices (by at least movementThreshold of that blendshape's largest
    /// movement, so slight sculpted corrections nearby don't count) and the result
    /// is out of sight: collapsed to almost nothing, or enclosed by other geometry
    /// (see <see cref="Occlusion"/>). A hide shape that only reshapes a surface,
    /// such as smoothing a fluff shell down onto the head, leaves it visible, so it
    /// is kept.
    ///
    /// Weights are passed in rather than read from the renderer, so the build and
    /// the inspector preview see the same values. Shared by both.
    /// </summary>
    internal static class HiddenTriangles {
        private const float MovedEpsilonSqr = 1e-10f;
        /// <summary>A triangle shrunk below this fraction of its area counts as gone.</summary>
        private const float CollapsedAreaRatio = 0.01f;

        /// <summary>Whether a triangle (by vertex index) is enclosed by other geometry.</summary>
        internal delegate bool EnclosedTest(int a, int b, int c);

        internal struct ShapeHit {
            public string name;
            public float weight;
            public bool animated;
            public bool removed;
            /// <summary>Triangles this blendshape takes out of sight on its own.</summary>
            public int triangles;
            /// <summary>Triangles it moves but leaves visible (kept).</summary>
            public int visible;
        }

        internal sealed class Analysis {
            public readonly List<ShapeHit> hits = new List<ShapeHit>();
            public readonly List<string> removedNames = new List<string>();
            /// <summary>Per sub-mesh, per triangle (index / 3): whether it is removed.</summary>
            public bool[][] removedTriangle;
            public int removedTriangles;
            /// <summary>Triangles a qualifying blendshape moves but leaves visible.</summary>
            public int keptVisible;
            /// <summary>Vertices no kept triangle uses (only removed if removeUnusedVertices is on).</summary>
            public int unusedVertices;
            public int vertexCount;
            public int triangleCount;
        }

        internal static float[] Weights(SkinnedMeshRenderer smr) {
            var mesh = smr.sharedMesh;
            var weights = new float[mesh != null ? mesh.blendShapeCount : 0];
            for (var i = 0; i < weights.Length; i++) weights[i] = smr.GetBlendShapeWeight(i);
            return weights;
        }

        /// <summary>Vertices that any blendshape this renderer would remove moves, or null if none.</summary>
        internal static bool[] CandidateVertices(Mesh mesh, float[] weights, RemoveHiddenMeshes settings, HashSet<string> animated) {
            bool[] mask = null;
            foreach (var shape in Qualifying(mesh, weights, settings)) {
                if (!settings.removeAnimated && animated.Contains(mesh.GetBlendShapeName(shape))) continue;
                var moved = MovedVertices(mesh, shape, settings.movementThreshold);
                mask = mask ?? new bool[mesh.vertexCount];
                for (var v = 0; v < moved.Length; v++) mask[v] |= moved[v];
            }
            return mask;
        }

        /// <param name="enclosed">Visibility test; null treats every moved triangle as hidden.</param>
        internal static Analysis Analyse(Mesh mesh, float[] weights, RemoveHiddenMeshes settings, HashSet<string> animated, EnclosedTest enclosed) {
            var result = new Analysis { vertexCount = mesh.vertexCount };
            var indices = new int[mesh.subMeshCount][];
            var topologies = new MeshTopology[mesh.subMeshCount];
            for (var sub = 0; sub < mesh.subMeshCount; sub++) {
                topologies[sub] = mesh.GetTopology(sub);
                indices[sub] = mesh.GetIndices(sub);
            }

            var shapes = new List<(int shape, bool removes, bool[] moved)>();
            var mask = new bool[mesh.vertexCount];
            var deformed = mesh.vertices;
            var original = mesh.vertices;
            var deltas = new Vector3[mesh.vertexCount];
            foreach (var shape in Qualifying(mesh, weights, settings)) {
                var name = mesh.GetBlendShapeName(shape);
                var removes = settings.removeAnimated || !animated.Contains(name);
                var moved = MovedVertices(mesh, shape, settings.movementThreshold);
                shapes.Add((shape, removes, moved));
                if (!removes) continue;
                result.removedNames.Add(name);
                for (var v = 0; v < moved.Length; v++) mask[v] |= moved[v];
                var frame = mesh.GetBlendShapeFrameCount(shape) - 1;
                mesh.GetBlendShapeFrameVertices(shape, frame, deltas, null, null);
                var scale = weights[shape] / Mathf.Max(0.0001f, mesh.GetBlendShapeFrameWeight(shape, frame));
                for (var v = 0; v < deltas.Length; v++) deformed[v] += deltas[v] * scale;
            }

            // Memoised per triangle: is it out of sight once moved?
            var hidden = new Dictionary<long, bool>();
            bool OutOfSight(int sub, int t, int a, int b, int c) {
                var key = ((long)sub << 32) | (uint)t;
                if (hidden.TryGetValue(key, out var known)) return known;
                var area = Vector3.Cross(original[b] - original[a], original[c] - original[a]).magnitude;
                var after = Vector3.Cross(deformed[b] - deformed[a], deformed[c] - deformed[a]).magnitude;
                var gone = after <= area * CollapsedAreaRatio || enclosed == null || enclosed(a, b, c);
                hidden[key] = gone;
                return gone;
            }

            result.removedTriangle = new bool[mesh.subMeshCount][];
            var used = new bool[mesh.vertexCount];
            for (var sub = 0; sub < indices.Length; sub++) {
                var tris = indices[sub];
                if (topologies[sub] != MeshTopology.Triangles) {
                    result.removedTriangle[sub] = new bool[0];
                    foreach (var i in tris) used[i] = true;
                    continue;
                }
                result.removedTriangle[sub] = new bool[tris.Length / 3];
                for (var t = 0; t + 2 < tris.Length; t += 3) {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    result.triangleCount++;
                    if (mask[a] && mask[b] && mask[c]) {
                        if (OutOfSight(sub, t, a, b, c)) {
                            result.removedTriangle[sub][t / 3] = true;
                            result.removedTriangles++;
                            continue;
                        }
                        result.keptVisible++;
                    }
                    used[a] = used[b] = used[c] = true;
                }
            }
            foreach (var u in used) {
                if (!u) result.unusedVertices++;
            }

            foreach (var (shape, removes, moved) in shapes) {
                int gone = 0, visible = 0;
                for (var sub = 0; sub < indices.Length; sub++) {
                    if (topologies[sub] != MeshTopology.Triangles) continue;
                    var tris = indices[sub];
                    for (var t = 0; t + 2 < tris.Length; t += 3) {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        if (!(moved[a] && moved[b] && moved[c])) continue;
                        if (OutOfSight(sub, t, a, b, c)) gone++;
                        else visible++;
                    }
                }
                result.hits.Add(new ShapeHit {
                    name = mesh.GetBlendShapeName(shape),
                    weight = weights[shape],
                    animated = animated.Contains(mesh.GetBlendShapeName(shape)),
                    removed = removes,
                    triangles = gone,
                    visible = visible,
                });
            }
            return result;
        }

        /// <summary>A copy of the mesh without the removed triangles, or null if there are none.</summary>
        internal static Mesh BuildWithoutHidden(Mesh mesh, Analysis analysis) {
            if (analysis.removedTriangles == 0) return null;
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name;
            var bounds = mesh.bounds;
            for (var sub = 0; sub < mesh.subMeshCount; sub++) {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(sub);
                var removed = analysis.removedTriangle[sub];
                var kept = new List<int>(tris.Length);
                for (var t = 0; t + 2 < tris.Length; t += 3) {
                    if (removed[t / 3]) continue;
                    kept.Add(tris[t]);
                    kept.Add(tris[t + 1]);
                    kept.Add(tris[t + 2]);
                }
                copy.SetTriangles(kept, sub, false);
            }
            copy.bounds = bounds;
            return copy;
        }

        private static IEnumerable<int> Qualifying(Mesh mesh, float[] weights, RemoveHiddenMeshes settings) {
            for (var shape = 0; shape < mesh.blendShapeCount && shape < weights.Length; shape++) {
                if (!BlendshapeFilter.Matches(mesh.GetBlendShapeName(shape), settings)) continue;
                if (weights[shape] < settings.minWeight - 0.01f) continue;
                yield return shape;
            }
        }

        /// <summary>
        /// Vertices the blendshape moves by at least <paramref name="fraction"/> of its
        /// largest movement. Hide shapes are often sculpted with small corrections to
        /// the surrounding surface; those vertices aren't being hidden.
        /// </summary>
        internal static bool[] MovedVertices(Mesh mesh, int shape, float fraction) {
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, deltas, null, null);
            var maxSqr = 0f;
            foreach (var d in deltas) maxSqr = Mathf.Max(maxSqr, d.sqrMagnitude);
            var threshold = Mathf.Max(MovedEpsilonSqr, maxSqr * fraction * fraction);
            var moved = new bool[deltas.Length];
            for (var v = 0; v < deltas.Length; v++) moved[v] = deltas[v].sqrMagnitude >= threshold;
            return moved;
        }
    }
}
