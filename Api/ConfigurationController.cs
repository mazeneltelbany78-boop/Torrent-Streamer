using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.TorrentStreamer;
using Jellyfin.Plugin.TorrentStreamer.Configuration;

namespace Jellyfin.Plugin.TorrentStreamer.Api
{
    [ApiController]
    [Route("TorrentStreamer")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class ConfigurationController : ControllerBase
    {
        [HttpGet("Status")]
        public ActionResult GetStatus()
        {
            try
            {
                var torrentService = Jellyfin.Plugin.TorrentStreamer.Streaming.TorrentStreamService.Instance;
                if (torrentService == null) return Content("[]", "application/json");

                var activeTorrents = torrentService.GetActiveTorrents(); // Needs implementation in TorrentStreamService

                // serialize object
                string json = System.Text.Json.JsonSerializer.Serialize(activeTorrents);
                return Content(json, "application/json");
            }
            catch (Exception)
            {
                return Content("[]", "application/json");
            }
        }

        [HttpGet("TestConnection")]
        public async Task<ActionResult> TestConnection([FromQuery] string type, [FromQuery] string url, [FromQuery] string key)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
                
                string endpoint = type.ToLower() == "radarr" ? "/api/v3/system/status" : "/api/v3/system/status";
                string fullUrl = url.TrimEnd('/') + endpoint;

                var res = await client.GetAsync(fullUrl);
                if (res.IsSuccessStatusCode)
                {
                    return Content("{\"success\": true}", "application/json");
                }
                else
                {
                    return Content($"{{\"success\": false, \"message\": \"HTTP {res.StatusCode}\"}}", "application/json");
                }
            }
            catch (Exception ex)
            {
                return Content($"{{\"success\": false, \"message\": \"{ex.Message}\"}}", "application/json");
            }
        }
    }
}
