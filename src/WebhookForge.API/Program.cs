using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WebhookForge.API.Hubs;
using WebhookForge.API.Middleware;
using WebhookForge.Application;
using WebhookForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Data Protection ──────────────────────────────────────────────────────────
// Provides the keyring used to encrypt user AI API keys at rest (ApiKeyProtector).
// In production, persist keys to a shared, durable store (e.g. Azure Blob + Key Vault)
// so they survive restarts and are shared across instances.
builder.Services.AddDataProtection();

// ── Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── SignalR ─────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));   // required for SignalR

// ── JWT Authentication ───────────────────────────────────────────────────────
var jwtSecret   = builder.Configuration["Jwt:Secret"]   ?? throw new InvalidOperationException("Jwt:Secret missing.");
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "WebhookForge";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "WebhookForge";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = true,
            ValidIssuer              = jwtIssuer,
            ValidateAudience         = true,
            ValidAudience            = jwtAudience,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero,
        };

        // Allow SignalR to pass token via query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path        = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
// Scoped to the public webhook receiver via [EnableRateLimiting("webhook")].
// Partitioned PER CLIENT IP so each caller gets its own token pool —
// one abusive IP can no longer exhaust the limit for everyone else.
// Limits are configurable via the "RateLimiting" section (defaults: 120 req / 60 s).
// NOTE: behind a reverse proxy/load balancer, configure ForwardedHeaders so that
// RemoteIpAddress reflects the real client and not the proxy.
var rateLimitPermits = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 120;
var rateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window               = TimeSpan.FromSeconds(rateLimitWindowSeconds),
                PermitLimit          = rateLimitPermits,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0, // Reject immediately — no queueing
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode  = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Too many requests. Please slow down.\"}", ct);
    };
});

// ── Swagger ──────────────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "WebhookForge API",
        Version     = "v1",
        Description = "Self-hosted webhook testing and API mocking platform.",
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter your JWT token.",
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Application + Infrastructure layers ─────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Must be first — catches anything the rest of the pipeline throws
app.UseMiddleware<ExceptionMiddleware>();

// Behind a reverse proxy/load balancer, rewrite RemoteIpAddress from X-Forwarded-For so the
// per-IP rate limiter keys on the real client. OFF by default; only trusts the proxies you list,
// so the header can't be spoofed by arbitrary clients. Must run before UseRateLimiter.
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    var fho = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
    fho.KnownProxies.Clear();
    fho.KnownNetworks.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
        if (IPAddress.TryParse(proxy, out var ip)) fho.KnownProxies.Add(ip);
    app.UseForwardedHeaders(fho);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<WebhookHub>("/hubs/webhook");

app.Run();

// Exposed so the integration test project (WebApplicationFactory<Program>) can boot the real app.
public partial class Program { }
