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

        public PluginConfiguration()
        {
        }
    }
}
