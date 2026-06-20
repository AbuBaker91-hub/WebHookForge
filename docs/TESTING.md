# Testing

Automated test suite for WebhookForge, living in `tests/WebhookForge.Tests` (added to `WebhookForge.sln`).

## How to run

```bash
# From the repo root
dotnet test                                            # whole solution
dotnet test tests/WebhookForge.Tests                   # just the test project
dotnet test --filter "FullyQualifiedName~Regression"   # only the regression tests
```

By default the suite runs against EF Core **InMemory** — no SQL Server or provider key required. To run the **same suite against the real SQL Server engine** (so "test == live" for EF mappings, `GETUTCDATE()` defaults, and unique indexes that InMemory ignores):

```powershell
$env:WEBHOOKFORGE_TEST_SQL_SERVER = '(localdb)\MSSQLLocalDB'   # any reachable SQL Server
dotnet test tests/WebhookForge.Tests
```

Each test class provisions its own throwaway database and drops it on teardown.

## What it covers and how

The suite mixes three styles:

| Layer | Mechanism | What it proves |
|---|---|---|
| **Integration** | `WebApplicationFactory<Program>` boots the **real** API with its full middleware pipeline (JWT auth, Data Protection, rate limiter, routing). SQL Server is swapped for EF Core **InMemory** (isolated per test class); the live AI provider is swapped for a deterministic stub. | End-to-end HTTP behavior — real requests in, real responses out. |
| **Unit** | Classes constructed directly with fakes (`EphemeralDataProtectionProvider`, a capturing `HttpMessageHandler`). | Isolated logic — encryption, outbound provider HTTP shape. |
| **Regression** | Targeted tests that lock in the interview-review fixes so they can't silently regress. | The specific security guarantees below. |

Test host plumbing lives in `tests/WebhookForge.Tests/Infrastructure/`:
- `WebhookForgeApiFactory` — boots the app, swaps DB + AI provider, adds the IP-spoofing filter.
- `TestDoubles` — `StubAiAnalysisService` and the `X-Test-IP` `IStartupFilter` (lets a test set `RemoteIpAddress` so the per-IP rate limiter is verifiable).
- `ApiClientExtensions` — typed register/login/create helpers so tests read like API calls.

## Interviewer findings → regression coverage

Every issue raised in the code review is now pinned by a test:

| Review finding | Fix | Guarding test(s) |
|---|---|---|
| Rate limiter was a single **global** 120/min bucket (README claimed "per IP") | Partitioned `FixedWindowRateLimiter` keyed on client IP | `RateLimitingRegressionTests.SingleIp_IsThrottled_AfterLimit`, `ExhaustedIp_DoesNotThrottleOtherIp` |
| Gemini key passed in the **URL** | Moved to `x-goog-api-key` header | `AiAnalysisServiceTests.Gemini_SendsKeyInHeader_NotInUrl` (+ `Groq_SendsKeyInAuthorizationHeader`) |
| `BCrypt.Verify` **skipped** for unknown users (timing oracle; comment claimed otherwise) | Always verify against a dummy hash | `LoginTimingRegressionTests.UnknownUserLogin_IncursBcryptCost_LikeWrongPassword`, `AuthApiTests.Login_UnknownUser_ReturnsSameGenericMessageAsWrongPassword` |
| AI API keys stored in **plaintext** | Encrypted at rest via Data Protection (`IApiKeyProtector`) | `AiAnalysisApiTests.SaveAiSettings_StoresEncryptedKey_NotPlaintext`, `Profile_NeverExposesApiKey`, `ApiKeyProtectorTests.*` |

## Test catalog

### Integration — Auth (`AuthApiTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-AUTH-01 | `Register_ReturnsTokensAndUser` | Register issues access + refresh tokens and the user profile |
| TC-AUTH-02 | `Register_DuplicateEmail_ReturnsBadRequest` | Duplicate email rejected (400) |
| TC-AUTH-03 | `Login_WithValidCredentials_Succeeds` | Valid login returns a token (200) |
| TC-AUTH-04 | `Login_WithWrongPassword_ReturnsUnauthorized` | Wrong password rejected (401) |
| TC-AUTH-05 | `Login_UnknownUser_ReturnsSameGenericMessageAsWrongPassword` | Unknown email and wrong password return identical 401 bodies (no enumeration) |
| TC-AUTH-06 | `Me_WithoutToken_ReturnsUnauthorized` | `/auth/me` requires a token |
| TC-AUTH-07 | `Me_WithToken_ReturnsProfile` | `/auth/me` returns the caller's profile |
| TC-AUTH-08 | `Refresh_RotatesToken_OldTokenRejected` | Refresh rotates the token; the old one is then rejected |

### Integration — Workspaces & Endpoints (`WorkspaceEndpointApiTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-WS-01 | `CreateWorkspace_ThenListAndGet` | Create → list → get a workspace |
| TC-WS-02 | `CreateEndpoint_ReturnsTokenAndWebhookUrl` | Endpoint gets a token embedded in its webhook URL |
| TC-WS-03 | `OtherUser_CannotAccessForeignWorkspace` | A non-member is denied (403/404) — service-layer access control |
| TC-WS-04 | `RegenerateToken_ChangesTheToken` | Regenerating rotates the endpoint token |

### Integration — Webhook capture & mock rules (`WebhookCaptureApiTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-HOOK-01 | `PostToHook_CapturesRequest_AndIsListable` | A posted webhook is stored and listable with method/body |
| TC-HOOK-02 | `PostToUnknownToken_ReturnsNotFound` | Unknown token → 404 |
| TC-HOOK-03 | `MatchingMockRule_ReturnsCustomStatusAndBody` | A matching rule overrides the default response |
| TC-HOOK-04 | `MockRulePriority_FirstMatchWins` | Lowest priority number wins when multiple rules match |

### Integration — AI analysis & key encryption (`AiAnalysisApiTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-AI-01 | `SaveAiSettings_StoresEncryptedKey_NotPlaintext` | The persisted key is ciphertext; plaintext never appears in the DB |
| TC-AI-02 | `Analyze_WithConfiguredProvider_ReturnsAnalysis` | Full pipeline: auth → **decrypt key** → fetch request → call provider → return summary |
| TC-AI-03 | `Analyze_WithoutProvider_ReturnsBadRequest` | Analyze with no provider configured → 400 |
| TC-AI-04 | `Profile_NeverExposesApiKey` | `/auth/me` exposes the provider name but never the key |

### Integration — Rate limiting, sequential (`RateLimitingRegressionTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-RATE-01 | `SingleIp_IsThrottled_AfterLimit` | One IP is throttled (429) once it exceeds its own 120/min quota |
| TC-RATE-02 | `ExhaustedIp_DoesNotThrottleOtherIp` | An IP that exhausts its quota does not affect a different IP |

### Integration — Rate limiting, concurrent (`RateLimitConcurrencyTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-RATE-03 | `ParallelBurst_AdmitsAtMostLimit_RestThrottled` | Under a 200-request parallel burst from one IP, **at most 120** are admitted and the rest are 429 (no over-admission under a race) |
| TC-RATE-04 | `ParallelFloodOnOneIp_DoesNotThrottleAnotherIp` | A concurrent flood on one IP never throttles a different IP |

### Integration — Adversarial / manipulation (`AdversarialEndpointTests`, `ForwardedHeaderSpoofTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-ADV-01 | `OversizedBody_IsCaptured` | A ~1 MB payload is captured, not rejected or crashed |
| TC-ADV-02 | `MalformedJsonBody_IsCaptured` | Malformed JSON is stored as a raw body (capture never parses it) |
| TC-ADV-03 | `EmptyBody_IsAccepted` | An empty body is accepted |
| TC-ADV-04 | `TamperedToken_ReturnsClientError_NotServerError` (×3) | Path-traversal / SQL-ish / null-byte tokens return 4xx, never 5xx |
| TC-ADV-05 | `VeryLongToken_IsHandledGracefully` | A 4000-char token does not 500 |
| TC-ADV-06 | `UnsupportedMethod_DoesNotServerError` | `OPTIONS /hooks/{token}` does not 500 |
| TC-ADV-07 | `SpoofingXForwardedFor_DoesNotBypassPerIpLimit` | Rotating `X-Forwarded-For` does **not** grant fresh quota (the app keys on the real connection IP, not the untrusted header) |
| TC-ADV-08 | `PayloadOverLimit_IsRejected` | A payload over the 5 MB cap is rejected with 413 before the body is buffered into memory |

### Unit — API key protection (`ApiKeyProtectorTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-PROT-01 | `Protect_ThenUnprotect_RoundTripsOriginal` | Round-trips; ciphertext ≠ plaintext |
| TC-PROT-02 | `Protect_NullOrBlank_ReturnsNull` (×3) | null / "" / whitespace → null |
| TC-PROT-03 | `Unprotect_NullOrBlank_ReturnsNull` (×2) | null / "" → null |
| TC-PROT-04 | `Unprotect_GarbageOrLegacyPlaintext_ReturnsNull` | Undecryptable input returns null instead of throwing |
| TC-PROT-05 | `Unprotect_WithDifferentKeyring_ReturnsNull` | A value can't be read by a different keyring |

### Unit — AI provider HTTP shape (`AiAnalysisServiceTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-AISVC-01 | `Gemini_SendsKeyInHeader_NotInUrl` | Key in `x-goog-api-key` header; absent from the URL |
| TC-AISVC-02 | `Groq_SendsKeyInAuthorizationHeader` | Key in `Authorization: Bearer`; absent from the URL |

### Regression — login timing (`LoginTimingRegressionTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-TIMING-01 | `UnknownUserLogin_IncursBcryptCost_LikeWrongPassword` | Unknown-user login still pays the BCrypt cost (≥5 ms, same ballpark as the real path) |

## Latest run

```
# InMemory (default)
dotnet test tests/WebhookForge.Tests
Passed!  - Failed: 0, Passed: 45, Skipped: 0, Total: 45, Duration: ~39 s

# Against the real SQL Server engine (LocalDB) — same 45 tests
WEBHOOKFORGE_TEST_SQL_SERVER='(localdb)\MSSQLLocalDB' dotnet test tests/WebhookForge.Tests
Passed!  - Failed: 0, Passed: 45, Skipped: 0, Total: 45, Duration: ~52 s
```

The full suite passes identically on InMemory and on real SQL Server, so the functional/regression behavior is validated against the live database engine.

> Notes
> - Integration tests run against a stubbed AI provider, so **no real provider key is required**.
> - To smoke-test a **real** AI provider end-to-end, configure a key in the running app (Settings → provider + key) and call `POST /api/requests/{id}/analyze`; the stub is only substituted inside the test host.

---

## Load testing (live)

`tests/WebhookForge.LoadTests` is a standalone HTTP load driver that hits a **real running API** (real Kestrel + real SQL Server), so the numbers reflect production behavior — not the in-memory test transport. It registers a user, creates an endpoint, then floods `POST /hooks/{token}` with N concurrent workers and reports throughput, latency percentiles, and the status-code split.

The rate limit is configurable (`RateLimiting:PermitLimit` / `WindowSeconds`, default 120/60), which lets the same endpoint be measured both with the cap lifted (raw capacity) and with the cap on (protection under flood).

```powershell
# Orchestrated end-to-end (migrate LocalDB -> launch API -> load -> teardown):
pwsh tests/run-load-test.ps1 -Mode throughput -DurationSec 20 -Concurrency 64
pwsh tests/run-load-test.ps1 -Mode ratelimit  -DurationSec 20 -Concurrency 64
```

### Results (LocalDB, SQL Server 2019, 64 concurrent workers, 20 s, single client IP)

| Scenario | Rate limit | Requests | Captured (2xx) | Throttled (429) | Errors | Throughput | p50 | p95 | p99 |
|---|---|---|---|---|---|---|---|---|---|
| **Raw capacity** | lifted | 9,173 | 9,173 | 0 | 0 | **456 req/s** | 122 ms | 281 ms | 702 ms |
| **Under flood** | 120/min | 193,501 | ~120 | 193,402 | 0 | 9,661 req/s | 5.4 ms | 14.6 ms | 23.7 ms |

**Reading the results:**
- *Raw capacity* — with the limiter out of the way, the capture path sustained **456 req/s with every request persisted to SQL Server and zero errors**. This is DB-write-bound on a dev-box LocalDB; a real SQL Server / pooled instance would go higher.
- *Under flood* — a single IP firing ~193k requests in 20 s had only its ~120/min quota admitted; the other **193,402 were rejected at ~5 ms each** — roughly 50× cheaper than an admitted DB write. The per-IP limiter shields the database under abuse exactly as intended, and (TC-ADV-07) `X-Forwarded-For` spoofing can't reset the bucket.

> These are dev-machine figures meant to validate behavior and relative cost, not a capacity guarantee. Re-run on target hardware for sizing.

---

## Memory & performance hardening

Each known resource/performance concern and how it's mitigated and verified:

| Concern | Mitigation | Where | Evidence |
|---|---|---|---|
| `HttpClient` socket exhaustion (one client per AI call) | All providers use a single **pooled, injected** `HttpClient`; Claude reuses it too (`new AnthropicClient(apiKey, _http)`) instead of allocating per call | `AiAnalysisService`, registered via `AddHttpClient<IAiAnalysisService, AiAnalysisService>()` | Load run: 0 errors over 9,173 calls |
| Unbounded webhook payload → memory exhaustion | Reject payloads over **5 MB** with 413 *before* buffering the body (explicit Content-Length guard + `[RequestSizeLimit]` for chunked uploads) | `WebhookReceiverController.Receive` | `TC-ADV-08` |
| One IP exhausting capacity for everyone | Per-IP partitioned rate limiter (configurable); rejects cheaply (~5 ms) without touching the DB | `Program.cs` rate limiter | `TC-RATE-01..04`, load "under flood" run |
| DB round-trip on every public webhook hit | Token→endpoint lookup cached in-memory with dual-key eviction | `EndpointRepository` | Hot path covered by `TC-HOOK-*` |
| Throwaway test databases left behind | Each SQL-mode factory drops its database on dispose | `WebhookForgeApiFactory.Dispose` | Post-run check: no `WebhookForge%` DBs remain |
| BCrypt cost as a timing oracle | Constant-time login (verify against a dummy hash for unknown users) | `AuthService.LoginAsync` | `TC-TIMING-01` |

### Reverse-proxy note
The per-IP limiter keys on the real connection IP. `X-Forwarded-For` is **ignored by default** (proven by `TC-ADV-07`), so it can't be spoofed to evade the limit. Behind a trusted proxy, set `ForwardedHeaders:Enabled=true` and list the proxy IPs in `ForwardedHeaders:KnownProxies` so the limiter sees the real client.
