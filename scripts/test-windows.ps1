param([string]$Executable, [string]$TestDataDirectory, [string]$BaseUrl='http://127.0.0.1:24680')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Executable) { $Executable = Join-Path $repo 'dist\windows\OBS Camera Dock.exe' }
if (-not $TestDataDirectory) { $TestDataDirectory = Join-Path $repo 'dist\test-data' }
$test = Start-Process -FilePath $Executable -ArgumentList @('--self-test', '--data-dir', ('"' + $TestDataDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Self-tests failed; see %TEMP%\OBS-Camera-Dock-error.txt.' }
Write-Output 'Control and preset self-tests passed.'
# HTTP tests are read-only or deliberately invalid. Run while a helper is listening.
function Check-Response([string]$Path, [string]$Body, [int]$Expected, [hashtable]$Headers = @{}) {
    $request = @{ Uri = "$BaseUrl$Path"; SkipHttpErrorCheck = $true; Headers = $Headers }
    if ($null -ne $Body -and $Body -ne '') { $request.Method = 'POST'; $request.ContentType = 'application/json'; $request.Body = $Body }
    $response = Invoke-WebRequest @request
    if ([int]$response.StatusCode -ne $Expected) { throw "$Path returned $($response.StatusCode), expected $Expected" }
    return $response
}
$state = (Check-Response '/api/state' '' 200).Content | ConvertFrom-Json
if ($null -eq $state.cameras -or $null -eq $state.controls) { throw 'State schema is incomplete.' }
$page = Check-Response '/' '' 200
if ($page.Content -notmatch 'cameraSelect') { throw 'Camera picker is missing.' }
$null = Check-Response '/missing' '' 404
$null = Check-Response '/api/control' '{broken' 400
$null = Check-Response '/api/control' '{"id":"not-a-control","value":1}' 400
$null = Check-Response '/api/control' '{"id":"gain","value":"oops"}' 400
$null = Check-Response '/api/camera' '{"id":"not-a-device"}' 400
$null = Check-Response '/api/presets/create' '{"panel":"invalid","name":"Test"}' 400
$null = Check-Response '/api/rescan' '{}' 403 @{Origin='https://example.com'}
Write-Output '9 HTTP checks passed.'
$face = (Check-Response '/api/face/state' '' 200).Content | ConvertFrom-Json
if ($face.running) { throw 'Face analysis should not auto-start in the test instance.' }
$null = Check-Response '/face' '' 200
$null = Check-Response '/api/face/preview' '' 404
$null = Check-Response '/api/face/start' '{"scene":"missing","itemId":1,"tracking":true,"maxZoom":9}' 400
Write-Output '4 face HTTP checks passed.'
