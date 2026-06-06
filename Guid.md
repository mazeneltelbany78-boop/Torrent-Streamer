# 🚀 Torrent Streamer for Jellyfin - Setup Guide

Welcome to the Torrent Streamer plugin! This guide will walk you through the entire process of installing and configuring the plugin from scratch. Even if you only have basic experience with Jellyfin, you'll have this up and running in no time.

---

## Step 1: Add the Plugin Repository to Jellyfin

Jellyfin needs to know where to download the plugin from. We do this by adding a "Repository" link.

1. Open your Jellyfin web interface.
2. Go to **Dashboard** (Admin section).
3. On the left sidebar, scroll down and click on **Plugins**.
4. Click on the **Repositories** tab at the top.
5. Click the **+ (Add)** button.
6. Fill in the details:
   - **Repository Name:** `Torrent Streamer`
   - **Repository URL:** `https://raw.githubusercontent.com/mazeneltelbany78-boop/Torrent-Streamer/main/manifest.json`
7. Click **Save**.

## Step 2: Install the Plugin

Now that Jellyfin knows where the plugin is, let's install it!

1. Still in the **Plugins** section, click on the **Catalog** tab.
2. Scroll down until you find **Torrent Streamer** (it might be under a "General" or "Media" category).
3. Click on it, select the latest version, and click **Install**.
4. Once the installation is complete, **Restart your Jellyfin server** so the plugin can load properly.

> [!IMPORTANT]
> The plugin will not appear in your "My Plugins" tab until you fully restart the Jellyfin server.

## Step 3: Configure the Plugin

To magically stream movies and shows, the plugin needs to talk to your Radarr, Sonarr, and Prowlarr setups.

1. Go back to **Dashboard** -> **Plugins**.
2. Click on the **My Plugins** tab.
3. Click on **Torrent Streamer** to open its settings.
4. Fill in the following information:
   - **Download Directory:** The folder where torrents will be temporarily downloaded while you watch them (e.g., `/config/data/TorrentStreams` or `C:\Temp\Torrents`). Make sure Jellyfin has permission to write to this folder!
   - **Radarr URL:** Your Radarr address (e.g., `http://192.168.1.50:7878`).
   - **Radarr API Key:** Found in Radarr -> Settings -> General.
   - **Sonarr URL:** Your Sonarr address (e.g., `http://192.168.1.50:8989`).
   - **Sonarr API Key:** Found in Sonarr -> Settings -> General.
   - **Prowlarr API Key:** Found in Prowlarr -> Settings -> General.
5. Click **Save**.

## Step 4: Sync Your Library

The plugin works by reading your Radarr and Sonarr databases and creating "virtual" media files in Jellyfin. When you click play on these virtual files, it streams the torrent!

1. In the Jellyfin **Dashboard**, go to **Scheduled Tasks** (on the left sidebar).
2. Look for a task named **Torrent Streams Sync**.
3. Click the **Play** button next to it to run it manually for the first time.
4. *(Optional but Recommended)* Click on the task name and add a "Trigger" to make it run automatically every 12 hours.

> [!TIP]
> The sync process might take a few minutes if you have a massive library. Once it finishes, you will see your missing movies and episodes appear in your Jellyfin libraries!

## Step 5: Grab the Popcorn! 🍿

You are completely finished! 

When you browse your Jellyfin library, you will now see movies and episodes that you haven't actually downloaded yet. When you click **Play**, the plugin will:
1. Ask Prowlarr for the absolute best torrent available.
2. Start streaming the file instantly into Jellyfin.
3. Automatically delete the temporary file when it's done (or based on your settings).

### Troubleshooting
- **"Playback Error" on the first try?** Sometimes the very first time you play a brand new movie, it takes 15-20 seconds to search the torrent indexers and find peers, which causes Jellyfin to time out. Simply **click play again** and it will load instantly!
- **Nothing is syncing?** Double check your API keys and URLs in the plugin settings. Make sure there are no trailing slashes (e.g., use `http://192.168.1.50:7878` instead of `http://192.168.1.50:7878/`).
