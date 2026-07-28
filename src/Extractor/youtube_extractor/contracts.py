from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


def to_camel(value: str) -> str:
    first, *rest = value.split("_")
    return first + "".join(word.capitalize() for word in rest)


class ContractModel(BaseModel):
    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        extra="forbid",
    )


class ExtractVideoPayload(ContractModel):
    job_id: UUID
    analysis_id: UUID
    video_id: str = Field(min_length=11, max_length=11)
    original_input: str = Field(min_length=1, max_length=2048)
    preferred_language: str | None = Field(default=None, max_length=32)


class CommandEnvelope(ContractModel):
    schema_version: Literal["1"]
    message_id: UUID
    correlation_id: UUID
    causation_id: UUID | None
    message_type: Literal["extract-video"]
    occurred_at: datetime
    payload: ExtractVideoPayload


class ArtifactReference(ContractModel):
    artifact_id: UUID
    kind: str
    object_key: str
    content_type: str
    size_bytes: int
    sha256: str


class VideoMetadata(ContractModel):
    video_id: str
    title: str
    channel_id: str | None
    channel_name: str | None
    published_at: datetime | None
    duration_seconds: int | None
    view_count: int | None
    like_count: int | None
    description: str | None
    webpage_url: str


class ExtractCompletedPayload(ContractModel):
    job_id: UUID
    analysis_id: UUID
    video: VideoMetadata
    transcript: ArtifactReference
    original_caption: ArtifactReference | None
    transcript_segments: ArtifactReference | None = None
    thumbnail: ArtifactReference


class ExtractFailedPayload(ContractModel):
    job_id: UUID
    analysis_id: UUID
    error_code: str
    error_message: str
    is_transient: bool


class EventEnvelope(ContractModel):
    schema_version: Literal["1"] = "1"
    message_id: UUID
    correlation_id: UUID
    causation_id: UUID
    message_type: Literal["extract-video-completed", "extract-video-failed"]
    occurred_at: datetime
    payload: ExtractCompletedPayload | ExtractFailedPayload
