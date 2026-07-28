# Operations Runbook

## Check the stack

```bash
docker compose ps
curl -fsS http://localhost:8080/health/live
curl -fsS http://localhost:8080/health/ready
docker compose exec rabbitmq rabbitmq-diagnostics -q ping
docker compose exec postgres pg_isready -U youtube -d postgres
```

RabbitMQ management is available on port 15672 and MinIO Console on port 9001
in the development profile.

## Trace a job

Start with the job ID returned by the API:

```bash
docker compose logs api orchestrator extractor analyzer | grep JOB_ID
```

The job ID is the RabbitMQ correlation ID. Check the API job response for the
current stage before inspecting the corresponding service.

## Stuck job

1. Check whether the stage is `extractingTranscript` or `analyzingTranscript`.
2. Check queue depth and consumer count in RabbitMQ.
3. Inspect the owning service logs.
4. Verify PostgreSQL, MinIO, and (for real analysis) Ollama connectivity.
5. Allow the workflow timeout scanner to produce a terminal
   `WORKFLOW_TIMEOUT`; do not edit workflow rows by hand.

## RabbitMQ error queues and replay

MassTransit creates `<queue>_error` queues for exhausted .NET messages. Inspect
the exception headers and correct the cause before moving a message back to its
original queue. Preserve the message ID: inbox deduplication depends on it.
Never purge a durable queue as routine recovery.

Invalid raw extractor commands are rejected. Retryable `yt-dlp`, S3, or
transport failures are negatively acknowledged and redelivered.

## YouTube/yt-dlp failures

Confirm the pinned version:

```bash
docker compose run --rm --entrypoint yt-dlp extractor --version
```

Test a public captioned video directly without downloading video bytes. Upgrade
`yt-dlp` in a branch, run contract and fixture tests, build the extractor
image, and perform one opt-in live test. Roll back by restoring the prior
`pyproject.toml` pin and rebuilding.

Do not add browser cookies or account credentials without a new security
decision.

## MinIO or disk pressure

```bash
docker compose exec minio mc admin info local
docker system df
```

Artifacts have a 30-day local lifecycle rule. Before deleting any object
manually, confirm no active or completed retained job references it. Back up
the PostgreSQL databases and artifact volume together to preserve referential
consistency.

## Ollama unavailable

The deterministic provider can validate plumbing but is not a production
summary engine. For Ollama:

```bash
docker compose --profile llm up -d ollama
docker compose exec ollama ollama list
docker compose exec ollama ollama pull llama3.2:3b
```

Then set `ANALYZER_PROVIDER=ollama` and recreate the analyzer.

## Backup

Back up:

- PostgreSQL logical dumps for all four service databases
- RabbitMQ definitions and durable data if in-flight recovery is required
- The S3 bucket/MinIO volume
- Deployment configuration with secrets stored separately

Test restore procedures before relying on the backups.
