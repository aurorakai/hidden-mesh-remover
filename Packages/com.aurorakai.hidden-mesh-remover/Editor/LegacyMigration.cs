using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Replaces components from the earlier project-local tool
    /// (Kai.StripHidden.StripHiddenBlendshapes) in the open scenes with
    /// RemoveHiddenMeshes, carrying over prefix, min weight and the animated
    /// option. Found by type name, so it works whether or not the old scripts are
    /// still in the project. Undoable.
    /// </summary>
    internal static class LegacyMigration {
        private const string LegacyType = "Kai.StripHidden.StripHiddenBlendshapes";

        [MenuItem("Tools/Kai/Hidden Mesh Remover/Convert Old Strip Hidden Components")]
        private static void Convert() {
            var converted = 0;
            for (var s = 0; s < SceneManager.sceneCount; s++) {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) {
                    foreach (var old in root.GetComponentsInChildren<MonoBehaviour>(true)) {
                        if (old == null || old.GetType().FullName != LegacyType) continue;
                        var go = old.gameObject;
                        var json = new SerializedObject(old);
                        var prefix = json.FindProperty("prefix")?.stringValue ?? "Hide";
                        var minWeight = json.FindProperty("minWeight")?.floatValue ?? 100f;
                        var animated = json.FindProperty("stripAnimated")?.boolValue ?? false;

                        var replacement = go.GetComponent<RemoveHiddenMeshes>();
                        if (replacement == null) replacement = Undo.AddComponent<RemoveHiddenMeshes>(go);
                        Undo.RecordObject(replacement, "Convert Strip Hidden");
                        if (!string.IsNullOrEmpty(prefix) && !BlendshapeFilter.MatchesAny(prefix, replacement.patterns)) {
                            replacement.patterns = replacement.patterns.Concat(new[] { prefix + "*" }).ToArray();
                        }
                        replacement.minWeight = minWeight;
                        replacement.removeAnimated = animated;
                        Undo.DestroyObjectImmediate(old);
                        converted++;
                    }
                }
            }
            EditorUtility.DisplayDialog("Hidden Mesh Remover",
                converted == 0 ? "No old Strip Hidden Blendshapes components were found in the open scenes."
                               : $"Converted {converted} component(s). Remove the old StripHidden scripts from Assets so they don't also run.",
                "OK");
        }
    }
}
