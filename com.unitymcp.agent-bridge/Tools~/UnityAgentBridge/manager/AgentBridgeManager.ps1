[CmdletBinding()]
param(
    [ValidateSet('help', 'install', 'list', 'select', 'setup', 'resolve', 'doctor', 'rollback', 'cleanup', 'launch')]
    [string]$Command = 'help',
    [string]$Version,
    [ValidateSet('stable', 'preview', 'nightly')]
    [string]$Channel,
    [string]$ProjectPath,
    [string]$ArtifactPath,
    [string]$ArtifactUrl,
    [string]$ArtifactSha256,
    [string]$PackageUrl,
    [string]$RuntimeHome,
    [switch]$Force,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ManagerHome {
    if (-not [string]::IsNullOrWhiteSpace($RuntimeHome)) {
        return [System.IO.Path]::GetFullPath($RuntimeHome.Trim())
    }

    $override = [Environment]::GetEnvironmentVariable('UNITY_AGENT_BRIDGE_HOME')
    if (-not [string]::IsNullOrWhiteSpace($override)) {
        return [System.IO.Path]::GetFullPath($override.Trim())
    }

    return Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'UnityAgentBridge'
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
        return $null
    }
    return Get-Content -LiteralPath $PathValue -Raw | ConvertFrom-Json
}

function Write-JsonFile {
    param([Parameter(Mandatory = $true)][object]$Value, [Parameter(Mandatory = $true)][string]$PathValue)
    $parent = Split-Path -Parent $PathValue
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PathValue -Encoding utf8
}

function Get-ProjectRoot {
    $effectiveProjectPath = $ProjectPath
    if ([string]::IsNullOrWhiteSpace($effectiveProjectPath)) {
        $effectiveProjectPath = [Environment]::GetEnvironmentVariable('UNITY_AGENT_BRIDGE_PROJECT_PATH')
    }
    if ([string]::IsNullOrWhiteSpace($effectiveProjectPath)) {
        throw 'ProjectPath is required.'
    }
    $resolved = (Resolve-Path -LiteralPath $effectiveProjectPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'ProjectSettings') -PathType Container)) {
        throw "Not a Unity project: $resolved"
    }
    return $resolved
}

function Get-SelectionPath {
    param([Parameter(Mandatory = $true)][string]$Root)
    return Join-Path $Root '.unitymcp\runtime-selection.json'
}

function Get-VersionRoot {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$SelectedVersion)
    if ($SelectedVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') {
        throw "Invalid runtime semantic version: $SelectedVersion"
    }
    return Join-Path (Join-Path $Root 'versions') $SelectedVersion
}

function Get-ReleaseManifest {
    param([Parameter(Mandatory = $true)][string]$VersionRoot)
    $manifestPath = Join-Path $VersionRoot 'release-manifest.json'
    $manifest = Read-JsonFile -PathValue $manifestPath
    if ($null -eq $manifest) {
        throw "Release manifest not found: $manifestPath"
    }
    if ([string]$manifest.product -ne 'unity-agent-bridge' -or [string]$manifest.version -ne (Split-Path -Leaf $VersionRoot)) {
        throw "Release manifest identity does not match cache path: $VersionRoot"
    }
    if ([string]$manifest.platform -ne 'win-x64') {
        throw "Unsupported runtime platform: $($manifest.platform)"
    }
    if ([string]$manifest.protocolVersion -ne '1.0') {
        throw "Unsupported protocol version: $($manifest.protocolVersion)"
    }
    return $manifest
}

function Get-ChannelVersion {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$ChannelName)
    $channelPath = Join-Path (Join-Path $Root 'channels') ($ChannelName + '.json')
    $channel = Read-JsonFile -PathValue $channelPath
    if ($null -eq $channel -or [string]::IsNullOrWhiteSpace([string]$channel.version)) {
        throw "Channel is not configured: $ChannelName"
    }
    return [string]$channel.version
}

function Resolve-SelectedVersion {
    param([Parameter(Mandatory = $true)][string]$Root)
    $selection = $null
    if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
        $projectRoot = Get-ProjectRoot
        $selection = Read-JsonFile -PathValue (Get-SelectionPath -Root $projectRoot)
    }

    $exact = if ($null -ne $selection) { [string]$selection.runtimeVersion } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($exact)) {
        return $exact
    }

    $projectChannel = if ($null -ne $selection) { [string]$selection.channel } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($projectChannel)) {
        return Get-ChannelVersion -Root $Root -ChannelName $projectChannel
    }

    $defaultPath = Join-Path $Root 'default.json'
    $default = Read-JsonFile -PathValue $defaultPath
    $defaultChannel = if ($null -ne $default -and -not [string]::IsNullOrWhiteSpace([string]$default.channel)) { [string]$default.channel } else { 'stable' }
    return Get-ChannelVersion -Root $Root -ChannelName $defaultChannel
}

function Get-RuntimeExecutable {
    param([Parameter(Mandatory = $true)][string]$VersionRoot)
    $candidates = @(
        (Join-Path $VersionRoot 'runtime\win-x64\unity-agent-bridge.exe'),
        (Join-Path $VersionRoot 'runtime\UnityAgentBridge\cli\out\win-x64\unity-agent-bridge.exe'),
        (Join-Path $VersionRoot 'runtime\UnityAgentBridge\cli\out\win-x64\unity-agent-bridge.exe'),
        (Join-Path $VersionRoot 'package\com.unitymcp.agent-bridge\Tools~\UnityAgentBridge\cli\out\win-x64\unity-agent-bridge.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }
    throw "Runtime executable not found under release version: $VersionRoot"
}

function Test-ArtifactManifest {
    param([Parameter(Mandatory = $true)][object]$Manifest, [Parameter(Mandatory = $true)][string]$ExpectedVersion)
    foreach ($field in @('schemaVersion', 'product', 'packageName', 'version', 'packageVersion', 'runtimeVersion', 'protocolVersion', 'platform', 'artifactSha256')) {
        if ($null -eq $Manifest.$field) {
            throw "Release manifest field is missing: $field"
        }
    }
    if ([string]$Manifest.product -ne 'unity-agent-bridge' -or [string]$Manifest.packageName -ne 'com.unitymcp.agent-bridge') {
        throw 'Release manifest product identity is invalid.'
    }
    if ([string]$Manifest.version -ne $ExpectedVersion -or [string]$Manifest.packageVersion -ne $ExpectedVersion -or [string]$Manifest.runtimeVersion -ne $ExpectedVersion) {
        throw 'Release manifest package/runtime/version fields must match for the initial runtime contract.'
    }
    if ($ExpectedVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') { throw "Invalid runtime semantic version: $ExpectedVersion" }
    if ([string]$Manifest.platform -ne 'win-x64' -or [string]$Manifest.protocolVersion -ne '1.0') {
        throw 'Release manifest platform or protocol is unsupported.'
    }
    if ([string]$Manifest.commitSha -notmatch '^[0-9a-fA-F]{40}$' -or [string]$Manifest.artifactSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Release manifest commitSha or artifactSha256 is invalid.'
    }
}

function Assert-RuntimeChecksum {
    param([Parameter(Mandatory = $true)][object]$Manifest, [Parameter(Mandatory = $true)][string]$VersionRoot)
    $runtimePath = Get-RuntimeExecutable -VersionRoot $VersionRoot
    $actual = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::IsNullOrWhiteSpace([string]$Manifest.artifactSha256) -and $actual -ne ([string]$Manifest.artifactSha256).ToLowerInvariant()) {
        throw "Runtime checksum mismatch for $VersionRoot. expected=$($Manifest.artifactSha256) actual=$actual"
    }
}

function Register-ProjectReference {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$ProjectRoot)
    $registryPath = Join-Path $Root 'projects.json'
    $rawRegistry = Read-JsonFile -PathValue $registryPath
    if ($null -eq $rawRegistry) { $registry = @() }
    elseif ($rawRegistry.PSObject.Properties['value']) { $registry = @($rawRegistry.value) }
    elseif ($rawRegistry -is [System.Array]) { $registry = @($rawRegistry) }
    else { $registry = @($rawRegistry) }
    $normalized = [System.IO.Path]::GetFullPath($ProjectRoot)
    $registry = @($registry | Where-Object { [string]$_ -ne $normalized }) + $normalized
    Write-JsonFile -Value $registry -PathValue $registryPath
}

function Get-ReferencedVersions {
    param([Parameter(Mandatory = $true)][string]$Root)
    $referenced = @{}
    $registryPath = Join-Path $Root 'projects.json'
    $rawRegistry = Read-JsonFile -PathValue $registryPath
    $registeredProjects = if ($null -eq $rawRegistry) { @() }
        elseif ($rawRegistry.PSObject.Properties['value']) { @($rawRegistry.value) }
        elseif ($rawRegistry -is [System.Array]) { @($rawRegistry) }
        else { @($rawRegistry) }
    foreach ($registeredProject in $registeredProjects) {
        if ([string]::IsNullOrWhiteSpace([string]$registeredProject)) { continue }
        $selectionPath = Get-SelectionPath -Root ([string]$registeredProject)
        $selection = Read-JsonFile -PathValue $selectionPath
        if ($null -ne $selection -and -not [string]::IsNullOrWhiteSpace([string]$selection.runtimeVersion)) { $referenced[[string]$selection.runtimeVersion] = $true }
    }
    return $referenced
}

function Ensure-ManagerShim {
    param([Parameter(Mandatory = $true)][string]$Root)
    $binRoot = Join-Path $Root 'bin'
    New-Item -ItemType Directory -Path $binRoot -Force | Out-Null
    $shimPath = Join-Path $binRoot 'agent-bridge-mcp.cmd'
    $managerPath = Join-Path $PSScriptRoot 'AgentBridgeManager.cmd'
    try {
        @(
            '@echo off',
            'setlocal',
            'set "MANAGER_HOME=' + $Root + '"',
            'set "UNITY_AGENT_BRIDGE_HOME=%MANAGER_HOME%"',
            'if /I "%~1"=="--project-path" (',
            '  if "%~2"=="" (echo --project-path requires a value 1>&2 & exit /b 2)',
            '  set "UNITY_AGENT_BRIDGE_PROJECT_PATH=%~2"',
            ')',
            'call "' + $managerPath + '" -Command launch',
            'exit /b %ERRORLEVEL%'
        ) | Set-Content -LiteralPath $shimPath -Encoding ascii
    }
    catch {
        if (-not (Test-Path -LiteralPath $shimPath -PathType Leaf)) { throw }
    }
    return $shimPath
}

function Install-Artifact {
    param([Parameter(Mandatory = $true)][string]$Root)
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    if ([string]::IsNullOrWhiteSpace($ArtifactPath) -and [string]::IsNullOrWhiteSpace($ArtifactUrl)) {
        throw 'ArtifactPath or ArtifactUrl is required for install.'
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('UnityAgentBridgeInstall-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    try {
        $sourcePath = $ArtifactPath
        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $downloadPath = Join-Path $temporaryRoot 'artifact.zip'
            Invoke-WebRequest -Uri $ArtifactUrl -OutFile $downloadPath
            $sourcePath = $downloadPath
        }

        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $expectedArchiveHash = $ArtifactSha256
            $sidecarPath = $sourcePath + '.sha256'
            if ([string]::IsNullOrWhiteSpace($expectedArchiveHash) -and (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
                $expectedArchiveHash = ([string](Get-Content -LiteralPath $sidecarPath -Raw)).Split(' ')[0].Trim()
            }
            if ([System.IO.Path]::GetExtension($sourcePath) -eq '.zip' -and [string]::IsNullOrWhiteSpace($expectedArchiveHash)) {
                throw "Archive checksum sidecar or ArtifactSha256 is required for zip installation: $sourcePath"
            }
            if (-not [string]::IsNullOrWhiteSpace($expectedArchiveHash)) {
                if ($expectedArchiveHash.Trim() -notmatch '^[0-9a-fA-F]{64}$') {
                    throw "Artifact archive checksum is not a SHA-256 value: $expectedArchiveHash"
                }
                $actualArchiveHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($actualArchiveHash -ne $expectedArchiveHash.Trim().ToLowerInvariant()) {
                    throw "Artifact archive checksum mismatch. expected=$expectedArchiveHash actual=$actualArchiveHash"
                }
            }
        }

        $unpackedRoot = Join-Path $temporaryRoot 'unpacked'
        if (Test-Path -LiteralPath $sourcePath -PathType Container) {
            Copy-Item -LiteralPath $sourcePath -Destination $unpackedRoot -Recurse -Force
        }
        else {
            Expand-Archive -LiteralPath $sourcePath -DestinationPath $unpackedRoot -Force
        }

        $manifestFile = Get-ChildItem -LiteralPath $unpackedRoot -Recurse -File -Filter 'release-manifest.json' | Select-Object -First 1
        if ($null -eq $manifestFile) {
            throw 'Artifact does not contain release-manifest.json.'
        }
        $manifest = Read-JsonFile -PathValue $manifestFile.FullName
        $selectedVersion = [string]$manifest.version
        Test-ArtifactManifest -Manifest $manifest -ExpectedVersion $selectedVersion
        $sourceRoot = $manifestFile.Directory.FullName
        $destinationRoot = Get-VersionRoot -Root $Root -SelectedVersion $selectedVersion
        $versionsRoot = Join-Path $Root 'versions'
        New-Item -ItemType Directory -Path $versionsRoot -Force | Out-Null
        $lockPath = Join-Path $versionsRoot ($selectedVersion + '.lock')
        $lock = $null
        for ($attempt = 0; $attempt -lt 300 -and $null -eq $lock; $attempt++) {
            try {
                $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 100
            }
        }
        if ($null -eq $lock) { throw "Timed out waiting for runtime version lock: $selectedVersion" }
        try {
            $stagedRoot = Join-Path $versionsRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $stagedRoot -Force | Out-Null
            Copy-Item -Path (Join-Path $sourceRoot '*') -Destination $stagedRoot -Recurse -Force
            Assert-RuntimeChecksum -Manifest $manifest -VersionRoot $stagedRoot
            $existing = Test-Path -LiteralPath $destinationRoot
            if ($existing) {
                $existingManifest = Read-JsonFile -PathValue (Join-Path $destinationRoot 'release-manifest.json')
                if ($null -ne $existingManifest -and [string]$existingManifest.artifactSha256 -eq [string]$manifest.artifactSha256) {
                    Remove-Item -LiteralPath $stagedRoot -Recurse -Force
                    Ensure-ManagerShim -Root $Root | Out-Null
                    return [pscustomobject]@{ version = $selectedVersion; status = 'already_installed'; path = $destinationRoot }
                }
                throw "Refusing to overwrite immutable runtime version: $selectedVersion"
            }

            Move-Item -LiteralPath $stagedRoot -Destination $destinationRoot
        }
        finally { $lock.Dispose() }
        Ensure-ManagerShim -Root $Root | Out-Null
        return [pscustomobject]@{ version = $selectedVersion; status = 'installed'; path = $destinationRoot }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

function Save-ProjectSelection {
    param([Parameter(Mandatory = $true)][string]$Root, [string]$SelectedVersion, [string]$SelectedChannel)
    $projectRoot = Get-ProjectRoot
    $selection = [ordered]@{
        schemaVersion = '1.0'
        runtimeMode = 'machine'
        runtimeVersion = if ([string]::IsNullOrWhiteSpace($SelectedVersion)) { '' } else { $SelectedVersion }
        channel = if ([string]::IsNullOrWhiteSpace($SelectedChannel)) { '' } else { $SelectedChannel }
        managerHome = $Root
        updatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    Write-JsonFile -Value $selection -PathValue (Get-SelectionPath -Root $projectRoot)
    Register-ProjectReference -Root $Root -ProjectRoot $projectRoot
    return $selection
}

function Setup-Project {
    $root = Get-ProjectRoot
    $managerRoot = Get-ManagerHome
    New-Item -ItemType Directory -Path $managerRoot -Force | Out-Null
    $selectedVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) { $Version } else { '' }
    if ([string]::IsNullOrWhiteSpace($selectedVersion) -and [string]::IsNullOrWhiteSpace($Channel)) {
        $selectedVersion = Resolve-SelectedVersion -Root $managerRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($selectedVersion)) {
        $versionRoot = Get-VersionRoot -Root $managerRoot -SelectedVersion $selectedVersion
        if (-not (Test-Path -LiteralPath $versionRoot -PathType Container)) {
            throw "Runtime version is not installed: $selectedVersion"
        }
        Get-ReleaseManifest -VersionRoot $versionRoot | Out-Null
        $selection = Save-ProjectSelection -Root $managerRoot -SelectedVersion $selectedVersion -SelectedChannel ''
    }
    else {
        $selection = Save-ProjectSelection -Root $managerRoot -SelectedVersion '' -SelectedChannel $Channel
    }

    $manifestPath = Join-Path $root 'Packages\manifest.json'
    if ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and -not [string]::IsNullOrWhiteSpace($PackageUrl)) {
        $manifestJson = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($null -eq $manifestJson.dependencies) {
            $manifestJson | Add-Member -MemberType NoteProperty -Name dependencies -Value ([pscustomobject]@{})
        }
        $manifestJson.dependencies | Add-Member -MemberType NoteProperty -Name 'com.unitymcp.agent-bridge' -Value $PackageUrl -Force
        $manifestJson | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }

    return [pscustomobject]@{ status = 'configured'; projectPath = $root; selection = $selection; packageManifest = $manifestPath }
}

function Resolve-InstalledRuntime {
    $managerRoot = Get-ManagerHome
    $selectedVersion = Resolve-SelectedVersion -Root $managerRoot
    $versionRoot = Get-VersionRoot -Root $managerRoot -SelectedVersion $selectedVersion
    $manifest = Get-ReleaseManifest -VersionRoot $versionRoot
    $executable = Get-RuntimeExecutable -VersionRoot $versionRoot
    Assert-RuntimeChecksum -Manifest $manifest -VersionRoot $versionRoot
    return [pscustomobject]@{ version = $selectedVersion; root = $versionRoot; executable = $executable; manifest = $manifest }
}

function Write-Result {
    param([AllowEmptyCollection()][object]$Value)
    if ($Json) {
        $Value | ConvertTo-Json -Depth 20
    }
    else {
        $Value | Format-List | Out-String | Write-Output
    }
}

$managerRoot = Get-ManagerHome
switch ($Command) {
    'help' {
        Write-Output 'Agent Bridge machine runtime manager'
        Write-Output 'Commands: install, list, select, setup, resolve, doctor, rollback, cleanup, launch'
        exit 0
    }
    'install' { Write-Result (Install-Artifact -Root $managerRoot); exit 0 }
    'list' {
        $versionsRoot = Join-Path $managerRoot 'versions'
        $items = if (Test-Path -LiteralPath $versionsRoot) { @(Get-ChildItem -LiteralPath $versionsRoot -Directory | Where-Object { $_.Name -notlike '.staging-*' } | ForEach-Object { [pscustomobject]@{ version = $_.Name; path = $_.FullName; manifest = Test-Path -LiteralPath (Join-Path $_.FullName 'release-manifest.json') } }) } else { @() }
        Write-Result $items
        exit 0
    }
    'select' {
        if ([string]::IsNullOrWhiteSpace($Version) -and [string]::IsNullOrWhiteSpace($Channel)) { throw 'Version or Channel is required for select.' }
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            $selectedRoot = Get-VersionRoot -Root $managerRoot -SelectedVersion $Version
            if (-not (Test-Path -LiteralPath $selectedRoot -PathType Container)) { throw "Runtime version is not installed: $Version" }
            Get-ReleaseManifest -VersionRoot $selectedRoot | Out-Null
        }
        Write-Result (Save-ProjectSelection -Root $managerRoot -SelectedVersion $Version -SelectedChannel $Channel)
        exit 0
    }
    'setup' { Write-Result (Setup-Project); exit 0 }
    'resolve' { Write-Result (Resolve-InstalledRuntime); exit 0 }
    'doctor' {
        $resolved = Resolve-InstalledRuntime
        Write-Result ([pscustomobject]@{ status = 'ready'; version = $resolved.version; executable = $resolved.executable; protocolVersion = $resolved.manifest.protocolVersion; projectPath = (Get-ProjectRoot) })
        exit 0
    }
    'rollback' {
        if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Version is required for rollback.' }
        Write-Result (Save-ProjectSelection -Root $managerRoot -SelectedVersion $Version -SelectedChannel '')
        exit 0
    }
    'cleanup' {
        $referenced = Get-ReferencedVersions -Root $managerRoot
        if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) { $referenced = Get-ReferencedVersions -Root $managerRoot }
        $removed = @()
        $versionsRoot = Join-Path $managerRoot 'versions'
        if (Test-Path -LiteralPath $versionsRoot) {
            foreach ($versionDirectory in @(Get-ChildItem -LiteralPath $versionsRoot -Directory)) {
                $active = @(Get-ChildItem -LiteralPath $versionDirectory.FullName -File -Filter 'active-*.json' -ErrorAction SilentlyContinue)
                if (-not $referenced.ContainsKey($versionDirectory.Name) -and $active.Count -eq 0 -and $Force) {
                    Remove-Item -LiteralPath $versionDirectory.FullName -Recurse -Force
                    $removed += $versionDirectory.Name
                }
            }
        }
        Write-Result ([pscustomobject]@{ status = 'cleaned'; removed = $removed; retained = @($referenced.Keys) })
        exit 0
    }
    'launch' {
        $resolved = Resolve-InstalledRuntime
        $projectRoot = Get-ProjectRoot
        $env:UNITY_AGENT_BRIDGE_PROJECT_PATH = $projectRoot
        $env:UNITY_AGENT_BRIDGE_RUNTIME_MODE = 'machine'
        $activeMarker = Join-Path $resolved.root ("active-" + $PID + '.json')
        Write-JsonFile -Value ([ordered]@{ projectPath = $projectRoot; version = $resolved.version; pid = $PID; startedUtc = (Get-Date).ToUniversalTime().ToString('o') }) -PathValue $activeMarker
        Push-Location $projectRoot
        try { & $resolved.executable 'mcp-server'; exit $LASTEXITCODE } finally { Pop-Location; if (Test-Path -LiteralPath $activeMarker) { Remove-Item -LiteralPath $activeMarker -Force } }
    }
}
