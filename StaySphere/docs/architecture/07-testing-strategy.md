# 16. Testing Strategy

| Layer | Tooling | Scope | Runs |
|---|---|---|---|
| Domain unit | xUnit, Shouldly | aggregates, value objects, state machine, pricing and cancellation math (property-based with FsCheck for Money/DateRange) | every push, < 10 s |
| Application unit | xUnit, NSubstitute | handlers, validators, authorization handlers, AI tool gating | every push |
| Architecture | NetArchTest | layer and module dependency rules, thin controllers | every push |
| Integration | Testcontainers (MsSql, Redis, Azurite), Respawn | EF mappings, migrations apply cleanly, outbox, inbox, idempotency store, **concurrency**, hold expiry, cache invalidation | every PR |
| API | `WebApplicationFactory` + Testcontainers | HTTP contracts, ProblemDetails, auth flows, every policy (positive and cross-tenant), rate-limit 429, versioning | every PR |
| Contract | OpenAPI spec exported in CI; `openapi-typescript` regenerated and `git diff --exit-code`; `oasdiff` breaking-change check against `main` | FE/BE drift, accidental breaking changes | every PR |
| Frontend unit/component | Vitest, React Testing Library, MSW | components, hooks, forms, error and empty states, a11y with `jest-axe` | every PR |
| E2E | Playwright (Chromium + mobile viewport) against `docker compose` | J1 book-and-pay, J2 publish listing, J4 cancel, messaging, admin suspend | PR (smoke) + nightly (full) |
| Performance | k6 | search p95 < 300 ms, booking contention test | nightly / pre-release (staging) |
| Security | CodeQL, gitleaks, Trivy, OWASP ZAP baseline vs staging | | PR + nightly |

## Mandated critical scenarios → test location
| Scenario | Expected | Test |
|---|---|---|
| Two users book overlapping dates concurrently (N = 20 parallel) | exactly 1 × 201, rest 409; one active night set | `IntegrationTests/Booking/DoubleBookingTests` |
| Payment webhook delivered twice | 1 PaymentTransaction capture, 1 ledger set, 1 event | `ApiTests/Payments/WebhookIdempotencyTests` |
| `POST /reservations` with the same Idempotency-Key twice | same response, one reservation | `ApiTests/Reservations/IdempotencyTests` |
| Same key, different body | 422 | same |
| Expired hold | job marks Expired, nights released, same dates bookable | `IntegrationTests/Booking/HoldExpiryTests` (using `FakeTimeProvider`) |
| Host edits another host's property | 404 (does not reveal) / 403 for admin-scoped actions | `ApiTests/Authorization/PropertyOwnershipTests` |
| Review without a reservation / before checkout / twice | 403 / 422 / 409 | `UnitTests/Reviews` + `ApiTests/Reviews` |
| Client sends a tampered total | ignored; server quote used; mismatched quoteId → 409 "price changed" | `ApiTests/Reservations/PricingIntegrityTests` |
| Refresh token reuse | whole family revoked | `ApiTests/Auth/RefreshRotationTests` |
| Outbox publish failure | retried; not lost; no publish before commit | `IntegrationTests/Messaging/OutboxTests` |
| AI tries `CreateReservationHold` without confirmation | PendingAction only, no DB row | `UnitTests/Ai/ConfirmationGateTests` |
| Logs never contain secrets | captured sink has no `password`/`Bearer` | `ApiTests/Observability/LogRedactionTests` |

## Quality gates
- The build fails on compiler warnings (nullable included) and analyzer errors.
- Coverage is reported (Coverlet → Codecov/summary), with a gate on Domain + Application of
  at least 80% lines. There is no gate on Infrastructure or UI, to avoid test theatre.
- `eslint`, `tsc --noEmit`, `prettier --check` and `dotnet format --verify-no-changes` must all pass.
