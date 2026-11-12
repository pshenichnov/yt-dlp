FROM python:3.12.16-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ffmpeg=7:7.1.5-0+deb13u1 \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN addgroup --system extractor && adduser --system --ingroup extractor extractor
WORKDIR /app
COPY src/Extractor/pyproject.toml ./
COPY src/Extractor/youtube_extractor ./youtube_extractor
RUN python -m pip install .
USER extractor
ENTRYPOINT ["python", "-m", "youtube_extractor.main"]
