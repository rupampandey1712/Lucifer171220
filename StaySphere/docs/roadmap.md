# 18. Development Roadmap

> **Status (implemented):** phases 1–10 are delivered as a working vertical slice: backend, frontend, tests, Docker, CI/CD and Bicep. See the project [README](../README.md) for what is verified and [ADR-008](adr/ADR-008-implementation-notes.md) for deviations and remaining gaps.

Each phase ends with the brief's §127 gate: the backend compiles with no warnings, unit,
integration and frontend tests pass, lint passes, Docker builds, APIs and UI are manually
verified, and docs are updated. **No phase starts on a red build.**

| Phase | Scope | Exit criteria / demo |
|---|---|---|
| **1. Foundation** | Solution and central packages; Domain primitives (Result, StronglyTypedId, Money, DateRange, EmailAddress…); ServiceDefaults (OTel, health, resilience); DbContext + interceptors (audit, timestamps, outbox skeleton); ProblemDetails; API versioning; rate limiting; **Identity**: register, verify email (Mailpit), login, refresh rotation, logout, forgot/reset, change password, lockout; roles and policies; seeder (users); docker-compose (sql, redis, azurite, mailpit, aspire, api, web); SPA shell (router, layout, auth pages, AuthProvider, generated client); architecture tests; CI `backend.yml` + `frontend.yml` | `docker compose up` → register → email in Mailpit → verify → login → refresh survives reload; auth API tests green |
| **2. Catalog & Search** | Hosts; Properties aggregate; addresses and geocoding (Nominatim + mock + cache); amenities; image upload (Azurite, validation, thumbnail worker); 12-step wizard with autosave; publish; SQL search provider with filters, sort, offset + cursor; Redis cache-aside; home, search (Leaflet map) and property detail pages; seeder (120 properties) | Host publishes a listing with photos; it appears in search and on the map |
| **3. Availability, Pricing, Reservations** | Blocked dates, calendar; pricing engine (seasonal, weekend, LOS, cleaning, service fee, taxes, coupons); signed quotes; Reservation aggregate and state machine; ReservationNights guard; holds + expiry job; idempotency middleware; Workers host + outbox publisher (in-memory bus first) | Concurrency test green; hold expiry demo |
| **4. Payments, Refunds, Notifications** | Fake provider + webhooks; payment idempotency; ledger; cancellation policies + refunds; Service Bus emulator transport; inbox; notification worker (email + in-app + SignalR hub) | Book → pay → confirm email; cancel → refund ledger entries |
| **5. Reviews, Favorites, Messaging** | Reviews with rules + moderation + host response; favorites; SignalR chat (typing, receipts, presence, Redis backplane); reports | Real-time chat between two browsers |
| **6. Host & Admin** | Host dashboard, earnings from ledger, analytics roll-ups; admin console (users, properties, reservations, payments, reviews, reports, audit); support tickets; fraud rule engine v1 | KPIs and charts populated from seed data |
| **7. AI Assistant** | Agent Framework + `IChatClient` (Ollama); tool registry; agents; confirmation gate; guards; SSE UI | J7 journey end to end against a local model |
| **8. Hardening** | Playwright suite; k6; ZAP; a11y audit; log redaction tests; perf indexes; SEO (prerendered meta for `/property/:id`) | Quality gates in the testing doc met |
| **9. CI/CD** | e2e, codeql, image build + scan, deploy workflows | Green pipeline to staging |
| **10. Azure** | Bicep modules, environments, managed identity, Front Door, migration job | `what-if` clean; staging deployed |

The CI workflows for build and test start in Phase 1 rather than Phase 9, so every phase is
validated in CI. Phase 9 adds the deployment parts.

## Open decisions for approval

| # | Decision | Recommendation | Alternative |
|---|---|---|---|
| D1 | .NET version | **.NET 10 LTS** (support to Nov 2028; .NET 8 support ends Nov 2026) | .NET 8 as requested in the brief |
| D2 | CQRS dispatcher | **In-house minimal dispatcher + decorators** (about 150 LOC, no licence risk) | MIT `Mediator` (source-gen); MediatR (commercial licence for v12+) |
| D3 | Test assertion / mocking libs | **Shouldly + NSubstitute** | FluentAssertions v7 (last free, frozen) / AwesomeAssertions fork; Moq |
| D4 | Local Service Bus | **Official emulator** + `InMemory` fallback | In-memory only (lighter; less realistic) |
| D5 | Identity | **ASP.NET Core Identity** (users in our DB) + custom JWT issuance; Entra ID only for staff later | Duende IdentityServer (commercial), Entra External ID |
| D6 | Search | **SQL Server** (geography + indexes) behind `ISearchProvider`; Azure AI Search later | Start with OpenSearch container |
| D7 | Component library | **Tailwind + Radix primitives (shadcn-style owned components)** | MUI, Mantine |
| D8 | Location of the code | **`StaySphere/` subfolder of this repo** | Separate dedicated repository (recommended long term for a portfolio piece) |
| D9 | Toolchain in this cloud workspace | The container has Node 22 + Docker but **no .NET SDK**. Phase 1 will install the .NET SDK via `dotnet-install.sh` (needs network access to `dot.net` / `builds.dotnet.microsoft.com`), or set up a SessionStart hook for it. | — |
