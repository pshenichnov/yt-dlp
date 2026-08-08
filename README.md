# YouTube Processing Platform

An asynchronous microservices platform that accepts a YouTube video ID or URL,
extracts captions and the original thumbnail with `yt-dlp`, stores artifacts in
S3-compatible storage, and produces a transcript-backed description with
subject bullets.

## Services

- `YouTubeProcessing.Api`: authenticated ASP.NET Core REST API and read model
- `YouTubeProcessing.Orchestrator`: durable MassTransit workflow process
- `YouTubeProcessing.Analyzer`: checksum-verifying Ollama analysis consumer
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

The default Compose configuration temporarily stops after transcript and
thumbnail extraction. The analyzer and Ollama services are not started, and a
successful extraction completes the job with `analysis: null`.

To restore deterministic transcript analysis:

```bash
ANALYSIS_ENABLED=true docker compose --profile analysis up --build
```

To restore Ollama-backed analysis, start both opt-in profiles, pull the
configured model, and select the Ollama provider:

```bash
docker compose --profile llm up -d ollama
docker compose exec ollama ollama pull llama3.2:3b
ANALYSIS_ENABLED=true ANALYZER_PROVIDER=ollama \
  docker compose --profile analysis --profile llm up --build
```

Submit a job:

```bash
curl -i http://localhost:8080/api/v1/video-analyses \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: replace-this-development-key" \
  -H "Idempotency-Key: example-1" \
  -d '{"video":"dQw4w9WgXcQ","preferredLanguage":"en"}'
```

Poll the returned `statusUrl`; when it reports `completed`, request the
`resultUrl`. Artifact links stream through the authenticated API.

Health endpoints are `GET /health/live` and `GET /health/ready`. Interactive
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
