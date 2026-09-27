# Sørby Gaming showreel

25 sekunders motion graphics-video (1920×1080, 60 fps, cirka 17,5 MB) med selvkomponeret lydspor.
Animationen er tegnet i en `<canvas>` frame for frame, så hvert billede bliver ens, hver gang du renderer.

## Filer

| Fil | Hvad |
| --- | --- |
| `reel.js` | Hele animationen: scener, tidslinje, effekter og overgange |
| `index.html` | Side der indlæser `reel.js` |
| `audio.js` | Syntetiserer lydsporet (`audio.wav`) i Node, synkroniseret til de samme tidspunkter som animationen |
| `server.js` | Lokal server på port 8765: serverer siden og gemmer frames fra Chrome i `frames/` |
| `render.ps1` | Kører hele processen: lyd, frames, NVENC-encoding og samling til MP4 |
| `assets/` | `bg.jpg` (standardbaggrunden fra `web/public`) og `logo256.png` (fra `app.ico`) |

## Render videoen

```powershell
cd B:\SorbyGaming\pc-client\video-reel
.\render.ps1
```

Resultatet lægges i `SorbyGaming_Showreel.mp4` i denne mappe (git ignorerer den). Brug `-SkipFrames` for kun at encode igen
(fx med en anden `-Bitrate`), og `-Output` for at give filen et andet navn.

Kræver: Node, Google Chrome, en ffmpeg med `h264_nvenc` (NVIDIA-grafikkort; SteelSeries GG's ffmpeg har den)
og en ffmpeg der kan læse WAV og encode AAC (CEWE's ffmpeg). Stierne kan ændres med parametrene i toppen af `render.ps1`.

## Se og rette animationen

Start serveren (`node server.js`) og åbn en af disse adresser i browseren:

- `http://localhost:8765/?t=9.05` viser ét billede ved 9,05 sekunder
- `http://localhost:8765/?sheet=7.5,11.5,16` viser 16 billeder i et gitter (kontaktark)
- `http://localhost:8765/?play` afspiller i realtid (uden lyd)

## Tidslinje (120 BPM, klip på beatet)

| Tid | Scene | Funktion i `reel.js` |
| --- | --- | --- |
| 0–3,5 s | Intro, logo-impact, zoom gennem Ø'et | `sIntro` |
| 3,5–7,5 | 01 SCAN: lock screen med QR | `sScan` |
| 7,5–11,5 | 02 SVAR: telefon og spørgeskema | `sSvar` |
| 11,5–15 | 03 SPIL: oplåsning, spil, timer | `sSpil` |
| 15–18,5 | Fuldt overblik: PC-dashboard | `sDash` |
| 18,5–21,5 | Alt i tal: statistik | `sStats` |
| 21,5–25 | Outro: "SCAN. SVAR. SPIL." | `sOutro` |

Ændrer du et tidspunkt i `reel.js`, skal den tilsvarende lydeffekt i `audio.js` (afsnittet `sfx`) flyttes med.
Tallene i statistik og dashboard er demotal.
