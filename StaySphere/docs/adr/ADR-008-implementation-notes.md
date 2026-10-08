# ADR-008: Implementation decisions and deviations from the initial design

- **Status:** Accepted (implemented)

The first design package was written before any code. Building the system led to these concrete choices. Each is listed with its reason, so a reviewer can tell deliberate trade-offs from omissions.

| Area | Design said | Implemented | Why |
|---|---|---|---|
| .NET version | .NET 10 LTS (D1) | .NET 10 | As recommended |
| CQRS dispatcher | in-house dispatcher (D2) | **Application services per module** (commands and queries as request records, one service method per use case) | Same separation as a mediator without reflection-heavy pipelines; easier to read and debug. Validation runs in an MVC filter; audit is explicit |
| EF Core in Application | Application has no EF reference | Application references `Microsoft.EntityFrameworkCore` (**abstractions only**) through `IAppDbContext` | Avoids a generic-repository layer that would hide LINQ projections. The SQL Server provider, `SqlClient` and Azure SDKs stay out of Application, and architecture tests enforce this |
| Strongly typed IDs | `PropertyId` record structs | `Guid` (UUIDv7, client-generated) | Kept the EF mapping simple; IDs are never mixed in practice, and this can be introduced per aggregate later |
| Schemas | one schema per module | Implemented (`identity`, `catalog`, `pricing`, `booking`, `payments`, `reviews`, `engagement`, `trust`, `platform`) | — |
| SignalR hubs | `/hubs/chat` + `/hubs/notifications` | one authenticated `/hubs/realtime` hub | One connection per client; the methods are still separate |
| Rate limiting | Redis-backed in prod | ASP.NET Core in-process limiter (per user or IP) | A distributed limiter is a deployment concern (Front Door WAF rate rules in Azure) |
| JWT signing | ES256 with `kid` rotation | HS256 key from Key Vault | Simpler key handling for a single issuer and audience. Asymmetric signing is a drop-in change in `JwtTokenService` |
| Search index worker | separate denormalised table | SQL search through `ISearchProvider` + Redis cache. The "indexer" handler invalidates caches | Fine at this scale. Azure AI Search plugs in behind the same port |
| Analytics roll-ups | aggregate tables | computed on demand from transactional tables and the ledger | Small data volumes. The interface allows materialised roll-ups later |
| Host accept/decline | request-to-book flow | All bookings are instant (hold, then pay) | Reduces scope. The `InstantBook` flag exists, and request-to-book is on the roadmap |
| Images | quarantine container + async worker | **Synchronous** validation in the API (magic bytes, decode, size limits, re-encode to JPEG which strips EXIF, thumbnail), plus an `IMalwareScanner` port | Fewer moving parts. Defender for Storage plugs in through the port |
| Seed images / map tiles | — | picsum.photos and OpenStreetMap tiles, with a branded fallback if a host is blocked | Free; no API keys |
| Resource ownership errors | 404 for foreign resources | **404** for private resources (reservations, conversations, tickets), **403** for modifying someone else's public listing (as the brief's test requires) | Avoids leaking the existence of private data |
| Validation status codes | — | 400 for request-shape validation (FluentValidation) and **422** for business-rule violations | Clients can tell the two apart |

## Known gaps (roadmap)
- Request-to-book (host approval) flow, host payouts, and promotions beyond coupons.
- Server-side rendering or prerendering of listing pages for SEO. Client-side meta tags and JSON-LD are implemented.
- k6 performance tests and an OWASP ZAP baseline in CI.
- Experiences (behind the `Experiences` feature flag).
