<#
.SYNOPSIS
    Multi-match load test (plan 05 D, input for plan 06 / I6): N pairs of scripted players
    against one dedicated server process, sampling the server's CPU and RAM.

.DESCRIPTION
    Needs a Windows "Dedicated Server" build of the project (the same build is used for the
    server and for the bots: -loadTestClient turns it into a scripted player, see
    Assets/Scripts/Infrastructure/Networking/Dedicated/LoadTestClient.cs).
    The server runs with MATCH_REPORTER_STUB=1, so nothing is written to Firestore.

.EXAMPLE
    ./run-loadtest.ps1 -Build "D:\Builds\KLTN-Server\KLTN.exe" -Pairs 4 -Capacity 4
#>
param(
    [Parameter(Mandatory = $true)] [string] $Build,
    [int] $Pairs = 2,
    [int] $Capacity = 2,
    [int] $Port = 7777,
    [int] $MatchSeconds = 180,
    [double] $ActionSeconds = 2,
    [int] $SampleSeconds = 2,
    # Leave empty to start the server here; set to run only bots against a server elsewhere.
    [string] $ServerHost = "",
    [string] $OutDir = (Join-Path $PSScriptRoot ("results-" + (Get-Date -Format "yyyyMMdd-HHmmss")))
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$server = $null

if ($ServerHost -eq "") {
    $env:MATCH_CAPACITY = "$Capacity"
    $env:MATCH_REPORTER_STUB = "1"
    Remove-Item Env:MATCH_ID, Env:SERVER_ID, Env:SERVER_TOKEN -ErrorAction SilentlyContinue
    $server = Start-Process -FilePath $Build -PassThru -WindowStyle Hidden `
        -ArgumentList @("-batchmode", "-nographics", "-logFile", (Join-Path $OutDir "server.log"))
    Write-Host "Server PID $($server.Id), capacity $Capacity. Waiting for it to listen..."
    Start-Sleep -Seconds 8
    $target = "127.0.0.1:$Port"
} else {
    $target = "${ServerHost}:$Port"
}

$bots = @()
for ($i = 1; $i -le 2 * $Pairs; $i++) {
    $bots += Start-Process -FilePath $Build -PassThru -WindowStyle Hidden -ArgumentList @(
        "-batchmode", "-nographics",
        "-loadTestClient", $target,
        "-loadTestMatchSeconds", "$MatchSeconds",
        "-loadTestActionSeconds", "$ActionSeconds",
        "-logFile", (Join-Path $OutDir "bot-$i.log"))
    # Arrival order pairs the bots: 1+2 form the first match, 3+4 the second, ...
    Start-Sleep -Milliseconds 700
}
Write-Host "Started $($bots.Count) bots against $target."

$csv = Join-Path $OutDir "server-usage.csv"
"seconds,cpuPercent,workingSetMB,privateMB,handles" | Out-File -Encoding utf8 $csv
$cores = [Environment]::ProcessorCount
$start = Get-Date
$lastCpu = $null
$lastTime = $null

while (($bots | Where-Object { -not $_.HasExited }).Count -gt 0) {
    Start-Sleep -Seconds $SampleSeconds
    if ($server -ne $null -and -not $server.HasExited) {
        $server.Refresh()
        $now = Get-Date
        $cpu = $server.TotalProcessorTime.TotalMilliseconds
        $percent = 0
        if ($lastCpu -ne $null) {
            $percent = [math]::Round(($cpu - $lastCpu) / (($now - $lastTime).TotalMilliseconds * $cores) * 100, 2)
        }
        $lastCpu = $cpu
        $lastTime = $now
        $line = "{0},{1},{2},{3},{4}" -f [math]::Round(($now - $start).TotalSeconds),
            $percent,
            [math]::Round($server.WorkingSet64 / 1MB, 1),
            [math]::Round($server.PrivateMemorySize64 / 1MB, 1),
            $server.HandleCount
        $line | Out-File -Append -Encoding utf8 $csv
    }
}

Write-Host "All bots finished after $([math]::Round(((Get-Date) - $start).TotalSeconds)) s."

if ($server -ne $null) {
    $rows = Import-Csv $csv | Where-Object { [double]$_.cpuPercent -gt 0 -or [double]$_.workingSetMB -gt 0 }
    $log = Join-Path $OutDir "server.log"
    $started = (Select-String -Path $log -Pattern "Match initialized" -ErrorAction SilentlyContinue).Count
    $errors = (Select-String -Path $log -Pattern "Exception|\bError\b" -ErrorAction SilentlyContinue).Count
    $summary = [pscustomobject]@{
        pairs             = $Pairs
        capacity          = $Capacity
        matchesStarted    = $started
        cpuAvgPercent     = [math]::Round(($rows | Measure-Object -Property cpuPercent -Average).Average, 2)
        cpuPeakPercent    = ($rows | Measure-Object -Property cpuPercent -Maximum).Maximum
        ramPeakMB         = ($rows | Measure-Object -Property workingSetMB -Maximum).Maximum
        serverLogErrors   = $errors
    }
    $summary | Format-List
    $summary | ConvertTo-Json | Out-File -Encoding utf8 (Join-Path $OutDir "summary.json")

    # A multi-match server waits 120 s for new matches before quitting (NFR-21).
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}

Write-Host "Results: $OutDir"
