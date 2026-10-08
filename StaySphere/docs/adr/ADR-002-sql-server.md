# ADR-002: SQL Server as system of record; DB-enforced booking exclusivity

- **Status:** Accepted (implemented)
- **Context:** Reservations, payments and the ledger need ACID guarantees. Double booking must be impossible across N API instances.
- **Decision:** Use SQL Server (Azure SQL in production) with EF Core. Booking exclusivity comes from a `ReservationNights(PropertyId, Night)` table with a **filtered unique index `WHERE IsActive = 1`**, written in the same transaction as the reservation. Aggregates use `rowversion` for optimistic concurrency. READ_COMMITTED_SNAPSHOT is on. The geography type serves map and radius search.
- **Alternatives:** Distributed Redis locks (correctness depends on lock leases and fails open under partitions); SERIALIZABLE range checks (deadlock-prone, lower throughput); an exclusion constraint (PostgreSQL only).
- **Consequences:** Correctness is enforced by the database and is testable. There is one row per night (a year of bookings for 1M listings is about 365M narrow rows, which is fine with partitioning or archiving of past nights).
