using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Finds VRCFury Toggle components that do nothing useful once their
    /// blendshapes are removed, so they can be removed before VRCFury builds their
    /// menu item, parameter and layer. Every state action must be an animation clip
    /// or a blendshape action, and every effect must be one of these:
    /// - a removed blendshape;
    /// - turning on an object that is already on (a no-op);
    /// - turning on an object with no renderers (supporting PhysBones, contacts).
    /// Anything else, including object-reference curves, keeps the toggle.
    /// </summary>
    internal static class DeadToggles {
        private const string BlendShapePrefix = "blendShape.";
        private static readonly Regex ActionClassRegex = new Regex("\"class\":\"(\\w+Action)\"");
        private static readonly Regex GuidRegex = new Regex("\"guid\":\"([0-9a-f]{32})\"");
        private static readonly Regex BlendShapeActionRegex = new Regex("\"blendShape\":\"([^\"]+)\"");
        private static readonly Regex NameRegex = new Regex("\"class\":\"Toggle\".*?\"name\":\"([^\"]*)\"");

        internal struct Found {
            public MonoBehaviour component;
            public string menuName;
        }

        internal static List<Found> Find(GameObject avatarRoot, HashSet<string> removed) {
            var result = new List<Found>();
            if (removed.Count == 0) return result;
            foreach (var behaviour in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (!AnimatedBlendshapes.IsVrcFury(behaviour)) continue;
                var json = EditorJsonUtility.ToJson(behaviour);
                if (!json.Contains("\"class\":\"Toggle\"")) continue;
                if (OnlyDrives(json, removed, behaviour.transform, avatarRoot.transform)) {
                    var name = NameRegex.Match(json);
                    result.Add(new Found { component = behaviour, menuName = name.Success ? name.Groups[1].Value : behaviour.name });
                }
            }
            return result;
        }

        private static bool OnlyDrives(string json, HashSet<string> removed, Transform owner, Transform avatarRoot) {
            var effects = 0;
            foreach (Match match in ActionClassRegex.Matches(json)) {
                var type = match.Groups[1].Value;
                if (type != "AnimationClipAction" && type != "BlendShapeAction") return false;
            }
            foreach (Match match in BlendShapeActionRegex.Matches(json)) {
                if (!removed.Contains(match.Groups[1].Value)) return false;
                effects++;
            }
            foreach (Match match in GuidRegex.Matches(json)) {
                var path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                if (string.IsNullOrEmpty(path)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset is RuntimeAnimatorController) return false;
                if (!(asset is AnimationClip clip)) continue;
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0) return false;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)) {
                    if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(BlendShapePrefix)) {
                        if (!removed.Contains(binding.propertyName.Substring(BlendShapePrefix.Length))) return false;
                        effects++;
                    } else if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") {
                        if (!HarmlessActivation(clip, binding, owner, avatarRoot)) return false;
                    } else {
                        return false;
                    }
                }
            }
            return effects > 0;
        }

        private static bool HarmlessActivation(AnimationClip clip, EditorCurveBinding binding, Transform owner, Transform avatarRoot) {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Any(k => k.value < 0.5f)) return false;
            var target = Resolve(binding.path, owner) ?? Resolve(binding.path, avatarRoot);
            if (target == null) return false;
            if (target.gameObject.activeSelf) return true;
            return target.GetComponentsInChildren<Renderer>(true).Length == 0;
        }

        private static Transform Resolve(string path, Transform from) {
            return string.IsNullOrEmpty(path) ? from : from.Find(path);
        }
    }
}
