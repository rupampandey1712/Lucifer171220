# StaySphere

A production-grade accommodation marketplace (guests, hosts, admins, support) built as a
**modular monolith** on ASP.NET Core + SQL Server + Redis + Azure Service Bus + Blob Storage,
with a React/TypeScript SPA, SignalR real-time features and a tool-based AI assistant.

Runs fully locally (`docker compose up`) using Azure emulators; deploys to Azure via Bicep +
GitHub Actions.

> **Status: Design package — awaiting approval.**
> Per the project brief (§129), no implementation code has been written yet. The documents
> below describe what will be built. Implementation starts with Phase 1 once approved.

## Design package

| # | Deliverable | Document |
|---|-------------|----------|
| 1–3 | System architecture, component diagram, module boundaries | [architecture/01-system-architecture.md](docs/architecture/01-system-architecture.md) |
| 4 | Database ER diagram and table design | [database/er-model.md](docs/database/er-model.md) |
| 5 | API inventory | [api/api-inventory.md](docs/api/api-inventory.md) |
| 6–7 | Frontend page inventory, user journeys | [architecture/02-frontend-and-journeys.md](docs/architecture/02-frontend-and-journeys.md) |
| 8–10 | Booking & payment sequences, Service Bus event map, outbox | [architecture/03-flows-and-events.md](docs/architecture/03-flows-and-events.md) |
| 11 | Local Docker architecture | [deployment/local-development.md](docs/deployment/local-development.md) |
| 12 | Azure production architecture | [deployment/azure-architecture.md](docs/deployment/azure-architecture.md) |
| 13 | Repository structure | [architecture/04-repository-structure.md](docs/architecture/04-repository-structure.md) |
| 14 | Security architecture & threat model | [architecture/05-security.md](docs/architecture/05-security.md) |
| 15 | AI agent architecture | [architecture/06-ai-agents.md](docs/architecture/06-ai-agents.md) |
| 16 | Testing strategy | [architecture/07-testing-strategy.md](docs/architecture/07-testing-strategy.md) |
| 17 | CI/CD architecture | [deployment/cicd.md](docs/deployment/cicd.md) |
| 18 | Development roadmap | [roadmap.md](docs/roadmap.md) |
| — | Architecture Decision Records | [adr/](docs/adr/) |

## Decisions that need your approval

These are the choices where the brief left room or where the ecosystem has moved. Each has a
recommendation; see [roadmap.md § Open decisions](docs/roadmap.md#open-decisions-for-approval).

1. **.NET 10 (LTS)** rather than .NET 8, whose support ends in November 2026.
2. **No MediatR / FluentAssertions / Moq.** MediatR v12+ and FluentAssertions v8+ are now
   commercially licensed. We'll use a small in-house command/query dispatcher (or the MIT-licensed
   `Mediator` source generator), plus **Shouldly** and **NSubstitute**.
3. **Official Azure Service Bus emulator** locally, behind an `IMessageBus` abstraction that also
   has an in-memory implementation for tests and lightweight dev.
4. **Microsoft Agent Framework + `Microsoft.Extensions.AI`** for agents, with Ollama as the
   default local provider.
5. **Auth tokens.** The access token is held only in memory in the SPA. The refresh token goes in
   an HttpOnly, Secure, SameSite=Strict cookie scoped to `/api/v1/auth`.
6. **Folder layout.** StaySphere lives in `StaySphere/` inside this repository, and CI workflows
   go in the repository root `.github/workflows` with path filters.
