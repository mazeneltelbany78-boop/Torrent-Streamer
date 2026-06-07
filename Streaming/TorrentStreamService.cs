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
        public static TorrentStreamService Instance { get; private set; }
        private readonly ILogger _logger;
        private ClientEngine _engine;
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        
        // Maps a streaming session ID to a TorrentManager
        private ConcurrentDictionary<string, TorrentManager> _activeTorrents = new ConcurrentDictionary<string, TorrentManager>();
        
        // Cache to deduplicate and store indexer searches so retries are instantaneous
        private static ConcurrentDictionary<string, System.Threading.Tasks.Task<string>> _activeSearches = new ConcurrentDictionary<string, System.Threading.Tasks.Task<string>>();

        // Lock to prevent race conditions when multiple Jellyfin ffmpeg threads request the stream simultaneously
        private static readonly SemaphoreSlim _addTorrentLock = new SemaphoreSlim(1, 1);

        // Lock to prevent Sonarr from crashing during mass library scans by throttling active searches
        private static readonly SemaphoreSlim _sonarrSearchLock = new SemaphoreSlim(2, 2);

        private string AppendTrackers(string magnet)
        {
            if (string.IsNullOrEmpty(magnet) || !magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return magnet;

            var config = Plugin.Instance.Configuration;
            var trackers = (config.CustomTrackers != null && config.CustomTrackers.Length > 0) ? config.CustomTrackers : new[] {
                "udp://tracker.opentrackr.org:1337/announce",
                "udp://tracker.openbittorrent.com:6969/announce",
                "udp://exodus.desync.com:6969/announce",
                "udp://tracker.torrent.eu.org:451/announce"
            };

            var sb = new System.Text.StringBuilder(magnet);
            foreach (var tr in trackers)
            {
                var trEncoded = System.Net.WebUtility.UrlEncode(tr);
                if (!magnet.Contains(trEncoded))
                {
                    sb.Append($"&tr={trEncoded}");
                }
            }
            return sb.ToString();
        }




        public TorrentStreamService(ILogger logger)
        {
            Instance = this;
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
                DhtEndPoint = new IPEndPoint(IPAddress.Any, 55123),
                MaximumConnections = 150,
                MaximumHalfOpenConnections = 20
            }.ToSettings();
            
            _engine = new ClientEngine(engineSettings);
            
            // Start the pre-initialized DHT engine
            if (_engine.Dht != null) {
                _ = Task.Run(async () => {
                    try { await ((MonoTorrent.Dht.DhtEngine)_engine.Dht).StartAsync(); } catch { }
                });
            }
            
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

        public System.Collections.Generic.IEnumerable<object> GetActiveTorrents()
        {
            var results = new System.Collections.Generic.List<object>();
            foreach (var manager in _activeTorrents.Values)
            {
                results.Add(new {
                    Name = manager.Torrent?.Name ?? "Loading...",
                    Progress = manager.Progress,
                    DownloadSpeed = manager.Monitor.DownloadRate,
                    UploadSpeed = manager.Monitor.UploadRate,
                    State = manager.State.ToString()
                });
            }
            return results;
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


        private System.Threading.Tasks.Task<string> GetOrResolveMagnetLinkAsync(string type, int mediaId, int attempt = 0)
        {
            var cacheKey = $"{type}_{mediaId}_{attempt}";
            return _activeSearches.GetOrAdd(cacheKey, async key =>
            {
                var config = Plugin.Instance.Configuration;
                using var httpClient = new System.Net.Http.HttpClient();
                string resolvedMagnet = null;
                string downloadUrl = null;

                try {
                    if (type == "movie" && !string.IsNullOrEmpty(config.RadarrUrl) && !string.IsNullOrEmpty(config.RadarrApiKey))
                    {
                        var radarrClient = new Jellyfin.Plugin.TorrentStreamer.Api.RadarrClient(httpClient, config.RadarrUrl, config.RadarrApiKey);
                        var releases = await radarrClient.GetReleases(mediaId);
                        var best = System.Linq.Enumerable.Skip(System.Linq.Enumerable.OrderByDescending(releases, r => r.Seeders), attempt).FirstOrDefault(r => !string.IsNullOrEmpty(r.DownloadUrl) || !string.IsNullOrEmpty(r.MagnetUrl) || !string.IsNullOrEmpty(r.ReleaseHash) || !string.IsNullOrEmpty(r.InfoHash) || (!string.IsNullOrEmpty(r.Guid) && r.Guid.StartsWith("magnet:")));
                        if (best != null) {
                            if (!string.IsNullOrEmpty(best.MagnetUrl)) resolvedMagnet = best.MagnetUrl;
                            else if (!string.IsNullOrEmpty(best.DownloadUrl)) resolvedMagnet = best.DownloadUrl;
                            else if (!string.IsNullOrEmpty(best.ReleaseHash)) resolvedMagnet = $"magnet:?xt=urn:btih:{best.ReleaseHash}";
                            else if (!string.IsNullOrEmpty(best.InfoHash)) resolvedMagnet = $"magnet:?xt=urn:btih:{best.InfoHash}";
                            else if (!string.IsNullOrEmpty(best.Guid) && best.Guid.StartsWith("magnet:")) resolvedMagnet = best.Guid;
                            if (string.IsNullOrEmpty(resolvedMagnet) && !string.IsNullOrEmpty(best.DownloadUrl)) downloadUrl = best.DownloadUrl;
                        }
                    }
                    else if (type == "episode" && !string.IsNullOrEmpty(config.SonarrUrl) && !string.IsNullOrEmpty(config.SonarrApiKey))
                    {
                        bool acquired = await _sonarrSearchLock.WaitAsync(2000);
                        if (!acquired) {
                            _logger.LogWarning("STREAM: Sonarr search concurrency limit reached (likely library scan). Skipping active search.");
                            return null;
                        }
                        try {
                            var sonarrClient = new Jellyfin.Plugin.TorrentStreamer.Api.SonarrClient(httpClient, config.SonarrUrl, config.SonarrApiKey);
                            var releases = await sonarrClient.GetReleases(mediaId);
                            var best = System.Linq.Enumerable.Skip(System.Linq.Enumerable.OrderByDescending(releases, r => r.Seeders), attempt).FirstOrDefault(r => !string.IsNullOrEmpty(r.DownloadUrl) || !string.IsNullOrEmpty(r.MagnetUrl) || !string.IsNullOrEmpty(r.ReleaseHash) || !string.IsNullOrEmpty(r.InfoHash) || (!string.IsNullOrEmpty(r.Guid) && r.Guid.StartsWith("magnet:")));
                            if (best != null) {
                                if (!string.IsNullOrEmpty(best.MagnetUrl)) resolvedMagnet = best.MagnetUrl;
                                else if (!string.IsNullOrEmpty(best.DownloadUrl)) resolvedMagnet = best.DownloadUrl;
                                else if (!string.IsNullOrEmpty(best.ReleaseHash)) resolvedMagnet = $"magnet:?xt=urn:btih:{best.ReleaseHash}";
                                else if (!string.IsNullOrEmpty(best.InfoHash)) resolvedMagnet = $"magnet:?xt=urn:btih:{best.InfoHash}";
                                else if (!string.IsNullOrEmpty(best.Guid) && best.Guid.StartsWith("magnet:")) resolvedMagnet = best.Guid;
                                if (string.IsNullOrEmpty(resolvedMagnet) && !string.IsNullOrEmpty(best.DownloadUrl)) downloadUrl = best.DownloadUrl;
                            }
                        } finally {
                            _sonarrSearchLock.Release();
                        }
                    }

                    if (!string.IsNullOrEmpty(downloadUrl) && string.IsNullOrEmpty(resolvedMagnet))
                    {
                        var targetUrl = downloadUrl.Replace("localhost", "host.docker.internal").Replace("127.0.0.1", "host.docker.internal");
                        using var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false };
                        using var hc = new System.Net.Http.HttpClient(handler);
                        if (!string.IsNullOrEmpty(config.ProwlarrApiKey)) hc.DefaultRequestHeaders.Add("X-Api-Key", config.ProwlarrApiKey);
                        
                        int hops = 0;
                        while (hops < 5 && string.IsNullOrEmpty(resolvedMagnet) && !string.IsNullOrEmpty(targetUrl))
                        {
                            var response = await hc.GetAsync(targetUrl);
                            if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 308)
                            {
                                var location = response.Headers.Location?.ToString();
                                if (!string.IsNullOrEmpty(location))
                                {
                                    if (location.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        resolvedMagnet = location;
                                        break;
                                    }
                                    
                                    if (!location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var uri = new Uri(targetUrl);
                                        targetUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{location}";
                                    }
                                    else
                                    {
                                        targetUrl = location;
                                    }
                                    hops++;
                                    continue;
                                }
                            }
                            break;
                        }
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "STREAM: Error resolving magnet.");
                }
                
                resolvedMagnet = AppendTrackers(resolvedMagnet);
                return resolvedMagnet;
            });
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                string? sessionId = context.Request.QueryString["sessionId"];
                string? magnetLink = context.Request.QueryString["magnet"];
                string? type = context.Request.QueryString["type"];
                string? idStr = context.Request.QueryString["id"];


                if (context.Request.Url.AbsolutePath.Contains("/stream"))
                {
                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(idStr) && int.TryParse(idStr, out int mediaId))
                    {
                        magnetLink = await GetOrResolveMagnetLinkAsync(type, mediaId);
                    }
                }

                var config = Plugin.Instance.Configuration;
                if (config.UseQBittorrent)
                {
                    await HandleQBittorrentRequestAsync(context, magnetLink, type, idStr);
                    return;
                }

                TorrentManager? manager = null;
                
                if (!string.IsNullOrEmpty(sessionId))
                {
                    _activeTorrents.TryGetValue(sessionId!, out manager);
                }
                
                if (manager == null && !string.IsNullOrEmpty(magnetLink))
                {
                    bool isHttp = magnetLink.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                    var targetMagnet = isHttp ? "" : magnetLink;
                    
                    if (!isHttp && !string.IsNullOrEmpty(targetMagnet)) {
                        if (!targetMagnet.Contains("&tr=")) {
                            targetMagnet += "&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce" +
                                            "&tr=udp%3A%2F%2Ftracker.openbittorrent.com%3A6969%2Fannounce" +
                                            "&tr=udp%3A%2F%2Fexodus.desync.com%3A6969%2Fannounce" +
                                            "&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce";
                        }
                        sessionId = Uri.EscapeDataString(targetMagnet).GetHashCode().ToString();
                    } else if (isHttp) {
                        sessionId = Uri.EscapeDataString(magnetLink).GetHashCode().ToString();
                    }
                    
                    if (!_activeTorrents.TryGetValue(sessionId, out manager))
                    {
                        await _addTorrentLock.WaitAsync();
                        try {
                            if (!_activeTorrents.TryGetValue(sessionId!, out manager)) {
                                var downloadPath = string.IsNullOrWhiteSpace(config.DownloadDirectory) 
                                    ? Path.Combine(Path.GetTempPath(), "JellyfinTorrentStreams") 
                                    : config.DownloadDirectory;

                                if (!Directory.Exists(downloadPath))
                                    Directory.CreateDirectory(downloadPath);

                                if (isHttp) {
                                    var targetUrl = magnetLink.Replace("localhost", "host.docker.internal").Replace("127.0.0.1", "host.docker.internal");
                                    
                                    using var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false };
                                    using var hc = new System.Net.Http.HttpClient(handler);
                                    if (!string.IsNullOrEmpty(config.ProwlarrApiKey)) hc.DefaultRequestHeaders.Add("X-Api-Key", config.ProwlarrApiKey);
                                    
                                    var response = await hc.GetAsync(targetUrl);
                                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 308) {
                                        var location = response.Headers.Location?.ToString();
                                        if (!string.IsNullOrEmpty(location) && location.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) {
                                            targetMagnet = location;
                                            if (!targetMagnet.Contains("&tr=")) {
                                                targetMagnet += "&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce" +
                                                                "&tr=udp%3A%2F%2Ftracker.openbittorrent.com%3A6969%2Fannounce" +
                                                                "&tr=udp%3A%2F%2Fexodus.desync.com%3A6969%2Fannounce" +
                                                                "&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce";
                                            }
                                            isHttp = false;
                                        }
                                    }
                                    
                                    if (isHttp) {
                                        var torrentBytes = await hc.GetByteArrayAsync(targetUrl);
                                        var torrent = await MonoTorrent.Torrent.LoadAsync(torrentBytes);
                                        
                                        manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes != null && t.InfoHashes.V1OrV2 == torrent.InfoHashes.V1OrV2);
                                        if (manager == null) {
                                            manager = await _engine.AddStreamingAsync(torrent, downloadPath, new TorrentSettingsBuilder().ToSettings());
                                            await manager.StartAsync();
                                        }
                                    }
                                }

                                if (!isHttp) {
                                    var torrentInfo = MagnetLink.Parse(targetMagnet);
                                    
                                    manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes != null && t.InfoHashes.V1OrV2 == torrentInfo.InfoHashes.V1OrV2);
                                    if (manager == null) {
                                        manager = await _engine.AddStreamingAsync(torrentInfo, downloadPath, new TorrentSettingsBuilder().ToSettings());
                                        await manager.StartAsync();
                                    }
                                }
                                    _activeTorrents.TryAdd(sessionId!, manager);
                                }
                            } finally {
                                _addTorrentLock.Release();
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
                var cfg = Plugin.Instance.Configuration;
                int timeoutMs = (cfg.MetadataTimeoutSeconds > 0 ? cfg.MetadataTimeoutSeconds : 180) * 1000;
                while (!manager.HasMetadata && timeoutMs > 0)
                {
                    await Task.Delay(500);
                    timeoutMs -= 500;
                    if (timeoutMs % 5000 == 0) {
                        _logger.LogInformation("STREAM: Waiting for metadata... {Remaining}s remaining. State: {State}, Peers: {Peers}", timeoutMs/1000, manager.State, manager.Peers.Available);
                    }
                }

                if (!manager.HasMetadata)
                {
                    _logger.LogError("STREAM: Timeout waiting for metadata.");
                    context.Response.StatusCode = 504; // Gateway Timeout
                    context.Response.Close();
                    return;
                }

                MonoTorrent.ITorrentManagerFile selectedFile = null;
                
                if (type == "episode" && int.TryParse(idStr, out int parsedEpisodeId))
                {
                    try {
                        if (!string.IsNullOrEmpty(config.SonarrUrl) && !string.IsNullOrEmpty(config.SonarrApiKey))
                        {
                            using var hc = new System.Net.Http.HttpClient();
                            var sonarr = new Jellyfin.Plugin.TorrentStreamer.Api.SonarrClient(hc, config.SonarrUrl, config.SonarrApiKey);
                            var episode = await sonarr.GetEpisode(parsedEpisodeId);
                            if (episode != null)
                            {
                                var sPad = episode.SeasonNumber.ToString("D2");
                                var ePad = episode.EpisodeNumber.ToString("D2");
                                var regexS = new System.Text.RegularExpressions.Regex($@"(s{sPad}e{ePad}|s{episode.SeasonNumber}e{episode.EpisodeNumber}|{episode.SeasonNumber}x{ePad}|{sPad}x{ePad})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
                                foreach (var file in manager.Files.OrderByDescending(f => f.Length))
                                {
                                    if (regexS.IsMatch(file.Path))
                                    {
                                        selectedFile = file;
                                        break;
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "STREAM: Error getting episode details.");
                    }
                }
                
                if (selectedFile == null) {
                    var videoExts = new[] { ".mkv", ".mp4", ".avi", ".ts", ".m4v", ".webm" };
                    selectedFile = manager.Files
                        .Where(f => videoExts.Contains(System.IO.Path.GetExtension(f.Path)?.ToLower()))
                        .OrderByDescending(f => f.Length)
                        .FirstOrDefault() ?? manager.Files.OrderByDescending(f => f.Length).FirstOrDefault();
                }

                if (selectedFile != null) {
                    _logger.LogInformation("STREAM: Selected file: {Path} ({Length} bytes)", selectedFile.Path, selectedFile.Length);
                    try {
                        foreach (var f in manager.Files) {
                            if (f.Path == selectedFile.Path) {
                                await manager.SetFilePriorityAsync(f, MonoTorrent.Priority.Highest);
                            } else {
                                await manager.SetFilePriorityAsync(f, MonoTorrent.Priority.DoNotDownload);
                            }
                        }
                    } catch (Exception px) {
                        _logger.LogError(px, "STREAM: Error setting priority.");
                    }
                } else {
                    _logger.LogError("STREAM: Torrent has no files!");
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                // Get stream from MonoTorrent which handles prioritizing pieces automatically
                using var stream = await manager.StreamProvider.CreateStreamAsync(selectedFile, CancellationToken.None);

                context.Response.ContentType = "video/mp4"; // Generic, Jellyfin will probe it
                context.Response.SendChunked = false;

                long start = 0;
                long end = selectedFile.Length - 1;

                if (!string.IsNullOrEmpty(context.Request.Headers["Range"]))
                {
                    var rangeHeader = context.Request.Headers["Range"].Replace("bytes=", "").Split('-');
                    start = long.Parse(rangeHeader[0]);
                    if (rangeHeader.Length > 1 && !string.IsNullOrEmpty(rangeHeader[1]))
                    {
                        end = long.Parse(rangeHeader[1]);
                    }

                    context.Response.StatusCode = 206; // Partial Content
                    context.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{selectedFile.Length}");
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
                try { context.Response.StatusCode = 500; } catch { }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task HandleQBittorrentRequestAsync(HttpListenerContext context, string? magnetLink, string? type, string? idStr)
        {
            try
            {
                if (string.IsNullOrEmpty(magnetLink))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                var config = Plugin.Instance.Configuration;
                using var hc = new System.Net.Http.HttpClient();
                var qbClient = new Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentClient(hc, config.QBittorrentUrl, config.QBittorrentUsername, config.QBittorrentPassword);
                
                if (!await qbClient.LoginAsync())
                {
                    _logger.LogError("Failed to login to qBittorrent.");
                    context.Response.StatusCode = 500;
                    return;
                }

                var hash = Jellyfin.Plugin.TorrentStreamer.Api.MagnetHelper.ExtractInfoHash(magnetLink);
                if (string.IsNullOrEmpty(hash))
                {
                    _logger.LogError("Failed to extract InfoHash from magnet link.");
                    context.Response.StatusCode = 400;
                    return;
                }

                await qbClient.AddMagnetAsync(magnetLink, config.QBittorrentSavePath);

                Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentFile[]? files = null;
                int timeoutMs = (config.MetadataTimeoutSeconds > 0 ? config.MetadataTimeoutSeconds : 180) * 1000;
                
                while (timeoutMs > 0)
                {
                    files = await qbClient.GetTorrentFilesAsync(hash);
                    if (files != null && files.Length > 0) break;
                    
                    await Task.Delay(1000);
                    timeoutMs -= 1000;
                }

                if (files == null || files.Length == 0)
                {
                    _logger.LogError("Timeout waiting for qBittorrent metadata.");
                    context.Response.StatusCode = 504;
                    return;
                }

                Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentFile? selectedFile = null;
                if (type == "episode" && int.TryParse(idStr, out int parsedEpisodeId))
                {
                    try {
                        if (!string.IsNullOrEmpty(config.SonarrUrl) && !string.IsNullOrEmpty(config.SonarrApiKey))
                        {
                            using var sonarrHc = new System.Net.Http.HttpClient();
                            var sonarr = new Jellyfin.Plugin.TorrentStreamer.Api.SonarrClient(sonarrHc, config.SonarrUrl, config.SonarrApiKey);
                            var episode = await sonarr.GetEpisode(parsedEpisodeId);
                            if (episode != null)
                            {
                                var sPad = episode.SeasonNumber.ToString("D2");
                                var ePad = episode.EpisodeNumber.ToString("D2");
                                var regexS = new System.Text.RegularExpressions.Regex($@"(s{sPad}e{ePad}|s{episode.SeasonNumber}e{episode.EpisodeNumber}|{episode.SeasonNumber}x{ePad}|{sPad}x{ePad})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
                                foreach (var file in files.OrderByDescending(f => f.Size))
                                {
                                    if (regexS.IsMatch(file.Name))
                                    {
                                        selectedFile = file;
                                        break;
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error getting episode details from Sonarr.");
                    }
                }

                if (selectedFile == null) {
                    var videoExts = new[] { ".mkv", ".mp4", ".avi", ".ts", ".m4v", ".webm" };
                    selectedFile = files
                        .Where(f => videoExts.Contains(System.IO.Path.GetExtension(f.Name)?.ToLower()))
                        .OrderByDescending(f => f.Size)
                        .FirstOrDefault() ?? files.OrderByDescending(f => f.Size).FirstOrDefault();
                }

                if (selectedFile == null)
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                var props = await qbClient.GetTorrentPropertiesAsync(hash);
                if (props == null || string.IsNullOrEmpty(props.SavePath))
                {
                    _logger.LogError("Could not get save path from qBittorrent.");
                    context.Response.StatusCode = 500;
                    return;
                }

                string fullPath = Path.Combine(props.SavePath, selectedFile.Name);
                
                // Wait for the physical file to be created by qBittorrent
                int fileWaitMs = 60000;
                while (!File.Exists(fullPath) && fileWaitMs > 0)
                {
                    await Task.Delay(1000);
                    fileWaitMs -= 1000;
                }

                if (!File.Exists(fullPath))
                {
                    _logger.LogError($"File does not exist on disk: {fullPath}");
                    context.Response.StatusCode = 404;
                    return;
                }

                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
                using var qbStream = new Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentStream(fs, qbClient, hash, selectedFile.Name, selectedFile.Size);

                context.Response.ContentType = "video/mp4"; 
                context.Response.SendChunked = false;

                long start = 0;
                long end = selectedFile.Size - 1;

                if (!string.IsNullOrEmpty(context.Request.Headers["Range"]))
                {
                    var rangeHeader = context.Request.Headers["Range"].Replace("bytes=", "").Split('-');
                    start = long.Parse(rangeHeader[0]);
                    if (rangeHeader.Length > 1 && !string.IsNullOrEmpty(rangeHeader[1]))
                    {
                        end = long.Parse(rangeHeader[1]);
                    }

                    context.Response.StatusCode = 206; 
                    context.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{selectedFile.Size}");
                }
                else
                {
                    context.Response.StatusCode = 200;
                }

                context.Response.ContentLength64 = end - start + 1;
                context.Response.AddHeader("Accept-Ranges", "bytes");

                qbStream.Seek(start, SeekOrigin.Begin);

                byte[] buffer = new byte[81920]; 
                long bytesRemaining = context.Response.ContentLength64;
                int bytesRead;

                while (bytesRemaining > 0 && (bytesRead = await qbStream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, bytesRemaining), CancellationToken.None)) > 0)
                {
                    await context.Response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "qBittorrent stream failed.");
                try { context.Response.StatusCode = 500; } catch { }
            }
            finally
            {
                context.Response.Close();
            }
        }

        public async void SetGlobalUploadLimit(int throttleKBps)
        {
            try {
                if (_engine != null && _engine.Settings != null)
                {
                    var newSettings = new EngineSettingsBuilder(_engine.Settings)
                    {
                        MaximumUploadRate = throttleKBps > 0 ? throttleKBps * 1024 : 0
                    }.ToSettings();
                    await _engine.UpdateSettingsAsync(newSettings);
                }
                
                var config = Plugin.Instance.Configuration;
                if (config.UseQBittorrent && !string.IsNullOrEmpty(config.QBittorrentUrl))
                {
                    using var hc = new System.Net.Http.HttpClient();
                    var qbClient = new Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentClient(hc, config.QBittorrentUrl, config.QBittorrentUsername, config.QBittorrentPassword);
                    if (await qbClient.LoginAsync())
                    {
                        await qbClient.SetUploadLimitAsync(throttleKBps > 0 ? throttleKBps * 1024 : 0);
                    }
                }
            } catch { }
        }

        public async void PurgeTorrent(string magnetOrUrl)
        {
            try {
                var hash = Jellyfin.Plugin.TorrentStreamer.Api.MagnetHelper.ExtractInfoHash(magnetOrUrl);
                if (string.IsNullOrEmpty(hash)) return;

                var tm = _engine.Torrents.FirstOrDefault(t => t.InfoHashes != null && (t.InfoHashes.V1?.ToHex()?.Equals(hash, StringComparison.OrdinalIgnoreCase) == true || t.InfoHashes.V2?.ToHex()?.Equals(hash, StringComparison.OrdinalIgnoreCase) == true));
                if (tm != null)
                {
                    await tm.StopAsync();
                    await _engine.RemoveAsync(tm, MonoTorrent.Client.RemoveMode.CacheDataAndDownloadedData);
                }

                var config = Plugin.Instance.Configuration;
                if (config.UseQBittorrent && !string.IsNullOrEmpty(config.QBittorrentUrl))
                {
                    using var hc = new System.Net.Http.HttpClient();
                    var qbClient = new Jellyfin.Plugin.TorrentStreamer.Api.QBittorrentClient(hc, config.QBittorrentUrl, config.QBittorrentUsername, config.QBittorrentPassword);
                    if (await qbClient.LoginAsync())
                    {
                        await qbClient.DeleteTorrentAsync(hash, true);
                    }
                }
            } catch { }
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
