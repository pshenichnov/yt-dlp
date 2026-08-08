from __future__ import annotations

import asyncio
import json
import logging
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

import aio_pika
from aio_pika import DeliveryMode, ExchangeType, IncomingMessage, Message

from .config import Settings
from .contracts import (
    CommandEnvelope,
    EventEnvelope,
    ExtractCompletedPayload,
    ExtractFailedPayload,
)
from .database import ExtractorDatabase
from .extractor import ExtractionError, YtDlpExtractor
from .storage import BlobStorage

logger = logging.getLogger(__name__)


class ExtractorWorker:
    def __init__(
        self,
        settings: Settings,
        database: ExtractorDatabase,
        storage: BlobStorage,
        extractor: YtDlpExtractor,
    ) -> None:
        self._settings = settings
        self._database = database
        self._storage = storage
        self._extractor = extractor
        self._connection: aio_pika.RobustConnection | None = None
        self._publish_channel: aio_pika.abc.AbstractChannel | None = None
        self._event_exchange: aio_pika.abc.AbstractExchange | None = None
        self._stopping = asyncio.Event()

    async def run(self) -> None:
        await self._database.open()
        await self._storage.ensure_bucket()
        self._connection = await aio_pika.connect_robust(self._settings.rabbitmq_url)

        consume_channel = await self._connection.channel()
        await consume_channel.set_qos(prefetch_count=self._settings.concurrency)
        command_exchange = await consume_channel.declare_exchange(
            self._settings.command_queue,
            ExchangeType.FANOUT,
            durable=True,
        )
        command_queue = await consume_channel.declare_queue(
            self._settings.command_queue,
            durable=True,
            arguments={"x-queue-type": "quorum"},
        )
        await command_queue.bind(
            command_exchange, routing_key=self._settings.command_queue
        )

        self._publish_channel = await self._connection.channel(
            publisher_confirms=True, on_return_raises=True
        )
        self._event_exchange = await self._publish_channel.declare_exchange(
            self._settings.event_queue,
            ExchangeType.FANOUT,
            durable=True,
        )
        event_queue = await self._publish_channel.declare_queue(
            self._settings.event_queue,
            durable=True,
            arguments={"x-queue-type": "quorum"},
        )
        await event_queue.bind(self._event_exchange, routing_key=self._settings.event_queue)

        await command_queue.consume(self._handle_message)
        outbox_task = asyncio.create_task(self._publish_outbox())
        logger.info("Extractor worker started")
        await self._stopping.wait()
        outbox_task.cancel()
        await asyncio.gather(outbox_task, return_exceptions=True)

    async def stop(self) -> None:
        self._stopping.set()
        if self._connection:
            await self._connection.close()
        await self._database.close()

    async def _handle_message(self, incoming: IncomingMessage) -> None:
        async with incoming.process(requeue=True, ignore_processed=True):
            try:
                envelope = CommandEnvelope.model_validate_json(incoming.body)
            except Exception:
                logger.exception("Rejecting invalid extractor command")
                await incoming.reject(requeue=False)
                return

            if await self._database.already_processed(envelope.message_id):
                return

            event_id = uuid4()
            try:
                with tempfile.TemporaryDirectory(prefix="youtube-extractor-") as directory:
                    result = await self._extractor.extract(
                        envelope.payload, Path(directory)
                    )
                    transcript = await self._storage.upload(
                        envelope.payload.analysis_id,
                        "transcript",
                        result.transcript,
                        "transcript.txt",
                        "text/plain; charset=utf-8",
                    )
                    caption = await self._storage.upload(
                        envelope.payload.analysis_id,
                        "caption",
                        result.caption,
                        "captions.vtt",
                        "text/vtt; charset=utf-8",
                    )
                    segments = await self._storage.upload(
                        envelope.payload.analysis_id,
                        "transcriptSegments",
                        result.segments,
                        "transcript.json",
                        "application/json",
                    )
                    thumbnail = await self._storage.upload(
                        envelope.payload.analysis_id,
                        "thumbnail",
                        result.thumbnail,
                        f"thumbnail{result.thumbnail.suffix.lower()}",
                    )
                    payload = ExtractCompletedPayload(
                        job_id=envelope.payload.job_id,
                        analysis_id=envelope.payload.analysis_id,
                        video=result.metadata,
                        transcript=transcript,
                        original_caption=caption,
                        transcript_segments=segments,
                        thumbnail=thumbnail,
                    )
                    event = EventEnvelope(
                        message_id=event_id,
                        correlation_id=envelope.correlation_id,
                        causation_id=envelope.message_id,
                        message_type="extract-video-completed",
                        occurred_at=datetime.now(timezone.utc),
                        payload=payload,
                    )
            except ExtractionError as error:
                if error.transient:
                    logger.warning(
                        "Retryable extraction failure for job %s: %s",
                        envelope.payload.job_id,
                        error.code,
                    )
                    raise
                event = EventEnvelope(
                    message_id=event_id,
                    correlation_id=envelope.correlation_id,
                    causation_id=envelope.message_id,
                    message_type="extract-video-failed",
                    occurred_at=datetime.now(timezone.utc),
                    payload=ExtractFailedPayload(
                        job_id=envelope.payload.job_id,
                        analysis_id=envelope.payload.analysis_id,
                        error_code=error.code,
                        error_message=str(error),
                        is_transient=error.transient,
                    ),
                )
            except Exception:
                logger.exception("Unexpected extraction failure; message will be retried")
                raise

            await self._database.save_result(
                envelope.message_id,
                event_id,
                envelope.correlation_id,
                event.model_dump(mode="json", by_alias=True),
            )

    async def _publish_outbox(self) -> None:
        while True:
            row = await self._database.next_outbox()
            if row is None:
                await asyncio.sleep(0.5)
                continue
            try:
                assert self._event_exchange
                body = json.dumps(row["body"]).encode("utf-8")
                await self._event_exchange.publish(
                    Message(
                        body=body,
                        content_type="application/json",
                        delivery_mode=DeliveryMode.PERSISTENT,
                        message_id=str(row["message_id"]),
                        correlation_id=str(row["correlation_id"]),
                        type="extractor-event-v1",
                    ),
                    routing_key=self._settings.event_queue,
                    mandatory=True,
                )
                await self._database.mark_published(row["message_id"])
            except asyncio.CancelledError:
                raise
            except Exception as error:
                logger.exception("Outbox publish failed")
                await self._database.mark_publish_failure(
                    row["message_id"], str(error)
                )
                await asyncio.sleep(2)
