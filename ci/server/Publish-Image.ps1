#Requires -Version 7.0
param([Parameter(Mandatory)][ValidatePattern('^sha-[0-9a-f]{12}$')][string]$Version)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
foreach ($key in @('EDGEGAP_IMAGE_REPOSITORY', 'EDGEGAP_REGISTRY_USERNAME', 'EDGEGAP_REGISTRY_PASSWORD')) {
    if (-not [Environment]::GetEnvironmentVariable($key)) { throw "Missing $key" }
}
if ($env:EDGEGAP_IMAGE_REPOSITORY -notmatch '^registry\.edgegap\.com/[a-z0-9][a-z0-9._/-]+$') {
    throw 'Expected EDGEGAP_IMAGE_REPOSITORY=registry.edgegap.com/<project>/<image>'
}
$image = "$($env:EDGEGAP_IMAGE_REPOSITORY):$Version"
function DockerChecked([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed: $($Arguments[0])" }
}
DockerChecked @('build', '--platform', 'linux/amd64', '-f', 'ci/server/Dockerfile', '-t', $image, 'Builds/LinuxServer')
$container = 'kltn-ci-' + [guid]::NewGuid().ToString('N')
try {
    DockerChecked @('run', '-d', '--name', $container, '-e', 'MATCH_REPORTER_STUB=1', $image)
    $ready = $false
    for ($i = 0; $i -lt 45; $i++) {
        $running = & docker inspect --format '{{.State.Running}}' $container
        if ($LASTEXITCODE -ne 0 -or $running -ne 'true') { throw 'Local server container exited before readiness.' }
        $logs = (& docker logs $container 2>&1 | Out-String)
        if ($logs.Contains('Dedicated server listening on 0.0.0.0:7777')) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'Local server did not open WebSocket 7777 within 90 seconds.' }
} finally {
    New-Item Logs -ItemType Directory -Force | Out-Null
    & docker logs $container *> Logs/server-container.log
    & docker rm -f $container | Out-Null
}
# Isolated Docker config prevents overwriting the developer's registry credentials.
$previousConfig = $env:DOCKER_CONFIG
$previousHost = $env:DOCKER_HOST
$previousContext = $env:DOCKER_CONTEXT
$engine = & docker context inspect --format '{{.Endpoints.docker.Host}}'
if ($LASTEXITCODE -ne 0 -or -not $engine) { throw 'Unable to resolve current Docker engine.' }
$temporaryConfig = Join-Path ([IO.Path]::GetTempPath()) ('kltn-docker-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryConfig | Out-Null
$env:DOCKER_CONFIG = $temporaryConfig
$env:DOCKER_CONTEXT = $null
$env:DOCKER_HOST = if ($previousHost -and -not $previousContext) { $previousHost } else { $engine }
try {
    $env:EDGEGAP_REGISTRY_PASSWORD | & docker login registry.edgegap.com --username $env:EDGEGAP_REGISTRY_USERNAME --password-stdin
    if ($LASTEXITCODE -ne 0) { throw 'Registry login failed.' }
    # Never overwrite a released SHA tag. Rerun deployment separately for recovery.
    & docker manifest inspect $image *> $null
    if ($LASTEXITCODE -eq 0) { throw 'Image tag already exists. Use the deploy-existing workflow input or a new commit.' }
    DockerChecked @('push', $image)
} finally {
    $env:DOCKER_CONFIG = $previousConfig
    $env:DOCKER_HOST = $previousHost
    $env:DOCKER_CONTEXT = $previousContext
    $resolved = [IO.Path]::GetFullPath($temporaryConfig)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolved -Leaf).StartsWith('kltn-docker-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
