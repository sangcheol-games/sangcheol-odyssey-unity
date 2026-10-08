"""기계적으로 고칠 수 있는 채보 문제를 고친 사본 만들기 (fmt) 와 백업 후 적용 (install).

자동으로 고치는 것 (의미가 바뀌지 않음):
  - 빠진 마디를 노멀 교대 규칙대로 빈 줄로 채움 (F01 해소)
  - #DIFFICULTY 를 파일 이름에 맞춤 (W04)
  - #NOTES 재계산 (쓰기 때 항상)
선택해서 고치는 것 (내용 결정):
  - 짝 없는 4 (E04): keep | tap(1 로 바꿈) | drop(지움)
  - 끝 없는 홀드·마디를 넘는 홀드 (E13/E14): --close-holds → 7/8 에 '4' (7/8 이후 시작은 탭), 넘어가던 끝은 삭제
"""
from __future__ import annotations

import difflib
import shutil
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .chart_io import read_chart, to_bytes
from .model import LANES, Chart
from .paths import out_dir, rel
from .validate import difficulty_from_filename, validate


@dataclass
class FormatResult:
    path: Path
    chart: Chart
    changes: list[str] = field(default_factory=list)
    before: bytes = b""
    after: bytes = b""

    @property
    def changed(self) -> bool:
        return self.before.replace(b"\r\n", b"\n") != self.after.replace(b"\r\n", b"\n")

    def diff(self) -> str:
        a = self.before.decode("utf-8", "replace").splitlines()
        b = self.after.decode("utf-8", "replace").splitlines()
        return "\n".join(difflib.unified_diff(a, b, f"현재/{self.path.name}", f"수정안/{self.path.name}", lineterm="", n=1))


def orphan_hold_ends(chart: Chart) -> list[tuple[int, int, object]]:
    """(마디, 레인, 위치) — 레인 큐를 마디를 넘어 따라가며 열린 홀드가 없는 '4'."""
    out = []
    for lane in LANES:
        items = sorted((b, p, c) for b, bar in chart.bars.items() for p, c in bar.cells[lane].items() if c in "1245")
        open_hold = False
        for b, p, c in items:
            if c == "2":
                open_hold = True
            elif c in "45":
                if c == "4" and not open_hold:
                    out.append((b, lane, p))
                open_hold = False
    return out


def format_chart(path: Path, fill_gaps: bool = True, fix_header: bool = True, orphan_end: str = "keep",
                 close_holds: bool = False) -> FormatResult:
    chart, raw = read_chart(path)
    res = FormatResult(path=path, chart=chart, before=path.read_bytes())
    if fill_gaps:
        missing = [n for n in range(0, chart.last_bar() + 1) if n not in chart.bars]
        if missing:
            res.changes.append(f"빠진 마디 {', '.join(f'{n:03d}' for n in missing)} 를 빈 줄로 채움")
    if fix_header:
        want = difficulty_from_filename(str(path))
        if want and chart.header.difficulty != want:
            res.changes.append(f"#DIFFICULTY {chart.header.raw_difficulty} → {want}")
            chart.header.difficulty = want
    if orphan_end != "keep":
        for b, lane, p in orphan_hold_ends(chart):
            if orphan_end == "tap":
                chart.bars[b].cells[lane][p] = "1"
                res.changes.append(f"{b:03d}마디 레인{lane} 짝 없는 4(위치 {p}) → 탭 1")
            elif orphan_end == "drop":
                del chart.bars[b].cells[lane][p]
                res.changes.append(f"{b:03d}마디 레인{lane} 짝 없는 4(위치 {p}) 삭제")
    if close_holds:
        from .holds import close_unterminated_holds
        for fx in close_unterminated_holds(chart):
            res.changes.append(f"{fx.bar:03d}마디 레인{fx.lane} 끝 없는 홀드({fx.start}): " + (fx.detail if fx.action != "manual" else "자동 수정 안 함 — " + fx.detail))
    if chart.header.notes != chart.count_notes():
        res.changes.append(f"#NOTES {chart.header.notes} → {chart.count_notes()}")
    res.after = to_bytes(chart, fill_gaps=fill_gaps)
    return res


def song_of(path: Path) -> int | None:
    parts = path.stem.split("_")
    return int(parts[1]) if len(parts) >= 3 and parts[0] == "Chart" and parts[1].isdigit() else None


def staging_path(path: Path, kind: str) -> Path:
    sid = song_of(path)
    base = out_dir(sid) if sid is not None else out_dir() / "misc"
    return base / kind / path.name


def backup(path: Path) -> Path:
    sid = song_of(path)
    base = out_dir(sid) if sid is not None else out_dir() / "misc"
    dst = base / "backups" / datetime.now().strftime("%Y%m%d-%H%M%S") / path.name
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, dst)
    return dst


def install(res: FormatResult) -> Path:
    """백업 후 적용. 읽은 뒤 파일이 바뀌었으면(에디터 저장 등) 중단한다. 적용 후 다시 읽어 FATAL/ERROR 가 늘지 않았는지 확인."""
    if res.path.read_bytes() != res.before:
        raise RuntimeError(f"{rel(res.path)} 이 수정안을 만든 뒤에 바뀌었음 → 다시 실행할 것")
    before_chart, before_raw = read_chart(res.path)
    bad_before = sum(i.severity in ("FATAL", "ERROR") for i in validate(before_chart, before_raw))
    saved = backup(res.path)
    res.path.write_bytes(res.after)
    chart, raw = read_chart(res.path)
    bad_after = sum(i.severity in ("FATAL", "ERROR") for i in validate(chart, raw))
    if bad_after > bad_before:
        shutil.copy2(saved, res.path)
        raise RuntimeError(f"{rel(res.path)}: 적용 후 FATAL/ERROR 가 {bad_before}→{bad_after} 로 늘어 원래대로 되돌림")
    return saved
