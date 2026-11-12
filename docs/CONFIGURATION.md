# Configuration Reference

Configuration is read from `appsettings.json` and environment variables.
Double underscores represent nested .NET configuration keys. Values in
`.env.example` are local-development defaults, not production secrets.

## Shared .NET settings

| Key | Default | Purpose |
|---|---|---|
| `RabbitMq__Host` | `rabbitmq` | RabbitMQ host |
| `RabbitMq__VirtualHost` | `/` | RabbitMQ virtual host |
| `RabbitMq__Username` | none | RabbitMQ credential |
| `RabbitMq__Password` | none | RabbitMQ credential |
| `Database__ApplyMigrations` | `true` | Apply checked-in EF migrations at startup |
| `Observability__OtlpEndpoint` | empty | Optional OTLP collector |

## API

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__ApiDatabase` | none | API PostgreSQL database |
| `Platform__ApiKey` | none | Required `X-Api-Key` value |
| `Platform__MaxInputLength` | `2048` | Maximum video input length |
| `Platform__Blob__Endpoint` | `http://minio:9000` | S3 endpoint |
| `Platform__Blob__AccessKey` | none | S3 credential |
| `Platform__Blob__SecretKey` | none | S3 credential |
| `Platform__Blob__Bucket` | `youtube-processing` | Artifact bucket |
| `Platform__Blob__Region` | `us-east-1` | Signing region |
| `Health__RabbitMqManagementEndpoint` | `http://rabbitmq:15672` | RabbitMQ management API used for aggregate service status |
| `Health__TimeoutSeconds` | `3` | Per-component aggregate health timeout |
| `Health__AnalyzerEnabled` | `false` | Require an active Analyzer consumer in aggregate health |

## Orchestrator

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__OrchestratorDatabase` | none | Workflow PostgreSQL database |
| `Workflow__AnalysisEnabled` | `false` | Continue to transcript analysis after extraction |
| `Workflow__ExtractionTimeoutMinutes` | `15` | Extraction deadline |
| `Workflow__AnalysisTimeoutMinutes` | `10` | Analysis deadline |
| `Workflow__ScanIntervalSeconds` | `30` | Timeout scan period |

## Extractor

| Variable | Default | Purpose |
|---|---|---|
| `RABBITMQ_URL` | required | AMQP URL |
| `POSTGRES_DSN` | required | Extractor PostgreSQL DSN |
| `S3_ENDPOINT` | required | S3 endpoint |
| `S3_ACCESS_KEY` | required | S3 credential |
| `S3_SECRET_KEY` | required | S3 credential |
| `S3_BUCKET` | `youtube-processing` | Artifact bucket |
| `EXTRACTOR_COMMAND_QUEUE` | `youtube.extract-video.commands.v1` | Raw command queue |
| `EXTRACTOR_EVENT_QUEUE` | `youtube.extract-video.events.v1` | Raw terminal-event queue |
| `EXTRACTOR_CONCURRENCY` | `2` | Prefetch and parallel subprocess limit |
| `EXTRACTOR_PROCESS_TIMEOUT_SECONDS` | `900` | Per-`yt-dlp` timeout |
| `MAX_VIDEO_DURATION_SECONDS` | `14400` | Four-hour limit |
| `MAX_CAPTION_BYTES` | `10485760` | 10 MiB |
| `MAX_THUMBNAIL_BYTES` | `10485760` | 10 MiB |

## Analyzer

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__AnalyzerDatabase` | none | Analyzer PostgreSQL database |
| `Analyzer__Provider` | `codex` | `codex` or development-only `deterministic` |
| `Analyzer__CodexExecutable` | `codex` | Codex CLI executable name or absolute path |
| `Analyzer__Model` | `gpt-5.6-sol` | ChatGPT-accessible Codex model |
| `Analyzer__ReasoningEffort` | `low` | Codex reasoning effort |
| `Analyzer__ProviderTimeoutSeconds` | `300` | Per-Codex-process timeout |
| `Analyzer__MaxProviderOutputCharacters` | `65536` | Bounded stdout capture |
| `Analyzer__PromptVersion` | `video-analysis-v2` | Scenario-aware prompt contract |
| `Analyzer__MaxTranscriptCharacters` | `2000000` | Hard transcript limit |
| `Analyzer__ChunkCharacterLimit` | `48000` | Map/reduce chunk size |
| `Analyzer__Blob__*` | see API | Analyzer S3 access |

Never commit `.env`. Use a secret manager or Docker secrets outside local
development. The API must only be bound to a trusted LAN/Tailscale interface
for the initial deployment.

The `codex` provider reuses `codex login` credentials. In Compose those
credentials live in the `codex-data` volume. Never copy `auth.json` into the
image, repository, environment variables, or logs. ChatGPT-managed automation
is restricted to trusted personal deployments and must not run in public CI.
