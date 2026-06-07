using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    public class QBittorrentClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private string _cookie;

        public QBittorrentClient(HttpClient httpClient, string baseUrl, string username, string password)
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
        }

        public async Task<bool> LoginAsync()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", _username),
                new KeyValuePair<string, string>("password", _password)
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v2/auth/login")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body.Trim() == "Ok.")
                {
                    if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    {
                        foreach (var cookie in cookies)
                        {
                            if (cookie.StartsWith("SID="))
                            {
                                _cookie = cookie.Split(';')[0];
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
            if (!string.IsNullOrEmpty(_cookie))
            {
                request.Headers.Add("Cookie", _cookie);
            }
            return request;
        }

        public async Task<bool> AddMagnetAsync(string magnet, string savePath)
        {
            var data = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("urls", magnet),
                new KeyValuePair<string, string>("sequentialDownload", "true"),
                new KeyValuePair<string, string>("firstLastPiecePrio", "true"),
                new KeyValuePair<string, string>("paused", "false")
            };

            if (!string.IsNullOrEmpty(savePath))
            {
                data.Add(new KeyValuePair<string, string>("savepath", savePath));
            }

            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/add");
            request.Content = new FormUrlEncodedContent(data);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        public async Task<QBittorrentProperties> GetTorrentPropertiesAsync(string hash)
        {
            var request = CreateRequest(HttpMethod.Get, $"/api/v2/torrents/properties?hash={hash}");
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content) || content == "Not Found") return null;

            return JsonSerializer.Deserialize<QBittorrentProperties>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<QBittorrentFile[]> GetTorrentFilesAsync(string hash)
        {
            var request = CreateRequest(HttpMethod.Get, $"/api/v2/torrents/files?hash={hash}");
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content) || content == "Not Found") return null;

            return JsonSerializer.Deserialize<QBittorrentFile[]>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles)
        {
            var data = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
            };

            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/delete");
            request.Content = new FormUrlEncodedContent(data);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SetUploadLimitAsync(long limitBytesPerSecond)
        {
            var data = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("limit", limitBytesPerSecond.ToString())
            };

            var request = CreateRequest(HttpMethod.Post, "/api/v2/transfer/setUploadLimit");
            request.Content = new FormUrlEncodedContent(data);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
    }

    public class QBittorrentProperties
    {
        [JsonPropertyName("save_path")]
        public string SavePath { get; set; }

        public string Name { get; set; }
    }

    public class QBittorrentFile
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public double Progress { get; set; }
    }

    public static class MagnetHelper
    {
        public static string ExtractInfoHash(string magnet)
        {
            if (string.IsNullOrEmpty(magnet)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(magnet, @"xt=urn:btih:([a-zA-Z0-9]+)");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
    }
}
