# Jellyfin Torrent Streamer

A Jellyfin plugin that seamlessly integrates your Sonarr and Radarr libraries, allowing you to instantly stream movies and TV shows directly from torrents via the BitTorrent network. Say goodbye to waiting for downloads to complete!

## Features
* **Instant Streaming:** Uses a custom MonoTorrent engine to prioritize video file pieces, allowing playback to begin in seconds.
* **Sonarr & Radarr Integration:** Automatically syncs your wanted media and generates native Jellyfin `.strm` files.
* **Prowlarr Support:** Automatically securely resolves magnet links and `.torrent` files using InfoHashes, seamlessly bypassing proxy and authentication barriers.
* **Native Jellyfin Experience:** Your streamed torrents appear exactly like local media in your Jellyfin dashboard, complete with metadata, posters, and watch tracking.
* **Transcoding Support:** Fully supports Jellyfin's built-in transcoding and Direct Play capabilities.

## How it Works
1. The plugin periodically queries your Sonarr and Radarr servers for monitored content.
2. It generates lightweight `.strm` files in your Jellyfin library folders.
3. When you click play in Jellyfin, the plugin dynamically queries the indexers for the best available release, connects to the swarm, and begins streaming the media instantly.

## Configuration
Once installed, configure the plugin via the Jellyfin Dashboard:
- Enter your **Radarr**, **Sonarr**, and **Prowlarr** URLs and API Keys.
- Select a temporary download/cache directory.
- Run a library scan!
still in beta 
