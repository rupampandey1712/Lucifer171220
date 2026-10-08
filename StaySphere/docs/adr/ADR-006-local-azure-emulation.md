# ADR-006: Local-first development with Azure emulators

- **Status:** Accepted (implemented)
- **Decision:** Every Azure dependency sits behind an interface and has a local substitute: SQL Server container, Redis, Azurite, the official Service Bus emulator (or in-memory), Mailpit, Aspire Dashboard for OTLP, Ollama, WireMock for public APIs, and a fake payment provider. `EXTERNAL_SERVICES_MODE=Mock|Live` toggles external API adapters. One `docker compose up` runs everything.
- **Consequences:** No Azure subscription is needed to develop or run CI. Some emulator behaviours differ from the cloud (for example, the Service Bus emulator has quotas and no Premium features), so staging is where cloud parity is verified.
