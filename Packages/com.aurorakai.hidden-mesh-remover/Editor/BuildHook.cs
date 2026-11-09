using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Runs the remover on every avatar build: uploads, Build &amp; Test, and Play
    /// mode (VRCFury runs the SDK's preprocess callbacks when entering Play mode).
    /// No NDMF needed.
    ///
    /// Order -11050 puts it before NDMF (-11000) and VRCFury (-10000), because
    /// VRCFury changes what it would see: "Apply During Upload" sets blendshapes to
    /// 100 even when a toggle shows them, and its Blendshape Optimizer bakes away
    /// blendshapes nothing animates. Optimisers that merge meshes, such as
    /// d4rkAvatarOptimizer (-1025), run later and get the smaller meshes.
    ///
    /// An error fails the build rather than upload a half-processed avatar.
    /// </summary>
    internal sealed class BuildHook : IVRCSDKPreprocessAvatarCallback, IVRCSDKPostprocessAvatarCallback {
        private const string LogPrefix = "[Hidden Mesh Remover]";

        public int callbackOrder => -11050;

        public bool OnPreprocessAvatar(GameObject avatar) {
            GeneratedAssets.BeginBuild();
            try {
                var outcome = RemoveProcessor.Run(avatar, GeneratedAssets.Persist);
                foreach (var result in outcome.renderers) {
                    Debug.Log($"{LogPrefix} {result.renderer.name}: removed {result.removedTriangles:N0} triangles and {result.removedVertices:N0} vertices");
                }
                foreach (var toggle in outcome.removedToggles) {
                    Debug.Log($"{LogPrefix} removed toggle '{toggle}': it only drives blendshapes whose geometry was removed");
                }
                return true;
            } catch (Exception e) {
                Debug.LogError($"{LogPrefix} failed on {avatar.name}; the build was stopped.");
                Debug.LogException(e);
                return false;
            } finally {
                GeneratedAssets.EndAvatar();
            }
        }

        public void OnPostprocessAvatar() {
            GeneratedAssets.EndBuild();
        }
    }
}
