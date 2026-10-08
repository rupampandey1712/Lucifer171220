# 5. API Inventory (v1)

Base path `/api/v1`. All responses are JSON and all errors are RFC 9457 ProblemDetails with
`traceId`. Auth uses `Authorization: Bearer <access JWT>`. The refresh token travels in an
HttpOnly cookie.

Legend for the access column:
- **Anon**: anonymous
- **User**: any authenticated user
- **Owner**: resource-based check (owning user or host)
- Policy names refer to the policies listed in [05-security.md](../architecture/05-security.md)

The **Idem.** column marks endpoints that require an `Idempotency-Key` header.
The **RL** column gives the rate-limit policy.

## Auth & identity
| Method | Path | Access | Idem. | RL |
|---|---|---|---|---|
| POST | /auth/register | Anon | | `auth-strict` |
| POST | /auth/login | Anon | | `auth-strict` |
| POST | /auth/refresh | cookie | | `auth` |
| POST | /auth/logout | User | | |
| POST | /auth/verify-email | Anon (token) | | `auth` |
| POST | /auth/resend-verification | Anon | | `auth-strict` |
| POST | /auth/forgot-password | Anon | | `auth-strict` |
| POST | /auth/reset-password | Anon (token) | | `auth-strict` |
| POST | /auth/change-password | User | | `auth` |
| GET/PUT | /me, /me/profile | User | | |
| POST | /me/avatar | User | | `upload` |
| GET | /me/export | User | | `auth` (GDPR export) |
| DELETE | /me | User | | (anonymise and delete) |

## Catalog & hosting
| Method | Path | Access |
|---|---|---|
| POST | /hosts (become a host) | User |
| GET | /properties/{id} | Anon (published) / Owner (draft) |
| POST | /properties | `CanCreateProperty` |
| PUT/PATCH | /properties/{id} | `CanManageProperty` (owner or admin) |
| DELETE | /properties/{id} | `CanManageProperty` (soft delete) |
| POST | /properties/{id}/publish, /unpublish | `CanManageProperty` |
| POST | /properties/{id}/images (multipart) | `CanManageProperty`, RL `upload` |
| PUT | /properties/{id}/images/order | `CanManageProperty` |
| DELETE | /properties/{id}/images/{imageId} | `CanManageProperty` |
| PUT | /properties/{id}/amenities, /rules, /policy | `CanManageProperty` |
| GET | /amenities | Anon |
| GET | /host/properties | Host |
| GET | /properties/{id}/similar | Anon |
| GET | /properties/{id}/weather | Anon (cached) |

## Availability & pricing
| Method | Path | Access |
|---|---|---|
| GET | /properties/{id}/availability?from&to | Anon |
| PUT | /properties/{id}/blocked-dates | `CanManageProperty` |
| GET/PUT | /properties/{id}/pricing (base, cleaning, rules, seasonal) | `CanManageProperty` |
| POST | /properties/{id}/quote `{checkIn, checkOut, guests, couponCode}` | Anon. The server-authoritative quote returns a line-item breakdown and a signed `quoteId`. |

## Search & geo
| Method | Path | Access |
|---|---|---|
| GET | /search/properties?q&lat&lng&radiusKm&bbox&checkIn&checkOut&guests&type&minPrice&maxPrice&bedrooms&beds&bathrooms&amenities[]&minRating&instantBook&petFriendly&sort&page&pageSize&cursor | Anon, RL `anon` |
| GET | /search/suggest?q | Anon (destinations autocomplete) |
| GET | /geo/geocode?q | Anon (cached, throttled proxy to provider) |
| GET | /destinations/featured | Anon |
| GET | /currencies, /currencies/rates | Anon |

## Reservations
| Method | Path | Access | Idem. |
|---|---|---|---|
| POST | /reservations `{propertyId, checkIn, checkOut, guests, quoteId, couponCode}`: creates a **Held** reservation (10 min) | User | ✅ |
| GET | /reservations (mine as guest) | User | |
| GET | /reservations/{id} | `CanViewReservation` (guest, host of property, support, admin) | |
| POST | /reservations/{id}/cancel/preview | Owner. Returns the refund amount from the policy. | |
| POST | /reservations/{id}/cancel | `CanManageReservation` | ✅ |
| GET | /host/reservations | Host | |
| POST | /host/reservations/{id}/accept, /decline `{reason}` (request-to-book) | Owner host or admin | ✅ |

## Payments
| Method | Path | Access | Idem. |
|---|---|---|---|
| POST | /reservations/{id}/payment `{paymentMethodToken}` | Owner guest, RL `payment` | ✅ required |
| GET | /reservations/{id}/payment | Owner | |
| POST | /payments/webhook/{provider} | Signature-verified (no JWT) | event-id based |
| POST | /admin/payments/{id}/refund | `CanProcessRefund` | ✅ |
| GET | /host/earnings?from&to, /host/payouts | Host | |
| PUT | /host/payout-account `{accountHolder, iban, country}` | Host | |
| POST | /host/payouts (pay out available balance now) | Host, RL `payment` | ✅ |
| GET | /admin/payouts | Admin | |

## Reviews, favorites, messaging, notifications
| Method | Path | Access |
|---|---|---|
| GET | /properties/{id}/reviews?page | Anon |
| POST | /reviews `{reservationId, ratings…, comment}` | Owner guest (completed stay, within 14 days, once only) |
| POST | /reviews/{id}/response | Host of property |
| POST | /reviews/{id}/report | User |
| GET | /favorites | User |
| PUT / DELETE | /favorites/{propertyId} | User (idempotent by design) |
| GET | /conversations | User |
| POST | /conversations `{propertyId, reservationId?, message}` | User |
| GET | /conversations/{id}/messages?before | Participant |
| POST | /conversations/{id}/messages | Participant, RL `messaging` |
| POST | /conversations/{id}/read | Participant |
| POST | /conversations/{id}/report | Participant |
| GET | /notifications, POST /notifications/read-all | User |

SignalR hubs: `/hubs/chat` (SendMessage, Typing, MarkRead; server → ReceiveMessage, Typing,
Presence, ReadReceipt) and `/hubs/notifications`. Both authenticate with an access token in the
query string, and that query string is excluded from request logging.

## Support & trust
| Method | Path | Access |
|---|---|---|
| POST/GET | /support/tickets | User |
| GET/POST | /support/tickets/{id}, /messages | Owner or SupportAgent |
| PATCH | /support/tickets/{id} (status, assign) | SupportAgent |
| POST | /reports | User |

## Admin
| Method | Path | Policy |
|---|---|---|
| GET | /admin/dashboard | `CanViewAdminDashboard` |
| GET/PATCH | /admin/users, /admin/users/{id}/suspend, /reinstate | `CanManageUsers` |
| GET/PATCH | /admin/properties, /{id}/suspend | `CanModerateProperty` |
| GET | /admin/reservations, /admin/payments | `CanViewAdminDashboard` |
| GET/PATCH | /admin/reviews (moderation queue) | `CanModerateReview` |
| GET/PATCH | /admin/reports, /admin/fraud-alerts | `CanManageUsers` |
| GET | /admin/audit?actor&entity&from&to | `CanViewAudit` |
| CRUD | /admin/promotions, /admin/coupons | `CanManagePromotions` |
| GET/PUT | /admin/feature-flags (read-only view in prod) | Admin |

## AI assistant
| Method | Path | Access |
|---|---|---|
| POST | /ai/conversations | User, flag `AiAssistant` |
| POST | /ai/conversations/{id}/messages (SSE streaming response) | Owner, RL `ai` |
| POST | /ai/conversations/{id}/confirmations/{actionId} `{approve: bool}` | Owner |

## Ops
| Path | Purpose |
|---|---|
| /health/live | process up (no dependencies) |
| /health/ready | SQL, Redis, Blob, Service Bus |
| /health | detailed (internal network or admin only) |
| /swagger, /openapi/v1.json | API docs (enabled in non-prod; spec exported in CI) |

## Pagination contract
- Offset pagination: `?page=1&pageSize=20`, where pageSize must be 50 or less. The response
  shape is `{ items, page, pageSize, totalCount }`.
- Cursor pagination (search, messages, notifications): `?cursor=<opaque base64 of sort key + id>`.
  The response shape is `{ items, nextCursor }`. Both use the same `PagedResult<T>` contract
  interface, so search can switch to cursors without a v2.
