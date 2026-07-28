# ADR 0003: Persistence and Blob Storage

- Status: accepted
- Date: 2026-07-25

## Context

Workflow state, idempotency, and projections require relational persistence.
Transcripts and thumbnails must not be placed in RabbitMQ or database large
object columns. The initial deployment needs a locally runnable S3-compatible
store.

## Decision

- Use PostgreSQL 18.4.
- Use one PostgreSQL server with separately owned databases for API,
  orchestrator, analyzer, and extractor services.
- Use Entity Framework Core 10 for .NET persistence.
- Use PostgreSQL directly from the Python extractor through `asyncpg`.
- Program against the S3 API.
- Use MinIO Community `RELEASE.2025-09-07T16-13-09Z` only for local development
  and the controlled phase-one evaluation environment.
- Treat the MinIO implementation as replaceable configuration.
- Stream authorized artifacts through the API in phase one rather than exposing
  presigned object-store URLs.
- Store immutable artifacts in the `youtube-artifacts` bucket.
- Use SHA-256 checksums and verify them on analyzer download.

The MinIO community repository was archived in April 2026. This decision does
not approve MinIO Community as a durable long-term production dependency.
Before HomeLab production use, replace it with a maintained S3-compatible
implementation or formally accept and mitigate that risk.

## Consequences

- Database migrations are maintained per service.
- Services never query another service's database.
- Blob-store replacement does not change message contracts.
- Local development remains simple, but production storage remains an open
  deployment decision.

## References

- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)
- [MinIO Community archive](https://github.com/minio/minio)

