Third-party source vendored for TOML parsing used by `CodexTomlConfigEditor`.

- Library: `Tommy`
- Upstream: `https://github.com/dezhidki/Tommy`
- File: `Tommy.cs`

Reason:
- Replace fragile text-based `config.toml` section replacement with real TOML parsing.
- Avoid Unity precompiled assembly friction and transitive runtime dependency issues from heavier NuGet packages.
