# ADR 0004: API Security, Artifact Access, and Resource Limits

- Status: accepted
- Date: 2026-07-25

## Context

Phase one is a private HomeLab API but still invokes a network extractor and an
LLM. It needs authentication and resource limits before accepting work.

## Decision

- Authenticate non-health endpoints with a configured `X-Api-Key`.
- Keep the API key in an environment variable or secret mount.
- Scope idempotency keys to the authenticated API-key identity.
- Stream authorized artifact downloads through the API.
- Accept video IDs and supported HTTPS YouTube URLs only.
- Limit the original input to 2,048 characters.
- Limit video duration to four hours.
- Limit original caption artifacts to 10 MiB.
- Limit normalized transcript text to 2,000,000 characters.
- Limit thumbnails to 10 MiB.
- Limit extraction to five minutes per yt-dlp invocation and ten minutes for
  the complete extraction stage.
- Limit analysis to fifteen minutes and a complete job to thirty minutes.
- Retain completed artifacts for 30 days by default.
- Retain failed-job artifacts for seven days.
- Remove command-local temporary files immediately after a terminal outcome,
  with a 24-hour stale-directory cleanup fallback.

## Consequences

- Static API-key authentication is sufficient only for a private initial
  deployment.
- Adding multiple users later requires a stronger identity provider and
  per-subject authorization.
- Limits are configurable but cannot be disabled with zero or negative values.
