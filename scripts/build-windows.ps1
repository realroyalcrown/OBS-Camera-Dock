param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist\windows' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required (Windows 10/11).' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path $OutputDirectory 'OBS Camera Dock.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Windows') -Filter '*.cs' | ForEach-Object FullName)
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$runtime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime') -Filter System.Runtime.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$winrt = @('Windows.Foundation','Windows.Media','Windows.Graphics','Windows.Storage') | ForEach-Object { "/reference:$(Join-Path $metadata ($_ + '.winmd'))" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/reference:$runtime" @winrt "/resource:$(Join-Path $repo 'Sources\CameraDockHelper\Resources\index.html'),index.html" "/resource:$(Join-Path $repo 'Windows\Resources\face.html'),face.html" "/win32icon:$(Join-Path $repo 'Windows\Resources\camera.ico')" "/resource:$(Join-Path $repo 'Windows\Resources\camera.ico'),camera.ico" @sources
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
Write-Output $output
