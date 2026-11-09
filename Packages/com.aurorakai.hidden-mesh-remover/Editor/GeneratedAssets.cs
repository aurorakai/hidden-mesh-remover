using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Saves the meshes made during an upload, because the VRChat SDK only keeps
    /// what is stored as an asset. Each avatar gets one .asset file under
    /// <see cref="Folder"/>, deleted again once the SDK reports the build done. In
    /// Play mode nothing is saved: the meshes only need to live for the session.
    /// Files from a build that never finished (a VRCFury test copy, a failed
    /// upload) stay until the next "Delete Generated Meshes".
    /// </summary>
    internal static class GeneratedAssets {
        internal const string Folder = "Assets/_AuroraKai Generated/Hidden Mesh Remover";

        private static readonly List<string> ThisBuild = new List<string>();
        private static string current;

        internal static void BeginBuild() {
            ThisBuild.Clear();
            current = null;
        }

        internal static void Persist(Mesh mesh) {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (current == null) {
                EnsureFolder(Folder);
                current = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{DateTime.Now:yyyyMMdd-HHmmss}.asset");
                AssetDatabase.CreateAsset(mesh, current);
                ThisBuild.Add(current);
            } else {
                AssetDatabase.AddObjectToAsset(mesh, current);
            }
        }

        internal static void EndAvatar() {
            if (current == null) return;
            AssetDatabase.SaveAssets();
            current = null;
        }

        internal static void EndBuild() {
            foreach (var path in ThisBuild) AssetDatabase.DeleteAsset(path);
            ThisBuild.Clear();
            DeleteFolderIfEmpty();
        }

        [MenuItem("Tools/Kai/Hidden Mesh Remover/Delete Generated Meshes")]
        private static void DeleteAll() {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            DeleteFolderIfEmpty();
        }

        private static void DeleteFolderIfEmpty() {
            foreach (var folder in new[] { Folder, Parent(Folder) }) {
                if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets("", new[] { folder }).Length == 0) {
                    AssetDatabase.DeleteAsset(folder);
                }
            }
        }

        private static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Parent(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }

        private static string Parent(string path) {
            return path.Substring(0, path.LastIndexOf('/'));
        }
    }
}
