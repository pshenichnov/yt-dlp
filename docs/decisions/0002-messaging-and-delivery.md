# ADR 0002: Messaging, MassTransit, and Delivery Semantics

- Status: accepted
- Date: 2026-07-25

## Context

.NET services use MassTransit and RabbitMQ. The Python extractor uses a native
RabbitMQ client. MassTransit v9 is commercially licensed, while phase one does
not have an approved commercial dependency budget.

## Decision

- Use RabbitMQ 4.3.4.
- Use durable quorum queues for phase-one command and service queues.
- Use MassTransit 8.5.10, pinned under its Apache-2.0 package license.
- Do not upgrade to MassTransit v9 without a licensing decision.
- Use normal MassTransit JSON envelopes for .NET-to-.NET messages.
- Use raw, versioned JSON contracts at the Python boundary.
- Treat delivery as at least once.
- Use MassTransit transactional outboxes in .NET services.
- Use an inbox, execution record, and durable outbox in the Python extractor.
- Use publisher confirms and explicit dead-letter routing.

## Consequences

- All consumers must be idempotent.
- Contract tests are required across C# and Python.
- Current MassTransit documentation may describe v9 APIs; implementation must
  be verified against pinned v8 packages.
- The MassTransit major-version pin is a deliberate technical-debt item.

## References

- [MassTransit serialization](https://masstransit.io/documentation/configuration/serialization)
- [MassTransit outbox](https://masstransit.io/documentation/patterns/transactional-outbox)
- [RabbitMQ release information](https://www.rabbitmq.com/release-information)

