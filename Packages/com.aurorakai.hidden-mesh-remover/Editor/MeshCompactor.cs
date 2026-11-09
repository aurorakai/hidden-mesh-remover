using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Rebuilds a mesh without the vertices that no index uses. Keeps every
    /// sub-mesh (so material slots still line up), positions, normals, tangents,
    /// vertex colours, all eight UV channels at their original dimension, bone
    /// weights, bind poses and every blendshape frame, in the same order and with
    /// the same names, so animations and renderer weights still apply. Bounds are
    /// copied from the source so culling doesn't change.
    /// </summary>
    internal static class MeshCompactor {
        /// <summary>The compacted copy, or null if every vertex is in use.</summary>
        internal static Mesh Compact(Mesh src, out int removedVertices) {
            removedVertices = 0;
            var n = src.vertexCount;
            var subCount = src.subMeshCount;
            var indices = new int[subCount][];
            var topologies = new MeshTopology[subCount];
            var used = new bool[n];
            for (var sub = 0; sub < subCount; sub++) {
                topologies[sub] = src.GetTopology(sub);
                indices[sub] = src.GetIndices(sub);
                foreach (var i in indices[sub]) used[i] = true;
            }

            var map = new int[n];
            var count = 0;
            for (var v = 0; v < n; v++) map[v] = used[v] ? count++ : -1;
            if (count == n) return null;
            removedVertices = n - count;
            var keep = new int[count];
            for (var v = 0; v < n; v++) {
                if (used[v]) keep[map[v]] = v;
            }

            var dst = new Mesh {
                name = src.name,
                indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            dst.SetVertices(Pick(src.vertices, keep));
            if (src.HasVertexAttribute(VertexAttribute.Normal)) dst.SetNormals(Pick(src.normals, keep));
            if (src.HasVertexAttribute(VertexAttribute.Tangent)) dst.SetTangents(Pick(src.tangents, keep));
            if (src.HasVertexAttribute(VertexAttribute.Color)) {
                if (src.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8) {
                    dst.SetColors(Pick(src.colors32, keep));
                } else {
                    dst.SetColors(Pick(src.colors, keep));
                }
            }
            for (var channel = 0; channel < 8; channel++) CopyUvs(src, dst, channel, keep);

            CopyBoneWeights(src, dst, keep);
            dst.bindposes = src.bindposes;

            dst.subMeshCount = subCount;
            for (var sub = 0; sub < subCount; sub++) {
                var remapped = new int[indices[sub].Length];
                for (var i = 0; i < remapped.Length; i++) remapped[i] = map[indices[sub][i]];
                dst.SetIndices(remapped, topologies[sub], sub, false);
            }

            var dv = new Vector3[n];
            var dn = new Vector3[n];
            var dt = new Vector3[n];
            for (var shape = 0; shape < src.blendShapeCount; shape++) {
                var name = src.GetBlendShapeName(shape);
                for (var frame = 0; frame < src.GetBlendShapeFrameCount(shape); frame++) {
                    src.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                    dst.AddBlendShapeFrame(name, src.GetBlendShapeFrameWeight(shape, frame),
                        Pick(dv, keep), Pick(dn, keep), Pick(dt, keep));
                }
            }

            dst.bounds = src.bounds;
            return dst;
        }

        private static void CopyUvs(Mesh src, Mesh dst, int channel, int[] keep) {
            var attribute = VertexAttribute.TexCoord0 + channel;
            if (!src.HasVertexAttribute(attribute)) return;
            switch (src.GetVertexAttributeDimension(attribute)) {
                case 2: {
                    var uvs = new List<Vector2>();
                    src.GetUVs(channel, uvs);
                    dst.SetUVs(channel, Pick(uvs, keep));
                    break;
                }
                case 3: {
                    var uvs = new List<Vector3>();
                    src.GetUVs(channel, uvs);
                    dst.SetUVs(channel, Pick(uvs, keep));
                    break;
                }
                default: {
                    var uvs = new List<Vector4>();
                    src.GetUVs(channel, uvs);
                    dst.SetUVs(channel, Pick(uvs, keep));
                    break;
                }
            }
        }

        private static void CopyBoneWeights(Mesh src, Mesh dst, int[] keep) {
            var perVertex = src.GetBonesPerVertex();
            if (perVertex.Length == 0) return;
            var weights = src.GetAllBoneWeights();
            var offsets = new int[perVertex.Length];
            for (int v = 0, offset = 0; v < perVertex.Length; v++) {
                offsets[v] = offset;
                offset += perVertex[v];
            }
            var total = 0;
            foreach (var v in keep) total += perVertex[v];

            using (var newPerVertex = new NativeArray<byte>(keep.Length, Allocator.Temp))
            using (var newWeights = new NativeArray<BoneWeight1>(total, Allocator.Temp)) {
                var dstPerVertex = newPerVertex;
                var dstWeights = newWeights;
                var w = 0;
                for (var i = 0; i < keep.Length; i++) {
                    var v = keep[i];
                    var bones = perVertex[v];
                    dstPerVertex[i] = bones;
                    for (var b = 0; b < bones; b++) dstWeights[w++] = weights[offsets[v] + b];
                }
                dst.SetBoneWeights(dstPerVertex, dstWeights);
            }
        }

        private static T[] Pick<T>(T[] source, int[] keep) {
            var result = new T[keep.Length];
            for (var i = 0; i < keep.Length; i++) result[i] = source[keep[i]];
            return result;
        }

        private static List<T> Pick<T>(List<T> source, int[] keep) {
            var result = new List<T>(keep.Length);
            foreach (var v in keep) result.Add(source[v]);
            return result;
        }
    }
}
