using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Processes one avatar (already a build copy): each SkinnedMeshRenderer under a
    /// RemoveHiddenMeshes component gets a copy of its mesh without the triangles
    /// its hide blendshapes keep out of sight, and without unused vertices if that
    /// option is on. With Remove Animated on, VRCFury toggles that only drive
    /// removed blendshapes are deleted. Finally the components themselves are
    /// removed.
    /// </summary>
    internal static class RemoveProcessor {
        internal struct Result {
            public SkinnedMeshRenderer renderer;
            public int removedTriangles;
            public int removedVertices;
            public int keptVisible;
        }

        internal sealed class Outcome {
            public readonly List<Result> renderers = new List<Result>();
            public readonly List<string> removedToggles = new List<string>();
        }

        internal sealed class RendererAnalysis {
            public SkinnedMeshRenderer renderer;
            public RemoveHiddenMeshes settings;
            public float[] weights;
            public HiddenTriangles.Analysis analysis;
        }

        /// <param name="persist">Called with each new mesh; uploads need them saved as assets.</param>
        internal static Outcome Run(GameObject avatarRoot, Action<Mesh> persist) {
            var outcome = new Outcome();
            var components = avatarRoot.GetComponentsInChildren<RemoveHiddenMeshes>(true);
            if (components.Length == 0) return outcome;

            var animated = AnimatedBlendshapes.Collect(avatarRoot);
            var removedAnimated = new HashSet<string>();
            foreach (var item in AnalyseAll(avatarRoot, animated)) {
                var smr = item.renderer;
                var settings = item.settings;
                if (settings.removeAnimated) removedAnimated.UnionWith(item.analysis.removedNames.Where(animated.Contains));

                var source = smr.sharedMesh;
                var mesh = HiddenTriangles.BuildWithoutHidden(source, item.analysis);
                if (mesh == null) continue;
                var removedVertices = 0;
                if (settings.removeUnusedVertices) {
                    var compact = MeshCompactor.Compact(mesh, out removedVertices);
                    if (compact != null) {
                        Object.DestroyImmediate(mesh);
                        mesh = compact;
                    }
                }
                mesh.name = source.name + " (Hidden Removed)";
                persist?.Invoke(mesh);

                smr.sharedMesh = mesh;
                for (var i = 0; i < item.weights.Length; i++) smr.SetBlendShapeWeight(i, item.weights[i]);
                outcome.renderers.Add(new Result {
                    renderer = smr,
                    removedTriangles = item.analysis.removedTriangles,
                    removedVertices = removedVertices,
                    keptVisible = item.analysis.keptVisible,
                });
            }

            foreach (var dead in DeadToggles.Find(avatarRoot, removedAnimated)) {
                outcome.removedToggles.Add(dead.menuName);
                Object.DestroyImmediate(dead.component);
            }
            foreach (var component in components) Object.DestroyImmediate(component);
            return outcome;
        }

        /// <summary>
        /// Analyses every renderer a RemoveHiddenMeshes component covers. First finds
        /// the triangles each one's hide shapes move, then builds the occlusion test
        /// from everything else that is always shown, then decides what is out of
        /// sight. Used by the build and the inspector preview alike.
        /// </summary>
        internal static List<RendererAnalysis> AnalyseAll(GameObject avatarRoot, HashSet<string> animated) {
            var items = new List<RendererAnalysis>();
            var candidates = new Dictionary<SkinnedMeshRenderer, bool[]>();
            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var settings = SettingsFor(smr);
                if (settings == null || smr.sharedMesh == null) continue;
                if (settings.excludeRenderers != null && settings.excludeRenderers.Contains(smr)) continue;
                var weights = HiddenTriangles.Weights(smr);
                var mask = HiddenTriangles.CandidateVertices(smr.sharedMesh, weights, settings, animated);
                if (mask != null) candidates[smr] = mask;
                items.Add(new RendererAnalysis { renderer = smr, settings = settings, weights = weights });
            }

            var occlusion = candidates.Count > 0
                ? Occlusion.Build(avatarRoot, smr => candidates.TryGetValue(smr, out var m) ? m : null)
                : null;

            foreach (var item in items) {
                HiddenTriangles.EnclosedTest enclosed = null;
                if (occlusion != null) {
                    Vector3[] world = null;
                    enclosed = (a, b, c) => {
                        world = world ?? Occlusion.WorldVertices(item.renderer);
                        return occlusion.Enclosed(world[a], world[b], world[c]);
                    };
                }
                item.analysis = HiddenTriangles.Analyse(item.renderer.sharedMesh, item.weights, item.settings, animated,
                    enclosed ?? ((a, b, c) => false));
            }
            return items;
        }

        /// <summary>The nearest RemoveHiddenMeshes at or above the renderer.</summary>
        internal static RemoveHiddenMeshes SettingsFor(Component renderer) {
            return renderer.GetComponentInParent<RemoveHiddenMeshes>(true);
        }
    }
}
