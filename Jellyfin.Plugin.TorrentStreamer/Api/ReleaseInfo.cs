using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    public class ReleaseInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("seeders")]
        public int Seeders { get; set; }

        [JsonPropertyName("magnetUrl")]
        public string MagnetUrl { get; set; }

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; }

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonPropertyName("releaseHash")]
        public string ReleaseHash { get; set; }

        [JsonPropertyName("infoHash")]
        public string InfoHash { get; set; }
    }
}
