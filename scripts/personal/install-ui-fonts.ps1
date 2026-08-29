param(
    [Parameter(Mandatory = $true)][string]$RegularFont,
    [Parameter(Mandatory = $true)][string]$MediumFont,
    [Parameter(Mandatory = $true)][string]$BoldFont
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$targetDir = Join-Path $repoRoot 'src/PBM.Web/public/fonts'
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$fonts = @(
    @{ Source = $RegularFont; Target = 'iranyekanwebregular.woff' },
    @{ Source = $MediumFont; Target = 'iranyekanwebmedium.woff' },
    @{ Source = $BoldFont; Target = 'iranyekanwebbold.woff' }
)

foreach ($font in $fonts) {
    $source = (Resolve-Path $font.Source).Path
    if ([System.IO.Path]::GetExtension($source).ToLowerInvariant() -ne '.woff') {
        throw "Font must be a .woff file: $source"
    }
    $destination = Join-Path $targetDir $font.Target
    Copy-Item -Path $source -Destination $destination -Force
    Write-Host "Installed: $($font.Target)" -ForegroundColor Green
}

Write-Host "IranYekan UI fonts are ready in: $targetDir" -ForegroundColor Cyan
Write-Host 'Rebuild the web image after installing the fonts.' -ForegroundColor Yellow
