using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.TorrentStreamer.Api;

namespace Jellyfin.Plugin.TorrentStreamer.Channels
{
    public class TorrentStreamChannel : IChannel
    {
        private readonly ILogger<TorrentStreamChannel> _logger;
        private readonly RadarrClient _radarrClient;

        public TorrentStreamChannel(ILogger<TorrentStreamChannel> logger)
        {
            _logger = logger;
        }

        public string Name => "Torrent Streamer";
        public string Description => "Stream torrents from Radarr and Sonarr";
        public string DataVersion => "1.0";
        public string HomePageUrl => "";
        
        public ChannelParentalRating ParentalRating => (ChannelParentalRating)0;

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Movie,
                    ChannelMediaContentType.Episode
                },
                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },
                MaxPageSize = 100
            };
        }

        public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();

            items.Add(new ChannelItemInfo
            {
                Name = "Torrent Streamer is active. To use, add movies to Radarr and they will be streamable via .strm files if configured.",
                Id = "info_root",
                Type = ChannelItemType.Folder
            });

            var result = new ChannelItemResult
            {
                Items = items
            };

            return Task.FromResult(result);
        }

        public Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<ChannelItemInfo>>(new List<ChannelItemInfo>());
        }

        public bool IsEnabledFor(string userId)
        {
            return true;
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>();
        }
    }
}
