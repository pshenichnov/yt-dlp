# YouTube Processing Platform

An asynchronous microservices platform that accepts a YouTube video ID or URL,
extracts captions and the original thumbnail with `yt-dlp`, stores artifacts in
S3-compatible storage, and produces a transcript-backed description with
subject bullets.

## Services

- `YouTubeProcessing.Api`: authenticated ASP.NET Core REST API and read model
- `YouTubeProcessing.Orchestrator`: durable MassTransit workflow process
- `YouTubeProcessing.Analyzer`: checksum-verifying Codex analysis consumer
- `youtube-processing-extractor`: Python `yt-dlp` worker with inbox/outbox

RabbitMQ carries commands and events. Each service has its own PostgreSQL
database. MinIO is the local S3-compatible artifact store.

## Prerequisites

- Docker with Compose v2
- Or, for host development: .NET SDK 10.0.302 and Python 3.10–3.12

## Start locally

```bash
cp .env.example .env
docker compose up --build
```

The default Compose configuration stops after transcript and thumbnail
extraction. The opt-in analyzer service is not started, and a successful
extraction completes the job with `analysis: null`.

To restore deterministic transcript analysis:

```bash
ANALYSIS_ENABLED=true docker compose --profile analysis up --build
```

To use analysis through a personal ChatGPT subscription, build the Analyzer,
sign its Codex CLI into ChatGPT with device authentication, and select the
`codex` provider:

```bash
docker compose --profile analysis build analyzer
docker compose --profile analysis run --rm --no-deps \
  --entrypoint codex analyzer login --device-auth
ANALYSIS_ENABLED=true ANALYZER_PROVIDER=codex \
  docker compose --profile analysis up --build
```

The login is stored in the private `codex-data` Docker volume and is reused by
the Analyzer. Treat that volume as a credential. This path consumes the Codex
allowance and credits attached to the signed-in ChatGPT account; it does not
use separately billed OpenAI API credits. Use it only in this trusted personal
deployment, never in public CI or on runners that execute untrusted code.

Submit a job:

```bash
curl -i http://localhost:8080/api/v1/video-analyses \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: replace-this-development-key" \
  -H "Idempotency-Key: example-1" \
  -d '{"video":"dQw4w9WgXcQ","preferredLanguage":"en","scenarios":["summary","topics"]}'
```

Poll the returned `statusUrl`; when it reports `completed`, request the
`resultUrl`. Artifact links stream through the authenticated API.

`GET /health` returns aggregate status for the API, PostgreSQL, RabbitMQ,
MinIO, Orchestrator, Extractor, and the optional Analyzer. Worker status is
derived from the required RabbitMQ queues and active consumer connections; a
failed required component produces HTTP 503. Lightweight probe endpoints
remain available at `GET /health/live` and `GET /health/ready`. Interactive
Swagger UI is available at `/swagger`, and the generated OpenAPI document is
available at `/openapi/v1.json`. In Swagger UI, select **Authorize** and enter
the configured API key; Swagger will send it in the `X-Api-Key` header.

## Verify

```bash
dotnet tool restore
dotnet restore YouTubeProcessing.slnx
dotnet build YouTubeProcessing.slnx --no-restore
dotnet test YouTubeProcessing.slnx --no-restore

python3.10 -m venv .venv
.venv/bin/python -m pip install -e 'src/Extractor[test]'
.venv/bin/python -m pytest src/Extractor/tests

docker compose config
```

## Documentation

- [Agent guide](AGENT.md)
- [Architecture](ARCHITECTURE.md)
- [Phase-one execution plan](PHASE_1_EXECUTION_PLAN.md)
- [Implementation status](docs/IMPLEMENTATION_STATUS.md)
- [Version matrix](docs/VERSIONS.md)
- [Configuration reference](docs/CONFIGURATION.md)
- [Architecture decisions](docs/decisions)
- [Operations runbook](docs/runbooks/operations.md)

Phase one intentionally excludes YouTube-wide search, comments, audio
transcription, playlists, and batch requests.
