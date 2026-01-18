using System.Collections.Generic;

namespace QuestPatcher.Core.Downgrading.Models
{
    /// <param name="Paths">PackageVersion -> DowngradePaths</param>
    /// <param name="Checksums">asset CRC32 checksums </param>
    public record DowngradeIndex(Dictionary<string, List<AppDiff>> Paths, Dictionary<string, uint> Checksums);
}
