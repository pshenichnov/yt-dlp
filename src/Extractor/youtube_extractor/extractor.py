from __future__ import annotations

import asyncio
import json
import re
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import webvtt

from .config import Settings
from .contracts import ExtractVideoPayload, VideoMetadata


class ExtractionError(Exception):
    def __init__(self, code: str, message: str, transient: bool = False) -> None:
        super().__init__(message)
        self.code = code
        self.transient = transient


@dataclass(frozen=True)
class ExtractionFiles:
    metadata: VideoMetadata
    transcript: Path
    caption: Path
    segments: Path
    thumbnail: Path


class YtDlpExtractor:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    async def extract(self, request: ExtractVideoPayload, work_dir: Path) -> ExtractionFiles:
        url = f"https://www.youtube.com/watch?v={request.video_id}"
        metadata_raw = await self._run(
            [
                "yt-dlp",
                "--dump-single-json",
                "--skip-download",
                "--no-playlist",
                "--no-warnings",
                url,
            ],
            work_dir,
        )
        try:
            info = json.loads(metadata_raw)
        except json.JSONDecodeError as error:
            raise ExtractionError(
                "invalid_metadata", "yt-dlp returned invalid metadata."
            ) from error

        duration = _as_int(info.get("duration"))
        if duration and duration > self._settings.max_duration_seconds:
            raise ExtractionError(
                "video_too_long",
                f"Video duration exceeds {self._settings.max_duration_seconds} seconds.",
            )

        language = normalize_language(request.preferred_language)
        await self._run(
            [
                "yt-dlp",
                "--skip-download",
                "--no-playlist",
                "--write-subs",
                "--write-auto-subs",
                "--sub-langs",
                f"{language}.*,{language}",
                "--sub-format",
                "vtt",
                "--write-thumbnail",
                "--output",
                "%(id)s.%(ext)s",
                url,
            ],
            work_dir,
        )

        caption = choose_caption(work_dir, request.video_id, language, info)
        thumbnail = choose_thumbnail(work_dir, request.video_id)
        enforce_size(caption, self._settings.max_caption_bytes, "caption_too_large")
        enforce_size(thumbnail, self._settings.max_thumbnail_bytes, "thumbnail_too_large")
        transcript = work_dir / "transcript.txt"
        segments = work_dir / "transcript.json"
        transcript_text, segment_data = vtt_to_transcript(caption)
        transcript.write_text(transcript_text, encoding="utf-8")
        segments.write_text(
            json.dumps({"segments": segment_data}, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        if not transcript.read_text(encoding="utf-8").strip():
            raise ExtractionError("empty_transcript", "The selected captions were empty.")

        return ExtractionFiles(
            metadata=to_metadata(info, request.video_id),
            transcript=transcript,
            caption=caption,
            segments=segments,
            thumbnail=thumbnail,
        )

    async def _run(self, args: list[str], cwd: Path) -> str:
        process = await asyncio.create_subprocess_exec(
            *args,
            cwd=cwd,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        try:
            stdout, stderr = await asyncio.wait_for(
                process.communicate(), timeout=self._settings.process_timeout_seconds
            )
        except TimeoutError as error:
            process.kill()
            await process.wait()
            raise ExtractionError(
                "extractor_timeout", "yt-dlp exceeded its processing timeout.", True
            ) from error

        if process.returncode != 0:
            detail = stderr.decode("utf-8", errors="replace").strip()
            lowered = detail.lower()
            if "private video" in lowered or "video unavailable" in lowered:
                code, transient = "video_unavailable", False
            elif "sign in" in lowered or "age-restricted" in lowered:
                code, transient = "authentication_required", False
            elif "subtitles" in lowered and "not available" in lowered:
                code, transient = "captions_unavailable", False
            elif "429" in lowered or "too many requests" in lowered:
                code, transient = "youtube_rate_limited", True
            else:
                code, transient = "yt_dlp_failed", True
            raise ExtractionError(code, detail[-1500:] or "yt-dlp failed.", transient)
        return stdout.decode("utf-8", errors="replace")


def normalize_language(language: str | None) -> str:
    if not language:
        return "en"
    normalized = language.strip().lower().replace("_", "-")
    if not re.fullmatch(r"[a-z]{2,3}(?:-[a-z0-9]{2,8})?", normalized):
        raise ExtractionError("invalid_language", "Preferred language is invalid.")
    return normalized


def choose_caption(
    work_dir: Path, video_id: str, language: str, info: dict[str, Any]
) -> Path:
    candidates = sorted(work_dir.glob(f"{video_id}*.vtt"))
    if not candidates:
        raise ExtractionError(
            "captions_unavailable",
            f"No captions were available for language '{language}'.",
        )

    manual_languages = set((info.get("subtitles") or {}).keys())

    def score(path: Path) -> tuple[int, int, str]:
        name = path.name.lower()
        exact = int(f".{language}.vtt" in name)
        manual = int(any(f".{item.lower()}." in name for item in manual_languages))
        return (-manual, -exact, name)

    return sorted(candidates, key=score)[0]


def choose_thumbnail(work_dir: Path, video_id: str) -> Path:
    candidates = [
        path
        for path in work_dir.glob(f"{video_id}*")
        if path.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"}
    ]
    if not candidates:
        raise ExtractionError("thumbnail_unavailable", "No thumbnail was downloaded.")
    return max(candidates, key=lambda path: path.stat().st_size)


def enforce_size(path: Path, maximum: int, code: str) -> None:
    if path.stat().st_size > maximum:
        raise ExtractionError(code, f"{path.name} exceeds {maximum} bytes.")


def vtt_to_text(path: Path) -> str:
    text, _ = vtt_to_transcript(path)
    return text


def vtt_to_transcript(path: Path) -> tuple[str, list[dict[str, str]]]:
    lines: list[str] = []
    segments: list[dict[str, str]] = []
    previous = ""
    for caption in webvtt.read(str(path)):
        text = " ".join(caption.text.replace("\n", " ").split())
        if text and text != previous:
            lines.append(text)
            segments.append(
                {"start": caption.start, "end": caption.end, "text": text}
            )
            previous = text
    return "\n".join(lines) + ("\n" if lines else ""), segments


def to_metadata(info: dict[str, Any], video_id: str) -> VideoMetadata:
    upload_date = info.get("upload_date")
    published_at = None
    if isinstance(upload_date, str) and len(upload_date) == 8:
        published_at = datetime.strptime(upload_date, "%Y%m%d").replace(
            tzinfo=timezone.utc
        )
    return VideoMetadata(
        video_id=str(info.get("id") or video_id),
        title=str(info.get("title") or "Untitled YouTube video")[:1000],
        channel_id=_as_str(info.get("channel_id")),
        channel_name=_as_str(info.get("channel") or info.get("uploader")),
        published_at=published_at,
        duration_seconds=_as_int(info.get("duration")),
        view_count=_as_int(info.get("view_count")),
        like_count=_as_int(info.get("like_count")),
        description=_as_str(info.get("description")),
        webpage_url=str(
            info.get("webpage_url")
            or f"https://www.youtube.com/watch?v={video_id}"
        ),
    )


def _as_int(value: Any) -> int | None:
    try:
        return int(value) if value is not None else None
    except (TypeError, ValueError):
        return None


def _as_str(value: Any) -> str | None:
    return str(value) if value is not None else None
