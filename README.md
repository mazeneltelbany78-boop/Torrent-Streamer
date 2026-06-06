# Torrent Streamer for Jellyfin

Torrent Streamer is a Jellyfin plugin that seamlessly integrates your Sonarr and Radarr libraries, allowing you to instantly stream movies and TV shows directly from torrents without waiting for the full download to complete.

## Features
- **Instant Streaming**: Stream movies and episodes instantly via built-in MonoTorrent or external qBittorrent.
- **qBittorrent Integration**: Supercharge your streaming speeds by offloading downloading to an external qBittorrent instance (highly recommended for 4K files).
- **Auto-Resolution**: Automatically queries Radarr/Sonarr/Prowlarr for the best available magnet link when you click Play.
- **Custom Trackers**: Automatically appends the best public trackers to ensure fast peer discovery.

## Installation
1. Go to your Jellyfin Dashboard > **Plugins** > **Repositories**.
2. Add a new repository with the following URL:
   `https://raw.githubusercontent.com/mazeneltelbany78-boop/Torrent-Streamer/main/manifest.json`
3. Go to the **Catalogs** tab and install **Torrent Streamer**.
4. Restart your Jellyfin server.

## Configuration

Go to your Jellyfin Dashboard > **Plugins** > **Torrent Streamer**.

### Indexer Setup (Required)
You must configure at least one *arr app for the plugin to resolve torrents.
- **Radarr URL / API Key**: For movies.
- **Sonarr URL / API Key**: For TV shows.
- **Prowlarr URL / API Key**: For broader fallback resolution (optional but recommended).

### qBittorrent Integration (Recommended for Speed & Stability)
For the best experience, enable qBittorrent mode. 
1. Check the **Use qBittorrent** box.
2. Enter your qBittorrent Web UI URL, username, and password.
3. **Crucial Limit**: The plugin serves the stream directly from the downloaded file on your disk. This means Jellyfin *must* be able to access the exact folder qBittorrent is saving the files to. If using Docker, ensure both containers map the same volume (e.g., `/downloads`).

### Built-in Engine Fallback
If you leave qBittorrent disabled, the plugin uses a built-in C# engine (MonoTorrent). This requires zero setup but may be slower to initialize and find peers compared to qBittorrent.

## Troubleshooting
If a stream fails to start:
- Check your Sonarr/Radarr connection. The plugin needs these to resolve the video file.
- Ensure the torrent has seeders.
- If using qBittorrent, check that Jellyfin has read/write permissions to the `qBittorrent Save Path`.

## Disclaimer
This plugin is provided as-is. Please ensure you comply with your local laws regarding copyright and torrenting.
