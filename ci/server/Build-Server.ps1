#Requires -Version 7.0
param([Parameter(Mandatory)][string]$UnityPath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $root
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'UNITY_EDITOR_PATH does not point to Unity.exe.' }
$expectedVersion = ((Get-Content ProjectSettings/ProjectVersion.txt)[0] -split ': ')[1].Trim()
$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion
if (-not $actualVersion.StartsWith($expectedVersion)) { throw "Expected Unity $expectedVersion; found $actualVersion" }
New-Item Logs -ItemType Directory -Force | Out-Null
$output = [IO.Path]::GetFullPath((Join-Path $root 'Builds/LinuxServer'))
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be inside the checkout.'
}
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
# ProcessStartInfo.ArgumentList avoids quoting errors on Windows paths with spaces.
$info = [Diagnostics.ProcessStartInfo]::new($UnityPath)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
foreach ($arg in @('-batchmode', '-nographics', '-quit', '-projectPath', $root,
    '-buildTarget', 'Linux64', '-standaloneBuildSubtarget', 'Server',
    '-executeMethod', 'KLTN.Editor.CI.ServerBuild.BuildLinux',
    '-logFile', (Join-Path $root 'Logs/server-build.log'))) {
    $info.ArgumentList.Add($arg)
}
$process = [Diagnostics.Process]::Start($info)
if (-not $process.WaitForExit(3600000)) {
    $process.Kill($true)
    throw 'Unity build exceeded 60 minutes.'
}
if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)); see Logs/server-build.log." }
foreach ($file in @('KLTNServer.x86_64', 'UnityPlayer.so', 'KLTNServer_Data')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $file))) { throw "Missing server output: $file" }
}
# Keep Unity debugging/backup directories out of the runtime container.
@('*BackUpThisFolder_ButDontShipWithYourGame*', '*BurstDebugInformation_DoNotShip*', '*.pdb') |
    Set-Content -LiteralPath (Join-Path $output '.dockerignore') -Encoding utf8NoBOM
