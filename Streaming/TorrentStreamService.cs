using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TorrentStreamer.Configuration;
using Microsoft.Extensions.Logging;
using MonoTorrent.Client;
using MonoTorrent;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.TorrentStreamer.Streaming
{
    public class TorrentStreamService : IDisposable
    {
        private readonly ILogger _logger;
        private ClientEngine _engine;
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        
        // Maps a streaming session ID to a TorrentManager
        private ConcurrentDictionary<string, TorrentManager> _activeTorrents = new ConcurrentDictionary<string, TorrentManager>();

        public TorrentStreamService(ILogger logger)
        {
            _logger = logger;
            _cts = new CancellationTokenSource();
            
            var cachePath = Path.Combine("/config/data/TorrentStreams", "Cache");
            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);

            var engineSettings = new EngineSettingsBuilder()
            {
                AllowPortForwarding = true,
                AutoSaveLoadDhtCache = true,
                AutoSaveLoadFastResume = true,
                CacheDirectory = cachePath,
                DhtEndPoint = new IPEndPoint(IPAddress.Any, 55123)
            }.ToSettings();
            
            _engine = new ClientEngine(engineSettings);
            
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://+:19420/");
        }

        public async Task StartAsync()
        {
            try
            {
                _httpListener.Start();
                _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token));
                _logger.LogInformation("TorrentStreamService HTTP Listener started on http://localhost:19420/stream/");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start TorrentStreamService HTTP Listener");
            }
        }

        public async Task<string> StartStreamingAsync(string magnetLink, PluginConfiguration config)
        {
            var downloadPath = string.IsNullOrWhiteSpace(config.DownloadDirectory) 
                ? Path.Combine(Path.GetTempPath(), "JellyfinTorrentStreams") 
                : config.DownloadDirectory;

            if (!Directory.Exists(downloadPath))
                Directory.CreateDirectory(downloadPath);

            var torrentInfo = MagnetLink.Parse(magnetLink);
            
            // Limit upload speed based on config globally for the engine
            var currentSettings = _engine.Settings;
            var newSettings = new EngineSettingsBuilder(currentSettings)
            {
                MaximumUploadRate = config.UploadThrottleKBps > 0 ? config.UploadThrottleKBps * 1024 : 0
            }.ToSettings();
            
            await _engine.UpdateSettingsAsync(newSettings);

            var managerSettings = new TorrentSettingsBuilder().ToSettings();

            var manager = await _engine.AddStreamingAsync(torrentInfo, downloadPath, managerSettings);
            await manager.StartAsync();

            string sessionId = Guid.NewGuid().ToString("N");
            _activeTorrents.TryAdd(sessionId, manager);

            return $"http://127.0.0.1:19420/stream/?sessionId={sessionId}";
        }

        private async Task AcceptConnectionsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    // Ignore expected exception on shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting HTTP connection");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var sessionId = context.Request.QueryString["sessionId"];
                var magnetLink = context.Request.QueryString["magnet"];
                var type = context.Request.QueryString["type"];
                var idStr = context.Request.QueryString["id"];
                string downloadUrl = null;

                if (context.Request.Url.AbsolutePath.Contains("/stream"))
                {
                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(idStr) && int.TryParse(idStr, out int mediaId))
                    {
                        var config = Plugin.Instance.Configuration;
                        using var httpClient = new System.Net.Http.HttpClient();
                        if (type == "movie" && !string.IsNullOrEmpty(config.RadarrUrl) && !string.IsNullOrEmpty(config.RadarrApiKey))
                        {
                            var radarrClient = new Jellyfin.Plugin.TorrentStreamer.Api.RadarrClient(httpClient, config.RadarrUrl, config.RadarrApiKey);
                            try {
                                var releases = await radarrClient.GetReleases(mediaId);
                                var best = releases.OrderByDescending(r => r.Seeders).FirstOrDefault(r => !string.IsNullOrEmpty(r.DownloadUrl) || !string.IsNullOrEmpty(r.MagnetUrl) || !string.IsNullOrEmpty(r.ReleaseHash) || !string.IsNullOrEmpty(r.InfoHash) || (!string.IsNullOrEmpty(r.Guid) && r.Guid.StartsWith("magnet:")));
                                if (best != null) {
                                    if (!string.IsNullOrEmpty(best.ReleaseHash)) magnetLink = $"magnet:?xt=urn:btih:{best.ReleaseHash}";
                                    else if (!string.IsNullOrEmpty(best.InfoHash)) magnetLink = $"magnet:?xt=urn:btih:{best.InfoHash}";
                                    else if (!string.IsNullOrEmpty(best.MagnetUrl)) magnetLink = best.MagnetUrl;
                                    else if (!string.IsNullOrEmpty(best.Guid) && best.Guid.StartsWith("magnet:")) magnetLink = best.Guid;
                                    
                                    if (string.IsNullOrEmpty(magnetLink) && !string.IsNullOrEmpty(best.DownloadUrl)) magnetLink = best.DownloadUrl;
                                    else if (!string.IsNullOrEmpty(best.DownloadUrl)) downloadUrl = best.DownloadUrl;
                                }
                            } catch (Exception ex) {
                                System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Radarr Exception - {ex.Message}\n");
                            }
                        }
                        else if (type == "episode" && !string.IsNullOrEmpty(config.SonarrUrl) && !string.IsNullOrEmpty(config.SonarrApiKey))
                        {
                            var sonarrClient = new Jellyfin.Plugin.TorrentStreamer.Api.SonarrClient(httpClient, config.SonarrUrl, config.SonarrApiKey);
                            var releases = await sonarrClient.GetReleases(mediaId);
                            var best = releases.OrderByDescending(r => r.Seeders).FirstOrDefault(r => !string.IsNullOrEmpty(r.DownloadUrl) || !string.IsNullOrEmpty(r.MagnetUrl) || !string.IsNullOrEmpty(r.ReleaseHash) || !string.IsNullOrEmpty(r.InfoHash) || (!string.IsNullOrEmpty(r.Guid) && r.Guid.StartsWith("magnet:")));
                            if (best != null) {
                                if (!string.IsNullOrEmpty(best.ReleaseHash)) magnetLink = $"magnet:?xt=urn:btih:{best.ReleaseHash}";
                                else if (!string.IsNullOrEmpty(best.InfoHash)) magnetLink = $"magnet:?xt=urn:btih:{best.InfoHash}";
                                else if (!string.IsNullOrEmpty(best.MagnetUrl)) magnetLink = best.MagnetUrl;
                                else if (!string.IsNullOrEmpty(best.Guid) && best.Guid.StartsWith("magnet:")) magnetLink = best.Guid;
                                
                                if (string.IsNullOrEmpty(magnetLink) && !string.IsNullOrEmpty(best.DownloadUrl)) magnetLink = best.DownloadUrl;
                                else if (!string.IsNullOrEmpty(best.DownloadUrl)) downloadUrl = best.DownloadUrl;
                            }
                        }
                    }
                }

                
                TorrentManager manager = null;

                if (!string.IsNullOrEmpty(sessionId))
                {
                    _activeTorrents.TryGetValue(sessionId, out manager);
                }
                else if (!string.IsNullOrEmpty(magnetLink))
                {
                    // Generate a deterministic session ID for this magnet
                    sessionId = Uri.EscapeDataString(magnetLink).GetHashCode().ToString();
                    if (!_activeTorrents.TryGetValue(sessionId, out manager))
                    {
                        var config = Plugin.Instance.Configuration;
                        var downloadPath = string.IsNullOrWhiteSpace(config.DownloadDirectory) 
                            ? Path.Combine(Path.GetTempPath(), "JellyfinTorrentStreams") 
                            : config.DownloadDirectory;

                        if (!Directory.Exists(downloadPath))
                            Directory.CreateDirectory(downloadPath);

                        bool loadedFromHttp = false;
                        if (magnetLink != null && magnetLink.StartsWith("http")) downloadUrl = magnetLink;

                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            try {
                                var targetUrl = downloadUrl.Replace("localhost", "host.docker.internal").Replace("127.0.0.1", "host.docker.internal");
                                using var handler = new System.Net.Http.HttpClientHandler {
                                    AllowAutoRedirect = false
                                };
                                using var hc = new System.Net.Http.HttpClient(handler);
                                if (!string.IsNullOrEmpty(config.ProwlarrApiKey)) {
                                    hc.DefaultRequestHeaders.Add("X-Api-Key", config.ProwlarrApiKey);
                                }
                                
                                var response = await hc.GetAsync(targetUrl);
                                if (response.StatusCode == HttpStatusCode.Redirect || 
                                    response.StatusCode == HttpStatusCode.MovedPermanently || 
                                    response.StatusCode == HttpStatusCode.Found || 
                                    response.StatusCode == (HttpStatusCode)307 || 
                                    response.StatusCode == (HttpStatusCode)308)
                                {
                                    var location = response.Headers.Location?.ToString();
                                    if (!string.IsNullOrEmpty(location))
                                    {
                                        if (location.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                                        {
                                            System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Prowlarr redirected to a magnet link: {location}\n");
                                            magnetLink = location;
                                        }
                                        else
                                        {
                                            System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Prowlarr redirected to another HTTP URL: {location}\n");
                                            using var hcRedirect = new System.Net.Http.HttpClient();
                                            var torrentBytes = await hcRedirect.GetByteArrayAsync(location);
                                            manager = await _engine.AddStreamingAsync(MonoTorrent.Torrent.Load(torrentBytes), downloadPath, new TorrentSettingsBuilder().ToSettings());
                                            loadedFromHttp = true;
                                        }
                                    }
                                }
                                else if (response.IsSuccessStatusCode)
                                {
                                    var torrentBytes = await response.Content.ReadAsByteArrayAsync();
                                    manager = await _engine.AddStreamingAsync(MonoTorrent.Torrent.Load(torrentBytes), downloadPath, new TorrentSettingsBuilder().ToSettings());
                                    loadedFromHttp = true;
                                }
                                else
                                {
                                    System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Prowlarr returned status: {response.StatusCode}\n");
                                }
                            } catch (Exception ex) {
                                System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Failed to download torrent file from {downloadUrl}: {ex.Message}\n");
                            }
                        }
                        
                        if (!loadedFromHttp)
                        {
                            var targetMagnet = magnetLink != null && magnetLink.StartsWith("http") ? "" : magnetLink;
                            if (!string.IsNullOrEmpty(targetMagnet)) {
                                targetMagnet += "&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce" +
                                                "&tr=udp%3A%2F%2Ftracker.openbittorrent.com%3A6969%2Fannounce" +
                                                "&tr=udp%3A%2F%2Fexodus.desync.com%3A6969%2Fannounce" +
                                                "&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce" +
                                                "&tr=udp%3A%2F%2Ftracker.moeking.me%3A6969%2Fannounce";
                                var torrentInfo = MagnetLink.Parse(targetMagnet);
                                manager = await _engine.AddStreamingAsync(torrentInfo, downloadPath, new TorrentSettingsBuilder().ToSettings());
                            }
                        }
                        
                        if (manager != null) {
                            await manager.StartAsync();
                            _activeTorrents.TryAdd(sessionId, manager);
                        }
                    }
                }

                if (manager == null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                // Wait for metadata to download if it's a magnet link
                int timeoutMs = 60000; // 60 seconds
                while (!manager.HasMetadata && timeoutMs > 0)
                {
                    await Task.Delay(500);
                    timeoutMs -= 500;
                    if (timeoutMs % 5000 == 0) {
                        System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Waiting for metadata... {timeoutMs/1000}s remaining. State: {manager.State}, Peers: {manager.Peers.Available}\n");
                    }
                }

                if (!manager.HasMetadata)
                {
                    System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Timeout waiting for metadata.\n");
                    context.Response.StatusCode = 504; // Gateway Timeout
                    context.Response.Close();
                    return;
                }

                // Find the largest file (assuming it's the video)
                var largestFile = manager.Files.OrderByDescending(f => f.Length).FirstOrDefault();
                
                if (largestFile != null) {
                    System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Selected largest file: {largestFile.Path} ({largestFile.Length} bytes)\n");
                } else {
                    System.IO.File.AppendAllText("/config/data/TorrentStreams/plugin_debug.log", $"[{DateTime.Now}] STREAM: Torrent has no files!\n");
                }
                if (largestFile == null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                // Get stream from MonoTorrent which handles prioritizing pieces automatically
                using var stream = await manager.StreamProvider.CreateStreamAsync(largestFile, CancellationToken.None);

                context.Response.ContentType = "video/mp4"; // Generic, Jellyfin will probe it
                context.Response.SendChunked = false;

                long start = 0;
                long end = largestFile.Length - 1;

                if (!string.IsNullOrEmpty(context.Request.Headers["Range"]))
                {
                    var rangeHeader = context.Request.Headers["Range"].Replace("bytes=", "").Split('-');
                    start = long.Parse(rangeHeader[0]);
                    if (rangeHeader.Length > 1 && !string.IsNullOrEmpty(rangeHeader[1]))
                    {
                        end = long.Parse(rangeHeader[1]);
                    }

                    context.Response.StatusCode = 206; // Partial Content
                    context.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{largestFile.Length}");
                }
                else
                {
                    context.Response.StatusCode = 200;
                }

                context.Response.ContentLength64 = end - start + 1;
                context.Response.AddHeader("Accept-Ranges", "bytes");

                stream.Seek(start, SeekOrigin.Begin);

                // Copy stream to response output
                byte[] buffer = new byte[81920]; // 80KB chunks
                long bytesRemaining = context.Response.ContentLength64;
                int bytesRead;

                while (bytesRemaining > 0 && (bytesRead = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, bytesRemaining))) > 0)
                {
                    await context.Response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client disconnected or stream failed.");
            }
            finally
            {
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _httpListener.Stop();
            _httpListener.Close();
            _engine.StopAllAsync().Wait(3000);
            _engine.Dispose();
        }
    }
}
