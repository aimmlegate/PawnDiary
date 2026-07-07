// Pure classifier: is this mod running from a Steam Workshop subscription or a local install?
//
// It decides purely from the mod's root directory path, so it can be unit-tested without RimWorld.
// RimWorld's Steam app id is 294100, and subscribed Workshop mods always live under
// ".../steamapps/workshop/content/294100/<publishedfileid>/". Anything else (the RimWorld Mods
// folder, a dev junction) is treated as a local install.
namespace PawnDiary
{
    /// <summary>Classifies a mod install location as Workshop, local, or unknown.</summary>
    internal static class InstallSource
    {
        public const string Workshop = "workshop";
        public const string Local = "local";
        public const string Unknown = "unknown";

        // The RimWorld-specific Workshop content path segment. Including the app id (294100) avoids
        // matching another game's workshop folder if a path somehow contains one.
        private const string WorkshopMarker = "/workshop/content/294100";

        /// <summary>
        /// Returns <see cref="Workshop"/>, <see cref="Local"/>, or <see cref="Unknown"/> for a mod
        /// root directory. Path separators are normalized so it works for Windows and Unix paths.
        /// </summary>
        public static string FromRootDir(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                return Unknown;
            }

            string normalized = rootDir.Replace('\\', '/').ToLowerInvariant();
            return normalized.Contains(WorkshopMarker) ? Workshop : Local;
        }
    }
}
