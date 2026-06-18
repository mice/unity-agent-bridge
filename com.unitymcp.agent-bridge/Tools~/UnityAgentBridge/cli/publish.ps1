param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$RIDS = @('win-x64','linux-x64','osx-arm64')
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$productRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptRoot)))
$projectPath = Join-Path $productRoot 'UnityAgentBridge.Cli\UnityAgentBridge.Cli\UnityAgentBridge.Cli.csproj'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Console App project not found: $projectPath"
}

foreach ($rid in $RIDS) {
    $outputPath = Join-Path $scriptRoot ("out\" + $rid)
    dotnet publish $projectPath `
      --configuration Release `
      --runtime $rid `
      --self-contained true `
      -p:PublishSingleFile=true `
      -p:IncludeNativeLibrariesForSelfExtract=true `
      -p:PublishAot=false `
      -p:DebugSymbols=false `
      -p:DebugType=None `
      --output $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for RID '$rid'."
    }

    $publishedName = if ($rid -eq 'win-x64') { 'UnityAgentBridge.Cli.exe' } else { 'UnityAgentBridge.Cli' }
    $productName = if ($rid -eq 'win-x64') { 'unity-agent-bridge.exe' } else { 'unity-agent-bridge' }
    $publishedPath = Join-Path $outputPath $publishedName
    $productPath = Join-Path $outputPath $productName
    if (-not (Test-Path -LiteralPath $publishedPath)) {
        throw "Published CLI executable not found: $publishedPath"
    }

    Move-Item -LiteralPath $publishedPath -Destination $productPath -Force

    $pdbPath = Join-Path $outputPath "unity-agent-bridge.pdb"
    if (Test-Path $pdbPath) {
        Remove-Item -LiteralPath $pdbPath -Force
    }
}
