<#
.SYNOPSIS
  End-to-end smoke test for the RAG (Ask-your-webhooks) feature against a running API.

.DESCRIPTION
  Exercises the full pipeline with real data and real scenarios:
    register -> save AI settings -> create workspace + endpoint ->
    POST several realistic webhooks -> ingest (chunk+embed into pgvector) ->
    ask a grounded question -> assert answer + citations come back.

  Also prints timings (ingest duration, ask latency) so it doubles as a light perf check.

.PREREQUISITES
  1. API running (dotnet run --project src/WebhookForge.API).
  2. pgvector Postgres up:        docker compose -f docker-compose.rag.yml up -d
  3. API configured (appsettings.Development.json):
       ConnectionStrings:RagVectorStore  -> the Postgres above
       Rag:EmbeddingApiKey               -> an OpenAI key (server-side, for embeddings)
  4. A generation provider key for the *answer* step (Groq free tier is easiest):
       pass via -GenProvider 3 -GenKey '<groq key>'   (1=Claude, 2=Gemini, 3=Groq)

.EXAMPLE
  pwsh tests/rag-smoke.ps1 -BaseUrl http://localhost:5000 -GenProvider 3 -GenKey $env:WEBHOOKFORGE_GROQ_KEY
#>
param(
  [string]$BaseUrl     = "http://localhost:5000",
  [int]   $GenProvider = 3,                 # 1=Claude, 2=Gemini, 3=Groq
  [string]$GenKey      = $env:WEBHOOKFORGE_GROQ_KEY
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "  [PASS] $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; exit 1 }

if ([string]::IsNullOrWhiteSpace($GenKey)) {
  Fail "No generation key. Pass -GenKey or set WEBHOOKFORGE_GROQ_KEY (the 'ask' step needs an LLM to answer)."
}

# ── 1. Register ───────────────────────────────────────────────────────────────
Step "Register"
$email = "rag-smoke-$([Guid]::NewGuid().ToString('N'))@example.com"
$reg = Invoke-RestMethod -Uri "$BaseUrl/api/auth/register" -Method Post -ContentType "application/json" `
  -Body (@{ email = $email; password = "Sup3rSecret!"; displayName = "RAG Smoke" } | ConvertTo-Json)
$jwt = $reg.accessToken
if (-not $jwt) { Fail "No access token returned." }
$headers = @{ Authorization = "Bearer $jwt" }
Ok "registered $email"

# ── 2. Save AI settings (provider used to generate the grounded answer) ────────
Step "Save AI settings"
Invoke-RestMethod -Uri "$BaseUrl/api/auth/me/ai-settings" -Method Put -Headers $headers -ContentType "application/json" `
  -Body (@{ provider = $GenProvider; apiKey = $GenKey } | ConvertTo-Json) | Out-Null
Ok "provider=$GenProvider configured"

# ── 3. Workspace + endpoint ───────────────────────────────────────────────────
Step "Create workspace + endpoint"
$ws = Invoke-RestMethod -Uri "$BaseUrl/api/workspaces" -Method Post -Headers $headers -ContentType "application/json" `
  -Body (@{ name = "RAG Smoke WS" } | ConvertTo-Json)
$ep = Invoke-RestMethod -Uri "$BaseUrl/api/workspaces/$($ws.id)/endpoints" -Method Post -Headers $headers -ContentType "application/json" `
  -Body (@{ name = "RAG Smoke EP" } | ConvertTo-Json)
$endpointId = $ep.id
$hookToken  = $ep.token
Ok "endpoint $endpointId (token $hookToken)"

# ── 4. Send realistic webhooks (the corpus we'll ask over) ────────────────────
Step "Send sample webhooks"
$samples = @(
  '{"type":"payment_intent.succeeded","data":{"object":{"amount":2000,"currency":"usd"}}}',
  '{"type":"payment_intent.payment_failed","data":{"object":{"amount":4999,"currency":"usd","failure_reason":"card_declined"}}}',
  '{"type":"customer.subscription.created","data":{"object":{"plan":"pro","seats":5}}}',
  '{"type":"charge.refunded","data":{"object":{"amount":2000,"currency":"usd"}}}'
)
foreach ($s in $samples) {
  Invoke-RestMethod -Uri "$BaseUrl/hooks/$hookToken" -Method Post -ContentType "application/json" -Body $s | Out-Null
}
Ok "sent $($samples.Count) webhooks"

# ── 5. Ingest (chunk + embed into pgvector) ───────────────────────────────────
Step "Ingest into pgvector"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$ingest = Invoke-RestMethod -Uri "$BaseUrl/api/endpoints/$endpointId/rag/ingest" -Method Post -Headers $headers
$sw.Stop()
if ($ingest.chunksIndexed -lt 1) { Fail "Nothing indexed (chunksIndexed=$($ingest.chunksIndexed))." }
Ok "indexed $($ingest.chunksIndexed) chunks from $($ingest.requestsProcessed) requests in $($sw.ElapsedMilliseconds) ms"

# ── 6. Ask a grounded question ────────────────────────────────────────────────
Step "Ask"
$question = "Were there any failed payments, and why?"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$ans = Invoke-RestMethod -Uri "$BaseUrl/api/endpoints/$endpointId/rag/ask" -Method Post -Headers $headers -ContentType "application/json" `
  -Body (@{ question = $question; topK = 5 } | ConvertTo-Json)
$sw.Stop()

if ([string]::IsNullOrWhiteSpace($ans.answer)) { Fail "Empty answer." }
if ($ans.chunksRetrieved -lt 1)                { Fail "No chunks retrieved." }
Ok "answer in $($sw.ElapsedMilliseconds) ms, $($ans.chunksRetrieved) chunks cited"
Write-Host "`n  Q: $question"
Write-Host "  A: $($ans.answer)" -ForegroundColor White
Write-Host "  Citations:"
$ans.citations | ForEach-Object { Write-Host ("    [{0:N2}] {1} {2}" -f $_.score, $_.method, $_.path) }

# Sanity: the failure reason should surface somewhere in answer or citations.
$haystack = ($ans.answer + ($ans.citations.snippet -join ' '))
if ($haystack -notmatch "(?i)declin|fail") {
  Write-Host "  [WARN] expected a failed-payment reference in the grounded result." -ForegroundColor Yellow
} else {
  Ok "grounded result references the failed payment"
}

Write-Host "`n=== RAG smoke test PASSED ===" -ForegroundColor Green
