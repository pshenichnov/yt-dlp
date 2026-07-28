from __future__ import annotations

import os
from dataclasses import dataclass


def _required(name: str) -> str:
    value = os.getenv(name)
    if not value:
        raise RuntimeError(f"Required environment variable {name} is not set")
    return value


@dataclass(frozen=True)
class Settings:
    rabbitmq_url: str
    postgres_dsn: str
    s3_endpoint: str
    s3_access_key: str
    s3_secret_key: str
    s3_bucket: str
    s3_region: str
    command_queue: str
    event_queue: str
    concurrency: int
    process_timeout_seconds: int
    max_duration_seconds: int
    max_caption_bytes: int
    max_thumbnail_bytes: int

    @classmethod
    def from_environment(cls) -> "Settings":
        return cls(
            rabbitmq_url=_required("RABBITMQ_URL"),
            postgres_dsn=_required("POSTGRES_DSN"),
            s3_endpoint=_required("S3_ENDPOINT"),
            s3_access_key=_required("S3_ACCESS_KEY"),
            s3_secret_key=_required("S3_SECRET_KEY"),
            s3_bucket=os.getenv("S3_BUCKET", "youtube-processing"),
            s3_region=os.getenv("S3_REGION", "us-east-1"),
            command_queue=os.getenv(
                "EXTRACTOR_COMMAND_QUEUE", "youtube.extract-video.commands.v1"
            ),
            event_queue=os.getenv(
                "EXTRACTOR_EVENT_QUEUE", "youtube.extract-video.events.v1"
            ),
            concurrency=int(os.getenv("EXTRACTOR_CONCURRENCY", "2")),
            process_timeout_seconds=int(
                os.getenv("EXTRACTOR_PROCESS_TIMEOUT_SECONDS", "900")
            ),
            max_duration_seconds=int(os.getenv("MAX_VIDEO_DURATION_SECONDS", "14400")),
            max_caption_bytes=int(os.getenv("MAX_CAPTION_BYTES", "10485760")),
            max_thumbnail_bytes=int(os.getenv("MAX_THUMBNAIL_BYTES", "10485760")),
        )
