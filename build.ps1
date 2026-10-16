# Builds dist\FSHWMonitor.exe: one self-contained file, no .NET install needed to run it.
# Requires the .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10
#
# .\build.ps1 -Package also builds the installer and a ready-to-share folder + zip:
#   dist\FS-HW-Monitor-Setup-<version>.exe
#   dist\FS HW Monitor <version>\  (setup + Read me.txt)  and  dist\FS HW Monitor <version>.zip
# Requires Inno Setup 6:  winget install JRSoftware.InnoSetup

param([switch]$Package)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\FSHWMonitor\FSHWMonitor.csproj'
$dist = Join-Path $PSScriptRoot 'dist'
$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText

dotnet publish $project -c Release -o $dist --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Get-Item (Join-Path $dist 'FSHWMonitor.exe')
Write-Host ("Built {0} {1} ({2:N1} MB)" -f $exe.FullName, $version, ($exe.Length / 1MB))

if (-not $Package) { return }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc /Qp "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\FSHWMonitor.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$setup = Get-Item (Join-Path $dist "FS-HW-Monitor-Setup-$version.exe")

# A clean folder with just the installer and a short read-me, plus a zip of it.
$folder = Join-Path $dist "FS HW Monitor $version"
if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
New-Item -ItemType Directory -Path $folder | Out-Null
Copy-Item -LiteralPath $setup.FullName -Destination $folder
$readme = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'installer\Read me.txt')).Replace('{VERSION}', $version).
    Replace('{UNDERLINE}', '=' * "FS HW Monitor $version".Length)
[IO.File]::WriteAllText((Join-Path $folder 'Read me.txt'), ($readme -replace "`r?`n", "`r`n"), [Text.UTF8Encoding]::new($true))

$zip = "$folder.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $folder -DestinationPath $zip
Write-Host ("Packaged {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
