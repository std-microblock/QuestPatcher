using System.Text.Json.Serialization;
using SemanticVersioning;

namespace QuestPatcher.Core.ModBrowser.Models
{
    /* JSON example: {
          "name": "BeatLeader",
          "description": "beatleader.xyz | In-game leaderboards for custom and OST maps | Score replays | Clans, events, playlists and much more",
          "id": "BeatLeader",
          "version": "0.8.4",
          "author": "NSGolova",
          "authorIcon": "https://avatars.githubusercontent.com/u/98843512?v=4",
          "modloader": "Scotland2",
          "download": "https://github.com/BeatLeader/beatleader-qmod/releases/download/v0.8.4/BeatLeader.qmod",
          "source": "https://github.com/BeatLeader/beatleader-qmod/",
          "cover": "https://raw.githubusercontent.com/BeatLeader/beatleader-qmod/master/cover.png",
          "funding": [],
          "website": "https://github.com/BeatLeader/beatleader-qmod/"
       }
     */
    
    public class ExternalMod
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";
        
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("version")]
        public string VersionString
        {
            get => this.Version.ToString();
            set => this.Version = Version.Parse(value);
        }

        [JsonIgnore]
        public Version Version { get; set; }
        
        [JsonPropertyName("author")]
        public string Author { get; set; }
        
        [JsonPropertyName("download")]
        public string DownloadUrl { get; set; }
        
        [JsonPropertyName("cover")]
        public string CoverUrl { get; set; }

        public override string ToString()
        {
            return $"{Id}@{VersionString}";
        }
    }
}
