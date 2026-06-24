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

if (-not (Test-Path -LiteralPath $cliScript)) {
    throw "CLI publish script not found: $cliScript"
}

if (-not (Test-Path -LiteralPath $roslynScript)) {
    throw "Roslyn publish script not found: $roslynScript"
}

& $cliScript -OutputRoot $OutputRoot -UnityProjectPath $UnityProjectPath -Rid $Rid -DotnetPath $DotnetPath
if ($LASTEXITCODE -ne 0) {
    throw "CLI runtime build failed."
}

& $roslynScript -OutputRoot $OutputRoot -UnityProjectPath $UnityProjectPath -Rid $Rid -DotnetPath $DotnetPath
if ($LASTEXITCODE -ne 0) {
    throw "Roslyn compiler runtime build failed."
}
