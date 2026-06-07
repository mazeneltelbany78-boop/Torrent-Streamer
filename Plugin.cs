using System;
using System.Collections.Generic;
using Jellyfin.Plugin.TorrentStreamer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentStreamer
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Torrent Streamer";

        public override Guid Id => Guid.Parse("b5d8dc02-b797-485b-9f53-c9e95689d5bd");

        public static Plugin? Instance { get; private set; }

        public Jellyfin.Plugin.TorrentStreamer.Streaming.TorrentStreamService StreamService { get; private set; }
        public BackgroundSyncService SyncService { get; private set; }

        public MediaBrowser.Controller.Library.ILibraryManager LibraryManager { get; private set; }
        public MediaBrowser.Controller.Session.ISessionManager SessionManager { get; private set; }
        public MediaBrowser.Controller.Library.IUserDataManager UserDataManager { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger, MediaBrowser.Controller.Library.ILibraryManager libraryManager, MediaBrowser.Controller.Session.ISessionManager sessionManager, MediaBrowser.Controller.Library.IUserDataManager userDataManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            LibraryManager = libraryManager;
            SessionManager = sessionManager;
            UserDataManager = userDataManager;
            
            var logFile = "/config/data/TorrentStreams/plugin_debug.log";
            Action<string> log = (msg) => {
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now}] PLUGIN LOAD: {msg}\n"); } catch { }
            };

            try
            {
                log("Initializing TorrentStreamService");
                StreamService = new Jellyfin.Plugin.TorrentStreamer.Streaming.TorrentStreamService(logger);
                _ = StreamService.StartAsync();
                
                log("Initializing BackgroundSyncService");
                SyncService = new BackgroundSyncService(logger);
                SyncService.Start();
                log("Plugin successfully initialized.");

                SessionManager.PlaybackStart += SessionManager_PlaybackStart;
                SessionManager.PlaybackStopped += SessionManager_PlaybackStopped;
                UserDataManager.UserDataSaved += UserDataManager_UserDataSaved;
            }
            catch (Exception ex)
            {
                log($"CRITICAL ERROR: {ex.Message} \n {ex.StackTrace}");
            }
        }

        private void SessionManager_PlaybackStart(object sender, MediaBrowser.Controller.Library.PlaybackProgressEventArgs e)
        {
            try {
                StreamService?.SetGlobalUploadLimit(1);

                if (e.Item != null && e.Item is MediaBrowser.Controller.Entities.TV.Episode episode)
                {
                    if (episode.Path != null && episode.Path.Contains("TorrentStreams") && episode.IndexNumber.HasValue)
                    {
                        var nextEpisode = System.Linq.Enumerable.FirstOrDefault(LibraryManager.GetItemsResult(new MediaBrowser.Controller.Entities.InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
                            ParentId = episode.SeriesId,
                            MinIndexNumber = episode.IndexNumber + 1,
                            Limit = 1
                        }).Items) as MediaBrowser.Controller.Entities.TV.Episode;

                        if (nextEpisode != null && nextEpisode.Path != null && nextEpisode.Path.Contains("TorrentStreams"))
                        {
                            var content = System.IO.File.ReadAllText(nextEpisode.Path);
                            if (content.StartsWith("http")) {
                                _ = System.Threading.Tasks.Task.Run(async () => {
                                    using var hc = new System.Net.Http.HttpClient();
                                    hc.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024 * 1024);
                                    try { await hc.GetAsync(content); } catch { }
                                });
                            }
                        }
                    }
                }
            } catch { }
        }

        private void SessionManager_PlaybackStopped(object sender, MediaBrowser.Controller.Library.PlaybackStopEventArgs e)
        {
            try {
                int defaultLimit = Configuration.UploadThrottleKBps;
                StreamService?.SetGlobalUploadLimit(defaultLimit);
            } catch { }
        }

        private void UserDataManager_UserDataSaved(object sender, MediaBrowser.Controller.Library.UserDataSaveEventArgs e)
        {
            try {
                if (e.UserData != null && e.UserData.Played)
                {
                    if (e.Item != null && e.Item.Path != null && e.Item.Path.Contains("TorrentStreams"))
                    {
                        if (!Configuration.KeepFilesAfterStreaming)
                        {
                            var content = System.IO.File.ReadAllText(e.Item.Path);
                            if (content.StartsWith("http"))
                            {
                                StreamService?.PurgeTorrent(content);
                            }
                        }
                    }
                }
            } catch { }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "TorrentStreamerConfig",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }
}
