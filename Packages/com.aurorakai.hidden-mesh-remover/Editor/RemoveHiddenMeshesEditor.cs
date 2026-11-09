using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Inspector for RemoveHiddenMeshes. Previews, per mesh, which blendshapes
    /// match, which are kept because something animates them, and how many
    /// triangles and vertices the build will remove. The preview is cached and only
    /// recomputed when the settings, a mesh or a matching weight changes, so the
    /// inspector stays responsive on large meshes.
    /// </summary>
    [CustomEditor(typeof(RemoveHiddenMeshes))]
    internal sealed class RemoveHiddenMeshesEditor : UnityEditor.Editor {
        private sealed class RendererPreview {
            public SkinnedMeshRenderer renderer;
            public HiddenTriangles.Analysis analysis;
        }

        private string cachedKey;
        private List<RendererPreview> previews = new List<RendererPreview>();
        private List<string> deadToggles = new List<string>();
        private HashSet<string> animated = new HashSet<string>();
        private bool showUnmatched;

        public override void OnInspectorGUI() {
            serializedObject.Update();
            var settings = (RemoveHiddenMeshes)target;

            EditorGUILayout.HelpBox(
                "On upload (and in Play mode), triangles that matching blendshapes collapse or tuck inside the avatar are deleted from the build copy of each mesh. " +
                "Geometry a blendshape only reshapes, and stays visible, is kept. Your scene and mesh assets are not changed.",
                MessageType.Info);

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.patterns)), true);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset to defaults", EditorStyles.miniButton)) {
                    Undo.RecordObject(settings, "Reset patterns");
                    settings.patterns = (string[])RemoveHiddenMeshes.DefaultPatterns.Clone();
                    serializedObject.Update();
                }
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.excludePatterns)), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.minWeight)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.removeAnimated)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.movementThreshold)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.removeUnusedVertices)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(RemoveHiddenMeshes.excludeRenderers)), true);
            serializedObject.ApplyModifiedProperties();

            if (settings.removeAnimated) {
                EditorGUILayout.HelpBox("Remove Animated is on: toggles can no longer bring back what is removed.", MessageType.Warning);
            }

            Refresh(settings);
            DrawPreview(settings);
        }

        private void Refresh(RemoveHiddenMeshes settings) {
            var root = AvatarRoot(settings);
            var renderers = Renderers(root, settings);
            var key = Key(settings, renderers);
            if (key == cachedKey) return;
            cachedKey = key;

            animated = AnimatedBlendshapes.Collect(root);
            previews = new List<RendererPreview>();
            var removedAnimated = new HashSet<string>();
            foreach (var item in RemoveProcessor.AnalyseAll(root, animated)) {
                if (item.settings != settings) continue;
                previews.Add(new RendererPreview { renderer = item.renderer, analysis = item.analysis });
                if (settings.removeAnimated) removedAnimated.UnionWith(item.analysis.removedNames.Where(animated.Contains));
            }
            deadToggles = DeadToggles.Find(root, removedAnimated).Select(t => t.menuName).ToList();
        }

        private void DrawPreview(RemoveHiddenMeshes settings) {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60))) cachedKey = null;
            }

            int triangles = 0, removedTriangles = 0, vertices = 0, removedVertices = 0;
            foreach (var preview in previews) {
                var a = preview.analysis;
                triangles += a.triangleCount;
                vertices += a.vertexCount;
                removedTriangles += a.removedTriangles;
                if (a.removedTriangles > 0 && settings.removeUnusedVertices) removedVertices += a.unusedVertices;
                if (a.hits.Count == 0) continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.ObjectField(preview.renderer, typeof(SkinnedMeshRenderer), true);
                        GUILayout.Label(Summary(a, settings), GUILayout.Width(170));
                    }
                    foreach (var hit in a.hits) {
                        var visible = hit.visible > 0 ? $", {hit.visible:N0} still visible (kept)" : "";
                        var status = !hit.removed ? "kept (animated)"
                            : hit.animated ? $"{hit.triangles:N0} tris{visible}, toggle loses it"
                            : $"{hit.triangles:N0} tris{visible}";
                        EditorGUILayout.LabelField($"    {hit.name}", status);
                    }
                }
            }

            if (removedTriangles == 0) {
                EditorGUILayout.HelpBox(
                    "Nothing will be removed. Set a matching blendshape to " + settings.minWeight.ToString("0") +
                    " or more on a mesh under this object, or check the patterns.",
                    MessageType.None);
            } else {
                var text = $"Removed at build: {removedTriangles:N0} of {triangles:N0} triangles ({Percent(removedTriangles, triangles)})";
                if (settings.removeUnusedVertices) text += $", {removedVertices:N0} of {vertices:N0} vertices ({Percent(removedVertices, vertices)})";
                EditorGUILayout.HelpBox(text, MessageType.None);
            }

            if (deadToggles.Count > 0) {
                EditorGUILayout.LabelField("Toggles removed at build", EditorStyles.boldLabel);
                foreach (var toggle in deadToggles) EditorGUILayout.LabelField("    " + toggle);
            }

            showUnmatched = EditorGUILayout.Foldout(showUnmatched, "Blendshapes that look like hide shapes but don't match", true);
            if (showUnmatched) DrawUnmatched(settings);
        }

        /// <summary>
        /// Blendshape names containing hide/off/shrink/remove that the patterns miss,
        /// to help spot a naming convention the defaults don't cover.
        /// </summary>
        private void DrawUnmatched(RemoveHiddenMeshes settings) {
            var hints = new[] { "hide", "off", "shrink", "remove", "delete", "hidden" };
            var any = false;
            foreach (var preview in previews) {
                var mesh = preview.renderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++) {
                    var name = mesh.GetBlendShapeName(i);
                    var lower = name.ToLowerInvariant();
                    if (!hints.Any(lower.Contains) || BlendshapeFilter.Matches(name, settings)) continue;
                    EditorGUILayout.LabelField($"    {preview.renderer.name}", name);
                    any = true;
                }
            }
            if (!any) EditorGUILayout.LabelField("    None");
        }

        private static string Summary(HiddenTriangles.Analysis a, RemoveHiddenMeshes settings) {
            if (a.removedTriangles == 0) return "no change";
            var verts = settings.removeUnusedVertices ? $", -{a.unusedVertices:N0} verts" : "";
            return $"-{a.removedTriangles:N0} tris{verts}";
        }

        private static string Percent(int part, int whole) {
            return whole == 0 ? "0%" : (100f * part / whole).ToString("0.#") + "%";
        }

        private static GameObject AvatarRoot(RemoveHiddenMeshes settings) {
            var descriptor = settings.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.gameObject : settings.transform.root.gameObject;
        }

        private static List<SkinnedMeshRenderer> Renderers(GameObject root, RemoveHiddenMeshes settings) {
            return settings.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(smr => smr.sharedMesh != null
                              && RemoveProcessor.SettingsFor(smr) == settings
                              && (settings.excludeRenderers == null || !settings.excludeRenderers.Contains(smr)))
                .ToList();
        }

        private static string Key(RemoveHiddenMeshes settings, List<SkinnedMeshRenderer> renderers) {
            var key = new StringBuilder(EditorJsonUtility.ToJson(settings));
            foreach (var smr in renderers) {
                var mesh = smr.sharedMesh;
                key.Append('|').Append(smr.GetInstanceID()).Append(':').Append(mesh.GetInstanceID());
                for (var i = 0; i < mesh.blendShapeCount; i++) {
                    if (BlendshapeFilter.Matches(mesh.GetBlendShapeName(i), settings)) {
                        key.Append(',').Append(smr.GetBlendShapeWeight(i).ToString("0.##"));
                    }
                }
            }
            return key.ToString();
        }
    }
}
