from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator


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


class FetchChannelPagePayload(ContractModel):
    request_id: UUID
    channel_id: str = Field(pattern=r"^UC[A-Za-z0-9_-]{22}$")
    start_index: int = Field(ge=1)
    page_size: int = Field(ge=1, le=50)


class CommandEnvelope(ContractModel):
    schema_version: Literal["1"]
    message_id: UUID
    correlation_id: UUID
    causation_id: UUID | None
    message_type: Literal["extract-video", "fetch-channel-page"]
    occurred_at: datetime
    payload: ExtractVideoPayload | FetchChannelPagePayload

    @model_validator(mode="after")
    def payload_matches_message_type(self) -> CommandEnvelope:
        expected = (
            ExtractVideoPayload
            if self.message_type == "extract-video"
            else FetchChannelPagePayload
        )
        if not isinstance(self.payload, expected):
            raise ValueError("payload does not match messageType")
        return self


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


class ChannelVideoMetadata(ContractModel):
    video_id: str
    title: str
    description: str | None
    thumbnail_url: str | None
    published_at: datetime | None
    webpage_url: str


class FetchChannelPageCompletedPayload(ContractModel):
    request_id: UUID
    channel_id: str
    channel_name: str | None
    channel_handle: str | None
    videos: list[ChannelVideoMetadata]
    next_start_index: int
    has_more: bool


class FetchChannelPageFailedPayload(ContractModel):
    request_id: UUID
    channel_id: str
    error_code: str
    error_message: str
    is_transient: bool


class EventEnvelope(ContractModel):
    schema_version: Literal["1"] = "1"
    message_id: UUID
    correlation_id: UUID
    causation_id: UUID
    message_type: Literal[
        "extract-video-completed",
        "extract-video-failed",
        "fetch-channel-page-completed",
        "fetch-channel-page-failed",
    ]
    occurred_at: datetime
    payload: (
        ExtractCompletedPayload
        | ExtractFailedPayload
        | FetchChannelPageCompletedPayload
        | FetchChannelPageFailedPayload
    )
