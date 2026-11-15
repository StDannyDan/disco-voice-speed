#Requires -Version 7
# (Windows PowerShell 5.1's ZipFile can write '\' separators, which breaks other unzip tools.)
<#
.SYNOPSIS
  Build DiscoVoiceSpeed and make the two release zips in dist\:
    DiscoVoiceSpeed-<version>-with-BepInEx.zip  BepInEx be.755 + plugin + BepInEx.cfg (console off)
    DiscoVoiceSpeed-<version>-plugin-only.zip   just BepInEx\plugins\DiscoVoiceSpeed\
  Both unpack straight into the game folder.

.PARAMETER GameDir
  Game folder with BepInEx installed and run once (the build references BepInEx\interop).
  The build does NOT copy the plugin into the game here.

.PARAMETER AllowDirty
  Package even with uncommitted changes (for testing only: the DLL embeds the commit
  hash, so a release package must come from a clean commit).
#>
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Disco Elysium",
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projDir = Join-Path $root 'DiscoVoiceSpeed'

# BepInEx build shipped in the full zip: be.755, the one the Disco Elysium Vortex
# extension installs. The plugin is also tested on be.688 (Furious Tare's build).
$bepInExBuild = 755
$bepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip'
$bepInExSha256 = '3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F'

# --- Version ---------------------------------------------------------------
[xml]$proj = Get-Content (Join-Path $projDir 'DiscoVoiceSpeed.csproj')
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'No <Version> in DiscoVoiceSpeed.csproj' }
Write-Host "DiscoVoiceSpeed $version"

$dirty = & git -C $root status --porcelain 2>$null
if ($dirty) {
    if (-not $AllowDirty) { throw 'Uncommitted changes: commit first (or pass -AllowDirty for a test package).' }
    Write-Warning 'Uncommitted changes: this test package will not match any commit.'
}

# --- Build -----------------------------------------------------------------
if (-not (Test-Path (Join-Path $GameDir 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw "No BepInEx interop assemblies in '$GameDir'. Install BepInEx there and start the game once, or pass -GameDir."
}
& dotnet build $projDir -c Release -nologo -v quiet "-p:GameDir=$GameDir" -p:CopyToGame=false
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
$dll = Join-Path $projDir 'bin\Release\net6.0\DiscoVoiceSpeed.dll'
$dllVersion = (Get-Item $dll).VersionInfo.ProductVersion
if (-not $dllVersion.StartsWith($version)) { throw "Built DLL is $dllVersion, expected $version" }

# The shipped DLL must not contain local paths (e.g. an embedded PDB path with the user name).
$dllBytes = [IO.File]::ReadAllBytes($dll)
foreach ($text in [Text.Encoding]::ASCII.GetString($dllBytes), [Text.Encoding]::Unicode.GetString($dllBytes)) {
    if ($text -match '[A-Za-z]:\\Users\\|\\Users\\[^\\]+\\|\.pdb') { throw "Built DLL contains a local path ('$($Matches[0])'); check DebugType/PathMap in the csproj" }
}

# --- BepInEx (cached, checksum-verified) -------------------------------------
$cache = Join-Path $root '.cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$bepInExZip = Join-Path $cache "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.$bepInExBuild.zip"
if (-not (Test-Path $bepInExZip)) {
    Write-Host "Downloading BepInEx be.$bepInExBuild..."
    Invoke-WebRequest $bepInExUrl -OutFile $bepInExZip
}
$hash = (Get-FileHash $bepInExZip -Algorithm SHA256).Hash
if ($hash -ne $bepInExSha256) {
    Remove-Item $bepInExZip
    throw "BepInEx zip checksum mismatch ($hash); deleted it, run again to re-download"
}

# --- Stage -----------------------------------------------------------------
$dist = Join-Path $root 'dist'
$stagePlugin = Join-Path $dist 'stage\plugin-only'
$stageFull = Join-Path $dist 'stage\with-BepInEx'
if (Test-Path (Join-Path $dist 'stage')) { Remove-Item -Recurse -Force (Join-Path $dist 'stage') }

function Add-Plugin([string]$stage, [switch]$WithThirdParty) {
    $p = Join-Path $stage 'BepInEx\plugins\DiscoVoiceSpeed'
    New-Item -ItemType Directory -Force $p | Out-Null
    Copy-Item $dll $p
    foreach ($f in 'README.md', 'CHANGELOG.md', 'LICENSE', 'NOTICE') { Copy-Item (Join-Path $root $f) $p }
    if ($WithThirdParty) {
        Copy-Item (Join-Path $root 'packaging\THIRD-PARTY-NOTICES.txt') $p
        Copy-Item (Join-Path $root 'packaging\BepInEx-LICENSE.txt') $p
    }
}

Add-Plugin $stagePlugin

Expand-Archive $bepInExZip -DestinationPath $stageFull
Add-Plugin $stageFull -WithThirdParty
$cfgDir = Join-Path $stageFull 'BepInEx\config'
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Copy-Item (Join-Path $root 'packaging\BepInEx.cfg') $cfgDir

# --- Zip -------------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression.FileSystem
$outputs = @(
    @{ Stage = $stageFull;   Zip = Join-Path $dist "DiscoVoiceSpeed-$version-with-BepInEx.zip" },
    @{ Stage = $stagePlugin; Zip = Join-Path $dist "DiscoVoiceSpeed-$version-plugin-only.zip" }
)
foreach ($o in $outputs) {
    if (Test-Path $o.Zip) { Remove-Item $o.Zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($o.Stage, $o.Zip, [IO.Compression.CompressionLevel]::Optimal, $false)
}
Remove-Item -Recurse -Force (Join-Path $dist 'stage')

# --- Report ----------------------------------------------------------------
foreach ($o in $outputs) {
    $zip = [IO.Compression.ZipFile]::OpenRead($o.Zip)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        Write-Host ''
        Write-Host ("{0}  {1:N1} MB, {2} files, SHA256 {3}" -f (Split-Path $o.Zip -Leaf), ((Get-Item $o.Zip).Length / 1MB),
            $entries.Count, (Get-FileHash $o.Zip -Algorithm SHA256).Hash)
        $entries | Where-Object { $_.FullName -notmatch '^(BepInEx/core|dotnet)/' } |
            ForEach-Object { '    ' + $_.FullName } | Write-Host
        $hidden = @($entries | Where-Object { $_.FullName -match '^(BepInEx/core|dotnet)/' }).Count
        if ($hidden) { Write-Host "    ... plus $hidden files in BepInEx/core and dotnet/" }
    }
    finally { $zip.Dispose() }
}
