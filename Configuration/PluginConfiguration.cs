using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TorrentStreamer.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string RadarrUrl { get; set; } = "http://localhost:7878";
        public string RadarrApiKey { get; set; } = "";
        
        public string SonarrUrl { get; set; } = "http://localhost:8989";
        public string SonarrApiKey { get; set; } = "";

        public string ProwlarrUrl { get; set; } = "http://localhost:9696";
        public string ProwlarrApiKey { get; set; } = "";

        public string DownloadDirectory { get; set; } = "C:\\TorrentStreams";
        
        public string ServerUrl { get; set; } = "http://127.0.0.1:19420";
        
        public bool KeepFilesAfterStreaming { get; set; } = false;
        
        public int UploadThrottleKBps { get; set; } = 50;

        public int MetadataTimeoutSeconds { get; set; } = 180;

        /// <summary>
        /// Optional custom list of trackers to append to magnet links.
        /// </summary>
        public string[] CustomTrackers { get; set; } = Array.Empty<string>();

        // ── qBittorrent Integration ──────────────────────────────────────────
        /// <summary>
        /// When true, the plugin delegates torrent downloads to qBittorrent
        /// instead of the built-in MonoTorrent engine.
        /// </summary>
        public bool UseQBittorrent { get; set; } = false;

        public string QBittorrentUrl { get; set; } = "http://localhost:8080";
        public string QBittorrentUsername { get; set; } = "admin";
        public string QBittorrentPassword { get; set; } = "";

        /// <summary>
        /// Save-path inside qBittorrent where torrents will be downloaded.
        /// Leave empty to use qBittorrent's default save path.
        /// </summary>
        public string QBittorrentSavePath { get; set; } = "";

        public PluginConfiguration()
        {
        }
    }
}
