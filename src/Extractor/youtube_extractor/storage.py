from __future__ import annotations

import asyncio
import hashlib
import mimetypes
from pathlib import Path
from uuid import UUID, uuid4

import boto3
from botocore.config import Config
from botocore.exceptions import ClientError

from .config import Settings
from .contracts import ArtifactReference


class BlobStorage:
    def __init__(self, settings: Settings) -> None:
        self._bucket = settings.s3_bucket
        self._client = boto3.client(
            "s3",
            endpoint_url=settings.s3_endpoint,
            aws_access_key_id=settings.s3_access_key,
            aws_secret_access_key=settings.s3_secret_key,
            region_name=settings.s3_region,
            config=Config(signature_version="s3v4", s3={"addressing_style": "path"}),
        )

    async def ensure_bucket(self) -> None:
        def create_if_needed() -> None:
            try:
                self._client.head_bucket(Bucket=self._bucket)
            except ClientError:
                self._client.create_bucket(Bucket=self._bucket)

        await asyncio.to_thread(create_if_needed)

    async def upload(
        self,
        analysis_id: UUID,
        kind: str,
        source: Path,
        target_name: str,
        content_type: str | None = None,
    ) -> ArtifactReference:
        data = await asyncio.to_thread(source.read_bytes)
        checksum = hashlib.sha256(data).hexdigest()
        object_key = f"analyses/{analysis_id}/{target_name}"
        actual_content_type = (
            content_type
            or mimetypes.guess_type(target_name)[0]
            or "application/octet-stream"
        )
        await asyncio.to_thread(
            self._client.put_object,
            Bucket=self._bucket,
            Key=object_key,
            Body=data,
            ContentType=actual_content_type,
            Metadata={"sha256": checksum, "artifact-kind": kind},
        )
        return ArtifactReference(
            artifact_id=uuid4(),
            kind=kind,
            object_key=object_key,
            content_type=actual_content_type,
            size_bytes=len(data),
            sha256=checksum,
        )
