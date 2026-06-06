using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    public class RadarrMovie
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }
    }

    public class RadarrClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public RadarrClient(HttpClient httpClient, string baseUrl, string apiKey)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
            _apiKey = apiKey;
        }

        public async Task<List<RadarrMovie>> GetMovies()
        {
            var requestUrl = $"api/v3/movie?apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<RadarrMovie>>(content) ?? new List<RadarrMovie>();
        }

        public async Task<List<ReleaseInfo>> GetReleases(int movieId)
        {
            var requestUrl = $"api/v3/release?movieId={movieId}&apikey={_apiKey}";
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ReleaseInfo>>(content) ?? new List<ReleaseInfo>();
        }
    }
}
