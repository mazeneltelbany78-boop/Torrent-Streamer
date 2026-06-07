param (
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$Changelog = "Minor update and bug fixes",

    [Parameter(Mandatory=$false)]
    [string]$GithubToken
)

$ErrorActionPreference = "Stop"

Write-Host "?? Starting Deployment for Torrent Streamer v$Version" -ForegroundColor Cyan

# 1. Update Project Version (.csproj)
Write-Host "Updating Jellyfin.Plugin.TorrentStreamer.csproj..."
$csprojPath = "Jellyfin.Plugin.TorrentStreamer.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
$csproj = $csproj -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$Version</FileVersion>"
Set-Content $csprojPath $csproj

# 2. Compile Release
Write-Host "Cleaning and Building Release..."
dotnet clean
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# 3. Zip the Payload
$zipPath = "Jellyfin.Plugin.TorrentStreamer_$Version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
Write-Host "Zipping Payload to $zipPath..."
Compress-Archive -Path "bin\Release\net8.0\*" -DestinationPath $zipPath
$hash = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToLower()
Write-Host "Payload MD5: $hash"

# 4. Update manifest.json
Write-Host "Updating manifest.json..."
$manifestPath = "manifest.json"
$manifest = Get-Content $manifestPath | ConvertFrom-Json
$manifest[0].versions[0].version = $Version
$manifest[0].versions[0].changelog = $Changelog
$manifest[0].versions[0].sourceUrl = "https://github.com/mazeneltelbany78-boop/Torrent-Streamer/releases/download/v$Version/Jellyfin.Plugin.TorrentStreamer_$Version.zip"
$manifest[0].versions[0].checksum = $hash
$manifest[0].versions[0].timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath

$content = Get-Content $manifestPath -Raw
if (-not $content.TrimStart().StartsWith("[")) {
    Set-Content $manifestPath "[$content]"
}

# 5. Git Commit
Write-Host "Committing changes to Git..."
$git = "C:\Program Files\Git\cmd\git.exe"
& $git add .
& $git commit -m "Release v${Version}: $Changelog"

# 6. GitHub Release (If Token provided)
if ($GithubToken) {
    Write-Host "Publishing to GitHub Releases..."
    $owner = "mazeneltelbany78-boop"
    $repo = "Torrent-Streamer"

    $headers = @{ "Authorization" = "token $GithubToken"; "Accept" = "application/vnd.github.v3+json" }
    $body = @{ tag_name = "v$Version"; name = "v$Version"; body = $Changelog } | ConvertTo-Json

    $releaseResp = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/releases" -Method Post -Headers $headers -Body $body
    $uploadUrl = $releaseResp.upload_url -replace '\{.*\}', "?name=Jellyfin.Plugin.TorrentStreamer_$Version.zip"

    $headersUpload = @{ "Authorization" = "token $GithubToken"; "Accept" = "application/vnd.github.v3+json"; "Content-Type" = "application/zip" }
    $uploadResp = Invoke-RestMethod -Uri $uploadUrl -Headers $headersUpload -Method Post -InFile $zipPath

    Write-Host "Pushing to GitHub..."
    & $git push origin main
    Write-Host "? Deployment Complete!" -ForegroundColor Green
} else {
    Write-Host "?? GitHub Token not provided. Skipping GitHub Release & Push." -ForegroundColor Yellow
}


