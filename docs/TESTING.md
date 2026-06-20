# Testing

Automated test suite for WebhookForge, living in `tests/WebhookForge.Tests` (added to `WebhookForge.sln`).

## How to run

```bash
# From the repo root
dotnet test                                            # whole solution
dotnet test tests/WebhookForge.Tests                   # just the test project
dotnet test --filter "FullyQualifiedName~Regression"   # only the regression tests
```

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

### Integration — Rate limiting (`RateLimitingRegressionTests`)
| ID | Test | Asserts |
|---|---|---|
| TC-RATE-01 | `SingleIp_IsThrottled_AfterLimit` | One IP is throttled (429) once it exceeds its own 120/min quota |
| TC-RATE-02 | `ExhaustedIp_DoesNotThrottleOtherIp` | An IP that exhausts its quota does not affect a different IP |

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
dotnet test tests/WebhookForge.Tests
Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33, Duration: ~31 s
```

> Notes
> - Integration tests run against EF Core InMemory and a stubbed AI provider, so **no SQL Server or real provider key is required** to run the suite.
> - To smoke-test a **real** AI provider end-to-end, configure a key in the running app (Settings → provider + key) and call `POST /api/requests/{id}/analyze`; the stub is only substituted inside the test host.
