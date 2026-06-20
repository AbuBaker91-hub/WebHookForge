<#
.SYNOPSIS
  Runs the WebhookForge live load test against a real Kestrel instance backed by SQL Server LocalDB.

.DESCRIPTION
  1. Creates/migrates a throwaway LocalDB database.
  2. Launches the real API (Program.cs) on Kestrel against it.
  3. Drives real HTTP load at POST /hooks/{token} via tests/WebhookForge.LoadTests.
  4. Tears the API and database down.

  Two modes:
    -Mode throughput   High rate limit -> measures raw capture capacity (req/s + latency).
    -Mode ratelimit    Default 120/min -> shows the per-IP limiter shielding the DB under flood.

.EXAMPLE
  pwsh tests/run-load-test.ps1 -Mode throughput -DurationSec 20 -Concurrency 64
#>
param(
  [ValidateSet('throughput', 'ratelimit')]
  [string]$Mode = 'throughput',
  [int]$DurationSec = 20,
  [int]$Concurrency = 64,
  [string]$SqlServer = '(localdb)\MSSQLLocalDB',
  [string]$Port = '5080'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$db = "WebhookForge_Load_$([Guid]::NewGuid().ToString('N').Substring(0,8))"
$conn = "Server=$SqlServer;Database=$db;Trusted_Connection=True;TrustServerCertificate=True;"
$url = "http://127.0.0.1:$Port"
$permit = if ($Mode -eq 'throughput') { 100000000 } else { 120 }

Write-Host "== build ==" -ForegroundColor Cyan
dotnet build "$repo/WebhookForge.sln" -v quiet --nologo | Out-Null

Write-Host "== migrate ($db) ==" -ForegroundColor Cyan
$env:ConnectionStrings__DefaultConnection = $conn
dotnet ef database update --project "$repo/src/WebhookForge.Infrastructure" --startup-project "$repo/src/WebhookForge.API" --no-build | Out-Null

Write-Host "== launch API (permit=$permit) ==" -ForegroundColor Cyan
$api = Start-Process dotnet -PassThru -WindowStyle Hidden -ArgumentList @(
  'run', '--no-build', '--project', "$repo/src/WebhookForge.API"
) -Environment @{
  'ConnectionStrings__DefaultConnection' = $conn
  'RateLimiting__PermitLimit'            = "$permit"
  'RateLimiting__WindowSeconds'          = '60'
  'ASPNETCORE_URLS'                      = $url
  'ASPNETCORE_ENVIRONMENT'               = 'Production'
}

try {
  for ($i = 0; $i -lt 30; $i++) {
    try {
      $code = (Invoke-WebRequest "$url/hooks/ping" -Method Post -Body '{}' -ContentType 'application/json' -SkipHttpErrorCheck).StatusCode
      if ($code -eq 404 -or $code -eq 429) { Write-Host "API up (HTTP $code)"; break }
    } catch { Start-Sleep -Seconds 1 }
  }

  Write-Host "== load ($Mode) ==" -ForegroundColor Cyan
  dotnet run --no-build --project "$repo/tests/WebhookForge.LoadTests" -- $url $DurationSec $Concurrency $Mode
}
finally {
  Write-Host "== teardown ==" -ForegroundColor Cyan
  if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  sqlcmd -S $SqlServer -Q "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
  Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
}
