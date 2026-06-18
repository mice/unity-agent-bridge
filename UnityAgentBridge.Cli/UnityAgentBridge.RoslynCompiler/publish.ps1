param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$rid = 'win-x64'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$productRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $productRoot 'UnityAgentBridge.Cli\UnityAgentBridge.RoslynCompiler\UnityAgentBridge.RoslynCompiler.csproj'
$outputPath = Join-Path $productRoot 'com.unitymcp.agent-bridge\Tools~\UnityAgentBridge\roslyn-execution\out\win-x64'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Roslyn compiler project not found: $projectPath"
}

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

$publishedPath = Join-Path $outputPath 'UnityAgentBridge.RoslynCompiler.exe'
$productPath = Join-Path $outputPath 'unity-roslyn-compiler.exe'
if (-not (Test-Path -LiteralPath $publishedPath)) {
    throw "Published Roslyn compiler executable not found: $publishedPath"
}

Move-Item -LiteralPath $publishedPath -Destination $productPath -Force

Get-ChildItem -LiteralPath $outputPath | Where-Object { $_.Name -ne 'unity-roslyn-compiler.exe' } | Remove-Item -Force -Recurse
