Set-StrictMode -Version Latest

function Get-AgentBridgeSourcePathLimit {
    param([int]$PowerShellMajor, [int]$LongPathsEnabled)
    if ($PowerShellMajor -lt 6 -and $LongPathsEnabled -ne 1) { return 259 }
    return [int]::MaxValue
}

function New-AgentBridgeSourceBuildWorkspace {
    param([int]$ReservedArchivePathLength = 180)
    if ($PSVersionTable.PSVersion.Major -lt 5 -or
        ($PSVersionTable.PSVersion.Major -eq 5 -and $PSVersionTable.PSVersion.Minor -lt 1)) {
        throw 'SOURCE_POWERSHELL_UNSUPPORTED: PowerShell 5.1 or newer is required. Install PowerShell 7 and restart Unity.'
    }
    $longPaths = 0
    try {
        $longPaths = [int](Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name LongPathsEnabled -ErrorAction Stop).LongPathsEnabled
    }
    catch { } # Unknown Windows configuration uses the conservative legacy budget.
    $pathLimit = Get-AgentBridgeSourcePathLimit -PowerShellMajor $PSVersionTable.PSVersion.Major -LongPathsEnabled $longPaths
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $workRoot = Join-Path $tempRoot ('agb-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $ancestor = $tempRoot
    while (-not [string]::IsNullOrWhiteSpace($ancestor)) {
        $relative = $workRoot.Substring($ancestor.Length).TrimStart('\', '/')
        $ignored = @($relative.Split([char[]]'\/') | Where-Object { $_.EndsWith('~') }).Count -gt 0
        $unityPackage = $false
        $manifest = Join-Path $ancestor 'package.json'
        if (Test-Path -LiteralPath $manifest -PathType Leaf) {
            try {
                $metadata = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
                $unityPackage = $null -ne $metadata.PSObject.Properties['unity']
            }
            catch { }
        }
        if ($unityPackage -and -not $ignored) {
            throw 'SOURCE_WORKSPACE_IMPORTED: TEMP/TMP is inside an imported package. Use a short external user-temp directory and restart Unity.'
        }
        if ((Test-Path -LiteralPath (Join-Path $ancestor 'ProjectSettings') -PathType Container) -and -not $ignored -and
            ($relative -match '^(Assets|Packages)[\\/]')) {
            throw 'SOURCE_WORKSPACE_IMPORTED: TEMP/TMP is inside Unity Assets/Packages. Use a short external user-temp directory and restart Unity.'
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    $predictedLength = (Join-Path $workRoot 's').Length + 1 + $ReservedArchivePathLength
    if ($predictedLength -gt $pathLimit) {
        throw "SOURCE_WORKSPACE_PATH_TOO_LONG: PowerShell $($PSVersionTable.PSVersion), LongPathsEnabled=$longPaths; extraction needs $predictedLength characters (limit $pathLimit). Set TEMP/TMP to a shorter writable user directory and restart Unity. Workspace: $workRoot"
    }
    $created = $false
    try {
        New-Item -ItemType Directory -Path $workRoot -ErrorAction Stop | Out-Null
        $created = $true
        $probe = Join-Path $workRoot 'write-check'
        [IO.File]::WriteAllText($probe, 'agent-bridge')
        if ([IO.File]::ReadAllText($probe) -cne 'agent-bridge') { throw 'Workspace readback failed.' }
        [IO.File]::Delete($probe)
    }
    catch {
        # Only remove this newly generated empty/probe directory; never touch the TEMP root.
        try { if ($created -and [IO.Directory]::Exists($workRoot)) { [IO.Directory]::Delete($workRoot, $false) } } catch { }
        throw "SOURCE_WORKSPACE_NOT_WRITABLE: Choose a writable short TEMP/TMP and restart Unity. $workRoot. $($_.Exception.Message)"
    }
    return [pscustomobject]@{
        TempRoot = $tempRoot
        WorkRoot = $workRoot
        PathLimit = $pathLimit
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        LongPathsEnabled = $longPaths
    }
}

function Remove-AgentBridgeSourceBuildWorkspace {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Workspace)
    try {
        $root = [IO.Path]::GetFullPath($Workspace.WorkRoot)
        $parent = [IO.Path]::GetFullPath($Workspace.TempRoot).TrimEnd('\', '/')
        if ((Split-Path -Parent $root) -cne $parent -or (Split-Path -Leaf $root) -notmatch '^agb-[0-9a-f]{12}$') {
            throw 'Cleanup refused a workspace outside its exact owned temp boundary.'
        }
        if (-not (Test-Path -LiteralPath $root)) { return }
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($root)
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            if ((Get-Item -LiteralPath $directory -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Cleanup refused a workspace containing a reparse point.'
            }
            foreach ($child in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
                if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Cleanup refused a reparse point.' }
                if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            }
        }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Warning -WarningAction Continue "SOURCE_WORKSPACE_CLEANUP_FAILED (secondary), $($Workspace.WorkRoot): $($_.Exception.Message)"
    }
}

function Expand-AgentBridgeSourceArchive {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$Destination,
        [int]$PathLimit = 259
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count -gt 0) {
        throw 'SOURCE_EXTRACTION_NOT_EMPTY: Refusing to extract over an existing directory.'
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $destinations = New-Object 'System.Collections.Generic.Dictionary[string,bool]' ([StringComparer]::OrdinalIgnoreCase)
        $entries = @()
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar).Replace('\', [IO.Path]::DirectorySeparatorChar)
            $directory = $relative.EndsWith([string][IO.Path]::DirectorySeparatorChar)
            $relative = $relative.TrimEnd('\', '/')
            if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or
                @($relative.Split([IO.Path]::DirectorySeparatorChar) | Where-Object { $_ -eq '.' -or $_ -eq '..' -or $_.EndsWith('.') -or $_.EndsWith(' ') }).Count -gt 0 -or
                ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000)) {
                throw "SOURCE_ARCHIVE_PATH_INVALID: Unsafe ZIP entry '$($entry.FullName)'."
            }
            try { $target = [IO.Path]::GetFullPath((Join-Path $root $relative)) }
            catch { throw "SOURCE_ARCHIVE_PATH_UNSUPPORTED: '$($entry.FullName)'. Use a shorter TEMP/TMP. $($_.Exception.Message)" }
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "SOURCE_ARCHIVE_PATH_INVALID: ZIP entry escapes extraction root: $($entry.FullName)"
            }
            if ($target.Length -gt $PathLimit) {
                throw "SOURCE_ARCHIVE_PATH_TOO_LONG: '$($entry.FullName)' expands to $($target.Length) characters (limit $PathLimit). Set a shorter TEMP/TMP and restart Unity."
            }
            if ($destinations.ContainsKey($target)) { throw "SOURCE_ARCHIVE_PATH_DUPLICATE: $($entry.FullName)" }
            $destinations.Add($target, $directory)
            $entries += [pscustomobject]@{ Entry = $entry; Target = $target; Directory = $directory }
        }
        foreach ($item in $entries) {
            $ancestor = Split-Path -Parent $item.Target
            while ($ancestor.Length -gt $root.Length) {
                if ($destinations.ContainsKey($ancestor) -and -not $destinations[$ancestor]) {
                    throw "SOURCE_ARCHIVE_PATH_INVALID: File is an ancestor of '$($item.Entry.FullName)'."
                }
                $ancestor = Split-Path -Parent $ancestor
            }
        }
        # All destinations have passed admission before the first archive payload write.
        [IO.Directory]::CreateDirectory($root) | Out-Null
        foreach ($item in $entries) {
            if ($item.Directory) { [IO.Directory]::CreateDirectory($item.Target) | Out-Null }
            else {
                [IO.Directory]::CreateDirectory((Split-Path -Parent $item.Target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($item.Entry, $item.Target, $false)
            }
        }
    }
    finally { $archive.Dispose() }
}

function Compress-AgentBridgeRuntimeArchive {
    param([Parameter(Mandatory)][string]$SourceDirectory, [Parameter(Mandatory)][string]$ArchivePath)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $zip = $null
    try {
        $zip = New-Object IO.Compression.ZipArchive ($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($file in @(Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse -Force -ErrorAction Stop)) {
            $fullName = [IO.Path]::GetFullPath($file.FullName)
            if (-not $fullName.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
                ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Runtime ZIP source escapes its owned stage.' }
            $entryName = $fullName.Substring($root.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $fullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { if ($null -ne $zip) { $zip.Dispose() }; $stream.Dispose() }
}
