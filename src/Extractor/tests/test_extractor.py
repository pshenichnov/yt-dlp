from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock
from uuid import uuid4

import json

from youtube_extractor.contracts import FetchChannelPagePayload
from youtube_extractor.extractor import (
    YtDlpExtractor,
    normalize_language,
    vtt_to_text,
    vtt_to_transcript,
)


def test_normalize_language_defaults_to_english() -> None:
    assert normalize_language(None) == "en"


def test_vtt_to_text_removes_adjacent_duplicate_cues(tmp_path: Path) -> None:
    caption = tmp_path / "caption.vtt"
    caption.write_text(
        """WEBVTT

00:00:00.000 --> 00:00:01.000
Hello world

00:00:01.000 --> 00:00:02.000
Hello world

00:00:02.000 --> 00:00:03.000
Next subject
""",
        encoding="utf-8",
    )

    assert vtt_to_text(caption) == "Hello world\nNext subject\n"


def test_vtt_to_transcript_removes_rolling_caption_overlap(tmp_path: Path) -> None:
    caption = tmp_path / "rolling-caption.vtt"
    caption.write_text(
        """WEBVTT

00:00:00.400 --> 00:00:03.110
Today I'm going to show you an extremely

00:00:03.110 --> 00:00:03.120
Today I'm going to show you an extremely

00:00:03.120 --> 00:00:04.630
Today I'm going to show you an extremely
reliable,

00:00:04.630 --> 00:00:04.640
reliable,

00:00:04.640 --> 00:00:07.670
reliable,
repeatable, and mechanical scalping

00:00:07.670 --> 00:00:07.680
repeatable, and mechanical scalping

00:00:07.680 --> 00:00:10.150
repeatable, and mechanical scalping
strategy.
""",
        encoding="utf-8",
    )

    text, segments = vtt_to_transcript(caption)

    assert text == (
        "Today I'm going to show you an extremely\n"
        "reliable,\n"
        "repeatable, and mechanical scalping\n"
        "strategy.\n"
    )
    assert [segment["text"] for segment in segments] == [
        "Today I'm going to show you an extremely",
        "reliable,",
        "repeatable, and mechanical scalping",
        "strategy.",
    ]


async def test_fetch_channel_page_reads_only_one_metadata_page(tmp_path: Path) -> None:
    extractor = YtDlpExtractor(SimpleNamespace(process_timeout_seconds=30))
    entries = [
        {
            "id": f"video{index:05d}",
            "title": f"Video {index}",
            "description": f"Description {index}",
            "upload_date": f"2026110{index}",
            "duration": 120 + index,
            "channel": "Example channel",
            "uploader_id": "@ExampleTrades",
            "thumbnails": [
                {"url": f"https://img.example/{index}-small.jpg", "width": 120},
                {"url": f"https://img.example/{index}-large.jpg", "width": 1280},
            ],
        }
        for index in range(1, 4)
    ]
    extractor._run = AsyncMock(  # type: ignore[method-assign]
        return_value="\n".join(json.dumps(entry) for entry in entries)
    )
    request = FetchChannelPagePayload(
        request_id=uuid4(),
        channel_id="UC_x5XG1OV2P6uZZ5FSM9Ttw",
        start_index=1,
        page_size=2,
    )

    page = await extractor.fetch_channel_page(request, tmp_path)

    assert len(page.videos) == 2
    assert page.has_more is True
    assert page.next_start_index == 3
    assert page.channel_name == "Example channel"
    assert page.channel_handle == "ExampleTrades"
    assert page.videos[0].description == "Description 1"
    assert page.videos[0].thumbnail_url == "https://img.example/1-large.jpg"
    assert page.videos[0].duration_seconds == 121
    args = extractor._run.await_args.args[0]  # type: ignore[attr-defined]
    assert args[args.index("--playlist-start") + 1] == "1"
    assert args[args.index("--playlist-end") + 1] == "3"
    assert "--write-subs" not in args
    assert "--write-thumbnail" not in args
