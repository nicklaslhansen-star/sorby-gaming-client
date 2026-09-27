# Renders the Sørby Gaming showreel: frames (headless Chrome) + audio (Node) -> MP4.
# Usage:  .\render.ps1            (full render)
#         .\render.ps1 -SkipFrames (re-encode only, reuse existing frames\)
param(
  [string]$Chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe",
  # Needs h264_nvenc (NVIDIA). The SteelSeries GG build has it.
  [string]$FfmpegVideo = "C:\Program Files\SteelSeries\GG\apps\moments\ffmpeg.exe",
  # Needs the wav demuxer and aac encoder. The CEWE build has them.
  [string]$FfmpegAudio = "C:\Program Files\CEWE\CEWE.DK Fotoprogram\ffmpeg.exe",
  [string]$Bitrate = "5.5M",
  [string]$Output = "SorbyGaming_Showreel.mp4",
  [switch]$SkipFrames
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
Set-Location $here

Write-Host "Lyd: genererer audio.wav..."
node audio.js
if (-not $?) { throw "audio.js fejlede" }

if (-not $SkipFrames) {
  Remove-Item -Recurse -Force "$here\frames", "$here\done.txt", "$here\.chrome-profile" -ErrorAction SilentlyContinue
  $server = Start-Process node -ArgumentList "server.js" -WorkingDirectory $here -PassThru -WindowStyle Hidden
  try {
    Start-Sleep -Seconds 1
    Write-Host "Billeder: renderer 1500 frames i headless Chrome (ca. 5 min)..."
    $chromeArgs = @("--headless=new", "--user-data-dir=$here\.chrome-profile", "--disable-background-timer-throttling",
      "--disable-renderer-backgrounding", "--window-size=1920,1080", "http://localhost:8765/?render=1")
    $chromeProc = Start-Process $Chrome -ArgumentList $chromeArgs -PassThru
    while (-not (Test-Path "$here\done.txt")) {
      Start-Sleep -Seconds 5
      $n = (Get-ChildItem "$here\frames" -Filter *.jpg -ErrorAction SilentlyContinue | Measure-Object).Count
      Write-Host "  $n / 1500"
    }
    try { Stop-Process -Id $chromeProc.Id -Force -ErrorAction Stop } catch {}
  } finally {
    try { Stop-Process -Id $server.Id -Force -ErrorAction Stop } catch {}
  }
}

Write-Host "Video: encoder med NVENC ($Bitrate)..."
& $FfmpegVideo -hide_banner -loglevel error -framerate 60 -i "$here\frames\%05d.jpg" `
  -vf "scale=in_range=full:out_range=tv:out_color_matrix=bt709,format=yuv420p" `
  -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 `
  -c:v h264_nvenc -preset p7 -tune hq -profile:v high -rc vbr -b:v $Bitrate -maxrate 11M -bufsize 11M `
  -multipass fullres -spatial-aq 1 -temporal-aq 1 -aq-strength 8 -bf 3 -g 120 -y "$here\video.mp4"
if (-not $?) { throw "video-encoding fejlede" }

Write-Host "Samler video og lyd..."
& $FfmpegAudio -hide_banner -loglevel error -i "$here\video.mp4" -i "$here\audio.wav" -map 0:v -map 1:a `
  -c:v copy -c:a aac -b:a 192k -movflags +faststart -shortest -y (Join-Path $here $Output)
if (-not $?) { throw "muxing fejlede" }

$f = Get-Item (Join-Path $here $Output)
Write-Host ("Færdig: {0} ({1:N2} MB)" -f $f.FullName, ($f.Length / 1MB))
