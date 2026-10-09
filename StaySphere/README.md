# StaySphere

A production-style accommodation marketplace. Guests search, book and pay. Hosts list homes and manage calendars and earnings. Admins and support staff run the platform. An AI travel assistant searches real listings and can prepare bookings, but it never books without your explicit confirmation.

| Layer | Tech |
|---|---|
| API | **.NET 10**, ASP.NET Core (controllers, versioned `/api/v1`), EF Core 10 + SQL Server, FluentValidation, SignalR, Serilog, OpenTelemetry, Swagger |
| Architecture | Clean Architecture **modular monolith**: Domain → Application → Infrastructure → Api, plus a separate **Workers** host. Transactional **outbox** → **Azure Service Bus** (or in-memory) → idempotent consumers |
| Data / infra | SQL Server, Redis (cache, SignalR backplane), Azure Blob Storage (Azurite locally), Mailpit, Aspire Dashboard (OTLP) |
| AI | Microsoft Agent Framework (`ChatClientAgent`) on `Microsoft.Extensions.AI`. **Gemini** by default (official `Google.GenAI` SDK); Foundry Local, Ollama or any OpenAI-compatible endpoint by configuration. An offline rule-based engine is the fallback |
| Web | React 19, TypeScript, Vite, Tailwind CSS 4, TanStack Query, Zustand, React Hook Form + Zod, Leaflet/OpenStreetMap, Recharts, SignalR client. API types are **generated from OpenAPI** |
| Delivery | Docker multi-stage images, Docker Compose, GitHub Actions, **Bicep** (Container Apps, Azure SQL, Redis, Service Bus, Storage, Key Vault, App Insights, Front Door + WAF) |

> This is a portfolio project. All data is fake and no real payments are processed. The payment provider is a local simulator.

## Quick start (everything in Docker)

Requires Docker Desktop with about 6 GB of RAM.

```bash
cd StaySphere
cp .env.example .env            # optional; the defaults work
docker compose up -d --build    # first build takes a few minutes
```

| URL | What |
|---|---|
| http://localhost:5173 | **The app** |
| http://localhost:8081/swagger | API docs (OpenAPI) |
| http://localhost:8025 | Mailpit inbox (verification, booking and refund emails) |
| http://localhost:18888 | Traces, metrics and logs (Aspire Dashboard) |

> **Listing photos** for the demo data load from `picsum.photos`. If your network blocks that host you'll see a green house placeholder instead. Photos you upload as a host are stored locally and always show.

**Demo accounts.** Every account uses the password `Passw0rd!Demo`.

| Account | Role |
|---|---|
| `guest@example.local` | Guest |
| `host@example.local` | Host |
| `admin@example.local` | Admin |
| `support@example.local` | Support |

There are also `host01..24@example.local` and `guest001..149@example.local`. The deterministic seed creates 120 listings in 24 cities and about 460 reservations with payments, a ledger and reviews.

**Test cards** (enter them on the checkout page; they are turned into tokens in the browser):

| Card | Result |
|---|---|
| `4242 4242 4242 4242` | Success |
| `4000 0000 0000 0002` | Declined |
| `4000 0000 0000 9995` | Insufficient funds |
| `4000 0000 0000 0119` | Delayed; a webhook confirms it about 5 seconds later |
| `4000 0000 0000 0259` | Success, with a duplicate webhook (tests idempotency) |

### AI assistant (Gemini by default)

The "Ask AI" assistant is a Microsoft Agent Framework agent that searches real listings through tools and never books without your confirmation. It uses **Google Gemini** by default:

1. Get a free API key at https://aistudio.google.com/apikey.
2. Docker: put `GEMINI_API_KEY=...` in `StaySphere/.env` (git-ignored) and run `docker compose up -d api`.
   Running the API from your IDE: `dotnet user-secrets set GEMINI_API_KEY ... --project src/StaySphere.Api`.

Without a key the app still works: it logs a warning at startup and the offline rule-based assistant answers. Each reply shows which engine answered.

| Provider | Settings (`.env` for Docker, user secrets or env vars for the IDE) |
|---|---|
| `Gemini` (default) | `GEMINI_API_KEY`; optional `AI_MODEL` to pin a version (default `gemini-flash-latest`) |
| `FoundryLocal` | `AI_PROVIDER=FoundryLocal`, `AI_BASE_URL=http://localhost:<port>/v1` (from `foundry service status`; use `host.docker.internal` instead of `localhost` when the API runs in Docker), `AI_MODEL=<id from foundry model list>`. Pick a model that supports tool calling |
| `Ollama` | `AI_PROVIDER=Ollama`, `AI_MODEL=llama3.2`, optional `AI_BASE_URL` |
| `OpenAI` | `AI_PROVIDER=OpenAI`, `AI_API_KEY`, `AI_MODEL`, optional `AI_BASE_URL` (Azure OpenAI v1, LM Studio, vLLM…) |
| `Rules` | Offline, no model |

In `appsettings`/user secrets the same settings are `AI:Provider`, `AI:Model`, `AI:BaseUrl` and `AI:ApiKey`.

### Optional profiles

```bash
# Azure Service Bus emulator + separate Workers container (the API only writes outbox rows)
docker compose -f docker-compose.yml -f docker-compose.servicebus.yml --profile servicebus up -d

# Local LLM for the assistant (Ollama in Docker)
docker compose --profile ai up -d
docker compose exec ollama ollama pull llama3.2      # any tool-calling model
AI_PROVIDER=Ollama AI_BASE_URL=http://ollama:11434 AI_MODEL=llama3.2 docker compose up -d api

# Real public APIs (OpenStreetMap Nominatim, Open-Meteo, Frankfurter) instead of mocks
EXTERNAL_SERVICES_MODE=Live docker compose up -d api
```

## Developing locally (hot reload)

**Debugging with breakpoints** (VS Code, Visual Studio or Rider), from the first API call to the last, plus the architecture in simple terms: see **[docs/debugging-guide.md](docs/debugging-guide.md)**.

```bash
cd StaySphere
docker compose up -d sqlserver redis azurite mailpit aspire-dashboard   # dependencies only
dotnet tool restore
dotnet run --project src/StaySphere.Api        # http://localhost:8081; migrates and seeds in Development
cd web/staysphere-web && npm ci && npm run dev # http://localhost:5173 (proxies /api and /hubs)
```

`appsettings.Development.json` points at `localhost` and uses in-memory messaging, local disk storage and SMTP to Mailpit. Override any setting with environment variables, for example `Messaging__Transport=ServiceBus`.

## Tests

| Suite | Command | What it proves |
|---|---|---|
| Unit (74) | `dotnet test tests/StaySphere.UnitTests` | Pricing, cancellation policies, reservation state machine, value objects, validators, PII redaction, request-to-book and payout rules, AI provider selection |
| Architecture (6) | `dotnet test tests/StaySphere.ArchitectureTests` | Layer and module boundaries; controllers can't touch the DB; AI tools can't reach infrastructure |
| Integration (32) | `dotnet test tests/StaySphere.IntegrationTests` | Real SQL Server via Testcontainers. **20 concurrent bookings → exactly one wins**; duplicate webhook → one capture; idempotent reservations; hold expiry; refunds through the event pipeline; authorization (403/404); review rules; AI confirmation gate; host accept (capture) / decline (void) / expiry; **concurrent payout requests → exactly one payout**; failed transfers reversed in the ledger; the Agent Framework agent calling real tools through the Gemini SDK and an OpenAI-compatible (Foundry Local) client against stub models |
| Frontend (16) | `cd web/staysphere-web && npm test` | Components, API client (refresh single-flight, ProblemDetails), card tokenization |
| E2E (9) | `npx playwright test` (stack running) | Log in → search → reserve → pay → confirmed; host dashboard; host accepts a booking request and sees payouts; RBAC; mobile smoke |

## Where things are

```
StaySphere/
├── src/
│   ├── StaySphere.Domain/          aggregates, value objects, domain events, pricing & cancellation rules
│   ├── StaySphere.Contracts/       API DTOs and versioned integration events
│   ├── StaySphere.Application/     use-case services, ports, validators, event handlers, AI toolbox
│   ├── StaySphere.Infrastructure/  EF Core, outbox, Service Bus, Redis, Blob, email, payments, external APIs, seeder
│   ├── StaySphere.Api/             controllers, SignalR hub, auth, rate limiting, idempotency, ProblemDetails
│   └── StaySphere.Workers/         outbox publisher, consumers, scheduled jobs
├── tests/                          unit, architecture, integration (Testcontainers)
├── web/staysphere-web/             React SPA + Vitest + Playwright
├── infrastructure/{docker,bicep}/  Dockerfiles, Service Bus emulator config, Azure IaC
└── docs/                           architecture, API, database, deployment, security, AI, ADRs, roadmap
```

## Key design decisions
- **No double booking.** Each reservation writes one row per night. A filtered unique index `(PropertyId, Night) WHERE IsActive = 1` makes overlapping holds impossible across any number of API instances. See [ADR-002](docs/adr/ADR-002-sql-server.md).
- **The server owns money.** `PriceCalculator` computes every price. The quote is HMAC-signed and re-verified when the hold is created. Money uses `decimal` everywhere, and the ledger is append-only with compensating entries for refunds.
- **Idempotency everywhere it matters.** An `Idempotency-Key` header protects reservations, payments, cancellations and refunds. The provider receives its own idempotency key. Webhooks are deduplicated by `(provider, eventId)` and checked with an HMAC signature plus a 5-minute replay window.
- **Outbox + inbox.** Events are written in the same transaction as the state change and published afterwards. Consumers deduplicate per handler. See [ADR-004](docs/adr/ADR-004-service-bus.md).
- **Auth.** Access JWTs last 15 minutes and are kept in memory. The refresh token lives in an HttpOnly, SameSite=Strict cookie that rotates on every use; reusing an old token revokes the whole token family. Accounts lock after 5 failed attempts, and rate limits apply per policy.
- **Request-to-book.** Listings with Instant Book off take requests. The guest's card is *authorized* (not charged) and the dates are blocked. The host has 24 hours to accept, which captures the payment and confirms the stay, or decline, which voids the authorization and frees the dates. Unanswered requests expire automatically, and a guest can withdraw at any time.
- **Host payouts.** Earnings become available 24 hours after check-in and are paid out daily per currency, or on demand with "Pay out now". Each payout writes negative `Payout` ledger entries in the same transaction as the payout row. A filtered unique index allows only one payout in flight per host, and a failed transfer is reversed with compensating entries. Only a masked IBAN is stored; in dev, an IBAN ending in `0000` simulates a failed transfer.
- **AI safety.** Agents act only through tools that call application services as the signed-in user. PII is redacted before text reaches a model. Bookings become a server-side pending action that executes only after the user clicks Confirm. See [AI agents](docs/architecture/06-ai-agents.md).

Full documentation: [docs/](docs/). Rewriting the back end in Python: [FastAPI port guide](docs/python-fastapi-port.md). Implementation notes and deviations from the original design: [ADR-008](docs/adr/ADR-008-implementation-notes.md).
