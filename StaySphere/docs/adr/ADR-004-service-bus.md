# ADR-004: Azure Service Bus with transactional outbox and idempotent inbox

- **Status:** Accepted (implemented)
- **Decision:** A single topic `staysphere.events` with filtered subscriptions per consumer, plus queues for work items (image processing, email). Producers only write to `OutboxMessages` inside the business transaction, and a background publisher forwards them (MessageId = outbox Id, duplicate detection on). Consumers dedupe through `InboxMessages(MessageId, Consumer)`. A max delivery count leads to the DLQ, which is monitored and replayable. An `IMessageBus` abstraction has ServiceBus and InMemory implementations.
- **Consequences:** No lost or phantom events (nothing is published before commit), with at-least-once delivery and effectively-once processing. Cross-module consistency is eventual, which is accepted for notifications, analytics, search and the ledger.
