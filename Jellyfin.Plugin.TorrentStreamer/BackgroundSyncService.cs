using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.TorrentStreamer.Api;

namespace Jellyfin.Plugin.TorrentStreamer
{
    public class BackgroundSyncService : IDisposable
    {
        private readonly ILogger _logger;
        private Timer _timer;

        public BackgroundSyncService(ILogger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            // Run every 30 minutes
            _timer = new Timer(async _ => await SyncAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(30));
            _logger.LogInformation("BackgroundSyncService started.");
        }

        private async Task SyncAsync()
        {
            var logFile = "/config/data/TorrentStreams/plugin_debug.log";
            Action<string> log = (msg) => {
                try { File.AppendAllText(logFile, $"[{DateTime.Now}] {msg}\n"); } catch { }
            };

            log("SyncAsync started");
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null) 
                {
                    log("Config is null!");
                    return;
                }

                log($"RadarrUrl: {config.RadarrUrl}");
                var libDir = string.IsNullOrWhiteSpace(config.DownloadDirectory) 
                    ? "/config/data/TorrentStreams" 
                    : config.DownloadDirectory;

                var moviesDir = Path.Combine(libDir, "Movies");
                var showsDir = Path.Combine(libDir, "Shows");

                if (!Directory.Exists(moviesDir)) { try { Directory.CreateDirectory(moviesDir); } catch { } }
                if (!Directory.Exists(showsDir)) { try { Directory.CreateDirectory(showsDir); } catch { } }

                // Sync Radarr
                if (!string.IsNullOrWhiteSpace(config.RadarrUrl) && !string.IsNullOrWhiteSpace(config.RadarrApiKey))
                {
                    log("Syncing Radarr...");
                    try {
                        using var httpClient = new HttpClient();
                        var radarrClient = new RadarrClient(httpClient, config.RadarrUrl, config.RadarrApiKey);
                        var movies = await radarrClient.GetMovies();
                        log($"Found {movies.Count} movies");
                        foreach (var movie in movies)
                        {
                            var cleanTitle = string.Join("_", (movie.Title ?? "Unknown").Split(Path.GetInvalidFileNameChars()));
                            var movieFolder = Path.Combine(moviesDir, cleanTitle);
                            if (!Directory.Exists(movieFolder)) Directory.CreateDirectory(movieFolder);
                            
                            var strmFile = Path.Combine(movieFolder, $"{cleanTitle}.strm");
                            var baseUrl = string.IsNullOrWhiteSpace(config.ServerUrl) ? "http://127.0.0.1:19420" : config.ServerUrl.TrimEnd('/');
                            var url = $"{baseUrl}/stream?type=movie&id={movie.Id}";
                            if (!File.Exists(strmFile) || File.ReadAllText(strmFile) != url)
                            {
                                File.WriteAllText(strmFile, url);
                            }
                        }
                    } catch (Exception rex) { log($"Radarr Error: {rex.Message}"); }
                }

                // Sync Sonarr
                if (!string.IsNullOrWhiteSpace(config.SonarrUrl) && !string.IsNullOrWhiteSpace(config.SonarrApiKey))
                {
                    log("Syncing Sonarr...");
                    try {
                        using var httpClient = new HttpClient();
                        var sonarrClient = new SonarrClient(httpClient, config.SonarrUrl, config.SonarrApiKey);
                        var seriesList = await sonarrClient.GetSeries();
                        log($"Found {seriesList.Count} series");
                        foreach (var series in seriesList)
                        {
                            var cleanTitle = string.Join("_", (series.Title ?? "Unknown").Split(Path.GetInvalidFileNameChars()));
                            var seriesFolder = Path.Combine(showsDir, cleanTitle);
                            if (!Directory.Exists(seriesFolder)) Directory.CreateDirectory(seriesFolder);

                            var episodes = await sonarrClient.GetEpisodes(series.Id);
                            foreach (var ep in episodes)
                            {
                                var seasonFolder = Path.Combine(seriesFolder, $"Season {ep.SeasonNumber}");
                                if (!Directory.Exists(seasonFolder)) Directory.CreateDirectory(seasonFolder);

                                var epName = $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2}";
                                var strmFile = Path.Combine(seasonFolder, $"{epName}.strm");
                                
                                var baseUrl = string.IsNullOrWhiteSpace(config.ServerUrl) ? "http://127.0.0.1:19420" : config.ServerUrl.TrimEnd('/');
                                var url = $"{baseUrl}/stream?type=episode&id={ep.Id}";
                                if (!File.Exists(strmFile) || File.ReadAllText(strmFile) != url)
                                {
                                    File.WriteAllText(strmFile, url);
                                }
                            }
                        }
                    } catch (Exception sex) { log($"Sonarr Error: {sex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                log($"Global Error: {ex.Message}");
            }
        }

        public async Task ForceSyncAsync()
        {
            await SyncAsync();
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
