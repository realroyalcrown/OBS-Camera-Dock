param([string]$Helper,[string]$OutputDirectory,[switch]$TestBuild)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$Helper){$Helper=Join-Path $repo 'dist\windows\OBS Camera Dock.exe'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo 'dist\installer'}
if(!(Test-Path -LiteralPath $Helper)){throw 'Build the Windows helper first.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output=Join-Path $OutputDirectory 'OBS-Camera-Dock-Windows-0.4.3-Setup.exe'
$manifest=if($TestBuild){'/nowin32manifest'}else{"/win32manifest:$(Join-Path $repo 'Windows\Installer\app.manifest')"}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $manifest "/win32icon:$(Join-Path $repo 'Windows\Resources\camera.ico')" "/resource:$(Join-Path $repo 'Windows\Resources\camera.ico'),camera.ico" "/resource:$Helper,payload.exe" (Join-Path $repo 'Windows\Installer\Program.cs')
if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
Write-Output $output
