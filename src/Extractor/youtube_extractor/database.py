from __future__ import annotations

import json
from datetime import datetime, timezone
from uuid import UUID

import asyncpg


SCHEMA_SQL = """
CREATE SCHEMA IF NOT EXISTS extractor;
CREATE TABLE IF NOT EXISTS extractor.inbox (
    message_id uuid PRIMARY KEY,
    received_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS extractor.outbox (
    message_id uuid PRIMARY KEY,
    correlation_id uuid NOT NULL,
    body jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz NULL,
    attempts integer NOT NULL DEFAULT 0,
    last_error text NULL
);
CREATE INDEX IF NOT EXISTS ix_extractor_outbox_pending
    ON extractor.outbox (created_at)
    WHERE published_at IS NULL;
"""


class ExtractorDatabase:
    def __init__(self, dsn: str) -> None:
        self._dsn = dsn
        self.pool: asyncpg.Pool | None = None

    async def open(self) -> None:
        async def initialize(connection: asyncpg.Connection) -> None:
            await connection.set_type_codec(
                "jsonb",
                schema="pg_catalog",
                encoder=json.dumps,
                decoder=json.loads,
                format="text",
            )

        self.pool = await asyncpg.create_pool(
            self._dsn, min_size=1, max_size=5, init=initialize
        )
        async with self.pool.acquire() as connection:
            await connection.execute(SCHEMA_SQL)

    async def close(self) -> None:
        if self.pool:
            await self.pool.close()

    async def already_processed(self, message_id: UUID) -> bool:
        assert self.pool
        return bool(
            await self.pool.fetchval(
                "SELECT EXISTS(SELECT 1 FROM extractor.inbox WHERE message_id=$1)",
                message_id,
            )
        )

    async def save_result(
        self,
        inbound_message_id: UUID,
        event_message_id: UUID,
        correlation_id: UUID,
        event: dict,
    ) -> None:
        assert self.pool
        async with self.pool.acquire() as connection:
            async with connection.transaction():
                inserted = await connection.fetchval(
                    """
                    INSERT INTO extractor.inbox(message_id)
                    VALUES ($1)
                    ON CONFLICT DO NOTHING
                    RETURNING message_id
                    """,
                    inbound_message_id,
                )
                if not inserted:
                    return
                await connection.execute(
                    """
                    INSERT INTO extractor.outbox(message_id, correlation_id, body)
                    VALUES ($1, $2, $3::jsonb)
                    """,
                    event_message_id,
                    correlation_id,
                    event,
                )

    async def next_outbox(self) -> asyncpg.Record | None:
        assert self.pool
        return await self.pool.fetchrow(
            """
            SELECT message_id, correlation_id, body
            FROM extractor.outbox
            WHERE published_at IS NULL
            ORDER BY created_at
            LIMIT 1
            """
        )

    async def mark_published(self, message_id: UUID) -> None:
        assert self.pool
        await self.pool.execute(
            """
            UPDATE extractor.outbox
            SET published_at=$2, attempts=attempts+1, last_error=NULL
            WHERE message_id=$1
            """,
            message_id,
            datetime.now(timezone.utc),
        )

    async def mark_publish_failure(self, message_id: UUID, error: str) -> None:
        assert self.pool
        await self.pool.execute(
            """
            UPDATE extractor.outbox
            SET attempts=attempts+1, last_error=$2
            WHERE message_id=$1
            """,
            message_id,
            error[:2000],
        )
