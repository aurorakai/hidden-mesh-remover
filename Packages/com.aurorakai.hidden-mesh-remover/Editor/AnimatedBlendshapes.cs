using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Finds what the avatar's animations can change. The remover runs before
    /// VRCFury, so besides the descriptor's controllers and any Animators, the
    /// VRCFury components are scanned for the clips, controllers, blendshape
    /// actions and object toggles they reference. VRCFury's "Apply During Upload"
    /// isn't an animation, so it is skipped. VRCFury is read through its serialised
    /// JSON, so there is no compile-time dependency on it.
    /// </summary>
    internal static class AnimatedBlendshapes {
        private const string BlendShapePrefix = "blendShape.";
        private const string VrcFuryType = "VF.Model.VRCFury";
        private static readonly Regex GuidRegex = new Regex("\"guid\":\"([0-9a-f]{32})\"");
        private static readonly Regex BlendShapeActionRegex = new Regex("\"blendShape\":\"([^\"]+)\"");
        private static readonly Regex InstanceIdRegex = new Regex("\"instanceID\":(-?\\d+)");

        /// <summary>
        /// Every blendshape name an animation can drive. A hide blendshape that
        /// something animates isn't hidden for good, so by default it is kept.
        /// </summary>
        internal static HashSet<string> Collect(GameObject avatarRoot) {
            var names = new HashSet<string>();
            foreach (var json in VrcFuryJson(avatarRoot)) {
                foreach (Match match in BlendShapeActionRegex.Matches(json)) names.Add(match.Groups[1].Value);
            }
            foreach (var clip in Clips(avatarRoot)) {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)) {
                    if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(BlendShapePrefix)) {
                        names.Add(binding.propertyName.Substring(BlendShapePrefix.Length));
                    }
                }
            }
            return names;
        }

        /// <summary>
        /// Objects that an animation can turn on or off (or whose renderer it can
        /// enable or disable), so they can't be relied on to cover anything. Clip
        /// paths are matched by object name anywhere on the avatar, which errs on
        /// the side of treating more objects as toggled.
        /// </summary>
        internal static HashSet<Transform> ToggledObjects(GameObject avatarRoot) {
            var names = new HashSet<string>();
            foreach (var clip in Clips(avatarRoot)) {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)) {
                    var toggles = (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                                  || (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled");
                    if (!toggles || string.IsNullOrEmpty(binding.path)) continue;
                    names.Add(binding.path.Substring(binding.path.LastIndexOf('/') + 1));
                }
            }

            var result = new HashSet<Transform>();
            foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true)) {
                if (t != avatarRoot.transform && names.Contains(t.name)) result.Add(t);
            }
            foreach (var json in VrcFuryJson(avatarRoot)) {
                if (!json.Contains("\"class\":\"Toggle\"")) continue;
                foreach (Match match in InstanceIdRegex.Matches(json)) {
                    var obj = EditorUtility.InstanceIDToObject(int.Parse(match.Groups[1].Value));
                    if (obj is GameObject go) result.Add(go.transform);
                    else if (obj is Renderer renderer) result.Add(renderer.transform);
                }
            }
            return result;
        }

        internal static bool IsVrcFury(MonoBehaviour behaviour) {
            return behaviour != null && behaviour.GetType().FullName == VrcFuryType;
        }

        private static IEnumerable<string> VrcFuryJson(GameObject avatarRoot) {
            return avatarRoot.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(IsVrcFury)
                .Select(b => EditorJsonUtility.ToJson(b))
                .Where(json => !json.Contains("\"class\":\"ApplyDuringUpload\""));
        }

        private static HashSet<AnimationClip> Clips(GameObject avatarRoot) {
            var clips = new HashSet<AnimationClip>();
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null) {
                foreach (var layer in descriptor.baseAnimationLayers) AddController(layer.animatorController, clips);
                foreach (var layer in descriptor.specialAnimationLayers) AddController(layer.animatorController, clips);
            }
            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true)) {
                AddController(animator.runtimeAnimatorController, clips);
            }
            foreach (var json in VrcFuryJson(avatarRoot)) {
                foreach (Match match in GuidRegex.Matches(json)) {
                    var path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                    if (string.IsNullOrEmpty(path)) continue;
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset is AnimationClip clip) clips.Add(clip);
                    else if (asset is RuntimeAnimatorController controller) AddController(controller, clips);
                }
            }
            return clips;
        }

        private static void AddController(RuntimeAnimatorController controller, HashSet<AnimationClip> clips) {
            if (controller == null) return;
            foreach (var clip in controller.animationClips) {
                if (clip != null) clips.Add(clip);
            }
        }
    }
}
