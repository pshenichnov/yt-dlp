# Version Matrix

Last reviewed: August 8, 2026

| Component | Pinned version | Notes |
|---|---:|---|
| .NET SDK | 10.0.302 | Pinned by `global.json` |
| .NET runtime/ASP.NET Core | 10.0.10 family | Container patch line; update within .NET 10 after validation |
| Entity Framework Core | 10.0.10 | Central package management |
| Npgsql EF provider | 10.0.3 | PostgreSQL provider |
| MassTransit | 8.5.10 | Apache-2.0 package line; do not move to v9 without licensing |
| OpenTelemetry .NET | 1.17.0 | Hosting and instrumentation packages |
| Microsoft.OpenApi | 2.11.0 | Patched 2.x line; fixes GHSA-v5pm-xwqc-g5wc |
| AWS SDK for S3 .NET | 4.0.101.4 | S3-compatible artifact adapter |
| Python | 3.12 | Extractor container |
| FFmpeg | 7.1.5 (Debian `7:7.1.5-0+deb13u1`) | Exact extractor image package |
| yt-dlp | 2026.07.04 | Exact Python dependency |
| aio-pika | 9.5.7 | RabbitMQ client |
| asyncpg | 0.30.0 | Extractor persistence |
| boto3 | 1.39.13 | S3 client |
| pydantic | 2.11.7 | Contract/config validation |
| webvtt-py | 0.5.1 | WebVTT parser |
| jsonschema | 4.25.1 | Contract test dependency |
| PostgreSQL | 18.4 | Supported through 2030 |
| RabbitMQ | 4.3.4-management | Quorum queues |
| MinIO Community | RELEASE.2025-09-07T16-13-09Z | Local evaluation only; archived upstream |
| MinIO client | RELEASE.2025-08-13T08-35-41Z | Bucket initializer |
| Node.js | 24.7.0 | Analyzer build stage for the Codex CLI wrapper |
| Codex CLI | 0.147.0 | Analyzer subscription-backed provider executable |
| Codex model | gpt-5.6-sol | Configurable ChatGPT-accessible model |

Patch upgrades require tests and an update to this matrix. Major upgrades
require an architecture decision.
