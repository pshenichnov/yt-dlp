from pathlib import Path

from youtube_extractor.extractor import (
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
