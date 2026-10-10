# Optional verified-analysis providers

The core package contains runtime-neutral Agent Runtime/AgentTask contracts, fixed Specialist templates, evidence loading, and analysis output/citation validation. A runner plugin is required to execute those templates. Compile, test and ordinary bridge tools operate without Pi.

Pi is supplied separately by the workbench's `Plugins/PiAgent/` source distribution as `com.unitymcp.pi-agent`, following the manifest-declared managed-DLL UPM pattern used for Lua and Android Debugging. Pi plugin 0.1.0 requires Agent Bridge 1.4.0 in both its UPM dependency and manifest minimum compatibility. Build it locally against those neutral contracts, install the generated package and explicitly enable Pi Agent in **MCP Setup & Diagnostics → Installed UnityMCP Plugins**. Core 1.3.1 does not include the new provider contracts.

The two canonical tools appear only with an enabled, ready plugin:

- `unity_analysis_compile_failure`
- `unity_analysis_asset_problem`

The MCP runtime must also come from matching 1.4.0 source. A pre-extraction machine runtime retains unconditional metadata. Local 1.4.0 candidate builds do not establish remote publication; until the release is published, use the documented project-local/source-build workflow instead of an older published machine runtime.

Their closed input remains `{ problemId, evidenceIds, timeoutMs }`; timeout is at most 90,000 ms. Core template/evidence/citation validation remains mandatory. There is no public generic agent execution or caller model, prompt, tools or folder override.

Pi owns **Configure**, project settings at `Library/AgentBridge/plugins/pi-agent/settings.json`, guard payloads and process/RPC behavior. Valid legacy `pi*` settings migrate once without deleting old values or unrelated core settings. Invalid legacy/configuration data blocks execution. Defaults remain Pi 0.85.1, DeepSeek Flash and bounded read-only tools.

Matching plugin refresh preserves runs. Disable/removal/readiness loss cancels owned runs and prevents new execution; terminal task queries/results remain available through core task control in the current Editor domain. Package/DLL changes can reload that domain; task recovery across reload is still unsupported.

The provider seam is generic. Codex CLI is a possible later adapter; this change implements Pi only. Current Pi workflows are preliminary. MCP SDK 2.x long-task persistence/reconnect/cleanup optimization is a separate next-version effort.
