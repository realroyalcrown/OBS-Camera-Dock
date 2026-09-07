param([string]$ObsRoot)
$ErrorActionPreference = 'Stop'

function Get-InstallPayload {
    $repo = Split-Path $PSScriptRoot -Parent
    if (Test-Path -LiteralPath (Join-Path $repo 'scripts\build-windows.ps1') -PathType Leaf) {
        Push-Location $repo
        try {
            powershell -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Sestavení Windows aplikace selhalo. Instalace byla zastavena.' }
        } finally { Pop-Location }
        $payload = Join-Path $repo 'dist\windows\OBS Camera Dock.exe'
    } else {
        $payload = Join-Path $PSScriptRoot 'OBS Camera Dock.exe'
    }
    if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) { throw 'Instalační EXE nebylo nalezeno.' }
    return $payload
}
function Test-ObsRoot([string]$Path) {
    return ($Path -and (Test-Path -LiteralPath (Join-Path $Path 'bin\64bit\obs64.exe') -PathType Leaf))
}
function Find-ObsRoot {
    $candidates = @()
    foreach ($process in @(Get-Process obs64 -ErrorAction SilentlyContinue)) {
        try { $candidates += [IO.Path]::GetFullPath((Join-Path (Split-Path $process.Path -Parent) '..\..')) } catch {}
    }
    foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio')) {
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($entry.InstallLocation) { $candidates += $entry.InstallLocation }
    }
    foreach ($base in @($env:ProgramFiles,${env:ProgramFiles(x86)},$env:ProgramW6432)) {
        if ($base) { $candidates += Join-Path $base 'obs-studio' }
    }
    $roots = @($candidates | Where-Object { Test-ObsRoot $_ } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') } | Select-Object -Unique)
    if ($roots.Count -eq 1) { return $roots[0] }
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Vyberte kořenovou složku OBS Studio (obsahuje bin\64bit\obs64.exe).'
    if ($roots.Count) { $dialog.SelectedPath = $roots[0] }
    try { if ($dialog.ShowDialog() -eq 'OK') { return $dialog.SelectedPath } } finally { $dialog.Dispose() }
    throw 'Výběr složky byl zrušen.'
}
function Assert-SafeTarget([string]$Target) {
    for ($dir = New-Object IO.DirectoryInfo($Target); $null -ne $dir; $dir = $dir.Parent) {
        if ($dir.Exists -and ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Cesta obsahuje odkaz nebo junction: $($dir.FullName)" }
    }
    if (Test-Path -LiteralPath $Target) {
        # Check each directory before descending; never follow a junction during cleanup.
        $pending = New-Object 'Collections.Generic.Queue[string]'
        $pending.Enqueue($Target)
        while ($pending.Count) {
            foreach ($entry in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
                if (-not $entry.FullName.StartsWith($Target+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Soubor mimo cílovou složku.' }
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cílová složka obsahuje odkaz: $($entry.FullName)" }
                if ($entry.PSIsContainer) { $pending.Enqueue($entry.FullName) }
            }
        }
    }
}
function Install-CameraDock([string]$Root,[string]$Payload) {
    if (-not (Test-ObsRoot $Root)) { throw 'Neplatná kořenová složka OBS Studio.' }
    if (-not (Test-Path -LiteralPath $Payload -PathType Leaf)) { throw 'Vedle install.ps1 chybí OBS Camera Dock.exe. Rozbalte celý instalační ZIP.' }
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $target = [IO.Path]::GetFullPath((Join-Path $rootPath 'obs-camera-dock'))
    if ((Split-Path $target -Parent) -ne $rootPath -or (Split-Path $target -Leaf) -ne 'obs-camera-dock') { throw 'Neplatný instalační cíl.' }
    Assert-SafeTarget $target
    if ([IO.Path]::GetFullPath($Payload).StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Spusťte instalaci z rozbaleného ZIPu mimo cílovou složku obs-camera-dock.' }
    $exe = Join-Path $target 'OBS Camera Dock.exe'
    foreach ($process in @(Get-Process 'OBS Camera Dock' -ErrorAction SilentlyContinue)) {
        if (-not $process.Path -or $process.Path -eq $exe) { throw 'Ukončete OBS Camera Dock přes ikonu v liště a spusťte instalaci znovu.' }
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $stage = Join-Path $target ('.install-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try {
        Copy-Item -LiteralPath $Payload -Destination $stage
        if ((Get-FileHash -LiteralPath $Payload).Hash -ne (Get-FileHash -LiteralPath $stage).Hash) { throw 'Kontrola zkopírovaného EXE selhala.' }
        if (Test-Path -LiteralPath $exe) { [IO.File]::Replace($stage,$exe,($stage+'.backup')) } else { [IO.File]::Move($stage,$exe) }
        Assert-SafeTarget $target
        foreach ($entry in Get-ChildItem -LiteralPath $target -Force) {
            if ($entry.FullName -eq $exe) { continue }
            $full = [IO.Path]::GetFullPath($entry.FullName)
            if (-not $full.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Úklid mimo cílovou složku byl zastaven.' }
            Remove-Item -LiteralPath $full -Recurse -Force
        }
    } finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Force } }
    Write-Host "Nainstalováno: $exe" -ForegroundColor Green
}
# Dot-sourcing exposes the functions for isolated tests without installing anything.
if ($MyInvocation.InvocationName -eq '.') { return }
try {
    $payload = Get-InstallPayload
    if (-not $ObsRoot) { $ObsRoot = Find-ObsRoot }
    if (-not (Test-ObsRoot $ObsRoot)) { throw 'Vybraná složka neobsahuje OBS Studio.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $scriptPath = $PSCommandPath.Replace("'","''")
        $quotedRoot = $ObsRoot.Replace("'","''")
        $command = "& '$scriptPath' -ObsRoot '$quotedRoot'"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $child = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -Wait -PassThru
        exit $child.ExitCode
    }
    Install-CameraDock -Root $ObsRoot -Payload $payload
    Write-Host 'OBS Studio a uložené presety zůstaly zachované. Helper nyní spusťte běžným dvojklikem.'
    Read-Host 'Stiskněte Enter pro zavření' | Out-Null
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Read-Host 'Stiskněte Enter pro zavření' | Out-Null
    exit 1
}
