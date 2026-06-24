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
$projectPath = Join-Path $sourceRoot "UnityAgentBridge.RoslynCompiler\UnityAgentBridge.RoslynCompiler.csproj"
$runtimeRoot = Resolve-OutputRoot -ExplicitOutputRoot $OutputRoot -ProjectPath $UnityProjectPath
$outputPath = Join-Path $runtimeRoot "UnityAgentBridge\roslyn-execution\out\win-x64"

if ($Rid -ne "win-x64") {
    throw "Only win-x64 is supported by the package-contained local runtime build."
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Package-contained Roslyn compiler project not found: $projectPath"
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

$publishedPath = Join-Path $outputPath "UnityAgentBridge.RoslynCompiler.exe"
$productPath = Join-Path $outputPath "unity-roslyn-compiler.exe"
if (-not (Test-Path -LiteralPath $publishedPath)) {
    throw "Published Roslyn compiler executable not found: $publishedPath"
}

Move-Item -LiteralPath $publishedPath -Destination $productPath -Force

Get-ChildItem -LiteralPath $outputPath |
    Where-Object { $_.Name -ne "unity-roslyn-compiler.exe" } |
    Remove-Item -Force -Recurse

Write-Output $productPath
