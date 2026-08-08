from types import SimpleNamespace
from unittest.mock import AsyncMock

from aio_pika import ExchangeType

from youtube_extractor.config import Settings
from youtube_extractor.worker import ExtractorWorker


async def test_worker_binds_quorum_command_queue_to_exchange(monkeypatch) -> None:
    settings = Settings(
        rabbitmq_url="amqp://guest:guest@rabbitmq/",
        postgres_dsn="postgresql://youtube@postgres/youtube_extractor",
        s3_endpoint="http://minio:9000",
        s3_access_key="youtube",
        s3_secret_key="secret",
        s3_bucket="youtube-processing",
        s3_region="us-east-1",
        command_queue="youtube.extract-video.commands.v1",
        event_queue="youtube.extract-video.events.v1",
        concurrency=2,
        process_timeout_seconds=900,
        max_duration_seconds=14400,
        max_caption_bytes=10485760,
        max_thumbnail_bytes=10485760,
    )
    database = SimpleNamespace(
        open=AsyncMock(),
        close=AsyncMock(),
        next_outbox=AsyncMock(return_value=None),
    )
    storage = SimpleNamespace(ensure_bucket=AsyncMock())
    connection = SimpleNamespace(channel=AsyncMock())
    consume_channel = SimpleNamespace(
        set_qos=AsyncMock(),
        declare_exchange=AsyncMock(),
        declare_queue=AsyncMock(),
    )
    publish_channel = SimpleNamespace(
        declare_exchange=AsyncMock(),
        declare_queue=AsyncMock(),
    )
    command_exchange = object()
    command_queue = SimpleNamespace(bind=AsyncMock(), consume=AsyncMock())
    event_exchange = object()
    event_queue = SimpleNamespace(bind=AsyncMock())
    consume_channel.declare_exchange.return_value = command_exchange
    consume_channel.declare_queue.return_value = command_queue
    publish_channel.declare_exchange.return_value = event_exchange
    publish_channel.declare_queue.return_value = event_queue
    connection.channel.side_effect = [consume_channel, publish_channel]

    connect_robust = AsyncMock(return_value=connection)
    monkeypatch.setattr(
        "youtube_extractor.worker.aio_pika.connect_robust", connect_robust
    )

    worker = ExtractorWorker(settings, database, storage, object())
    worker._stopping.set()
    await worker.run()

    consume_channel.declare_exchange.assert_awaited_once_with(
        settings.command_queue,
        ExchangeType.FANOUT,
        durable=True,
    )
    consume_channel.declare_queue.assert_awaited_once_with(
        settings.command_queue,
        durable=True,
        arguments={"x-queue-type": "quorum"},
    )
    command_queue.bind.assert_awaited_once_with(
        command_exchange, routing_key=settings.command_queue
    )
