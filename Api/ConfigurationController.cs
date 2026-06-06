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


        [HttpGet("Configuration")]
        [Produces("text/html")]
        public ActionResult GetConfiguration([FromQuery] bool saved = false)
        {
            try
            {
                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                string savedMessage = saved ? "<div class='toast slide-in'>Configuration Saved & Sync Triggered!</div>" : "";

                string html = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>Torrent Streamer Configuration</title>
    <link href='https://fonts.googleapis.com/css2?family=Inter:wght@300;400;600&display=swap' rel='stylesheet'>
    <style>
        :root {{
            --bg-dark: #0a0a0f;
            --glass-bg: rgba(255, 255, 255, 0.05);
            --glass-border: rgba(255, 255, 255, 0.1);
            --accent: #8b5cf6;
            --accent-hover: #7c3aed;
            --text-main: #f8fafc;
            --text-muted: #94a3b8;
            --input-bg: rgba(0, 0, 0, 0.3);
            --success: #10b981;
            --error: #ef4444;
        }}
        body {{
            margin: 0;
            padding: 0;
            font-family: 'Inter', sans-serif;
            background-color: var(--bg-dark);
            color: var(--text-main);
            display: flex;
            justify-content: center;
            align-items: flex-start;
            min-height: 100vh;
            background-image: 
                radial-gradient(circle at 15% 50%, rgba(139, 92, 246, 0.15), transparent 25%),
                radial-gradient(circle at 85% 30%, rgba(56, 189, 248, 0.15), transparent 25%);
            background-attachment: fixed;
        }}
        .container {{
            width: 100%;
            max-width: 650px;
            margin: 40px 20px;
            padding: 40px;
            background: var(--glass-bg);
            border: 1px solid var(--glass-border);
            border-radius: 24px;
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
            animation: fade-in 0.6s ease-out;
        }}
        h1 {{
            margin-top: 0;
            font-size: 2.2rem;
            font-weight: 600;
            background: linear-gradient(135deg, #a78bfa, #38bdf8);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 8px;
        }}
        p.subtitle {{
            color: var(--text-muted);
            margin-bottom: 32px;
            font-size: 0.95rem;
            line-height: 1.5;
        }}
        .form-group {{
            margin-bottom: 24px;
        }}
        label {{
            display: block;
            margin-bottom: 8px;
            font-size: 0.85rem;
            font-weight: 600;
            color: #cbd5e1;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }}
        input[type='text'], input[type='number'], input[type='password'] {{
            width: 100%;
            box-sizing: border-box;
            padding: 14px 16px;
            background: var(--input-bg);
            border: 1px solid var(--glass-border);
            border-radius: 12px;
            color: var(--text-main);
            font-size: 1rem;
            font-family: inherit;
            transition: all 0.2s ease;
        }}
        input[type='text']:focus, input[type='number']:focus, input[type='password']:focus {{
            outline: none;
            border-color: var(--accent);
            box-shadow: 0 0 0 3px rgba(139, 92, 246, 0.2);
            background: rgba(0, 0, 0, 0.5);
        }}
        .help-text {{
            font-size: 0.8rem;
            color: var(--text-muted);
            margin-top: 6px;
        }}
        button {{
            width: 100%;
            padding: 16px;
            background: linear-gradient(135deg, var(--accent), var(--accent-hover));
            border: none;
            border-radius: 12px;
            color: white;
            font-size: 1.1rem;
            font-weight: 600;
            cursor: pointer;
            transition: transform 0.2s ease, box-shadow 0.2s ease;
            margin-top: 16px;
            font-family: inherit;
        }}
        button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 10px 20px -10px rgba(139, 92, 246, 0.6);
        }}
        button.btn-secondary {{
            background: rgba(255,255,255,0.1);
            border: 1px solid var(--glass-border);
            margin-top: 8px;
            font-size: 0.9rem;
            padding: 10px;
        }}
        button.btn-secondary:hover {{
            background: rgba(255,255,255,0.15);
            box-shadow: none;
        }}
        .toast {{
            position: fixed;
            bottom: 30px;
            left: 50%;
            transform: translateX(-50%);
            background: linear-gradient(135deg, #10b981, #059669);
            color: white;
            padding: 14px 28px;
            border-radius: 99px;
            font-weight: 600;
            box-shadow: 0 10px 25px -5px rgba(16, 185, 129, 0.4);
            z-index: 1000;
        }}
        .test-result {{
            display: none;
            padding: 10px;
            margin-top: 8px;
            border-radius: 8px;
            font-size: 0.9rem;
            font-weight: 600;
        }}
        .test-success {{ display: block; background: rgba(16, 185, 129, 0.2); color: var(--success); border: 1px solid rgba(16, 185, 129, 0.3); }}
        .test-error {{ display: block; background: rgba(239, 68, 68, 0.2); color: var(--error); border: 1px solid rgba(239, 68, 68, 0.3); }}
        
        @keyframes fade-in {{ from {{ opacity: 0; transform: translateY(20px); }} to {{ opacity: 1; transform: translateY(0); }} }}
        .slide-in {{ animation: slide-up 0.5s cubic-bezier(0.16, 1, 0.3, 1) forwards, fade-out 0.5s ease 4s forwards; }}
        @keyframes slide-up {{ from {{ opacity: 0; transform: translate(-50%, 20px); }} to {{ opacity: 1; transform: translate(-50%, 0); }} }}
        @keyframes fade-out {{ to {{ opacity: 0; pointer-events: none; }} }}
    </style>
    <script>
        async function testConnection(type) {{
            const btn = document.getElementById('btn-test-' + type);
            const resultDiv = document.getElementById('result-' + type);
            const url = document.querySelector(`input[name=${{type}}Url]`).value;
            const key = document.querySelector(`input[name=${{type}}ApiKey]`).value;
            
            btn.innerText = 'Testing...';
            resultDiv.className = 'test-result';
            
            try {{
                const res = await fetch(`/TorrentStreamer/TestConnection?type=${{type}}&url=${{encodeURIComponent(url)}}&key=${{encodeURIComponent(key)}}`);
                const data = await res.json();
                
                if (data.success) {{
                    resultDiv.innerText = '✅ Connection Successful!';
                    resultDiv.className = 'test-result test-success';
                }} else {{
                    resultDiv.innerText = '❌ Failed: ' + data.message;
                    resultDiv.className = 'test-result test-error';
                }}
            }} catch(e) {{
                resultDiv.innerText = '❌ Request Failed.';
                resultDiv.className = 'test-result test-error';
            }}
            btn.innerText = 'Test Connection';
        }}
    </script>
</head>
<body>
    {savedMessage}
    <div class='container'>
        <h1>Torrent Streamer Settings</h1>
        <p class='subtitle'>Configure your Radarr and Sonarr instances to seamlessly stream movies and TV shows directly within Jellyfin.</p>
        
        <form action='/TorrentStreamer/Configuration' method='POST'>
            
            <div class='form-group'>
                <label>Radarr URL</label>
                <input type='text' name='radarrUrl' value='{config.RadarrUrl}' placeholder='http://10.0.1.2:7878' required>
            </div>
            <div class='form-group'>
                <label>Radarr API Key</label>
                <input type='text' name='radarrApiKey' value='{config.RadarrApiKey}' placeholder='Your Radarr API Key' required>
                <button type='button' id='btn-test-radarr' class='btn-secondary' onclick=""testConnection('radarr')"">Test Connection</button>
                <div id='result-radarr' class='test-result'></div>
            </div>
            
            <div class='form-group' style='margin-top: 40px;'>
                <label>Sonarr URL</label>
                <input type='text' name='sonarrUrl' value='{config.SonarrUrl}' placeholder='http://10.0.1.2:8989' required>
            </div>
            <div class='form-group'>
                <label>Sonarr API Key</label>
                <input type='text' name='sonarrApiKey' value='{config.SonarrApiKey}' placeholder='Your Sonarr API Key' required>
                <button type='button' id='btn-test-sonarr' class='btn-secondary' onclick=""testConnection('sonarr')"">Test Connection</button>
                <div id='result-sonarr' class='test-result'></div>
            </div>

            <div class='form-group' style='margin-top: 40px;'>
                <label>Server IP / URL</label>
                <input type='text' name='serverUrl' value='{config.ServerUrl}' placeholder='http://10.0.1.2:19420' required>
                <div class='help-text'>The IP or domain of your Jellyfin server with port 19420 (e.g., http://10.0.1.2:19420). Used in generated .strm files.</div>
            </div>

            <div class='form-group'>
                <label>Download Directory</label>
                <input type='text' name='downloadDirectory' value='{config.DownloadDirectory}' placeholder='/config/data/TorrentStreams' required>
                <div class='help-text'>Where the .strm files will be automatically generated.</div>
            </div>
            
            <div class='form-group'>
                <label>Upload Throttle (KB/s)</label>
                <input type='number' name='uploadThrottle' value='{config.UploadThrottleKBps}' placeholder='0' min='0'>
                <div class='help-text'>Set to 0 for unlimited upload speed.</div>
            </div>

            <button type='submit'>Save & Sync Now</button>
        </form>
    </div>
</body>
</html>
";
                return Content(html, "text/html");
            }
            catch (Exception ex)
            {
                return Content($"<html><body><h1>Error</h1><pre>{ex.Message}\n{ex.StackTrace}</pre></body></html>", "text/html");
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

        [HttpPost("Configuration")]
        [Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryToken]
        public ActionResult PostConfiguration([FromForm] string radarrUrl, [FromForm] string radarrApiKey, [FromForm] string sonarrUrl, [FromForm] string sonarrApiKey, [FromForm] string serverUrl, [FromForm] string downloadDirectory, [FromForm] int uploadThrottle)
        {
            var config = Plugin.Instance?.Configuration;
            if (config != null)
            {
                config.RadarrUrl = radarrUrl;
                config.RadarrApiKey = radarrApiKey;
                config.SonarrUrl = sonarrUrl;
                config.SonarrApiKey = sonarrApiKey;
                config.ServerUrl = serverUrl;
                config.DownloadDirectory = downloadDirectory;
                config.UploadThrottleKBps = uploadThrottle;

                Plugin.Instance?.SaveConfiguration();
                
                // Fire background sync manually so user doesn't wait
                _ = Task.Run(async () => {
                    try {
                        if (Plugin.Instance?.SyncService != null) {
                            await Plugin.Instance.SyncService.ForceSyncAsync();
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"Error forcing sync: {ex.Message}");
                    }
                });
            }

            return Redirect("/TorrentStreamer/Configuration?saved=true");
        }
    }
}
