import json
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

from youtube_extractor.contracts import CommandEnvelope, EventEnvelope


def test_command_example_matches_worker_contract() -> None:
    path = (
        Path(__file__).parents[3]
        / "contracts"
        / "extractor"
        / "extract-video.command.v1.example.json"
    )
    envelope = CommandEnvelope.model_validate_json(path.read_text(encoding="utf-8"))

    assert envelope.message_type == "extract-video"
    assert envelope.payload.video_id == "dQw4w9WgXcQ"


def test_examples_match_json_schemas() -> None:
    root = Path(__file__).parents[3]
    pairs = [
        (
            "contracts/json-schema/extract-video-command.v1.schema.json",
            "contracts/extractor/extract-video.command.v1.example.json",
        ),
        (
            "contracts/json-schema/extractor-event.v1.schema.json",
            "contracts/extractor/extract-video.completed.v1.example.json",
        ),
        (
            "contracts/json-schema/extractor-event.v1.schema.json",
            "contracts/extractor/extract-video.failed.v1.example.json",
        ),
    ]
    for schema_name, example_name in pairs:
        schema = json.loads((root / schema_name).read_text(encoding="utf-8"))
        example = json.loads((root / example_name).read_text(encoding="utf-8"))
        Draft202012Validator(
            schema, format_checker=FormatChecker()
        ).validate(example)


def test_terminal_examples_match_python_contract() -> None:
    root = Path(__file__).parents[3] / "contracts" / "extractor"
    for name in (
        "extract-video.completed.v1.example.json",
        "extract-video.failed.v1.example.json",
    ):
        EventEnvelope.model_validate_json((root / name).read_text(encoding="utf-8"))
