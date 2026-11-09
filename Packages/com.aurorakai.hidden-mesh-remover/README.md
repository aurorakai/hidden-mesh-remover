# Hidden Mesh Remover

Removes the parts of your avatar's meshes that "hide" blendshapes keep out of sight, when you upload. If a `Hide Pants` or `Scarf-off` blendshape is at 100 and nothing toggles it, the triangles it tucks away still cost you in game. This deletes them, and the vertices left unused, from the uploaded copy of the mesh. Your scene and mesh assets are not changed.

## Use

1. Add **Kai > Hidden Mesh Remover** to your avatar root (or to any object; it covers every Skinned Mesh Renderer below it).
2. Set the hide blendshapes you want gone to 100 in the editor.
3. Check the preview in the inspector, then upload.

The inspector shows, per mesh, which blendshapes match, which are kept because a toggle animates them, and how many triangles and vertices the build will remove.

## Settings

| Setting | What it does |
|---|---|
| **Patterns** | Blendshape names to treat as hide shapes. `*` matches anything, `?` one character, case-insensitive. Defaults: `Hide*`, `*-off`, `*_off`, `* off`. |
| **Exclude Patterns** | Names never removed, even if they match. |
| **Min Weight** | A matching blendshape counts only at this weight or more (default 100). |
| **Remove Animated** | Also remove geometry hidden by blendshapes that a toggle or animation drives. Those toggles can then no longer show it, and VRCFury toggles that only drive such blendshapes are dropped from the menu. Off by default. |
| **Movement Threshold** | How far a vertex must move, as a fraction of the blendshape's largest movement, to count as hidden (default 0.1). Hide shapes are often sculpted with small fixes to the surrounding surface; this keeps those. |
| **Remove Unused Vertices** | After removing triangles, also drop the vertices nothing uses. Saves skinning and blendshape work, not just triangles. On by default. |
| **Exclude Renderers** | Meshes below this object to leave alone. |

If components are nested, each mesh uses the nearest one above it.

## How it works

- A triangle is removed only when a qualifying blendshape moves all three of its vertices (by at least the movement threshold) **and** the result is out of sight: either collapsed to almost nothing, or tucked inside other geometry. "Inside" is checked by casting rays both ways along the triangle's normal, from its centre and near each corner; all of them must hit geometry that is always shown.
- A hide shape that only reshapes a surface, such as smoothing a spiky fluff shell down onto the head, leaves that surface visible, so it is kept. The inspector shows these as "still visible (kept)".
- Clothing or anything else a toggle can turn off never counts as covering, because it can be turned off.
- Blendshapes that something animates are found from the avatar's playable layers, any Animators, and VRCFury components (toggles, full controllers, blendshape actions). VRCFury's *Apply During Upload* isn't an animation, so it doesn't count.
- It runs as a VRChat SDK build callback (order -11050), before NDMF and VRCFury, so it sees your blendshape weights as you set them. Mesh optimisers such as d4rkAvatarOptimizer run later and get the smaller meshes. NDMF is not required.
- It runs in Play mode too, when VRCFury's play mode processing is on.
- Meshes made during an upload are saved under `Assets/_AuroraKai Generated/Hidden Mesh Remover` (the SDK needs them as assets) and deleted when the build finishes. **Tools > Kai > Hidden Mesh Remover > Delete Generated Meshes** clears any left by an interrupted build.
- Blendshapes, UV channels, vertex colours, bone weights and sub-meshes are all kept, so materials, animations and face tracking still work.

## Coming from the old Strip Hidden Blendshapes script

**Tools > Kai > Hidden Mesh Remover > Convert Old Strip Hidden Components** swaps the old component in the open scenes for this one, keeping its settings. Then delete the old `StripHidden` folder from `Assets`, so both don't run.

## Install

Add the listing in VRChat Creator Companion:

```
https://aurorakai.github.io/vpm/index.json
```

Then add **Hidden Mesh Remover** to your project.

## Licence

MIT. See `LICENSE.md`.
