#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
CONFIG_PATH="$SCRIPT_DIR/Start-Codex-With-UnityMcp.json"
UNITY_PROJECT="${UNITY_AGENT_BRIDGE_PROJECT_PATH:-}"
TOOLS_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
CLI_EXE="${UNITY_AGENT_BRIDGE_CLI_PATH:-}"
CLI_ROOT="$TOOLS_ROOT/UnityAgentBridge/cli"

if [ -z "$CLI_EXE" ]; then
  CLI_EXE="$CLI_ROOT/out/win-x64/unity-agent-bridge.exe"
fi

if [ ! -f "$CLI_EXE" ]; then
  CLI_EXE="$CLI_ROOT/unity-agent-bridge.exe"
fi

if [ ! -f "$CLI_EXE" ]; then
  echo "Missing Unity Agent Bridge executable: $CLI_EXE"
  echo "Expected a prepared CLI payload under \"$CLI_ROOT\"."
  exit 1
fi

if [ -z "$UNITY_PROJECT" ] && [ -f "$CONFIG_PATH" ]; then
  if ! CONFIG_UNITY_PROJECT=$(CONFIG_PATH="$CONFIG_PATH" python -c 'import json, os, pathlib, sys; p=os.environ["CONFIG_PATH"]; data=json.load(open(p, "r", encoding="utf-8")); value=str(data.get("unityProjectPath","")).strip(); sys.stdout.write(str(pathlib.Path(value).resolve()) if value else "")'); then
    echo "Failed to parse launcher config \"$CONFIG_PATH\"."
    echo 'Expected JSON: { "unityProjectPath": "/path/to/UnityProject" }'
    exit 1
  fi

  if [ -n "$CONFIG_UNITY_PROJECT" ]; then
    UNITY_PROJECT="$CONFIG_UNITY_PROJECT"
  fi
fi

if [ -n "$UNITY_PROJECT" ]; then
  if [ ! -e "$UNITY_PROJECT" ]; then
    echo "Missing Unity project path \"$UNITY_PROJECT\"."
    exit 1
  fi
  export UNITY_AGENT_BRIDGE_PROJECT_PATH="$UNITY_PROJECT"
fi

cd "$(dirname "$CLI_EXE")"
exec "$CLI_EXE" mcp-server
