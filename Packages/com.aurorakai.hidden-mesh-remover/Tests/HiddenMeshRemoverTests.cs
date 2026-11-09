using System.Collections.Generic;
using AuroraKai.HiddenMeshRemover.Editor;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace AuroraKai.HiddenMeshRemover.Tests {
    /// <summary>
    /// Everything here is built in memory and destroyed afterwards, so the tests
    /// leave nothing behind in the project they run in.
    /// </summary>
    public class HiddenMeshRemoverTests {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown() {
            foreach (var obj in created) {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            created.Clear();
        }

        // ---- Pattern matching ----

        [TestCase("Hide Glasses", "Hide*", true)]
        [TestCase("hide_pants", "Hide*", true)]
        [TestCase("Tuft-belly-off", "*-off", true)]
        [TestCase("Scarf off", "* off", true)]
        [TestCase("Takeoff", "*-off", false)]
        [TestCase("Shoes_OFF", "*_off", true)]
        [TestCase("Hair-off", "H?ir-*", true)]
        [TestCase("Flat-chest", "Hide*", false)]
        [TestCase("", "*", true)]
        [TestCase("abc", "", false)]
        public void Glob_MatchesCaseInsensitively(string name, string pattern, bool expected) {
            Assert.AreEqual(expected, BlendshapeFilter.Glob(name, pattern));
        }

        [Test]
        public void Filter_ExcludeWins() {
            var settings = Settings();
            settings.excludePatterns = new[] { "Hide Glasses" };
            Assert.IsFalse(BlendshapeFilter.Matches("Hide Glasses", settings));
            Assert.IsTrue(BlendshapeFilter.Matches("Hide Pants", settings));
        }

        [Test]
        public void Filter_DefaultsCoverCommonConventions() {
            var settings = Settings();
            foreach (var name in new[] { "Hide Bag", "Hair-off", "Socks_off", "Scarf off" }) {
                Assert.IsTrue(BlendshapeFilter.Matches(name, settings), name);
            }
            foreach (var name in new[] { "Flat-chest", "JawOpen", "Leg-fluff-tufts-shrink", "Slender Thighs" }) {
                Assert.IsFalse(BlendshapeFilter.Matches(name, settings), name);
            }
        }

        // ---- Build processing ----

        [Test]
        public void Run_RemovesHiddenTrianglesAndUnusedVertices() {
            var (root, smr) = Avatar(100f);
            var outcome = RemoveProcessor.Run(root, null);

            var mesh = smr.sharedMesh;
            created.Add(mesh);
            Assert.AreEqual(1, outcome.renderers.Count);
            Assert.AreEqual(1, outcome.renderers[0].removedTriangles);
            Assert.AreEqual(3, outcome.renderers[0].removedVertices);
            Assert.AreEqual(3, mesh.GetTriangles(0).Length);
            Assert.AreEqual(3, mesh.vertexCount);
            Assert.AreEqual(2, mesh.subMeshCount, "sub-meshes must stay so material slots line up");
            Assert.AreEqual(0, mesh.GetTriangles(1).Length);
            Assert.IsNull(root.GetComponent<RemoveHiddenMeshes>(), "the marker component is removed at build");
        }

        [Test]
        public void Run_KeepsBlendshapesWeightsUvsAndBoneWeights() {
            var (root, smr) = Avatar(100f);
            var source = smr.sharedMesh;
            RemoveProcessor.Run(root, null);
            var mesh = smr.sharedMesh;
            created.Add(mesh);

            Assert.AreEqual(source.blendShapeCount, mesh.blendShapeCount);
            Assert.AreEqual("Hide Part", mesh.GetBlendShapeName(0));
            Assert.AreEqual("Smile", mesh.GetBlendShapeName(1));
            Assert.AreEqual(100f, smr.GetBlendShapeWeight(0));
            Assert.AreEqual(25f, smr.GetBlendShapeWeight(1));

            // The kept triangle was vertices 3..5; "Smile" moves vertex 4 up.
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(1, 0, deltas, null, null);
            Assert.AreEqual(Vector3.up, deltas[1]);
            Assert.AreEqual(Vector3.zero, deltas[0]);

            Assert.AreEqual(3, mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1), "UV dimension is preserved");
            var uv = new List<Vector3>();
            mesh.GetUVs(1, uv);
            Assert.AreEqual(new Vector3(4, 0, 0), uv[1]);

            var bones = mesh.boneWeights;
            Assert.AreEqual(1, bones[2].boneIndex0, "vertex 5 was weighted to bone 1");
            Assert.AreEqual(source.bindposes.Length, mesh.bindposes.Length);
            Assert.AreEqual(source.bounds, mesh.bounds);
        }

        [Test]
        public void Run_KeepsVerticesAHideShapeOnlyNudges() {
            // A 1 mm correction next to a 50 cm collapse is not hiding anything.
            var (root, smr) = Avatar(100f, noise: 0.001f);
            var outcome = RemoveProcessor.Run(root, null);
            created.Add(smr.sharedMesh);
            Assert.AreEqual(1, outcome.renderers[0].removedTriangles);
            Assert.AreEqual(3, smr.sharedMesh.vertexCount);
        }

        [Test]
        public void Run_KeepsGeometryAHideShapeOnlyReshapes() {
            // Moved but neither collapsed nor covered: still visible, so kept.
            var (root, smr) = Avatar(100f, collapse: false);
            var source = smr.sharedMesh;
            var outcome = RemoveProcessor.Run(root, null);
            Assert.AreEqual(0, outcome.renderers.Count);
            Assert.AreSame(source, smr.sharedMesh);
        }

        [Test]
        public void Run_RemovesGeometryTuckedInsideAnotherMesh() {
            var (root, smr) = Avatar(100f, collapse: false);
            Cover(root);
            var outcome = RemoveProcessor.Run(root, null);
            created.Add(smr.sharedMesh);
            Assert.AreEqual(1, outcome.renderers[0].removedTriangles);
        }

        [Test]
        public void Run_DoesNotCountToggledObjectsAsCover() {
            var (root, smr) = Avatar(100f, collapse: false);
            var cover = Cover(root);
            var clip = new AnimationClip();
            created.Add(clip);
            clip.SetCurve(cover.name, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            var controller = new AnimatorController();
            created.Add(controller);
            controller.AddLayer("Base");
            controller.AddMotion(clip);
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            var source = smr.sharedMesh;
            RemoveProcessor.Run(root, null);
            Assert.AreSame(source, smr.sharedMesh, "a toggleable object can be turned off, so it covers nothing");
        }

        /// <summary>A cube around where the uncollapsed hide shape moves triangle 0-2.</summary>
        private GameObject Cover(GameObject root) {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cover";
            cube.transform.SetParent(root.transform);
            cube.transform.position = new Vector3(1f / 3f, 1f / 3f, -0.5f);
            cube.transform.localScale = Vector3.one * 3f;
            return cube;
        }

        [Test]
        public void Run_LeavesMeshAloneBelowMinWeight() {
            var (root, smr) = Avatar(99f);
            var source = smr.sharedMesh;
            var outcome = RemoveProcessor.Run(root, null);
            Assert.AreEqual(0, outcome.renderers.Count);
            Assert.AreSame(source, smr.sharedMesh);
        }

        [Test]
        public void Run_KeepsVerticesWhenCompactionIsOff() {
            var (root, smr) = Avatar(100f);
            root.GetComponent<RemoveHiddenMeshes>().removeUnusedVertices = false;
            RemoveProcessor.Run(root, null);
            created.Add(smr.sharedMesh);
            Assert.AreEqual(6, smr.sharedMesh.vertexCount);
            Assert.AreEqual(3, smr.sharedMesh.GetTriangles(0).Length);
        }

        [Test]
        public void Run_SkipsExcludedRenderers() {
            var (root, smr) = Avatar(100f);
            var source = smr.sharedMesh;
            root.GetComponent<RemoveHiddenMeshes>().excludeRenderers = new[] { smr };
            RemoveProcessor.Run(root, null);
            Assert.AreSame(source, smr.sharedMesh);
        }

        [Test]
        public void Run_KeepsAnimatedBlendshapesUnlessRemoveAnimated() {
            var (root, smr) = Avatar(100f);
            var clip = new AnimationClip();
            created.Add(clip);
            clip.SetCurve("Mesh", typeof(SkinnedMeshRenderer), "blendShape.Hide Part", AnimationCurve.Constant(0, 1, 0));
            var controller = new AnimatorController();
            created.Add(controller);
            controller.AddLayer("Base");
            controller.AddMotion(clip);
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            var source = smr.sharedMesh;

            RemoveProcessor.Run(root, null);
            Assert.AreSame(source, smr.sharedMesh, "an animated hide shape must be kept");

            var (root2, smr2) = Avatar(100f);
            root2.GetComponent<RemoveHiddenMeshes>().removeAnimated = true;
            root2.AddComponent<Animator>().runtimeAnimatorController = controller;
            RemoveProcessor.Run(root2, null);
            created.Add(smr2.sharedMesh);
            Assert.AreEqual(3, smr2.sharedMesh.vertexCount);
        }

        [Test]
        public void Run_NearestComponentWins() {
            var (root, smr) = Avatar(100f);
            var inner = smr.gameObject.AddComponent<RemoveHiddenMeshes>();
            inner.patterns = new[] { "Nothing*" };
            var source = smr.sharedMesh;
            RemoveProcessor.Run(root, null);
            Assert.AreSame(source, smr.sharedMesh);
            Assert.IsNull(smr.GetComponent<RemoveHiddenMeshes>());
        }

        [Test]
        public void Compact_ReturnsNullWhenEveryVertexIsUsed() {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            created.Add(mesh);
            Assert.IsNull(MeshCompactor.Compact(mesh, out var removed));
            Assert.AreEqual(0, removed);
        }

        [Test]
        public void Compact_SwitchesTo16BitIndicesWhenSmallEnough() {
            var (_, smr) = Avatar(100f);
            var mesh = Object.Instantiate(smr.sharedMesh);
            created.Add(mesh);
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetTriangles(new[] { 3, 4, 5 }, 0);
            var compact = MeshCompactor.Compact(mesh, out _);
            created.Add(compact);
            Assert.AreEqual(IndexFormat.UInt16, compact.indexFormat);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, compact.GetTriangles(0));
        }

        // ---- Fixtures ----

        private RemoveHiddenMeshes Settings() {
            var go = new GameObject("Settings");
            created.Add(go);
            return go.AddComponent<RemoveHiddenMeshes>();
        }

        /// <summary>
        /// Two separate triangles (vertices 0-2 and 3-5) in sub-mesh 0, an empty
        /// sub-mesh 1, "Hide Part" moving vertices 0-2, "Smile" moving vertex 4,
        /// UV1 in 3D, and two bones.
        /// </summary>
        private (GameObject root, SkinnedMeshRenderer smr) Avatar(float hideWeight, float noise = 0f, bool collapse = true) {
            var root = new GameObject("Avatar");
            created.Add(root);
            root.AddComponent<RemoveHiddenMeshes>();
            var child = new GameObject("Mesh");
            child.transform.SetParent(root.transform);
            var bone0 = new GameObject("Bone0").transform;
            var bone1 = new GameObject("Bone1").transform;
            bone0.SetParent(root.transform);
            bone1.SetParent(root.transform);

            var mesh = new Mesh { name = "Test" };
            created.Add(mesh);
            mesh.vertices = new[] {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(2, 1, 0),
            };
            mesh.SetUVs(1, new List<Vector3> {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0),
                new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(5, 0, 0),
            });
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 3, 4, 5 }, 0);
            mesh.SetTriangles(new int[0], 1);
            mesh.boneWeights = new[] {
                W(0), W(0), W(0), W(0), W(0), W(1),
            };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            var hide = new Vector3[6];
            var centre = new Vector3(1f / 3f, 1f / 3f, 0f);
            for (var i = 0; i < 3; i++) hide[i] = collapse ? centre - mesh.vertices[i] : new Vector3(0, 0, -0.5f);
            for (var i = 3; i < 6; i++) hide[i] = new Vector3(0, 0, noise);
            mesh.AddBlendShapeFrame("Hide Part", 100, hide, null, null);
            var smile = new Vector3[6];
            smile[4] = Vector3.up;
            mesh.AddBlendShapeFrame("Smile", 100, smile, null, null);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { bone0, bone1 };
            smr.SetBlendShapeWeight(0, hideWeight);
            smr.SetBlendShapeWeight(1, 25f);
            return (root, smr);
        }

        private static BoneWeight W(int bone) {
            return new BoneWeight { boneIndex0 = bone, weight0 = 1f };
        }
    }
}
