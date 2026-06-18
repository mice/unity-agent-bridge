@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "TOOLS_ROOT=%%~fI"
set "CONFIG_PATH=%SCRIPT_DIR%Start-Codex-With-UnityMcp.json"
set "CONFIG_UNITY_PROJECT="
set "UNITY_PROJECT=%UNITY_AGENT_BRIDGE_PROJECT_PATH%"
set "CLI_EXE=%UNITY_AGENT_BRIDGE_CLI_PATH%"
set "CLI_ROOT=%TOOLS_ROOT%\UnityAgentBridge\cli"

if "%CLI_EXE%"=="" (
  set "CLI_EXE=%CLI_ROOT%\out\win-x64\unity-agent-bridge.exe"
)

if not exist "%CLI_EXE%" (
  set "CLI_EXE=%CLI_ROOT%\unity-agent-bridge.exe"
)

if not exist "%CLI_EXE%" (
  echo Missing Unity Agent Bridge executable: "%CLI_EXE%"
  echo Expected a prepared CLI payload under "%CLI_ROOT%".
  exit /b 1
)

if exist "%CONFIG_PATH%" (
  set "MCP_LAUNCHER_CONFIG=%CONFIG_PATH%"
  for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:MCP_LAUNCHER_CONFIG; if (Test-Path -LiteralPath $p) { try { $json = Get-Content -LiteralPath $p -Raw | ConvertFrom-Json; $value = if ($null -ne $json) { [string]$json.unityProjectPath } else { '' }; if (-not [string]::IsNullOrWhiteSpace($value)) { [Console]::Out.Write([System.IO.Path]::GetFullPath($value.Trim())) } } catch { exit 1 } }"`) do set "CONFIG_UNITY_PROJECT=%%I"
  if errorlevel 1 (
    echo Failed to parse launcher config "%CONFIG_PATH%".
    echo Expected JSON: { "unityProjectPath": "D:\\Path\\To\\UnityProject" }
    exit /b 1
  )
)

if "%UNITY_PROJECT%"=="" (
  if not "%CONFIG_UNITY_PROJECT%"=="" (
    set "UNITY_PROJECT=%CONFIG_UNITY_PROJECT%"
  )
)

if not "%UNITY_PROJECT%"=="" (
  if not exist "%UNITY_PROJECT%" (
    echo Missing Unity project path "%UNITY_PROJECT%".
    exit /b 1
  )
  set "UNITY_AGENT_BRIDGE_PROJECT_PATH=%UNITY_PROJECT%"
)

for %%I in ("%CLI_EXE%") do set "CLI_WORKDIR=%%~dpI"
pushd "%CLI_WORKDIR%" >nul
"%CLI_EXE%" mcp-server
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul
exit /b %EXIT_CODE%
