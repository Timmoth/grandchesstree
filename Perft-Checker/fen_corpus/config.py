"""Shared configuration for the fen_corpus pipeline.

All paths are anchored to this file's parent dir so the scripts can be invoked
from anywhere. Env vars override the defaults — see README.md for the full
table.
"""

from __future__ import annotations

import os
from pathlib import Path

ROOT = Path(__file__).resolve().parent

QUERIES_FILE     = ROOT / "queries.txt"
SOURCES_DIR      = ROOT / "sources"
QUEUE_FILE       = SOURCES_DIR / "queue.jsonl"
PAGES_DIR        = SOURCES_DIR / "pages"
EXTRACTED_DIR    = ROOT / "extracted"
CORPUS_CSV       = ROOT / "corpus.csv"
EPD_DIR          = ROOT / "epd"
EPD_FILE         = EPD_DIR / "corpus.epd"

LLM_BASE_URL = os.environ.get("LLM_BASE_URL", "http://10.0.50.4:8080/v1")
LLM_MODEL    = os.environ.get("LLM_MODEL",    "local")
LLM_API_KEY  = os.environ.get("LLM_API_KEY",  "none")

SEARCH_BACKEND   = os.environ.get("SEARCH_BACKEND",  "ddg").lower()
BRAVE_API_KEY    = os.environ.get("BRAVE_API_KEY",   "")
TAVILY_API_KEY   = os.environ.get("TAVILY_API_KEY",  "")

USER_AGENT = os.environ.get(
    "USER_AGENT",
    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36",
)

REPO_ROOT = ROOT.parent.parent
TGCT_ENGINE = REPO_ROOT / "Leaderboard" / "bin" / "tgct_engine_local" / "GrandChessTree.Engine"

GITHUB_TOKEN = os.environ.get("GITHUB_TOKEN", "")
# Where to look for engine repo descriptors (mined for the GitHub-issues
# fetcher). Engines live in Leaderboard/.
ENGINES_DIR = REPO_ROOT / "Leaderboard" / "engines"


def ensure_dirs() -> None:
    SOURCES_DIR.mkdir(parents=True, exist_ok=True)
    PAGES_DIR.mkdir(parents=True, exist_ok=True)
    EXTRACTED_DIR.mkdir(parents=True, exist_ok=True)
    EPD_DIR.mkdir(parents=True, exist_ok=True)
