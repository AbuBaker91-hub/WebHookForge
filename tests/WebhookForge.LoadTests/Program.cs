using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// WebhookForge live load test.
//
// Drives REAL HTTP traffic at a running API instance (real Kestrel + real SQL Server),
// so the numbers reflect live behavior. Registers a user, creates an endpoint, then
// floods POST /hooks/{token} with N concurrent workers for D seconds and reports
// throughput + latency percentiles + status-code distribution.
//
// Usage:
//   dotnet run --project tests/WebhookForge.LoadTests -- <baseUrl> <durationSec> <concurrency> [label]
//   (defaults: http://127.0.0.1:5080  15  50)
// ─────────────────────────────────────────────────────────────────────────────

var baseUrl     = Args(0, "http://127.0.0.1:5080").TrimEnd('/');
var durationSec = int.Parse(Args(1, "15"));
var concurrency = int.Parse(Args(2, "50"));
var label       = Args(3, "load");

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine($"=== WebhookForge load test [{label}] ===");
Console.WriteLine($"target={baseUrl}  duration={durationSec}s  concurrency={concurrency}");

// ── Setup: register → workspace → endpoint → token ───────────────────────────
var email = $"load-{Guid.NewGuid():N}@example.com";
var reg = await http.PostAsJsonAsync("/api/auth/register",
    new { email, password = "Sup3rSecret!", displayName = "Load Tester" });
reg.EnsureSuccessStatusCode();
var token = (await reg.Content.ReadFromJsonAsync<JsonElement>(json)).GetProperty("accessToken").GetString()!;
http.DefaultRequestHeaders.Authorization = new("Bearer", token);

var ws = await (await http.PostAsJsonAsync("/api/workspaces", new { name = "Load" }))
    .Content.ReadFromJsonAsync<JsonElement>(json);
var wsId = ws.GetProperty("id").GetString();

var ep = await (await http.PostAsJsonAsync($"/api/workspaces/{wsId}/endpoints", new { name = "Load EP" }))
    .Content.ReadFromJsonAsync<JsonElement>(json);
var hookToken = ep.GetProperty("token").GetString();
var hookPath = $"/hooks/{hookToken}";
Console.WriteLine($"endpoint ready: {hookPath}");

// ── Warm-up ──────────────────────────────────────────────────────────────────
for (var i = 0; i < 20; i++)
    await SendAsync(http, hookPath);

// ── Load ─────────────────────────────────────────────────────────────────────
var deadline = Stopwatch.StartNew();
var workers = new Task<WorkerResult>[concurrency];
for (var w = 0; w < concurrency; w++)
    workers[w] = RunWorker(baseUrl, hookPath, durationSec);

var results = await Task.WhenAll(workers);
deadline.Stop();

// ── Aggregate ────────────────────────────────────────────────────────────────
var latencies = results.SelectMany(r => r.Latencies).OrderBy(x => x).ToArray();
var total     = results.Sum(r => r.Total);
var ok        = results.Sum(r => r.Ok);
var throttled = results.Sum(r => r.Throttled);
var errors    = results.Sum(r => r.Errors);
var seconds   = deadline.Elapsed.TotalSeconds;

Console.WriteLine();
Console.WriteLine($"--- results [{label}] ---");
Console.WriteLine($"wall time        : {seconds:F1} s");
Console.WriteLine($"requests sent    : {total}");
Console.WriteLine($"throughput       : {total / seconds:F0} req/s");
Console.WriteLine($"2xx (captured)   : {ok}");
Console.WriteLine($"429 (throttled)  : {throttled}");
Console.WriteLine($"errors/timeouts  : {errors}");
if (latencies.Length > 0)
{
    Console.WriteLine($"latency p50      : {Pct(latencies, 50):F1} ms");
    Console.WriteLine($"latency p95      : {Pct(latencies, 95):F1} ms");
    Console.WriteLine($"latency p99      : {Pct(latencies, 99):F1} ms");
    Console.WriteLine($"latency max      : {latencies[^1]:F1} ms");
}
Console.WriteLine("=== done ===");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────
static string Args(int i, string fallback)
    => i < Environment.GetCommandLineArgs().Length - 1
        ? Environment.GetCommandLineArgs()[i + 1]
        : fallback;

static async Task<WorkerResult> RunWorker(string baseUrl, string hookPath, int durationSec)
{
    using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    var r = new WorkerResult();
    var sw = Stopwatch.StartNew();
    var end = TimeSpan.FromSeconds(durationSec);

    while (sw.Elapsed < end)
    {
        var t = Stopwatch.GetTimestamp();
        try
        {
            var status = await SendAsync(client, hookPath);
            r.Latencies.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            r.Total++;
            if (status == HttpStatusCode.TooManyRequests) r.Throttled++;
            else if ((int)status is >= 200 and < 300) r.Ok++;
            else r.Errors++;
        }
        catch
        {
            r.Total++;
            r.Errors++;
        }
    }
    return r;
}

static async Task<HttpStatusCode> SendAsync(HttpClient client, string hookPath)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, hookPath)
    {
        Content = new StringContent("""{"event":"load.test","n":1}""", Encoding.UTF8, "application/json")
    };
    using var res = await client.SendAsync(req);
    return res.StatusCode;
}

static double Pct(double[] sorted, double p)
{
    if (sorted.Length == 0) return 0;
    var idx = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
    return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
}

sealed class WorkerResult
{
    public readonly List<double> Latencies = new();
    public int Total, Ok, Throttled, Errors;
}
