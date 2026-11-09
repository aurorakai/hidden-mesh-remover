namespace AuroraKai.HiddenMeshRemover.Editor {
    /// <summary>
    /// Case-insensitive wildcard matching of blendshape names against the
    /// component's include and exclude patterns. * matches any run of characters
    /// (including none), ? matches exactly one. Names are compared as they are,
    /// spaces included; blank patterns are ignored.
    /// </summary>
    internal static class BlendshapeFilter {
        internal static bool Matches(string name, RemoveHiddenMeshes settings) {
            return MatchesAny(name, settings.patterns) && !MatchesAny(name, settings.excludePatterns);
        }

        internal static bool MatchesAny(string name, string[] patterns) {
            if (patterns == null) return false;
            foreach (var pattern in patterns) {
                if (!string.IsNullOrWhiteSpace(pattern) && Glob(name, pattern)) return true;
            }
            return false;
        }

        internal static bool Glob(string text, string pattern) {
            int t = 0, p = 0, starP = -1, starT = 0;
            while (t < text.Length) {
                if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], text[t]))) {
                    t++;
                    p++;
                } else if (p < pattern.Length && pattern[p] == '*') {
                    starP = p++;
                    starT = t;
                } else if (starP >= 0) {
                    p = starP + 1;
                    t = ++starT;
                } else {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static bool Same(char a, char b) {
            return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
        }
    }
}
