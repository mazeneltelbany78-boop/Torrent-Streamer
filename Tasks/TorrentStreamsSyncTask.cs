using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorrentStreamer.Tasks
{
    public class TorrentStreamsSyncTask : IScheduledTask
    {
        private readonly ILogger<TorrentStreamsSyncTask> _logger;

        public TorrentStreamsSyncTask(ILogger<TorrentStreamsSyncTask> logger)
        {
            _logger = logger;
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Torrent Streams Library Sync from Scheduled Tasks");
            
            if (Plugin.Instance?.SyncService != null)
            {
                progress.Report(10);
                await Plugin.Instance.SyncService.ForceSyncAsync();
                progress.Report(100);
            }
            else
            {
                _logger.LogWarning("SyncService is not initialized yet.");
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(12).Ticks
                }
            };
        }

        public string Name => "Sync Torrent Streams Library";

        public string Key => "TorrentStreamsSyncTask";

        public string Description => "Syncs movies and series from Radarr and Sonarr into virtual Jellyfin stream files.";

        public string Category => "Torrent Streamer";
    }
}
