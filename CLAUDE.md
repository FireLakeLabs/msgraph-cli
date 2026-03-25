# CLAUDE.md — msgraph-cli

## What This Is

A CLI tool for interacting with Microsoft 365 via the Microsoft Graph API. Designed for unattended agent use (NetClaw, Claude Code, etc.) with JSON output and secure 1Password-backed credential storage.

## Quick Reference

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run --project src/MsGraphCli -- --help
dotnet run --project src/MsGraphCli -- auth status --json
dotnet run --project src/MsGraphCli -- mail list --json --max 10

# Publish single binary (linux-x64)
dotnet publish src/MsGraphCli -c Release -r linux-x64 --self-contained -o ./publish
```

## Architecture

Three-layer design. CLI depends on Core. Core has zero dependency on CLI.

```
CLI (MsGraphCli)           → Thin shell: parse args, call service, format output
Core (MsGraphCli.Core)     → All business logic: services, auth, Graph client, models
Tests (MsGraphCli.Tests)   → xUnit, mocked Graph client and 1Password
```

### Project Layout

```
src/MsGraphCli/              CLI entry point
  Commands/                  System.CommandLine command definitions
                             Auth, Mail, Calendar, Drive, Tasks, Excel, Docs, Config, Completions
  Middleware/                 ActionRunner (exception handling), CommandGuard (readonly/allowlist)
  Output/                    IOutputFormatter implementations (JSON, table, plain)
  Program.cs                 Root command + global options
  GlobalOptions.cs           Shared option references

src/MsGraphCli.Core/         Library (future NuGet extraction target)
  Auth/                      ISecretStore, OnePasswordSecretStore, GraphAuthProvider,
                             TokenCacheHelper, ScopeRegistry
  Config/                    AppConfig
  Exceptions/                Typed exceptions with exit codes
  Graph/                     GraphClientFactory, MsalAccessTokenProvider,
                             PaginationHelper, RetryHandler
  Models/                    Record DTOs (Mail, Calendar, Drive, Task, Excel, Document models)
  Services/                  MailService, CalendarService, DriveService, TasksService,
                             ExcelService, DocumentService (all with interfaces)

src/MsGraphCli.Tests/        Test project
  Unit/                      Mocked tests for all services + infrastructure
  Integration/               Live API tests (gated by MSGRAPH_LIVE=1)
```

### Key Patterns

- **System.CommandLine 2.0.5 stable API:** Commands use `SetAction(async (parseResult, ct) => { ... })`. Options are read via `parseResult.GetValue(option)`.
- **Interface-first in Core:** Every service has an interface for testability.
- **Output formatting:** Commands produce DTOs. The `IOutputFormatter` (JSON/table/plain) handles rendering. Commands never write data directly to stdout — they go through the formatter.
- **Data to stdout, diagnostics to stderr.** Always.
- **Errors:** Core throws typed `MsGraphCliException` subclasses. CLI middleware maps them to exit codes + formatted error output.

### Auth Flow

1. `ISecretStore` (backed by 1Password CLI) stores secrets in two vaults: a read-only vault for app config, a read-write vault for token cache.
2. `GraphAuthProvider` takes two `ISecretStore` instances (config store + token cache store), builds MSAL `PublicClientApplication`, registers cache callbacks that read/write via the token cache store.
3. `GraphClientFactory` creates `GraphServiceClient` using `MsalAccessTokenProvider` (implements `IAccessTokenProvider` from Kiota).
4. Services receive `GraphServiceClient` and make API calls.

### 1Password Integration

All `op` CLI calls go through `ISecretStore` → `OnePasswordSecretStore`. Never call `op` directly.

Two vaults (both configurable):
- `msgraph-cli` (read-only) — app configuration
  - `ms-graph-app-registration` — fields: `client-id`, `tenant-id`
- `netclaw-rw` (read-write) — token cache
  - `msal-token-cache` — field: `notesPlain` (Base64-encoded MSAL V3 cache blob)

## Code Conventions

- **.NET 10**, C# latest, nullable enabled, implicit usings.
- **File-scoped namespaces.**
- **Records for DTOs.** Models returned from services are `record` types.
- **Async all the way.** Use `CancellationToken` on all async methods.
- **TreatWarningsAsErrors** is on. Fix all warnings before committing.

## Testing

- **Unit tests:** Mock `ISecretStore` using `InMemorySecretStore` (already in tests). Mock Graph via `HttpMessageHandler`.
- **Integration tests:** Gated behind `MSGRAPH_LIVE=1`. Require authenticated 1Password session.
- **Test runner:** `dotnet test` from the repo root.

## What NOT to Do

- Don't add a GUI, web server, or MCP server mode.
- Don't add multi-user or multi-tenant support.
- Don't store secrets anywhere except 1Password.
- Don't bypass `ISecretStore` to call `op` directly.
- Don't write data to stdout from anywhere except `IOutputFormatter`.
- Don't add Teams commands (explicitly deferred).
- Don't use `var` when the type isn't obvious from the right-hand side.

## Microsoft Graph SDK Reference

When implementing Graph API calls, use these resources — **never** decompile DLLs or inspect binaries:

- **SDK API Reference (primary):** https://microsoftgraph-msgraph-sdk-dotnet.mintlify.app/api-reference/graph-service-client
- **SDK source code:** https://github.com/microsoftgraph/msgraph-sdk-dotnet/tree/main/src/Microsoft.Graph
- **REST API docs (C# examples):** https://learn.microsoft.com/en-us/graph/api/

### What NOT to do for SDK reference
- Do NOT decompile NuGet packages or DLLs (no ILSpy, dotnet-decompile, etc.)
- Do NOT search the local filesystem for SDK source or documentation
- Do NOT guess API signatures — fetch the docs above instead
