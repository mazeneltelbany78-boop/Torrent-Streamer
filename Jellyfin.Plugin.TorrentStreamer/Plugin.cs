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

        public override Guid Id => Guid.Parse("f9e7b231-15cf-4822-ba6e-7164b3c4f7a2");

        public static Plugin? Instance { get; private set; }

        public Jellyfin.Plugin.TorrentStreamer.Streaming.TorrentStreamService StreamService { get; private set; }
        public BackgroundSyncService SyncService { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            
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
            }
            catch (Exception ex)
            {
                log($"CRITICAL ERROR: {ex.Message} \n {ex.StackTrace}");
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }
}
