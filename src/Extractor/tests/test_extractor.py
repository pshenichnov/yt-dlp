from pathlib import Path

from youtube_extractor.extractor import normalize_language, vtt_to_text


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
