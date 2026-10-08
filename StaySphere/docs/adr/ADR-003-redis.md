# ADR-003: Redis for cache, rate limits and SignalR backplane only

- **Status:** Accepted (implemented)
- **Decision:** Redis serves cache-aside reads (property detail, search pages, geocode, weather, FX), distributed rate-limit counters, and the SignalR backplane. It is **never** the source of truth: reservation holds live in SQL. All Redis use fails open with a circuit breaker.
- **Consequences:** Losing Redis degrades latency, not correctness. No cache invalidation bugs can cause a double booking, because the write path never reads the cache.
