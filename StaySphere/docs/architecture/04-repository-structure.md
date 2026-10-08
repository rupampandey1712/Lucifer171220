# 13. Repository Structure

```
StaySphere/
├── StaySphere.sln
├── Directory.Build.props          # net10.0, Nullable, TreatWarningsAsErrors, analyzers
├── Directory.Packages.props       # central package management
├── global.json  .editorconfig  .env.example  docker-compose.yml  docker-compose.infra-only.yml
├── src/
│   ├── StaySphere.Domain/
│   │   ├── Common/                # Entity, AggregateRoot, IDomainEvent, Result, Error, StronglyTypedId
│   │   ├── ValueObjects/          # Money, Currency, Address, Coordinates, DateRange, EmailAddress, PhoneNumber
│   │   ├── Identity/ Hosting/ Catalog/ Pricing/ Booking/ Payments/ Reviews/ Engagement/ Trust/
│   ├── StaySphere.Application/
│   │   ├── Abstractions/          # ICommand/IQuery/handlers, IUnitOfWork, IClock, ICurrentUser,
│   │   │                          # IFileStorageService, IMessageBus, IEmailSender, IPaymentProvider,
│   │   │                          # IGeocodingService, IWeatherService, ICurrencyService, ISearchProvider, IAiService
│   │   ├── Behaviors/             # validation, logging, transaction, audit pipeline decorators
│   │   └── <Module>/{Commands,Queries,Contracts,EventHandlers,Validators}/
│   ├── StaySphere.Contracts/      # API request/response records, integration events (versioned)
│   ├── StaySphere.Infrastructure/
│   │   ├── Persistence/           # StaySphereDbContext, Configurations/<Module>/, Migrations/, Interceptors/ (outbox, audit, timestamps)
│   │   ├── Messaging/             # ServiceBusMessageBus, InMemoryMessageBus, OutboxPublisher, Inbox
│   │   ├── Storage/ Caching/ Email/ Payments/ Geo/ Weather/ Currency/ Search/ Ai/ Identity/
│   │   └── DependencyInjection.cs
│   ├── StaySphere.Api/
│   │   ├── Controllers/v1/  Hubs/  Middleware/  Authorization/  RateLimiting/  OpenApi/
│   │   └── Program.cs
│   ├── StaySphere.Workers/        # BackgroundServices, Service Bus processors, scheduled jobs
│   ├── StaySphere.Seeder/         # deterministic dev data
│   └── StaySphere.ServiceDefaults/# OpenTelemetry, health checks, resilience defaults (shared by Api and Workers)
├── tests/
│   ├── StaySphere.UnitTests/          # Domain + Application (no I/O)
│   ├── StaySphere.IntegrationTests/   # Testcontainers: SQL, Redis, Azurite; EF, outbox, concurrency
│   ├── StaySphere.ApiTests/           # WebApplicationFactory + Testcontainers: HTTP, auth, ProblemDetails
│   └── StaySphere.ArchitectureTests/  # NetArchTest layer rules
├── web/staysphere-web/
│   ├── src/ (see frontend doc)  e2e/ (Playwright)  Dockerfile  nginx.conf
├── infrastructure/
│   ├── bicep/ (see Azure doc)
│   └── docker/ wiremock/mappings/  servicebus-emulator/config.json  sql/init.sql
├── scripts/  dev-up.sh  dev-infra.sh  export-openapi.sh  seed.sh
├── docs/  architecture/ api/ database/ deployment/ adr/ roadmap.md
└── (repository root) .github/workflows/  backend.yml  frontend.yml  infra.yml  deploy.yml  codeql.yml
```

Conventions:
- One file per type. `sealed` by default. Records for DTOs, commands, queries and events.
  Primary constructors for DI.
- `CancellationToken` flows through every async boundary. No `async void`. No `.Result` or `.Wait()`.
- Commands and queries are plain records with handlers. Pipeline decorators handle validation,
  logging, transactions and audit (see [roadmap open decisions](../roadmap.md#open-decisions-for-approval)
  for MediatR licensing).
