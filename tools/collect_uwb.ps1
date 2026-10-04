<#
.SYNOPSIS
  Copies UnityWebBrowser (UWB) out of a small Unity Windows build into the mod folder (mod\FlappyCrix).

.DESCRIPTION
  UnityWebBrowser is distributed as Unity packages, not as loose DLLs. The reliable way to get
  DLLs that match Gorilla Tag's Unity version is to make a throwaway Unity project with the SAME
  Unity version as the game, add UWB + the Win-x64 CEF engine from the official VoltUPR registry,
  build it for Windows x64, and run this script on that build. See README.md, "Installing the
  browser engine", for the step-by-step.

  Copies:
    <Build>_Data\Managed\  VoltstroStudios.UnityWebBrowser.dll, VoltstroStudios.UnityWebBrowser.Shared.dll,
                           VoltRpc.dll, UniTask.dll, VoltstroStudios.NativeArraySpanExtensions.dll,
                           Newtonsoft.Json.dll (only if the game doesn't already ship it)
    <Build>_Data\UWB\      the CEF engine process (UnityWebBrowser.Engine.Cef.exe + Chromium files) -> UWB\
  and the licence files it can find.

.EXAMPLE
  .\tools\collect_uwb.ps1 -UnityBuild "C:\Builds\UwbHost" -GamePath "C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag"
#>
param(
    [Parameter(Mandatory = $true)][string]$UnityBuild,
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$Dist = (Join-Path $PSScriptRoot "..\mod\FlappyCrix"),
    [string]$UnityProject = ""
)
$ErrorActionPreference = "Stop"

$data = Get-ChildItem -Path $UnityBuild -Directory -Filter "*_Data" | Select-Object -First 1
if (-not $data) { throw "No *_Data folder in $UnityBuild - point -UnityBuild at the folder containing the built .exe" }
$managed = Join-Path $data.FullName "Managed"
$engineSrc = Join-Path $data.FullName "UWB"
$gameManaged = Join-Path $GamePath "Gorilla Tag_Data\Managed"
if (-not (Test-Path $gameManaged)) { throw "Gorilla Tag not found at $GamePath" }
if (-not (Test-Path (Join-Path $engineSrc "UnityWebBrowser.Engine.Cef.exe"))) {
    throw "$engineSrc\UnityWebBrowser.Engine.Cef.exe not found. Was the 'Unity Web Browser CEF Engine (Win x64)' package installed before building?"
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null

# --- managed assemblies ---
$required = @("VoltstroStudios.UnityWebBrowser.dll", "VoltstroStudios.UnityWebBrowser.Shared.dll", "VoltRpc.dll",
              "UniTask.dll", "VoltstroStudios.NativeArraySpanExtensions.dll")
foreach ($dll in $required) {
    $src = Join-Path $managed $dll
    if (-not (Test-Path $src)) { throw "Missing $dll in $managed" }
    if (Test-Path (Join-Path $gameManaged $dll)) {
        Write-Warning "$dll already ships with Gorilla Tag; using the game's copy (not copied). If UWB fails to load, compare versions."
        continue
    }
    Copy-Item $src $Dist -Force
    $v = [System.Reflection.AssemblyName]::GetAssemblyName($src).Version
    Write-Host "  + $dll ($v)"
}
$nj = "Newtonsoft.Json.dll"
if (Test-Path (Join-Path $gameManaged $nj)) {
    Write-Host "  = ${nj}: using the game's copy"
} else {
    Copy-Item (Join-Path $managed $nj) $Dist -Force
    Write-Host "  + $nj"
}

# --- engine process (CEF) ---
$engineDst = Join-Path $Dist "UWB"
if (Test-Path $engineDst) { Remove-Item $engineDst -Recurse -Force }
Copy-Item $engineSrc $engineDst -Recurse -Force
$mb = [math]::Round(((Get-ChildItem $engineDst -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "  + UWB\ engine: $mb MB"

# --- licences ---
$lic = Join-Path $Dist "LICENSES"
New-Item -ItemType Directory -Force -Path $lic | Out-Null
Get-ChildItem $engineDst -Recurse -Include "LICENSE*", "*credits*.html", "THIRD_PARTY*" -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $lic ("CEF-" + $_.Name)) -Force }
if ($UnityProject -and (Test-Path (Join-Path $UnityProject "Library\PackageCache"))) {
    Get-ChildItem (Join-Path $UnityProject "Library\PackageCache") -Directory |
        Where-Object { $_.Name -match "voltstro|unitask|voltrpc|newtonsoft" } | ForEach-Object {
            $l = Get-ChildItem $_.FullName -Filter "LICENSE*" -File | Select-Object -First 1
            if ($l) { Copy-Item $l.FullName (Join-Path $lic (($_.Name -split "@")[0] + "-LICENSE.md")) -Force }
        }
} else {
    Write-Warning "Pass -UnityProject <path> to also copy the package licence files (UWB, UniTask, VoltRpc, Newtonsoft). THIRD_PARTY_NOTICES.md lists them either way."
}

Write-Host ""
Write-Host "Done. Next: dotnet build src\FlappyCrix -c Release -p:GamePath=`"$GamePath`""
