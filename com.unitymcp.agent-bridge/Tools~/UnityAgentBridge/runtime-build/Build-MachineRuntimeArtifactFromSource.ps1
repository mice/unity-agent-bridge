[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceArchivePath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$TagName,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [Parameter(Mandatory = $true)][string]$OutputArchivePath,
    [Parameter(Mandatory = $true)][string]$UnityProjectPath,
    [string]$SourceArchiveUrl,
    [string]$ArtifactUrl,
    [string]$DotnetPath = 'dotnet',
    [string]$Rid = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-DotnetExecutablePath {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $command = Get-Command -Name $ExecutablePath -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $command -or [string]::IsNullOrWhiteSpace($command.Source)) {
        throw "dotnet executable could not be resolved: $ExecutablePath"
    }
    if ($command.Source.Contains('"')) {
        throw "dotnet executable path contains an unsupported quote: $($command.Source)"
    }
    return $command.Source
}

function Resolve-Dotnet8SdkVersion {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $sdkLines = @(& $ExecutablePath --list-sdks 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list installed .NET SDKs using: $ExecutablePath"
    }

    $versions = @(
        foreach ($line in $sdkLines) {
            if ([string]$line -match '^(8\.0\.\d+)\s+\[') {
                [System.Version]$Matches[1]
            }
        }
    )
    $selected = $versions | Sort-Object -Descending | Select-Object -First 1
    if ($null -eq $selected) {
        throw '.NET 8 SDK is required to build the runtime from tag source.'
    }
    return $selected.ToString()
}

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid runtime semantic version: $Version"
}
if ($CommitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "CommitSha must be a 40-character Git SHA: $CommitSha"
}
if ($Rid -ne 'win-x64') {
    throw "Only win-x64 source builds are supported: $Rid"
}

$sourceArchive = (Resolve-Path -LiteralPath $SourceArchivePath -ErrorAction Stop).Path
$projectRoot = (Resolve-Path -LiteralPath $UnityProjectPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings') -PathType Container)) {
    throw "Not a Unity project: $projectRoot"
}

$outputArchive = [System.IO.Path]::GetFullPath($OutputArchivePath)
if (Test-Path -LiteralPath $outputArchive) {
    throw "Refusing to overwrite cached runtime artifact: $outputArchive"
}
$outputParent = Split-Path -Parent $outputArchive
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$workRoot = Join-Path $outputParent ('.source-build-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workRoot 'source'
$runtimeBuildRoot = Join-Path $workRoot 'runtime-build'
$stageRoot = Join-Path $workRoot 'stage'

try {
    New-Item -ItemType Directory -Path $sourceRoot,$runtimeBuildRoot,$stageRoot -Force | Out-Null
    Expand-Archive -LiteralPath $sourceArchive -DestinationPath $sourceRoot -Force

    $dotnetExecutable = Resolve-DotnetExecutablePath -ExecutablePath $DotnetPath
    $dotnetSdkVersion = Resolve-Dotnet8SdkVersion -ExecutablePath $dotnetExecutable
    [ordered]@{
        sdk = [ordered]@{
            version = $dotnetSdkVersion
            rollForward = 'latestPatch'
            allowPrerelease = $false
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $workRoot 'global.json') -Encoding utf8

    $dotnetWrapper = Join-Path $workRoot 'dotnet-single-node.cmd'
    @(
        '@echo off',
        ('"' + $dotnetExecutable + '" %* -m:1')
    ) | Set-Content -LiteralPath $dotnetWrapper -Encoding ascii

    $packageRoot = $null
    foreach ($manifestFile in @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter 'package.json')) {
        try {
            $metadata = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
            if ([string]$metadata.name -eq 'com.unitymcp.agent-bridge') {
                $packageRoot = $manifestFile.Directory.FullName
                break
            }
        }
        catch {
        }
    }
    if ([string]::IsNullOrWhiteSpace($packageRoot)) {
        throw 'Tag source archive does not contain com.unitymcp.agent-bridge/package.json.'
    }

    $packageMetadata = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
    if ([string]$packageMetadata.version -ne $Version) {
        throw "Tag package version '$($packageMetadata.version)' does not match selected version '$Version'."
    }

    $buildScript = Join-Path $packageRoot 'Tools~\UnityAgentBridge\runtime-build\Build-LocalRuntime.ps1'
    if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
        throw "Tag local-runtime build script is missing: $buildScript"
    }

    Push-Location -LiteralPath $workRoot
    try {
        & $buildScript -OutputRoot $runtimeBuildRoot -UnityProjectPath $projectRoot -Rid $Rid -DotnetPath $dotnetWrapper
        if ($LASTEXITCODE -ne 0) {
            throw "Tag local-runtime build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $sourceCli = Join-Path $runtimeBuildRoot 'UnityAgentBridge\cli\out\win-x64\unity-agent-bridge.exe'
    $sourceRoslyn = Join-Path $runtimeBuildRoot 'UnityAgentBridge\roslyn-execution\out\win-x64\unity-roslyn-compiler.exe'
    foreach ($requiredPath in @($sourceCli, $sourceRoslyn)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Built runtime payload is missing: $requiredPath"
        }
    }

    $runtimeRoot = Join-Path $stageRoot 'runtime\win-x64'
    $launcherRoot = Join-Path $stageRoot 'launcher'
    New-Item -ItemType Directory -Path $runtimeRoot,$launcherRoot -Force | Out-Null
    Copy-Item -LiteralPath $sourceCli -Destination (Join-Path $runtimeRoot 'unity-agent-bridge.exe') -Force
    Copy-Item -LiteralPath $sourceRoslyn -Destination (Join-Path $runtimeRoot 'unity-roslyn-compiler.exe') -Force

    @(
        '@echo off',
        'setlocal',
        'set "SCRIPT_DIR=%~dp0"',
        'set "UNITY_AGENT_BRIDGE_RUNTIME_MODE=machine"',
        'set "PROJECT_PATH=%UNITY_AGENT_BRIDGE_PROJECT_PATH%"',
        'if "%~1"=="--project-path" ( set "PROJECT_PATH=%~2" )',
        'if "%PROJECT_PATH%"=="" ( echo UNITY_AGENT_BRIDGE_PROJECT_PATH is required 1>&2 & exit /b 2 )',
        'set "RUNTIME_EXE=%SCRIPT_DIR%..\runtime\win-x64\unity-agent-bridge.exe"',
        'if not exist "%RUNTIME_EXE%" ( echo Runtime executable is missing: %RUNTIME_EXE% 1>&2 & exit /b 3 )',
        'set "UNITY_AGENT_BRIDGE_PROJECT_PATH=%PROJECT_PATH%"',
        '"%RUNTIME_EXE%" mcp-server'
    ) | Set-Content -LiteralPath (Join-Path $launcherRoot 'Start-UnityAgentBridge-Mcp.cmd') -Encoding ascii

    $runtimeSha = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot 'unity-agent-bridge.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schemaVersion = '1.0'
        product = 'unity-agent-bridge'
        packageName = 'com.unitymcp.agent-bridge'
        version = $Version
        packageVersion = $Version
        runtimeVersion = $Version
        protocolVersion = '1.0'
        gitTag = $TagName
        commitSha = $CommitSha.ToLowerInvariant()
        unityMinimum = [string]$packageMetadata.unity
        platform = $Rid
        artifactUrl = if (-not [string]::IsNullOrWhiteSpace($ArtifactUrl)) { $ArtifactUrl.Trim() } elseif ($null -eq $SourceArchiveUrl) { '' } else { $SourceArchiveUrl.Trim() }
        artifactSha256 = $runtimeSha
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        buildOrigin = 'git-tag-source'
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stageRoot 'release-manifest.json') -Encoding utf8

    Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $outputArchive -CompressionLevel Optimal
    $archiveSha = (Get-FileHash -LiteralPath $outputArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($outputArchive + '.sha256') -Value "$archiveSha  $([System.IO.Path]::GetFileName($outputArchive))" -Encoding ascii
    Write-Output ([pscustomobject]@{
        version = $Version
        tag = $TagName
        commitSha = $CommitSha.ToLowerInvariant()
        archivePath = $outputArchive
        archiveSha256 = $archiveSha
        sourceArchivePath = $sourceArchive
    } | ConvertTo-Json -Compress)
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
