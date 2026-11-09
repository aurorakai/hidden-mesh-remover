using UnityEngine;
using VRC.SDKBase;

namespace AuroraKai.HiddenMeshRemover {
    /// <summary>
    /// Build-time marker. For every SkinnedMeshRenderer under this GameObject, the
    /// upload deletes the triangles that a matching "hide" blendshape keeps hidden,
    /// then drops the vertices nothing uses any more. Only the build copy changes;
    /// the mesh asset and the scene are left alone.
    ///
    /// A blendshape matches when its name fits one of <see cref="patterns"/>, fits
    /// none of <see cref="excludePatterns"/>, and its weight in the editor is at
    /// least <see cref="minWeight"/>. Blendshapes that a toggle or animation drives
    /// are kept unless <see cref="removeAnimated"/> is on. When components are
    /// nested, a renderer uses the settings of the nearest one above it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kai/Hidden Mesh Remover")]
    [HelpURL("https://github.com/aurorakai/hidden-mesh-remover")]
    public sealed class RemoveHiddenMeshes : MonoBehaviour, IEditorOnly {
        /// <summary>Common names for blendshapes that hide part of a mesh.</summary>
        public static readonly string[] DefaultPatterns = { "Hide*", "*-off", "*_off", "* off" };

        [Tooltip("Blendshape names to treat as \"hide\" shapes. Case-insensitive. * matches any run of characters, ? matches one.")]
        public string[] patterns = (string[])DefaultPatterns.Clone();

        [Tooltip("Blendshape names that are never removed, even if they match a pattern above.")]
        public string[] excludePatterns = new string[0];

        [Range(0f, 100f)]
        [Tooltip("A matching blendshape is removed only if its weight in the editor is at least this.")]
        public float minWeight = 100f;

        [Tooltip("Also remove geometry hidden by blendshapes that a toggle or animation drives. A toggle can then no longer show the removed part, and VRCFury toggles that drive nothing but removed blendshapes are removed from the menu.")]
        public bool removeAnimated;

        [Range(0.01f, 1f)]
        [Tooltip("How far a vertex must move, as a fraction of the blendshape's largest movement, to count as hidden. Hide shapes often also nudge nearby vertices slightly; this keeps those. Raise it if visible geometry goes missing, lower it if hidden parts are left behind.")]
        public float movementThreshold = 0.1f;

        [Tooltip("After removing triangles, also remove the vertices no triangle uses any more. Cuts skinning and blendshape cost, not just triangle count.")]
        public bool removeUnusedVertices = true;

        [Tooltip("Renderers under this object to leave untouched.")]
        public SkinnedMeshRenderer[] excludeRenderers = new SkinnedMeshRenderer[0];
    }
}
