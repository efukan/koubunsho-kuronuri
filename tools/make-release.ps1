# Builds the distribution ZIP (ARCHITECTURE §1.2):
#   Kuronuri.exe, Kuronuri.exe.config, Kuronuri.Core.dll, pdfium.dll, settings.json,
#   LICENSE, THIRD-PARTY-NOTICES.txt, 利用の手引き.txt
# Usage (repository root):  pwsh tools/make-release.ps1 [-SkipTests]
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$PdfiumSha256 = "ba884cc5c6f2c3cd7c2ecca9dce36c2e341f78eb586abd691a2bbc017aa9871e"
$PdfiumBytes = 7830016

Write-Output "== forbidden-API check"
& pwsh -NoProfile -File (Join-Path $PSScriptRoot "check-forbidden.ps1")
if ($LASTEXITCODE -ne 0) { throw "check-forbidden failed" }

Write-Output "== build"
dotnet build Kuronuri.sln -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if (-not $SkipTests) {
    Write-Output "== test"
    dotnet test Kuronuri.sln -c Release --nologo --no-build -v quiet
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

$bin = Join-Path $root "src/Kuronuri.App/bin/Release"
$version = (Get-Item (Join-Path $bin "Kuronuri.exe")).VersionInfo.ProductVersion
if (-not $version) { $version = "1.0.0" }
$version = ($version -split '\+')[0]

$pdfium = Get-Item (Join-Path $bin "pdfium.dll")
$sha = (Get-FileHash $pdfium.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pdfium.Length -ne $PdfiumBytes -or $sha -ne $PdfiumSha256) { throw "pdfium.dll does not match the pinned build (size $($pdfium.Length), sha256 $sha)" }

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "kuronuri-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null

foreach ($f in "Kuronuri.exe", "Kuronuri.exe.config", "Kuronuri.Core.dll", "pdfium.dll") { Copy-Item (Join-Path $bin $f) $stage }
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.txt") $stage

# Default settings (identical to the built-in defaults) so that administrators can edit the presets before handing it out.
$settingsSrc = Join-Path $root "docs/settings.default.json"
Copy-Item $settingsSrc (Join-Path $stage "settings.json")

# User guide as plain UTF-8 text with BOM (opens correctly in Notepad on every Windows version).
$guide = (Get-Content (Join-Path $root "docs/USER_GUIDE.md") -Raw -Encoding UTF8).Replace("`r`n", "`n")   # LF first; CRLF is added below
$label = "$version" + $(if ($version.StartsWith("0.")) { "（プレビュー）" } else { "" })   # same wording as HelpWindow.WithVersion
$nl = $guide.IndexOf("`n")
$guide = $guide.Substring(0, $nl + 1) + "`n対象の版: $label`n" + $guide.Substring($nl + 1)
[IO.File]::WriteAllText((Join-Path $stage "利用の手引き.txt"), $guide.Replace("`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))

$zip = Join-Path $dist "kuronuri-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
$zipSha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "== done"
Get-ChildItem $stage | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Output
Write-Output "$zip"
Write-Output "sha256 $zipSha"
