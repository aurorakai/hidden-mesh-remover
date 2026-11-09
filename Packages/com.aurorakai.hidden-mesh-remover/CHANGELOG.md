# Changelog

## 1.0.0

First release as a VPM package, replacing the project-local Strip Hidden Blendshapes script.

- Name patterns with wildcards and exclusions instead of a single prefix; defaults cover `Hide*`, `*-off`, `*_off` and `* off`.
- Visibility check: geometry is removed only if the hide shape collapses it or tucks it inside geometry that is always shown. Hide shapes that just reshape a visible surface no longer leave holes, which the old script did.
- Movement threshold: vertices a hide shape only nudges (sculpted corrections around the hidden part) are kept. The old script removed them, which could delete visible geometry.
- Removes the vertices left unused, not just triangles, keeping blendshapes, UVs, colours and bone weights.
- No NDMF dependency: runs as a VRChat SDK build callback, before NDMF and VRCFury.
- Per-renderer exclusions; nested components use the nearest settings.
- Faster inspector preview, cached until settings or weights change, with a list of likely hide shapes the patterns miss.
- Menu item to convert old Strip Hidden Blendshapes components.
