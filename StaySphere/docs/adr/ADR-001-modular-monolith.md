# ADR-001: Modular monolith with event-driven side effects

- **Status:** Proposed
- **Context:** The brief lists 28 functional areas, a small team, and a requirement to scale and allow later extraction. Microservices from day one would add network hops, distributed transactions, per-service pipelines and operational cost, before the boundaries have been validated.
- **Decision:** Build one API and one worker deployable, organised into 12 bounded contexts. Each context has its own SQL schema and a public contract interface. Cross-context side effects go through integration events using the outbox and Service Bus. Architecture tests enforce the boundaries.
- **Consequences:** Transactions within a module are simple and refactoring is cheap. A module can be extracted by moving its schema and swapping its in-process contract for HTTP or messaging. The risk is boundary erosion, which architecture tests and code review mitigate. Scale is horizontal: API replicas behind a load balancer, and workers scaled by queue depth.
