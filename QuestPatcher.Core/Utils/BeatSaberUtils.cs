using System;
using QuestPatcher.Core.Models;
using Serilog;
using Version = SemanticVersioning.Version;

namespace QuestPatcher.Core.Utils
{
    public class BeatSaberUtils
    {
        public static Version? ParseVersion(string packageVersion)
        {
            try
            {
                if (Version.TryParse(packageVersion, true, out var semVersion))
                {
                    return semVersion;
                }

                string cleanedVersion = packageVersion.Replace(" ", "").Replace('_', '+');
                return Version.TryParse(cleanedVersion, true, out semVersion) ? semVersion : null;
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to parse version {Version} to SemVer", packageVersion);
                return null;
            }
        }


        public static ModLoader GetDefaultModLoader(Version? version = null)
        {
            if (version == null)
            {
                return ModLoader.Scotland2;
            }

            return version > SharedConstants.BeatSaberLastQuestLoaderVersion
                ? ModLoader.Scotland2
                : ModLoader.QuestLoader;
        }
    }
}
