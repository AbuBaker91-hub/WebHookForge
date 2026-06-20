# CLAUDE.md

Guidance for AI agents working in this repository. Keep this file in sync with the code — if you change behavior, update this file and `README.md` in the same change.

## What this is

**WebhookForge** — a self-hosted webhook testing & API mocking platform. Capture incoming HTTP requests on unique endpoint URLs, inspect them, define mock responses, watch them live via SignalR, and analyze payloads with a user-supplied AI provider.

## Stack

- **Backend:** .NET 8, ASP.NET Core Web API, EF Core 8 (SQL Server), SignalR
- **Frontend:** Angular 17.3 (standalone components, signals), `@microsoft/signalr` 8, RxJS 7.8
- **Auth:** JWT access tokens (15 min) + rotating refresh tokens (30 days), BCrypt password hashing
- **AI:** Claude (`Anthropic.SDK`), Gemini, Groq — each user brings their own key

## Layout

```
src/
  WebhookForge.Domain/          # Entities, enums — zero external deps
  WebhookForge.Application/      # Interfaces, DTOs, services, Result<T>; depends only on Domain
  WebhookForge.Infrastructure/   # EF Core, repositories, JWT, AI providers, ApiKeyProtector
  WebhookForge.API/              # Controllers, SignalR hub, middleware, Program.cs (DI composition)
client/                          # Angular frontend
database/                        # Raw SQL schema + dev seed (EF migrations are the source of truth)
```

Dependency direction: **API → Application ← Infrastructure**, with **Domain** at the base. Never make Application or Domain depend on Infrastructure or ASP.NET Core types — cross that boundary with an interface in `Application/Common/Interfaces` implemented in Infrastructure (see `IApiKeyProtector` → `ApiKeyProtector`).

## Commands

```bash
# Backend (from repo root)
dotnet build WebhookForge.sln
dotnet test                                        # tests/WebhookForge.Tests (InMemory) — see docs/TESTING.md
# Same suite against real SQL Server:  $env:WEBHOOKFORGE_TEST_SQL_SERVER='(localdb)\MSSQLLocalDB'; dotnet test
# Live load test:                      pwsh tests/run-load-test.ps1 -Mode throughput|ratelimit
dotnet run --project src/WebhookForge.API          # http://localhost:5000, swagger at /swagger
dotnet ef database update --project src/WebhookForge.Infrastructure --startup-project src/WebhookForge.API
dotnet ef migrations add <Name> --project src/WebhookForge.Infrastructure --startup-project src/WebhookForge.API

# Frontend (from client/)
npm install
npm run dev        # ng serve with proxy → forwards /api and /hubs to the API
npm run build      # production build
```

## Conventions

- **Result pattern:** services return `Result` / `Result<T>` (never throw for expected errors). Controllers stay thin and map via `BaseController.ToActionResult(...)`.
- **Access control:** enforced in the service layer via `AccessGuard` / repository checks, not in controllers. Controllers only carry `[Authorize]` and pass `CurrentUserId`.
- **Routing:** most controllers use `[Route("api")]` with explicit sub-paths; `BaseController` defaults to `api/[controller]`. Mock-rule item routes are `/api/rules/{id}` (NOT `/api/mock-rules/...`). The Angular client's routes live in `client/src/app/core/constants/api.constants.ts` — keep API routes, that file, and the README API table in agreement.
- **Webhook hot path:** `EndpointRepository.GetByTokenAsync` is cached in `IMemoryCache` with a dual key (`ep:tok:{token}` + `ep:id:{id}`) so token regeneration can evict correctly. Don't add DB calls to the public `/hooks/{token}` path without considering this cache.

## Security model (do not regress)

- **Passwords:** BCrypt. Login runs `BCrypt.Verify` against a dummy hash when the user doesn't exist, so timing is constant whether or not the email is registered — do not reintroduce a short-circuit that skips verification for unknown users.
- **AI API keys:** encrypted at rest with ASP.NET Core Data Protection (`IApiKeyProtector`). Persist only ciphertext; decrypt on demand in `AuthService.GetAiSettingsAsync`; never return the key to the frontend.
- **Provider keys in transit:** pass keys via request headers (Claude/Groq `Authorization`, Gemini `x-goog-api-key`) — never in the URL/query string.
- **Rate limiting:** the public webhook receiver is rate-limited per client IP (partitioned fixed window, configurable via `RateLimiting:*`, default 120/60s) — keep it partitioned so one IP cannot exhaust the limit for others. `X-Forwarded-For` is ignored unless `ForwardedHeaders:Enabled` + `KnownProxies` are set.
- **Payload size:** the public webhook endpoint caps bodies at 5 MB (`WebhookReceiverController.MaxBodyBytes`) and rejects larger with 413 before buffering — don't remove the guard.
- **HTTP clients:** AI providers share one pooled, injected `HttpClient` (`AddHttpClient<IAiAnalysisService, AiAnalysisService>`); Claude reuses it too. Don't `new HttpClient()` per request (socket leak).
- **Secrets:** `Jwt:Secret` must be ≥32 chars and supplied via env vars / a secrets manager in production, never committed.

## Gotchas

- The shell here is PowerShell on Windows; the only installed .NET SDK is 9.x but projects target net8.0 (build with the net8 targeting pack).
- `appsettings.json` ships safe placeholders; real config goes in `appsettings.Development.json` (gitignored) or environment variables.
- Data Protection defaults to a local keyring — in production it must be persisted to a durable, shared store or encrypted AI keys won't survive restarts / scale-out.
