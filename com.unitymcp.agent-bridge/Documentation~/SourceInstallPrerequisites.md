# Source installation prerequisites

Download & Install runs source-build preflight before downloading tagged source, including the source fallback for unavailable binary releases. Progress shows Checking source-build prerequisites. Failed admission reports `source_preflight_failed`; Copy Error copies the environment/tool/path diagnostic and remediation. Installing a verified cached runtime ZIP does not require a source-build SDK or workspace.

Source builds require Windows PowerShell 5.1 or newer, .NET 8 SDK, and a writable user-temp directory. Python is not required. PowerShell 7 is preferred when available but is not mandatory. Windows long-path support is inspected rather than modified.

The builder uses a unique short `agb-*` directory under the process user-temp root, outside imported Unity package/Assets content. Project cache placement and build workspace placement are independent. Under Windows PowerShell 5.1 without confirmed long-path support, preflight reserves a 259-character extraction budget; the downloaded ZIP's actual entry paths are validated before any payload is written. Successful short-path preflight permits installation without registry changes.

## Failure remediation

| Diagnostic | Action |
|---|---|
| SOURCE_DOTNET_UNAVAILABLE / SOURCE_DOTNET8_SDK_MISSING | Install .NET 8 SDK or correct the configured dotnet executable, then restart Unity. A newer SDK alone does not satisfy the pinned .NET 8 build requirement. |
| SOURCE_POWERSHELL_UNSUPPORTED | Install a supported PowerShell, preferably PowerShell 7, and restart Unity so its PATH is refreshed. |
| SOURCE_WORKSPACE_PATH_TOO_LONG / SOURCE_ARCHIVE_PATH_TOO_LONG / SOURCE_ARCHIVE_PATH_UNSUPPORTED | Choose a shorter writable TEMP/TMP directory and restart Unity. Copy the complete diagnostic for the path and active budget. |
| SOURCE_WORKSPACE_NOT_WRITABLE | Correct user-temp directory access or select a writable TEMP/TMP and restart Unity. |
| SOURCE_WORKSPACE_IMPORTED | Set TEMP/TMP outside imported package content and Unity Assets/Packages, then restart Unity. |
| SOURCE_ARCHIVE_PATH_INVALID / SOURCE_ARCHIVE_PATH_DUPLICATE | Reject the archive; retain its exact identity and Copy Error output for investigation. |

Runtime ZIP entry paths are explicitly normalized to forward slashes for portable payload lookup across PowerShell implementations. The extractor does not use Expand-Archive rollback. Primary extraction/build exceptions remain the operation error. A cleanup problem is reported as `SOURCE_WORKSPACE_CLEANUP_FAILED (secondary)` with the owned workspace path; cleanup never deletes source archives, installed runtimes or unrelated directories, and refuses reparse points. The installer retains both stdout and stderr in failed-build feedback.

These source changes retain package version 1.4.3-rc.3. Implementation does not rewrite the existing RC3 tag or hosted release, edit Windows configuration, or install missing tools automatically.

## Stable promotion — 2026-10-17

The maintainer accepted the repair and authorized stable 1.4.3. The stable package includes these checks and the portable ZIP writer; the RC3 implementation identity described above is historical. Install the v1.4.3 package and matching 1.4.3 runtime separately. Retired 1.4.3 RC tags are no longer fresh-install entrypoints.
