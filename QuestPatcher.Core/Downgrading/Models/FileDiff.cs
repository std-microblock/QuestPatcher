using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Downgrading.Models
{
    /*
  {
  "diff_name": "bs1.36-1.35.apk.diff",
  "file_name": "bs136.apk",
  "file_crc": 1675847848,
  "output_file_name": "bs135.apk",
  "output_crc": 2088061822,
  "output_size": 49104123
}
 */

    public sealed record FileDiff
    {
        [JsonPropertyName("diff_name")]
        public string DiffName { get; }

        [JsonPropertyName("file_name")]
        public string FileName { get; }

        [JsonPropertyName("file_crc")]
        public uint FileCrc { get; }

        [JsonPropertyName("output_file_name")]
        public string OutputFileName { get; }

        [JsonPropertyName("output_crc")]
        public uint OutputCrc { get; }

        [JsonPropertyName("output_size")]
        public long OutputSize { get; }

        [JsonConstructor]
        public FileDiff(string diffName, string fileName, uint fileCrc, string outputFileName, uint outputCrc,
            long outputSize)
        {
            DiffName = diffName;
            FileName = fileName;
            FileCrc = fileCrc;
            OutputFileName = outputFileName;
            OutputCrc = outputCrc;
            OutputSize = outputSize;
        }
    }
}
