using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Decides whether a triangle that a hide shape has moved is really out of
    /// sight, as opposed to reshaped into a surface that is still visible (such as
    /// a fluff shell smoothed down onto the head). A triangle counts as enclosed
    /// when rays cast both ways along its normal, from its centre and from near
    /// each corner, all hit geometry that is always shown: renderers that are on
    /// and that nothing toggles, minus every triangle that is itself a removal
    /// candidate. Positions are the avatar as posed in the editor, with the
    /// renderers' current blendshape weights.
    /// </summary>
    internal sealed class Occlusion {
        private readonly TriangleBvh bvh;

        private Occlusion(TriangleBvh bvh) {
            this.bvh = bvh;
        }

        internal int OccluderTriangles => bvh.TriangleCount;

        /// <param name="candidateMask">Per skinned renderer, vertices a hide shape moves (null for none). Triangles with all three moved are left out.</param>
        internal static Occlusion Build(GameObject avatarRoot, Func<SkinnedMeshRenderer, bool[]> candidateMask) {
            var toggled = AnimatedBlendshapes.ToggledObjects(avatarRoot);
            var corners = new List<Vector3>();
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true)) {
                if (!renderer.enabled || !ActiveBelowRoot(renderer.transform, avatarRoot.transform)) continue;
                if (IsToggled(renderer.transform, avatarRoot.transform, toggled)) continue;

                Mesh mesh;
                Vector3[] world;
                bool[] skip = null;
                if (renderer is SkinnedMeshRenderer smr) {
                    if (smr.sharedMesh == null) continue;
                    mesh = smr.sharedMesh;
                    world = WorldVertices(smr);
                    skip = candidateMask?.Invoke(smr);
                } else if (renderer is MeshRenderer) {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    mesh = filter.sharedMesh;
                    world = mesh.vertices;
                    var matrix = renderer.transform.localToWorldMatrix;
                    for (var i = 0; i < world.Length; i++) world[i] = matrix.MultiplyPoint3x4(world[i]);
                } else {
                    continue;
                }

                for (var sub = 0; sub < mesh.subMeshCount; sub++) {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var tris = mesh.GetTriangles(sub);
                    for (var t = 0; t + 2 < tris.Length; t += 3) {
                        if (skip != null && skip[tris[t]] && skip[tris[t + 1]] && skip[tris[t + 2]]) continue;
                        corners.Add(world[tris[t]]);
                        corners.Add(world[tris[t + 1]]);
                        corners.Add(world[tris[t + 2]]);
                    }
                }
            }
            return new Occlusion(new TriangleBvh(corners));
        }

        /// <summary>Vertex positions in world space, skinned and with current blendshape weights.</summary>
        internal static Vector3[] WorldVertices(SkinnedMeshRenderer smr) {
            var baked = new Mesh();
            try {
                smr.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var t = smr.transform;
                var matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                for (var i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                return vertices;
            } finally {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        internal bool Enclosed(Vector3 a, Vector3 b, Vector3 c) {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-20f) return true;
            normal.Normalize();
            var centre = (a + b + c) / 3f;
            return Covered(centre, normal)
                   && Covered(Vector3.Lerp(centre, a, 0.8f), normal)
                   && Covered(Vector3.Lerp(centre, b, 0.8f), normal)
                   && Covered(Vector3.Lerp(centre, c, 0.8f), normal);
        }

        private bool Covered(Vector3 point, Vector3 normal) {
            return bvh.Hits(point, normal) && bvh.Hits(point, -normal);
        }

        /// <summary>
        /// Active, ignoring the avatar root itself: a PC/Quest pair often keeps one
        /// root disabled in the scene, and the build copy is active either way.
        /// </summary>
        private static bool ActiveBelowRoot(Transform t, Transform root) {
            for (; t != null && t != root; t = t.parent) {
                if (!t.gameObject.activeSelf) return false;
            }
            return true;
        }

        private static bool IsToggled(Transform t, Transform root, HashSet<Transform> toggled) {
            for (; t != null && t != root; t = t.parent) {
                if (toggled.Contains(t)) return true;
            }
            return false;
        }
    }
}
