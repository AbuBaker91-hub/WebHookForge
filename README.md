# WebhookForge

**Self-hosted webhook testing & API mocking platform.**

Instantly capture and inspect incoming HTTP requests, define mock responses per endpoint, watch events arrive live in your dashboard, and analyze payloads with AI — all running on your own infrastructure with no third-party services required.

---

## Features

| Feature | Description |
|---|---|
| **Webhook capture** | Every endpoint gets a unique URL. Any HTTP request to that URL is stored and fully inspectable (headers, body, IP, timing). |
| **AI analysis** | Analyze any captured request with AI. Supports Claude (Anthropic), Gemini (Google), and Groq (Llama). Each user brings their own API key — no shared server key required. |
| **API mocking** | Define priority-ordered rules that match by method, path, and body expression. Return a custom status, headers, body, and optional delay. |
| **Live feed** | Dashboard subscribes via SignalR — new requests appear in real-time without polling. |
| **Multi-workspace** | Separate teams or projects with role-based access: Viewer, Editor, Admin. |
| **JWT authentication** | Stateless access tokens (60 min) + long-lived refresh token rotation (30 days). No third-party auth dependency. |
| **Rate limiting** | Public webhook endpoint is protected with a fixed-window rate limiter (120 req/min per IP). |
| **In-process caching** | Endpoint token lookups are cached in-memory with dual-key eviction, keeping hot paths off the database. |

---

## Tech stack

### Backend
- **.NET 8** — ASP.NET Core Web API
- **SQL Server 2019+** / Azure SQL
- **Entity Framework Core 8** — Fluent API mappings, repository + Unit of Work pattern
- **ASP.NET Core SignalR** — Real-time live request feed
- **Anthropic.SDK** — Claude AI integration (Haiku model)
- **BCrypt.Net-Next** — Password hashing
- **Microsoft.Extensions.Caching.Memory** — In-process endpoint token cache
- **Swashbuckle / Swagger UI** — API documentation with JWT support

### Frontend
- **Angular 17.3** — Standalone components, signals-based state management
- **@microsoft/signalr 8.0** — Hub client for live request feed
- **RxJS 7.8**

### Architecture
- **Clean Architecture** — Domain / Application / Infrastructure / API layers
- **Result\<T\> pattern** — Explicit success/failure returns instead of exceptions for expected errors
- **Access guard** — Centralised workspace permission checks across all service methods

---

## Project structure

```
WebhookForge/
├── src/
│   ├── WebhookForge.Domain/           # Entities, enums — zero external dependencies
│   ├── WebhookForge.Application/      # Interfaces, DTOs, services, Result<T>
│   ├── WebhookForge.Infrastructure/   # EF Core, repositories, JWT, AI providers
│   └── WebhookForge.API/              # Controllers, SignalR hub, middleware, Program.cs
├── database/
│   ├── schema/
│   │   ├── 001_create_tables.sql      # All tables with constraints
│   │   └── 002_create_indexes.sql     # Performance indexes
│   └── seed/
│       └── 001_seed_data.sql          # Demo data (dev only)
├── client/                            # Angular 17 frontend
│   └── src/app/
│       ├── core/                      # Services, guards, interceptors, models
│       ├── features/                  # auth, workspaces, endpoints, requests
│       └── shared/                    # Pipes, directives, icon registry
└── README.md
```

### Dependency direction

```
API → Application ← Infrastructure
           ↑
         Domain
```

- **Domain** — pure entities and enums, no external packages
- **Application** — depends only on Domain; owns interfaces and business logic
- **Infrastructure** — implements Application interfaces (EF Core, JWT, BCrypt, AI providers)
- **API** — composes everything via DI, handles HTTP transport and SignalR

---

## Getting started

### Prerequisites

| Tool | Minimum version |
|---|---|
| .NET SDK | 8.0 |
| SQL Server | 2019+ (Express, Developer, or Azure SQL) |
| Node.js | 18+ |

---

### 1. Clone

```bash
git clone https://github.com/AbuBaker91-hub/WebhookForge.git
cd WebhookForge
```

---

### 2. Create the database

Run in SSMS, Azure Data Studio, or `sqlcmd`:

```sql
CREATE DATABASE WebhookForge;
```

Then apply EF Core migrations:

```bash
cd src/WebhookForge.API
dotnet ef database update
```

Or apply the schema scripts manually:

```bash
sqlcmd -S localhost -d WebhookForge -i database/schema/001_create_tables.sql
sqlcmd -S localhost -d WebhookForge -i database/schema/002_create_indexes.sql

# Optional: load demo data for local development
sqlcmd -S localhost -d WebhookForge -i database/seed/001_seed_data.sql
```

---

### 3. Configure the API

Edit `src/WebhookForge.API/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=WebhookForge;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Secret": "YOUR_SECRET_AT_LEAST_32_CHARACTERS_LONG",
    "Issuer": "WebhookForge",
    "Audience": "WebhookForge",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  },
  "AllowedOrigins": ["http://localhost:4200"]
}
```

> `Jwt:Secret` must be at least 32 characters. Never commit real secrets — use environment variables or a secrets manager in production.

---

### 4. Start the API

```bash
cd src/WebhookForge.API
dotnet run
```

- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:7000`
- Swagger UI: `http://localhost:5000/swagger`

---

### 5. Start the Angular frontend

```bash
cd client
npm install
ng serve
```

Frontend available at `http://localhost:4200`. The dev proxy (`proxy.conf.json`) forwards `/api` and `/hubs` calls to the API automatically.

---

## Sending your first webhook

1. Open the frontend at `http://localhost:4200` and register an account
2. Create a workspace, then create an endpoint inside it
3. Copy the webhook URL from the endpoint detail page
4. Send any HTTP request to that URL:

```bash
curl -X POST http://localhost:5000/hooks/YOUR_TOKEN \
  -H "Content-Type: application/json" \
  -d '{"event":"order.created","orderId":42}'
```

5. The request appears instantly in the live feed and is fully inspectable (headers, body, IP, timing).

---

## AI analysis

Each user can configure their own AI provider to analyze captured webhook requests in plain English.

### Setup

1. Click the **gear icon** in the sidebar footer
2. Select your AI provider and paste your API key
3. Click **Save settings**

### Supported providers

| Provider | Model | Where to get a key |
|---|---|---|
| **Claude** (Anthropic) | `claude-haiku-4-5-20251001` | [console.anthropic.com](https://console.anthropic.com) |
| **Gemini** (Google) | `gemini-2.5-flash` | [aistudio.google.com](https://aistudio.google.com) — free tier available |
| **Groq** (Llama) | `llama-3.1-8b-instant` | [console.groq.com](https://console.groq.com) — free tier available |

### How it works

- API keys are stored per-user in the database — never returned to the frontend
- Open any captured request → click **Analyze with AI**
- The backend routes the request to your configured provider and returns a plain-English summary identifying the source service, event type, key data points, and recommended action

---

## Defining mock rules

Rules let an endpoint return a controlled response instead of the default `{"received":true}`:

```bash
curl -X POST http://localhost:5000/api/endpoints/{endpointId}/rules \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Return 201 for POST",
    "priority": 10,
    "matchMethod": "POST",
    "responseStatus": 201,
    "responseBody": "{\"status\":\"created\"}",
    "responseHeaders": {"Content-Type": "application/json"},
    "delayMs": 0
  }'
```

**Matching logic:**
- `matchMethod` — optional; `null` matches any HTTP method
- `matchPath` — optional; `null` matches any path
- `matchBodyExpression` — optional regex applied to the raw request body
- Rules are evaluated in **ascending priority order**; the first match wins
- Unmatched requests always return `{"received":true}` with status 200 and are still captured

---

## Real-time live feed

The Angular frontend connects automatically. If you're building your own client:

```typescript
import { HubConnectionBuilder } from '@microsoft/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('/hubs/webhook', { accessTokenFactory: () => yourJwtToken })
  .withAutomaticReconnect()
  .build();

await connection.start();
await connection.invoke('Subscribe', endpointId);

connection.on('NewRequest', (request) => {
  console.log(request);
});
```

---

## API reference

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/register` | — | Create account |
| POST | `/api/auth/login` | — | Get access + refresh token |
| POST | `/api/auth/refresh` | — | Rotate refresh token |
| POST | `/api/auth/revoke` | JWT | Revoke a refresh token |
| GET | `/api/auth/me` | JWT | Current user profile |
| PUT | `/api/auth/me/ai-settings` | JWT | Save AI provider + API key |
| GET | `/api/workspaces` | JWT | List my workspaces |
| POST | `/api/workspaces` | JWT | Create workspace |
| GET | `/api/workspaces/{id}` | JWT | Get workspace |
| PUT | `/api/workspaces/{id}` | JWT | Update workspace |
| DELETE | `/api/workspaces/{id}` | JWT | Delete workspace |
| POST | `/api/workspaces/{id}/members` | JWT | Add member |
| DELETE | `/api/workspaces/{id}/members/{userId}` | JWT | Remove member |
| GET | `/api/workspaces/{id}/endpoints` | JWT | List endpoints |
| POST | `/api/workspaces/{id}/endpoints` | JWT | Create endpoint |
| GET | `/api/endpoints/{id}` | JWT | Get endpoint |
| PUT | `/api/endpoints/{id}` | JWT | Update endpoint |
| DELETE | `/api/endpoints/{id}` | JWT | Delete endpoint |
| POST | `/api/endpoints/{id}/regenerate-token` | JWT | Rotate webhook token |
| GET | `/api/endpoints/{id}/requests` | JWT | Paginated request history |
| DELETE | `/api/endpoints/{id}/requests` | JWT | Purge old requests |
| GET | `/api/requests/{id}` | JWT | Single request detail |
| POST | `/api/requests/{id}/analyze` | JWT | Analyze request with AI |
| GET | `/api/endpoints/{id}/rules` | JWT | List mock rules |
| POST | `/api/endpoints/{id}/rules` | JWT | Create rule |
| GET | `/api/mock-rules/{id}` | JWT | Get rule |
| PUT | `/api/mock-rules/{id}` | JWT | Update rule |
| DELETE | `/api/mock-rules/{id}` | JWT | Delete rule |
| PATCH | `/api/mock-rules/{id}/toggle` | JWT | Enable / disable rule |
| PUT | `/api/endpoints/{id}/rules/reorder` | JWT | Bulk reorder by priority |
| ANY | `/hooks/{token}` | — | **Public** — receive webhook |

---

## Production checklist

- [ ] Set `Jwt:Secret` to a strong random value (32+ chars) via environment variables or Azure Key Vault
- [ ] Use SQL authentication or a managed identity instead of `Trusted_Connection`
- [ ] Enable HTTPS-only (`app.UseHsts()` + redirect)
- [ ] Set `AllowedOrigins` to your actual frontend domain(s)
- [ ] Remove or restrict the seed data endpoint
- [ ] Schedule periodic purge of old `IncomingRequests` rows
- [ ] Set `AccessTokenExpiryMinutes` to 15 minutes for production
- [ ] Review rate limiter limits for your expected traffic

---

## License

MIT
