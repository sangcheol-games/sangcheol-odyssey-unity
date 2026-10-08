"""저장소 경로 찾기."""
from __future__ import annotations

from functools import lru_cache
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent.parent


@lru_cache(maxsize=1)
def repo_root() -> Path:
    """Assets/ 와 ProjectSettings/ 가 있는 Unity 프로젝트 루트."""
    for p in [TOOL_DIR, *TOOL_DIR.parents]:
        if (p / "Assets").is_dir() and (p / "ProjectSettings").is_dir():
            return p
    raise FileNotFoundError("Unity 프로젝트 루트(Assets/, ProjectSettings/)를 찾지 못함")


def charts_dir() -> Path:
    return repo_root() / "Assets" / "Charts"


def music_so_dir() -> Path:
    return repo_root() / "Assets" / "Resources" / "Music"


def music_audio_dir() -> Path:
    return repo_root() / "Assets" / "StreamingAssets" / "Music"


def songs_dir() -> Path:
    return TOOL_DIR / "songs"


def out_dir(song_id: int | None = None) -> Path:
    base = TOOL_DIR / "out"
    return base if song_id is None else base / f"{song_id:04d}"


def rel(path: str | Path) -> str:
    """저장소 루트 기준 상대 경로 (표시용)."""
    p = Path(path).resolve()
    try:
        return p.relative_to(repo_root()).as_posix()
    except ValueError:
        return str(p)
