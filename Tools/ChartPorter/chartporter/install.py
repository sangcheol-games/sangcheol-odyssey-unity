"""난이도별 설치: out/ 의 초안 → Assets/Charts.

- 초안에 FATAL/ERROR 가 있으면 설치하지 않는다.
- 대상 파일이 이미 있으면 백업한다. 지난 설치 뒤 바뀐 파일(에디터 저장, 팀원 채보 등)은 force 없이 덮어쓰지 않는다.
- 상태 파일 out/000N/install_state.json 은 난이도별 {sha, source}. 예전 형식({sha, source} 한 개)은 Hard 로 읽는다.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from .chart_io import read_chart
from .fmt import backup
from .validate import validate


class InstallError(Exception):
    pass


def sha_of(data: bytes) -> str:
    return hashlib.sha1(data.replace(b"\r\n", b"\n")).hexdigest()


def load_install_state(path: Path) -> dict[str, dict]:
    if not path.exists():
        return {}
    d = json.loads(path.read_text(encoding="utf-8"))
    if "sha" in d and isinstance(d.get("sha"), str):        # 예전 형식 (하드만 설치하던 때)
        return {"Hard": {"sha": d["sha"], "source": d.get("source")}}
    return d


def save_install_state(path: Path, state: dict[str, dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(state, ensure_ascii=False, indent=1), encoding="utf-8")


def install_chart(src: Path, dst: Path, state_path: Path, diff: str, force: bool = False,
                  source_label: str | None = None) -> Path | None:
    """설치하고 백업 경로(없으면 None)를 돌려준다. 막히면 InstallError."""
    if not src.exists():
        raise InstallError(f"{src} 없음")
    chart, raw = read_chart(src)
    bad = [i for i in validate(chart, raw) if i.severity in ("FATAL", "ERROR")]
    if bad:
        raise InstallError(f"초안에 FATAL/ERROR {len(bad)}건 — 설치하지 않음 ({', '.join(i.code for i in bad[:5])})")
    state = load_install_state(state_path)
    saved = None
    if dst.exists():
        if state.get(diff, {}).get("sha") != sha_of(dst.read_bytes()) and not force:
            raise InstallError(f"{dst.name} 가 이미 있고 이 도구가 마지막으로 설치한 내용과 다름"
                               "(직접 편집했거나 다른 사람이 만든 파일일 수 있음). 덮어쓰려면 --force")
        saved = backup(dst)
    data = src.read_bytes()
    dst.write_bytes(data)
    state[diff] = {"sha": sha_of(data), "source": source_label or str(src)}
    save_install_state(state_path, state)
    return saved
