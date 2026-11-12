# Harden extractor retries so transient failures terminate predictably

## Context

A production-like job exposed a failure in the extractor's transient-error
handling. `yt-dlp` received `HTTP 429: Too Many Requests` while downloading
English subtitles. The extractor classified this as `youtube_rate_limited` and
raised from an `incoming.process(requeue=True)` context.

RabbitMQ immediately redelivered the command until the quorum queue exhausted
its implicit delivery limit. Because the queue had no dead-letter route, the
message was discarded. No extraction failure event was published, so the API
continued to report `running / extractingTranscript` until the workflow
timeout. The retry loop also emitted repeated `Task exception was never
retrieved` errors from `aio-pika`.

## Goal

Make transient extraction failures retry with durable backoff and always
produce an observable terminal outcome before the workflow deadline.

## Required changes

- Replace immediate `nack/requeue` retries with an explicit, durable, bounded
  retry mechanism.
- Add configurable retry settings for maximum attempts, initial delay, maximum
  delay, and jitter. Choose defaults whose total retry window fits inside
  `Workflow__ExtractionTimeoutMinutes`, with enough time to publish and project
  the terminal event.
- Preserve the original command identity and correlation ID across attempts so
  inbox/outbox idempotency remains correct.
- When retries are exhausted, publish `extract-video-failed` with the original
  stable error code, such as `youtube_rate_limited`, a safe diagnostic message,
  `isTransient: true`, and attempt metadata if the contract is extended.
- Configure explicit dead-letter handling for commands that cannot be processed
  or exceed the broker delivery limit. Do not depend on RabbitMQ's implicit
  quorum-queue delivery limit.
- Ensure invalid or poison messages are rejected to the dead-letter path and
  cannot enter a hot loop.
- Handle expected retryable failures without unobserved asyncio task
  exceptions.
- Add structured logs and metrics for job ID, message ID, attempt number, retry
  delay, terminal failure, and dead-lettering.
- Review the extractor image's `yt-dlp` runtime warnings. Install and configure
  a supported JavaScript runtime and required pinned impersonation dependencies
  if they are part of the supported extraction path; document the decision.
- Update the operations runbook with retry inspection, dead-letter
  inspection/replay, and YouTube 429 guidance.

## Acceptance criteria

- A simulated YouTube 429 does not cause rapid redelivery.
- Retries occur at the configured delays and survive an extractor restart.
- The same command is never processed concurrently during delayed retry.
- A later successful attempt completes the job exactly once.
- Exhausted retries publish exactly one terminal failure event and the API
  reaches `failed` before the workflow timeout.
- The terminal API response exposes `youtube_rate_limited` rather than only
  `WORKFLOW_TIMEOUT`.
- Exhausted or poison messages remain inspectable in a configured dead-letter
  queue.
- Expected extraction failures do not produce `Task exception was never
  retrieved` logs.
- The aggregate health endpoint reports an unhealthy or degraded state when
  required queues have dead-lettered messages or no viable consumer, according
  to the documented policy.

## Tests

- Unit tests for retry classification, delay calculation, maximum attempts,
  and terminal-event construction.
- Worker integration test covering transient failures followed by success.
- Worker integration test covering retry exhaustion and a single failure event.
- RabbitMQ integration test proving delayed retries survive consumer restart
  and exhausted messages reach the dead-letter queue.
- End-to-end test proving the API transitions from `running` to `failed` with
  `youtube_rate_limited` within the configured deadline.
- Regression test ensuring non-retryable extraction errors still fail
  immediately.

## Validation

Reproduce with injected extractor outcomes instead of repeatedly calling
YouTube. Run the Python test suite, relevant .NET orchestration/API tests,
`docker compose config`, and one opt-in live smoke test after deterministic
tests pass.
