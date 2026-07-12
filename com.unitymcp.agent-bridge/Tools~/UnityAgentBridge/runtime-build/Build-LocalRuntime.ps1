[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$UnityProjectPath,
    [string]$Rid = "win-x64",
    [string]$DotnetPath = "dotnet"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$cliScript = Join-Path $scriptRoot "Publish-UnityAgentBridgeCli.ps1"
$roslynScript = Join-Path $scriptRoot "Publish-UnityAgentBridgeRoslynCompiler.ps1"
$toolsRoot = Split-Path -Parent $scriptRoot
$luaLintSource = Join-Path $toolsRoot ("lua-gc-lint\out\{0}\lua-gc-lint.exe" -f $Rid)
$luaLintDestination = Join-Path $OutputRoot ("UnityAgentBridge\lua-gc-lint\out\{0}\lua-gc-lint.exe" -f $Rid)

if (-not (Test-Path -LiteralPath $cliScript)) {
    throw "CLI publish script not found: $cliScript"
}

if (-not (Test-Path -LiteralPath $roslynScript)) {
    throw "Roslyn publish script not found: $roslynScript"
}

if (-not (Test-Path -LiteralPath $luaLintSource)) {
    throw "Lua GC lint payload not found: $luaLintSource"
}

& $cliScript -OutputRoot $OutputRoot -UnityProjectPath $UnityProjectPath -Rid $Rid -DotnetPath $DotnetPath
if ($LASTEXITCODE -ne 0) {
    throw "CLI runtime build failed."
}

& $roslynScript -OutputRoot $OutputRoot -UnityProjectPath $UnityProjectPath -Rid $Rid -DotnetPath $DotnetPath
if ($LASTEXITCODE -ne 0) {
    throw "Roslyn compiler runtime build failed."
}

$luaLintDirectory = Split-Path -Parent $luaLintDestination
New-Item -ItemType Directory -Path $luaLintDirectory -Force | Out-Null
Copy-Item -LiteralPath $luaLintSource -Destination $luaLintDestination -Force
