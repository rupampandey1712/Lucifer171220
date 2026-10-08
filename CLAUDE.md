# StaySphere — guidance for Claude Code (and humans)

The project lives in `StaySphere/`. Read `StaySphere/README.md` first, then `StaySphere/docs/`.

## Commands (run from `StaySphere/`)
- Backend build: `dotnet build` (warnings are errors)
- Backend tests: `dotnet test tests/StaySphere.UnitTests`, `dotnet test tests/StaySphere.ArchitectureTests`,
  `dotnet test tests/StaySphere.IntegrationTests` (needs Docker — Testcontainers starts SQL Server)
- New migration: `dotnet tool restore && dotnet ef migrations add <Name> --project src/StaySphere.Infrastructure --startup-project src/StaySphere.Infrastructure --output-dir Persistence/Migrations`
- Frontend (from `web/staysphere-web`): `npm ci`, `npm run dev`, `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`
- Regenerate TS API types after changing API contracts (API running on :8081): `npm run gen:api`
- Full stack: `docker compose up -d --build` → http://localhost:5173
- E2E (stack running): `npx playwright install chromium && npx playwright test`

## Architecture rules (enforced by ArchitectureTests)
- Domain has no dependencies. Business rules live in aggregates (`Reservation`, `Property`, `Payment`, …).
- Application holds use-case services + ports (interfaces). It may use EF Core abstractions (ADR-008) but never
  SqlServer/Azure/ASP.NET types.
- Controllers stay thin: validate → call one application service → map `Result` to HTTP.
- Expected failures return `Result`/`Error`; exceptions are for bugs and infrastructure faults.
- Cross-module side effects go through domain events → outbox → `IIntegrationEventHandler<T>` (idempotent via inbox).
- Money is `decimal`; prices are always computed server-side (`PriceCalculator`) and bound with signed quote tokens.
- Double booking is prevented by the filtered unique index on `booking.ReservationNights` — keep it.
- AI tools (`AssistantToolbox`) call application services as the current user; financial actions need `AiPendingAction` confirmation.

## Definition of done
Backend + tests, frontend + loading/error/empty states, docs updated, `dotnet test` and `npm test` green, Docker builds.
