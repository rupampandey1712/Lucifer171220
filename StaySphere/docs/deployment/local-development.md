# 11. Local Docker Architecture

The goal is one `docker compose up` that starts everything, with no Azure subscription and no
paid service. See [ADR-006](../adr/ADR-006-local-azure-emulation.md).

```mermaid
flowchart TB
    dev((Developer browser))
    subgraph compose["docker compose (network: staysphere)"]
        web["web :5173 → nginx :8080<br/>React SPA"]
        api["api :8081<br/>StaySphere.Api"]
        workers["workers<br/>StaySphere.Workers"]
        sql[("sqlserver :1433<br/>mcr.microsoft.com/mssql/server:2022")]
        redis[("redis :6379")]
        azurite[("azurite :10000-10002<br/>Blob/Queue/Table")]
        sbemu[["servicebus-emulator :5672<br/>+ its own mssql dependency"]]
        mailpit["mailpit :8025 UI / :1025 SMTP"]
        wiremock["wiremock :8089<br/>Nominatim, Open-Meteo, FX, REST Countries stubs"]
        aspire["aspire-dashboard :18888<br/>OTLP :4317 traces/metrics/logs"]
        ollama["ollama :11434 (profile: ai)"]
    end
    dev --> web
    dev -->|/api, /hubs proxied| web --> api
    api --> sql & redis & azurite & sbemu & wiremock & ollama
    workers --> sql & redis & azurite & sbemu & mailpit
    api & workers -->|OTLP| aspire
```

| Azure service | Local substitute | Abstraction | Mock/in-memory option |
|---|---|---|---|
| Azure SQL | SQL Server 2022 container | EF Core | Testcontainers in tests |
| Azure Cache for Redis | `redis:7` | `IDistributedCache`, `IConnectionMultiplexer` | `MemoryDistributedCache` |
| Blob Storage | **Azurite** | `IFileStorageService` | `InMemoryFileStorage` (tests) |
| Service Bus | **Official Service Bus emulator** | `IMessageBus` | `InMemoryMessageBus` (`Messaging:Transport=InMemory`) |
| Key Vault | .NET User Secrets / env vars | `IConfiguration` | — |
| Application Insights / Monitor | **Aspire Dashboard** (OTLP) | OpenTelemetry | console exporter |
| Communication Services (email) | **Mailpit** (SMTP) | `IEmailSender` | `LoggingEmailSender` |
| Front Door / CDN | nginx in web container | — | — |
| Azure OpenAI | **Ollama** | `IChatClient` (Microsoft.Extensions.AI) | `ScriptedChatClient` (tests) |
| Payment provider | `LocalFakePaymentProvider` (in-process plus webhook emitter) | `IPaymentProvider` | — |
| External public APIs | Built-in mocks (`EXTERNAL_SERVICES_MODE=Mock`, default), WireMock stubs (`docker-compose.wiremock.yml`) or the real free APIs (`=Live`) | `IGeocodingService`, `IWeatherService`, `ICurrencyService`, `ICountryService` | `Mock*Service` |

## External APIs: three modes
| Mode | How | What runs |
|---|---|---|
| Mock (default) | nothing to do | in-process `Mock*Service` classes; no HTTP at all |
| WireMock | `docker compose -f docker-compose.yml -f docker-compose.wiremock.yml up -d` | the real HTTP clients (retries, circuit breaker, health tracking, JSON parsing) against stubs in `infrastructure/wiremock/mappings` |
| Live | `EXTERNAL_SERVICES_MODE=Live` | the real free APIs (rate-limited, cached) |

`WireMockStubTests` serves the same mapping files and runs the real clients against them, so the stubs can't drift
from the parsers.

## Profiles
- `docker compose up`: infrastructure + api + workers + web, in mock external mode.
- `docker compose --profile ai up`: adds Ollama. Pull a model once with
  `docker compose exec ollama ollama pull <model>` and set `AI__Model`.
- `docker compose up -d sqlserver redis azurite mailpit aspire-dashboard`: dependencies only, for
  running the API/web from an IDE with breakpoints. See the [debugging guide](../debugging-guide.md).

## Developer commands
```bash
git clone <repo> && cd StaySphere
cp .env.example .env            # dev-only fake secrets; .env is git-ignored
docker compose up -d            # full stack
# or IDE mode (breakpoints): see docs/debugging-guide.md
docker compose up -d sqlserver redis azurite mailpit aspire-dashboard
dotnet run --project src/StaySphere.Api   # auto-migrates + seeds in Development only
cd web/staysphere-web && npm ci && npm run dev
```

| URL | What |
|---|---|
| http://localhost:5173 | SPA |
| http://localhost:8081/swagger | API docs |
| http://localhost:8025 | Mailpit inbox |
| http://localhost:18888 | Traces, metrics and logs (Aspire Dashboard) |

All containers run as **non-root** where the image allows it (API and web on
`mcr.microsoft.com/dotnet/aspnet:10.0` with `USER app`, nginx-unprivileged). Multi-stage builds:
SDK restore → build → test → publish → chiseled runtime; Node build → nginx-unprivileged.

> **Resource note:** SQL Server plus the Service Bus emulator (which needs its own SQL instance)
> uses about 4 GB RAM. Set `Messaging__Transport=InMemory` to skip the emulator on small machines.


## Health endpoints
| Endpoint | Checks | Used by |
|---|---|---|
| `/health/live` | process is up | liveness probe |
| `/health/ready` | SQL Server, outbox backlog, Redis, Blob storage, Service Bus (when configured) | readiness probe |
| `/health` | everything above plus external APIs | dashboards |

External APIs report **Degraded**, never Unhealthy, after 3 consecutive failures within 15 minutes. They are judged from real
calls (a tracking handler on each HTTP client) instead of probing free public APIs on every health check.
