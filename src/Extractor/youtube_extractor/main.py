from __future__ import annotations

import asyncio
import json
import logging
import signal
from datetime import datetime, timezone

from .config import Settings
from .database import ExtractorDatabase
from .extractor import YtDlpExtractor
from .storage import BlobStorage
from .worker import ExtractorWorker


class JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        payload = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        return json.dumps(payload, ensure_ascii=False)


async def run() -> None:
    settings = Settings.from_environment()
    worker = ExtractorWorker(
        settings,
        ExtractorDatabase(settings.postgres_dsn),
        BlobStorage(settings),
        YtDlpExtractor(settings),
    )
    loop = asyncio.get_running_loop()
    for signal_name in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(
            signal_name, lambda: asyncio.create_task(worker.stop())
        )
    await worker.run()


def main() -> None:
    handler = logging.StreamHandler()
    handler.setFormatter(JsonFormatter())
    logging.basicConfig(level=logging.INFO, handlers=[handler])
    asyncio.run(run())


if __name__ == "__main__":
    main()
