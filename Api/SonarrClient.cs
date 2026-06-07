using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    public class SonarrSeries
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }
    }

    public class SonarrEpisode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("seriesId")]
        public int SeriesId { get; set; }

        [JsonPropertyName("seasonNumber")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episodeNumber")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }
    }

    public class SonarrClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public SonarrClient(HttpClient httpClient, string baseUrl, string apiKey)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
            _apiKey = apiKey;
        }

        public async Task<List<SonarrSeries>> GetSeries()
        {
            var requestUrl = $"api/v3/series?apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<SonarrSeries>>(content) ?? new List<SonarrSeries>();
        }

        public async Task<List<SonarrEpisode>> GetEpisodes(int seriesId)
        {
            var requestUrl = $"api/v3/episode?seriesId={seriesId}&apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<SonarrEpisode>>(content) ?? new List<SonarrEpisode>();
        }

        public async Task<SonarrEpisode> GetEpisode(int episodeId)
        {
            var requestUrl = $"api/v3/episode/{episodeId}?apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SonarrEpisode>(content);
        }

        public async Task<List<ReleaseInfo>> GetReleases(int episodeId)
        {
            var requestUrl = $"api/v3/release?episodeId={episodeId}&apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ReleaseInfo>>(content) ?? new List<ReleaseInfo>();
        }
    }
}
