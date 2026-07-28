SELECT 'CREATE DATABASE youtube_api OWNER youtube'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'youtube_api')\gexec

SELECT 'CREATE DATABASE youtube_orchestrator OWNER youtube'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'youtube_orchestrator')\gexec

SELECT 'CREATE DATABASE youtube_analyzer OWNER youtube'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'youtube_analyzer')\gexec

SELECT 'CREATE DATABASE youtube_extractor OWNER youtube'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'youtube_extractor')\gexec
