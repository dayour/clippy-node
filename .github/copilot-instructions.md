# Copilot Instructions for OpenClaw Windows Hub

## Repository Overview

This is **OpenClaw Windows Hub** (`openclaw-windows-node`) — a native Windows companion suite for [OpenClaw](https://openclaw.ai), the AI-powered personal assistant. It is a .NET 10 / WinUI 3 monorepo targeting Windows 10 (20H2+) and Windows 11.

**Key capabilities exposed to agents:**
- System tray application with gateway WebSocket connection
- Local MCP (Model Context Protocol) HTTP server at `http://127.0.0.1:8765/`
- Windows Node: canvas (WebView2), screen capture, camera, location, command execution, TTS, browser proxy

---

## Required Validation After Every Change

All agents must run these steps after every code change before marking work complete:

```powershell
# 1. Full build
.\build.ps1

# 2. Required test suites
dotnet test .\tests\OpenClaw.Shared.Tests\OpenClaw.Shared.Tests.csproj
dotnet test .\tests\OpenClaw.Tray.Tests\OpenClaw.Tray.Tests.csproj
```

**First-run gotcha:** In a fresh worktree where `bin/` doesn't exist yet, `dotnet test --no-restore` silently no-ops. Omit `--no-restore` on the first run, or run `dotnet build` on the test project first.

**Worktree env var:** In linked git worktrees, set `OPENCLAW_REPO_ROOT` before running tests:
```powershell
$env:OPENCLAW_REPO_ROOT = (Get-Location).Path
```

---

## Project Structure

```
src/
  OpenClaw.Shared/              # Cross-platform shared library (net10.0): gateway client, capabilities, MCP, models
  OpenClaw.Connection/          # Connection lifecycle (net10.0): GatewayConnectionManager, GatewayRegistry, CredentialResolver
  OpenClaw.Chat/                # Native chat model and reducer (ChatTimelineReducer, ChatModels)
  OpenClawTray.FunctionalUI/    # Declarative WinUI helper (Component, hooks, elements)
  OpenClaw.Tray.WinUI/          # WinUI 3 tray application (net10.0-windows): primary app
  OpenClaw.Cli/                 # CLI validator using tray settings
  OpenClaw.SetupEngine/         # Local WSL gateway setup engine
  OpenClaw.WinNode.Cli/         # Standalone Windows node CLI

tests/
  OpenClaw.Shared.Tests/        # Required: 1,920 cases (capabilities, MCP, URL handling, exec approval)
  OpenClaw.Tray.Tests/          # Required: 1,178 cases (tray state, settings, onboarding, localization)
  OpenClaw.Connection.Tests/    # Connection architecture (registry, state machine, pairing)
  OpenClaw.Tray.UITests/        # WinUI/A2UI rendering
  OpenClaw.WinNode.Cli.Tests/   # Windows node CLI contract
  OpenClawTray.FunctionalUI.Tests/
  OpenClaw.E2ETests/            # E2E; CI-only by default (set OPENCLAW_RUN_E2E=1 locally)
```

---

## Architecture: Three-Layer Connection Stack

```
OpenClaw.Shared (net10.0)           — WebSocket transport, device identity, protocol models
    ↑
OpenClaw.Connection (net10.0)       — GatewayConnectionManager, GatewayRegistry, CredentialResolver
    ↑
OpenClaw.Tray.WinUI (net10.0-windows) — UI, tray icon, pages
```

**Never** create gateway clients directly in the tray UI — `GatewayConnectionManager` owns all connection lifecycle. Tray UI code observes `StateChanged` / `OperatorClientChanged` events only.

### Key Docs to Read First

Before changing connection, pairing, node, MCP, or tray UX behavior, read:

| Doc | What it covers |
|-----|----------------|
| `docs/CONNECTION_ARCHITECTURE.md` | Gateway registry, connection manager, credential precedence, migration, MCP-only mode, tray action behavior |
| `docs/MCP_MODE.md` | Local MCP server mode, `EnableNodeMode` / `EnableMcpServer` matrix |
| `docs/WINDOWS_NODE_TESTING.md` | Node capabilities, manual smokes, gateway-dependent behavior |
| `docs/ONBOARDING_WIZARD.md` | First-run setup flow, setup-code/bootstrap pairing, test isolation |

---

## Critical Invariants

### Credentials

- Gateway credentials are **not** in `SettingsData.Token` / `SettingsData.BootstrapToken`. `SettingsManager` may read those legacy fields for one-time migration only; **all new credential writes go through `GatewayRegistry`**.
- Credential files live at:
  - `%APPDATA%\OpenClawTray\gateways.json` — gateway records
  - `%APPDATA%\OpenClawTray\gateways\<gateway-id>\device-key-ed25519.json` — per-gateway keypair + tokens
- **Credential precedence (strict):** device token → SharedGatewayToken → BootstrapToken → no credential. Never downgrade a paired device from its device token.
- `InteractiveGatewayCredentialResolver` (for HTTP surfaces like chat URL) **prefers SharedGatewayToken** over DeviceToken — HTTP endpoints expect the shared token.

### Connection Manager

- `GatewayConnectionManager` owns operator/node connection state.
- UI surfaces must call `ConnectAsync` / `DisconnectAsync` / `ReconnectAsync` on the manager; never construct parallel `OpenClawGatewayClient` instances from the UI layer.
- Chat/canvas/tray actions must **visibly route users to Connection settings** when pairing is incomplete or credentials are missing — avoid silent no-ops.

### MCP-Only Mode

| EnableNodeMode | EnableMcpServer | Behavior |
|---|---|---|
| false | false | Operator-only tray |
| false | true | Local MCP server only; **no gateway required** |
| true | false | Gateway node only |
| true | true | Gateway node + local MCP server |

`EnableMcpServer=true`, `EnableNodeMode=false` must start local `NodeService` without requiring a gateway credential.

### Chat Timeline

Inbound chat/agent timeline events must include the gateway's canonical `sessionKey`. Do not synthesize a literal `main` key for keyless inbound events — drop the event and raise a one-shot diagnostic.

---

## Building

```powershell
# Prerequisites check
.\build.ps1 -CheckOnly

# Full solution (recommended)
.\build.ps1

# Tray app (WinUI requires an RID)
dotnet build src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj -r win-x64
dotnet build src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj -r win-arm64

# Cross-platform (shared library only)
dotnet build src/OpenClaw.Shared

# WinUI on Linux (CI-style)
dotnet build -p:EnableWindowsTargeting=true
```

### Local Inno Installer

```powershell
.\scripts\build-inno-local.ps1 -Arch x64 -Fast     # fast x64 for smoke testing
.\scripts\build-inno-local.ps1 -Arch All             # both architectures
```

---

## Testing

```powershell
# Required after code changes
$env:OPENCLAW_REPO_ROOT = (Get-Location).Path
.\build.ps1
dotnet test .\tests\OpenClaw.Shared.Tests\OpenClaw.Shared.Tests.csproj
dotnet test .\tests\OpenClaw.Tray.Tests\OpenClaw.Tray.Tests.csproj

# All solution tests (excluding E2E)
dotnet test

# Specific test class
dotnet test --filter "FullyQualifiedName~GatewayRegistryTests"

# E2E (local only, requires OPENCLAW_RUN_E2E=1)
$env:OPENCLAW_RUN_E2E = "1"
dotnet test .\tests\OpenClaw.E2ETests\OpenClaw.E2ETests.csproj -r win-x64

# Local sandbox integration (requires OPENCLAW_RUN_INTEGRATION=1 and built tray binaries)
.\build.ps1
$env:OPENCLAW_RUN_INTEGRATION = "1"
dotnet test .\tests\OpenClaw.Shared.Tests\OpenClaw.Shared.Tests.csproj --filter "FullyQualifiedName~Mxc"
```

### Test Isolation (Important)

`SettingsManager` loads `%APPDATA%\OpenClawTray\settings.json` by default. Tests must **not** use `new SettingsManager()` without an isolated settings directory, because local user settings (`EnableNodeMode=true`) change behavior.

Use a temp settings directory or set `OPENCLAW_TRAY_DATA_DIR` before the test process starts:

```csharp
// In tests, pass a temp directory to SettingsManager
var settingsDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
var settings = new SettingsManager(settingsDir);
```

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/OpenClaw.Shared/OpenClawGatewayClient.cs` | Operator WebSocket client, protocol v3, reconnect/backoff |
| `src/OpenClaw.Shared/WindowsNodeClient.cs` | Node protocol client |
| `src/OpenClaw.Shared/Capabilities/*.cs` | Node capability handlers (canvas, screen, camera, system, location, TTS, browser) |
| `src/OpenClaw.Shared/Mcp/McpHttpServer.cs` | Local MCP HTTP server (HttpListener @ 127.0.0.1:8765) |
| `src/OpenClaw.Shared/Mcp/McpToolBridge.cs` | Transport-agnostic JSON-RPC 2.0 bridge from capabilities to MCP tools |
| `src/OpenClaw.Shared/SettingsData.cs` | Settings model (`EnableNodeMode`, `EnableMcpServer`, etc.) |
| `src/OpenClaw.Connection/GatewayConnectionManager.cs` | Connection lifecycle, state machine, SSH tunnel, pairing |
| `src/OpenClaw.Connection/GatewayRegistry.cs` | Persistent gateway records and migration target |
| `src/OpenClaw.Connection/CredentialResolver.cs` | Device-token/shared/bootstrap credential precedence |
| `src/OpenClaw.Tray.WinUI/App.xaml.cs` | Main app: tray icon, startup wiring, gateway connection, event routing |
| `src/OpenClaw.Tray.WinUI/Services/NodeService.cs` | Orchestrates node capabilities, starts MCP server |
| `src/OpenClaw.Tray.WinUI/Services/SettingsManager.cs` | JSON settings persistence (`%APPDATA%\OpenClawTray\settings.json`) |
| `src/OpenClaw.Chat/ChatTimelineReducer.cs` | Chat state transitions |
| `src/OpenClaw.Tray.WinUI/Chat/OpenClawChatDataProvider.cs` | Adapts gateway client to chat provider |

---

## Versioning

- **Never** add `<Version>` literals to product `.csproj` files.
- Versions come from git tags (`vX.Y.Z` stable, `vX.Y.Z-alpha.N` alpha) via **GitVersion**.
- Untagged `main` builds are alpha prereleases.
- CI validates that tagged builds produce the exact tag SemVer before publishing artifacts.
- Use `AppVersionInfo.Version` / `AppVersionInfo.DisplayVersion` for user-visible version strings; do not hardcode version strings in code.

```powershell
# Get local version (requires full git history)
.\scripts\Get-OpenClawVersion.ps1 -Variable SemVer
```

---

## CI/CD

The **Build and Test** workflow (`ci.yml`) runs on `windows-latest`. Key jobs:
- `repo-hygiene` — ensures `.squad` files stay untracked
- `test` — GitVersion, builds, unit/integration tests
- `e2etests` — E2E suite
- `build (win-x64)` / `build (win-arm64)` — publish tray payload artifacts
- `release` — triggered by `v*` tags; signs executables, builds Inno installers, creates GitHub prerelease

CI must check out full history (`fetch-depth: 0`) for GitVersion to work.

**CI build of WinUI** uses `EnableWindowsTargeting=true` for cross-platform build on Linux runners.

**Executable signing policy:**
- Only `OpenClaw.Tray.WinUI.exe` is OpenClaw-signed.
- `wxc-exec.exe`, `createdump.exe`, `RestartAgent.exe` must **not** be OpenClaw-signed.
- Script `scripts\Test-ReleaseExecutableSignatures.ps1` enforces this.

---

## Localization

User-visible strings use `LocalizationHelper.GetString()`. Onboarding uses the `Onboarding_*` key namespace. Supported locales: English, French, Dutch, Chinese Simplified, Chinese Traditional. Translation files are in `src/OpenClaw.Tray.WinUI/Strings/<locale>/Resources.resw`.

---

## Common Errors and Workarounds

| Error | Cause | Fix |
|-------|-------|-----|
| `dotnet test --no-restore` reports 0 tests | `bin/` doesn't exist in fresh worktree | Omit `--no-restore` on first run, or `dotnet build` first |
| `SettingsManager` reads real user settings in tests | Test uses `new SettingsManager()` | Pass temp dir or set `OPENCLAW_TRAY_DATA_DIR` |
| Build fails locking output assemblies | Running tray process locks `bin/` | Stop/close the tray process, then rebuild |
| `wt.exe` resolves to WorkTrunk, not Windows Terminal | Name collision | Use full Windows Terminal path explicitly |
| GitVersion fails or gives wrong version | Shallow clone missing tag history | Ensure `fetch-depth: 0` in checkout; run `git fetch --unshallow origin` if needed |
| `.squad` files tracked by Git | Accidentally staged | Add to `.gitignore`, run `git rm --cached .squad` |
| `GatewayRecord` has empty credentials after migration | Legacy `SettingsData.Token` not migrated | `GatewayRegistry.MigrateFromSettings()` runs on first startup; check migration logs |

---

## Security Conventions

- Canvas blocks `file://`, `javascript:`, localhost, private IPs, and IPv6 localhost.
- Only `ms-settings:` URIs for permissions and `http/https` for browser-launch links are allowed in onboarding.
- Token/secret query params are stripped from all log output (`TokenSanitizer`).
- MCP server bearer token lives at `%APPDATA%\OpenClawTray\mcp-token.txt` (43-char base64url, 256-bit entropy). It is only created after the user enables MCP in Settings.
- `screen.record` must be explicitly allowed by the gateway allowlist.
- Node Mode must be explicitly enabled by the user.
- Do not introduce new security vulnerabilities; apply `GatewayUrlHelper` for URL validation and `InputValidator` for onboarding input.

---

## Adding New Node Capabilities

1. Implement `INodeCapability` in `src/OpenClaw.Shared/Capabilities/`.
2. Register it with `NodeService.Register(cap)` — this automatically exposes it over both gateway WebSocket and local MCP HTTP (no MCP-side code changes needed).
3. Add unit tests in `OpenClaw.Shared.Tests`.

---

## Useful Scripts

```powershell
.\build.ps1                              # Build everything
.\run-app-local.ps1                      # Build + launch tray app (unpackaged)
.\scripts\Get-OpenClawVersion.ps1 -Variable SemVer   # Local SemVer
.\scripts\build-inno-local.ps1 -Arch x64 -Fast       # Local unsigned installer
```
