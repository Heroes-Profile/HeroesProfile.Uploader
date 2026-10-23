# Builds the WPF uploader's last update (2.9.1) and packages it for Squirrel, the way its 2.x releases were:
#   legacy/WpfBridge/build.ps1 <out-dir>
# <out-dir> gets RELEASES and Heroesprofile.Uploader-2.9.1-full.nupkg. release-desktop.yml attaches both to
# every release, in place of Velopack's RELEASES, so old installs update to this - and only to this.
# Windows only (.NET Framework, and Squirrel 1.7.8's own Squirrel.exe from its NuGet package).
param([Parameter(Mandatory = $true)][string]$OutDir)
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$project = Join-Path $here 'Heroesprofile.Uploader.WpfBridge.csproj'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "hp-wpf-bridge-$PID"
New-Item -ItemType Directory -Force -Path $work, $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

dotnet build $project -c Release -o "$work\bin"
if ($LASTEXITCODE -ne 0) { throw "Building the bridge failed" }

# The nuspec takes the build output as it is (lib\net45, like 2.x).
dotnet pack $project -c Release --no-build -o "$work\pkg" `
    -p:NuspecFile="$here\Heroesprofile.Uploader.nuspec" -p:NuspecBasePath="$work\bin" -p:OutputPath="$work\bin\"
if ($LASTEXITCODE -ne 0) { throw "Packing the bridge failed" }
$nupkg = Get-ChildItem "$work\pkg\Heroesprofile.Uploader.2.9.1.nupkg"

$squirrel = Join-Path $env:USERPROFILE '.nuget\packages\squirrel.windows\1.7.8\tools\Squirrel.exe'
if (-not (Test-Path $squirrel)) { throw "Squirrel.exe not found at $squirrel (restore the project first)" }
# Squirrel.exe returns before its work is done unless waited on.
$p = Start-Process -FilePath $squirrel -Wait -PassThru -NoNewWindow -ArgumentList @(
    "--releasify=`"$($nupkg.FullName)`"", '--no-msi', "--setupIcon=`"$here\uploader_icon_light.ico`"", "--releaseDir=`"$work\releases`"")
if ($p.ExitCode -ne 0) { throw "Squirrel --releasify failed ($($p.ExitCode))" }

Copy-Item "$work\releases\RELEASES", "$work\releases\Heroesprofile.Uploader-2.9.1-full.nupkg" $OutDir
Get-Content "$OutDir\RELEASES"
Remove-Item -Recurse -Force $work
