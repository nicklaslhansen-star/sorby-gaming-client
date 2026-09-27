# Udgiver en ny version af PC-klienten til GitHub Releases.
# PC'erne henter den automatisk, næste gang de står låst og har internet.
#
# Brug:
#   $env:GITHUB_TOKEN = "<dit token>"   # kun i den aktuelle PowerShell
#   .\publish.ps1 -Version 1.0.1
#
# Versionsnummeret skal være højere end den seneste udgivelse.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/nicklaslhansen-star/sorby-gaming-client"
$project = Join-Path $PSScriptRoot "SorbyGamingClient\SorbyGamingClient.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$releaseDir = Join-Path $PSScriptRoot "Releases"

# Tokenet hentes fra den krypterede lagring (tools\Secrets.ps1), hvis det
# ikke allerede er sat i denne PowerShell.
$secretsScript = Join-Path $PSScriptRoot "..\tools\Secrets.ps1"
if (-not $env:GITHUB_TOKEN -and (Test-Path $secretsScript)) {
    . $secretsScript
    $env:GITHUB_TOKEN = Get-SorbySecret GITHUB_TOKEN
}

if (-not $env:GITHUB_TOKEN) {
    throw "Intet GitHub-token. Gem det én gang med: . B:\SorbyGaming\tools\Secrets.ps1; Set-SorbySecret GITHUB_TOKEN"
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versionen skal have formen 1.2.3."
}

function Invoke-Step([string]$Description, [scriptblock]$Command) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description fejlede (exit code $LASTEXITCODE)."
    }
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

# Henter den seneste udgivelse, så vpk kan lave en lille delta-opdatering.
# Fejler første gang, hvor der endnu ikke findes udgivelser. Det er i orden.
Write-Host "==> Henter seneste udgivelse" -ForegroundColor Cyan
vpk download github --repoUrl $repoUrl --outputDir $releaseDir --token $env:GITHUB_TOKEN
if ($LASTEXITCODE -ne 0) {
    Write-Host "    Ingen tidligere udgivelse fundet - fortsætter." -ForegroundColor Yellow
}

Invoke-Step "Bygger version $Version" {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir
}

Invoke-Step "Pakker med Velopack" {
    vpk pack --packId SorbyGamingClient --packVersion $Version --packDir $publishDir `
        --mainExe SorbyGamingClient.exe --packTitle "Sorby Gaming" --outputDir $releaseDir
}

Invoke-Step "Uploader til GitHub Releases" {
    vpk upload github --repoUrl $repoUrl --outputDir $releaseDir --token $env:GITHUB_TOKEN `
        --publish --releaseName "Sorby Gaming $Version" --tag "v$Version"
}

Write-Host ""
Write-Host "Version $Version er udgivet." -ForegroundColor Green
Write-Host "Installationsfil til nye PC'er: $releaseDir\SorbyGamingClient-win-Setup.exe"
