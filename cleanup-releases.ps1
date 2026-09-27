# Sletter gamle udgivelser på GitHub og beholder kun de nyeste.
# Køres automatisk af publish.ps1, men kan også køres alene:
#   .\cleanup-releases.ps1            # beholder de 2 nyeste
#   .\cleanup-releases.ps1 -Keep 3
#
# Git-tags (v1.0.0 osv.) bevares, så historikken kan findes igen.

param(
    [int]$Keep = 2
)

$ErrorActionPreference = "Stop"

$repo = "nicklaslhansen-star/sorby-gaming-client"

if ($Keep -lt 2) {
    throw "Behold mindst 2 udgivelser."
}

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

$headers = @{
    Authorization          = "Bearer $env:GITHUB_TOKEN"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=100" -Headers $headers

# Kun færdige udgivelser tælles med; nyeste først efter versionsnummer.
$published = @($releases |
    Where-Object { -not $_.draft -and $_.tag_name -match '^v\d+\.\d+\.\d+$' } |
    Sort-Object { [version]($_.tag_name.TrimStart('v')) } -Descending)

if ($published.Count -le $Keep) {
    Write-Host "==> Ingen gamle udgivelser at slette ($($published.Count) i alt)." -ForegroundColor Cyan
    return
}

$kept = $published | Select-Object -First $Keep
$old = $published | Select-Object -Skip $Keep

Write-Host "==> Beholder: $(($kept | ForEach-Object tag_name) -join ', ')" -ForegroundColor Cyan

foreach ($release in $old) {
    Write-Host "    Sletter $($release.tag_name)" -ForegroundColor Yellow
    Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$repo/releases/$($release.id)" -Headers $headers | Out-Null
}

Write-Host "==> $($old.Count) gamle udgivelse(r) slettet." -ForegroundColor Green
