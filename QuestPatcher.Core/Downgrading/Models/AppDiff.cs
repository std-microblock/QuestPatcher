using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Downgrading.Models
{
    public sealed record AppDiff
    {
        [JsonPropertyName("from_version")]
        public string FromVersion { get; }
        
        [JsonPropertyName("to_version")]
        public string ToVersion { get; }
        
        [JsonPropertyName("apk_diff")]
        public FileDiff ApkDiff { get; }
        
        [JsonPropertyName("obb_diffs")]
        public List<FileDiff> ObbDiffs { get; }

        [JsonConstructor]
        public AppDiff(string fromVersion, string toVersion, FileDiff apkDiff, List<FileDiff> obbDiffs)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
            ApkDiff = apkDiff;
            ObbDiffs = obbDiffs;
        }
    }
}
