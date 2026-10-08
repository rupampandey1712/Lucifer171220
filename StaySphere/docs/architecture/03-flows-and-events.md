# 8–10. Booking & Payment Flows, Outbox, Service Bus Event Map

## Reservation state machine

```mermaid
stateDiagram-v2
    [*] --> Held: POST /reservations (nights locked)
    Held --> PaymentPending: payment submitted
    Held --> Expired: hold timeout (10 min) → nights released
    PaymentPending --> Confirmed: PaymentSucceeded
    PaymentPending --> Failed: PaymentFailed → nights released
    PaymentPending --> Expired: no result before timeout + grace
    Held --> Cancelled: guest abandons
    Confirmed --> RefundPending: cancel (refund > 0)
    Confirmed --> Cancelled: cancel (refund = 0)
    RefundPending --> Refunded: RefundCompleted
    Confirmed --> Completed: checkout date passed (daily job)
    Completed --> [*]
    Cancelled --> [*]
    Refunded --> [*]
    Failed --> [*]
    Expired --> [*]
```

The brief's `Pending` state is represented by `Held`, because there is no reservation before the
hold. `Expired` is added as a terminal state, distinct from `Failed`, so analytics can tell them
apart. Transitions are methods on the `Reservation` aggregate (`Hold`, `MarkPaymentPending`,
`Confirm`, `Cancel(policy, now)`, `Complete`, `Expire`), and illegal transitions return a
domain error.

## 8. Booking sequence (hold → pay → confirm)

```mermaid
sequenceDiagram
    autonumber
    actor Guest
    participant React
    participant API as API (ReservationsController)
    participant RS as ReservationService
    participant PR as PricingService
    participant SQL as SQL Server
    participant PS as PaymentService
    participant FP as FakePaymentProvider
    participant OB as Outbox Worker
    participant SB as Service Bus
    participant NS as Notification Worker

    Guest->>React: choose dates, click Reserve
    React->>API: POST /properties/{id}/quote
    API->>PR: Quote(property, dates, guests, coupon)
    PR->>SQL: base, seasonal, rules, taxes (read)
    PR-->>React: breakdown + signed quoteId (5 min TTL)
    Guest->>React: confirm
    React->>API: POST /reservations (Idempotency-Key, quoteId)
    API->>RS: CreateReservationHold
    RS->>PR: re-quote and compare (never trust client total)
    RS->>SQL: BEGIN TRAN (READ COMMITTED SNAPSHOT)
    RS->>SQL: check property published, not blocked dates, guest limits
    RS->>SQL: INSERT Reservation(Held, HoldExpiresAt=now+10m)
    RS->>SQL: INSERT ReservationNights (UQ PropertyId+Night WHERE IsActive)
    alt another booking holds any night
        SQL-->>RS: unique violation 2601
        RS-->>API: Result.Conflict
        API-->>React: 409 "Those dates were just booked"
    else free
        RS->>SQL: INSERT OutboxMessage(ReservationHeld), COMMIT
        API-->>React: 201 {reservationId, holdExpiresAt}
    end
    React->>API: POST /reservations/{id}/payment (Idempotency-Key, test card token)
    API->>PS: ProcessPayment
    PS->>SQL: Reservation→PaymentPending, Payment(Initiated) COMMIT
    PS->>FP: Authorize+Capture (timeout 10s, NO retry)
    alt success
        FP-->>PS: succeeded(providerPaymentId)
        PS->>SQL: BEGIN, Payment Succeeded, Txn rows, Ledger entries, Reservation→Confirmed, Outbox(PaymentSucceeded + ReservationConfirmed), COMMIT
        API-->>React: 200 Confirmed
    else declined / failed
        PS->>SQL: Payment Failed, Reservation→Failed, nights IsActive=0, Outbox(PaymentFailed), COMMIT
        API-->>React: 402 ProblemDetails (card declined)
    else timeout / unknown
        PS-->>API: Pending (reconciliation decides via webhook or poll)
        API-->>React: 202 Accepted, client polls GET payment
    end
    OB->>SQL: claim unprocessed outbox rows (UPDLOCK, READPAST)
    OB->>SB: publish ReservationConfirmed (MessageId = outbox Id)
    OB->>SQL: mark processed
    SB-->>NS: ReservationConfirmed
    NS->>NS: inbox dedupe, then email guest and host (Mailpit) + in-app + SignalR push
```

### Concurrency: two guests, the same nights

```mermaid
sequenceDiagram
    participant A as Guest A
    participant B as Guest B
    participant API
    participant SQL
    par
        A->>API: POST /reservations (3–7 Jun)
        B->>API: POST /reservations (5–9 Jun)
    end
    API->>SQL: Tx A: insert nights 3,4,5,6
    API->>SQL: Tx B: insert nights 5,6,7,8
    Note over SQL: Tx B blocks on the index key lock for night 5 until Tx A commits
    SQL-->>API: Tx A COMMIT ✅
    SQL-->>API: Tx B unique violation ❌
    API-->>A: 201 Held
    API-->>B: 409 Conflict
```

This is verified by an integration test that fires N parallel requests against a real SQL
Server (Testcontainers) and asserts exactly one success.

### Hold expiry
`HoldExpiryJob` runs in Workers every 30 seconds. It claims expired holds with
`UPDATE TOP (100) ... OUTPUT ... WITH (READPAST)` on `Status IN ('Held','PaymentPending')
AND HoldExpiresAt < now - grace`. For each one it calls `Reservation.Expire()`, deactivates the
nights and writes an outbox `ReservationExpired`. This is safe with many worker replicas because
of `READPAST` row claiming, so no distributed lock is needed. For `PaymentPending` it **first asks
the provider** for status, so a late success can't be lost.

## 9. Payment sequence: idempotency and webhooks

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant MW as Idempotency filter
    participant SQL
    participant PS as PaymentService
    participant P as Provider (Fake/Stripe…)
    participant WH as Webhook endpoint

    C->>MW: POST /reservations/{id}/payment, Idempotency-Key: K
    MW->>SQL: INSERT IdempotencyRecord(scope,user,K,requestHash, status=InProgress)
    alt key exists, completed, same hash
        MW-->>C: replay stored response (same status and body)
    else key exists, in progress
        MW-->>C: 409 "request in progress"
    else key exists, different body
        MW-->>C: 422 "key reused with different payload"
    else new
        MW->>PS: execute
        PS->>P: charge(amount, currency, providerIdempotencyKey = paymentId)
        P-->>PS: result
        PS-->>MW: response
        MW->>SQL: store response for K (24 h TTL)
        MW-->>C: response
    end

    P-)WH: POST /payments/webhook/fake (signature, eventId)
    WH->>WH: verify HMAC signature + timestamp within 5 min (replay window)
    WH->>SQL: INSERT WebhookEvents(provider,eventId) – UQ blocks duplicates
    alt duplicate
        WH-->>P: 200 (already processed)
    else new
        WH->>SQL: apply state change idempotently (only if Payment not already terminal) + Outbox, COMMIT
        WH-->>P: 200
    end
```

`LocalFakePaymentProvider` picks its scenario from the test card number:

| Card | Outcome |
|---|---|
| 4242 4242 4242 4242 | success |
| 4000 0000 0000 0002 | declined |
| 4000 0000 0000 9995 | insufficient funds |
| 4000 0000 0000 0119 | timeout (no response, webhook arrives 15 s later) |
| 4000 0000 0000 0259 | success + duplicate webhook delivered twice |

The provider also exposes `POST /fake-provider/refunds` and emits signed webhooks to the API.
The server only ever sees a token like `tok_4242`, never a PAN. Real PANs are rejected by
format in non-dev builds.

### Refund and ledger
Cancelling calls `ICancellationPolicyService.Calculate(policy, reservation, now)`, which
returns a refund amount. Then: `Refund(Requested)` → provider → `RefundCompleted`.
The ledger adds compensating entries (`Refund` debit to platform, reversal of `HostEarning`
proportional to the refund). Rows are never updated or deleted.

| Ledger entry on confirmation (example, total 500.00) | Debit | Credit |
|---|---|---|
| GuestPayment | 500.00 | |
| PlatformFee (service fee + 3% host fee) | | 62.00 |
| Taxes payable | | 38.00 |
| HostEarning | | 400.00 |

## Outbox pattern

```mermaid
flowchart LR
    subgraph Tx["Single DB transaction"]
        A[Aggregate change] --> D[Domain events collected]
        D --> M[Map to integration events]
        M --> O[(OutboxMessages)]
    end
    Tx -->|COMMIT| W[OutboxPublisher<br/>BackgroundService, every 1 s<br/>batch 100, READPAST claim]
    W -->|MessageId = outbox Id<br/>dedupe enabled| SB[[Service Bus topic: staysphere.events]]
    W -->|success| P[ProcessedAt = now]
    W -->|failure| R[Attempts++, backoff<br/>after 10 → FailedAt + alert]
    SB --> S1[sub: notifications] & S2[sub: analytics] & S3[sub: search] & S4[sub: ledger/host] & S5[sub: fraud]
    S1 -->|max delivery 10| DLQ[(Dead-letter queue)]
```

- The outbox rows are written by an EF Core `SaveChangesInterceptor`, so a handler can't
  "forget". **Nothing is published before commit.**
- Delivery is at-least-once. Service Bus duplicate detection (on `MessageId`) plus consumer
  `InboxMessages` gives effectively-once processing.
- Ordering per reservation uses `SessionId = ReservationId` on subscriptions that need it.
- The DLQ is surfaced on the admin dashboard, and `staysphere ops replay-dlq` re-submits after a fix.

## 10. Service Bus event map

One topic, `staysphere.events`, with SQL-filter subscriptions per consumer on the `EventType`
property. Commands that need a single consumer (image processing, emails) use dedicated
**queues**.

| Integration event | Producer | Notification | Analytics | Search idx | Ledger/Host | Fraud | Other |
|---|---|---|---|---|---|---|---|
| UserRegistered | Identity | ✅ verify email | ✅ | | | ✅ multi-account check | Hosting |
| EmailVerified | Identity | ✅ welcome | ✅ | | | | |
| PropertyPublished / Updated / Unpublished | Catalog | ✅ host | ✅ | ✅ | | | cache invalidation |
| ImageUploaded → queue `image-processing` | Catalog | | | | | | ImageWorker produces ImageProcessed |
| PricingChanged / AvailabilityChanged | Pricing | | | ✅ | | | cache invalidation |
| ReservationHeld | Booking | | ✅ funnel | | | ✅ velocity | |
| ReservationConfirmed | Booking | ✅ guest + host | ✅ | ✅ (occupancy) | ✅ | | conversation auto-created |
| ReservationCancelled | Booking | ✅ | ✅ | ✅ | ✅ | ✅ rapid-cancel | Payments → refund |
| ReservationExpired | Booking | ✅ "hold expired" | ✅ | | | | |
| ReservationCompleted | Booking | ✅ review reminder | ✅ | | ✅ payout eligible | | Reviews enabled |
| PaymentSucceeded | Payments | ✅ receipt | ✅ | | | | Booking confirms |
| PaymentFailed | Payments | ✅ | ✅ | | | ✅ repeated failures | Booking fails |
| RefundCompleted | Payments | ✅ | ✅ | | ✅ | | Booking → Refunded |
| ReviewCreated | Reviews | ✅ host | ✅ | ✅ rating | | ✅ spam heuristics | |
| MessageSent | Engagement | ✅ email if offline >5 min | | | | | |
| FraudAlertRaised | Trust | ✅ admins | ✅ | | | | |
| UserSuspended | Identity | ✅ | ✅ | ✅ hide listings | | | sessions revoked |

Scheduled jobs (Workers, using a cron-style `PeriodicTimer` plus row claiming; Container Apps
Jobs in Azure): hold expiry (30 s), reservation completion (hourly), check-in reminder (daily,
48 h before), review reminder (daily), payment reconciliation (5 min), analytics roll-up
(hourly/nightly), idempotency and outbox cleanup (daily).

## Caching (cache-aside)

| Key | TTL | Invalidated by |
|---|---|---|
| `property:{id}:v{rowversion}` detail DTO | 10 min | PropertyUpdated/Published, PricingChanged |
| `search:{hash(normalised query)}` | 60 s | short TTL only (no fan-out invalidation) |
| `geocode:{normalised q}` | 30 days | — |
| `weather:{lat2dp}:{lng2dp}` | 30 min | — |
| `fx:{base}` | 6 h | — |
| `availability:{propertyId}:{month}` | 2 min | AvailabilityChanged, ReservationConfirmed/Cancelled/Expired |

The quote and reservation write path **never reads from cache**.
