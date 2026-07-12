[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$UnityProjectPath,
    [string]$Rid = "win-x64",
    [string]$DotnetPath = "dotnet"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function Resolve-OutputRoot {
    param(
        [string]$ExplicitOutputRoot,
        [string]$ProjectPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitOutputRoot)) {
        return $ExplicitOutputRoot
    }

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        throw "OutputRoot or UnityProjectPath is required."
    }

    return (Join-Path $ProjectPath ".unitymcp\runtime")
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$payloadRoot = Split-Path -Parent $scriptRoot
$sourceRoot = Join-Path $payloadRoot "src"
$projectPath = Join-Path $sourceRoot "UnityAgentBridge.Cli\UnityAgentBridge.Cli.csproj"
$runtimeRoot = Resolve-OutputRoot -ExplicitOutputRoot $OutputRoot -ProjectPath $UnityProjectPath
$outputPath = Join-Path $runtimeRoot ("UnityAgentBridge\cli\out\" + $Rid)

if ($Rid -ne "win-x64") {
    throw "Only win-x64 is supported by the package-contained local runtime build."
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Package-contained CLI project not found: $projectPath"
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

& $DotnetPath publish $projectPath `
  --configuration Release `
  --runtime $Rid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishAot=false `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for RID '$Rid'."
}

$publishedPath = Join-Path $outputPath "UnityAgentBridge.Cli.exe"
$productPath = Join-Path $outputPath "unity-agent-bridge.exe"
if (-not (Test-Path -LiteralPath $publishedPath)) {
    throw "Published CLI executable not found: $publishedPath"
}

Copy-Item -LiteralPath $publishedPath -Destination $productPath -Force
Remove-Item -LiteralPath $publishedPath -Force

Get-ChildItem -LiteralPath $outputPath |
    Where-Object { $_.Name -ne "unity-agent-bridge.exe" } |
    Remove-Item -Force -Recurse

Write-Output $productPath
