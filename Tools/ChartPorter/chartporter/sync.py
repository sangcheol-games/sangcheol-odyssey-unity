"""1·2절 복사 규칙 다시 적용 (sync).

songs/000N.yaml 의 sections 중 copy_of 가 있는 항목을 채보(하드·이지·노멀)에 적용한다:
  대상 마디 ← 원본 마디, drop 으로 노트 빼기. transform:
    same   = 그대로, mirror = 같은 그룹 위아래 교체(1↔2·3↔4)   — 마디 간격이 짝수일 때 (메인 라인 그룹이 같음)
    cross  = 다른 그룹의 같은 높이(1↔3·2↔4), 방향도 반대로       — 마디 간격이 홀수일 때 (메인 라인 그룹이 바뀜)
             원래 방향이던 그룹은 원래 방향, 뒤집혀 있던 그룹은 뒤집힌 채로 옮겨진다 (판정 시각은 그대로)
사용자가 직접 고친 복사 대상 마디를 덮어쓰지 않도록 마디별 3-way 판단을 한다:
  지금 마디 == 지난번 sync 가 쓴 내용  → 새 복사본으로 바꿈 (원본이 바뀐 경우)
  지금 마디 == 노멀의 그 마디 (첫 sync)  → 아직 손대지 않은 것으로 보고 바꿈
  그 밖                                  → 사용자 편집으로 보고 건드리지 않음 (locked_bars 추가 제안)
상태는 out/000N/sync_state.json (하드), sync_state_Easy.json / sync_state_Normal.json (로컬 전용).
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path

from .config import SongConfig, parse_bars
from .model import LANES, Bar, Chart, cross_lane, mirror_lane
from .paths import out_dir


@dataclass
class SyncRule:
    targets: list[int]
    sources: list[int]
    transform: str = "same"
    drop: list[tuple[int, Fraction]] = field(default_factory=list)


class RuleError(ValueError):
    pass


def rules_from_config(cfg: SongConfig) -> list[SyncRule]:
    rules = []
    for s in cfg.sections or []:
        if not isinstance(s, dict) or s.get("copy_of") is None or s.get("transform", "same") == "manual":
            continue
        t = sorted(parse_bars(s.get("bars")))
        src = sorted(parse_bars(s.get("copy_of")))
        if len(t) != len(src):
            raise RuleError(f"sections 규칙 bars={s.get('bars')} 와 copy_of={s.get('copy_of')} 의 마디 수가 다름")
        tf = s.get("transform", "same")
        if tf not in ("same", "mirror", "cross"):
            raise RuleError(f"알 수 없는 transform: {tf}")
        for a, b in zip(t, src):
            odd = (a - b) % 2 == 1
            if odd and tf != "cross":
                raise RuleError(f"{a:03d}←{b:03d}: 마디 간격이 홀수라 메인 라인 그룹이 바뀜 — transform: cross 를 쓸 것")
            if not odd and tf == "cross":
                raise RuleError(f"{a:03d}←{b:03d}: 마디 간격이 짝수인데 transform: cross — same 이나 mirror 를 쓸 것")
        drop = [(int(d["lane"]), Fraction(str(d["pos"]))) for d in s.get("drop") or []]
        rules.append(SyncRule(t, src, tf, drop))
    return rules


def ordered_pairs(rules: list[SyncRule]) -> list[tuple[int, int, SyncRule]]:
    """(대상, 원본, 규칙)을 의존 순서로: 원본이 다른 규칙의 대상이면 그 규칙을 먼저 (A→A'→A'' 역방향 사슬도).
    같은 대상에 다른 원본이 둘 이상이면, 또는 순환이면 RuleError."""
    by_target: dict[int, tuple[int, SyncRule]] = {}
    for r in rules:
        for t, s in zip(r.targets, r.sources):
            if t == s:
                raise RuleError(f"{t:03d}마디가 자기 자신을 복사함")
            if t in by_target and by_target[t][0] != s:
                raise RuleError(f"{t:03d}마디에 복사 원본이 둘 이상 ({by_target[t][0]:03d}, {s:03d})")
            by_target.setdefault(t, (s, r))
    out, done, visiting = [], set(), set()

    def visit(t: int):
        if t in done:
            return
        if t in visiting:
            raise RuleError(f"복사 규칙이 순환함 ({t:03d}마디)")
        visiting.add(t)
        s, r = by_target[t]
        if s in by_target:
            visit(s)
        visiting.discard(t)
        done.add(t)
        out.append((t, s, r))

    for t in sorted(by_target):
        visit(t)
    return out


def bar_hash(bar: Bar | None) -> str:
    """마디의 의미 해시 (줄 순서·분할 수·줄바꿈과 무관)."""
    if bar is None:
        return "none"
    key = (tuple(bar.dirs.get(g) for g in (0, 1)),
           tuple((l, tuple(sorted((str(p), c) for p, c in bar.cells[l].items() if c != "0"))) for l in LANES))
    return hashlib.sha1(repr(key).encode()).hexdigest()[:12]


def map_lane(lane: int, transform: str) -> int:
    return mirror_lane(lane) if transform == "mirror" else cross_lane(lane) if transform == "cross" else lane


def transformed(src: Bar, number: int, transform: str, drop: list[tuple[int, Fraction]]) -> Bar:
    out = src.copy(number=number)
    if transform == "mirror":
        out.cells = {l: dict(src.cells[mirror_lane(l)]) for l in LANES}
    elif transform == "cross":
        out.cells = {l: dict(src.cells[cross_lane(l)]) for l in LANES}
        out.dirs = {1 - g: (None if d is None else 1 - d) for g, d in src.dirs.items()}
    for lane, pos in drop:
        out.cells[lane].pop(pos, None)
    return out


@dataclass
class SyncItem:
    bar: int
    source: int
    transform: str
    status: str          # same | write | user_edited | locked
    new: Bar | None = None


def state_path(song_id: int, diff: str = "Hard") -> Path:
    return out_dir(song_id) / ("sync_state.json" if diff == "Hard" else f"sync_state_{diff}.json")


def load_state(song_id: int, diff: str = "Hard") -> dict:
    p = state_path(song_id, diff)
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else {"bars": {}}


def save_state(song_id: int, state: dict, diff: str = "Hard") -> None:
    p = state_path(song_id, diff)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(state, ensure_ascii=False, indent=1), encoding="utf-8")


def plan_sync(cfg: SongConfig, target: Chart, normal: Chart | None, state: dict, force: bool = False) -> tuple[Chart, list[SyncItem]]:
    """규칙을 마디 번호 순서대로 적용한 새 채보와 마디별 판단 결과. 사슬 복사(A→A'→A'')도 앞에서부터 반영된다."""
    out = target.copy()
    items: list[SyncItem] = []
    written = state.get("bars", {})
    for t, s, rule in ordered_pairs(rules_from_config(cfg)):
        if s not in out.bars:
            raise RuleError(f"원본 {s:03d}마디가 채보에 없음")
        new = transformed(out.bars[s], t, rule.transform, rule.drop)
        cur = out.bars.get(t)
        h_cur, h_new = bar_hash(cur), bar_hash(new)
        if t in cfg.locked_bars or t in cfg.frozen_bars or t in cfg.keep_normal_bars:
            status = "locked"
        elif h_cur == h_new:
            status = "same"
        elif force or written.get(str(t)) == h_cur or (str(t) not in written and normal is not None and h_cur == bar_hash(normal.bars.get(t))):
            status = "write"
        else:
            status = "user_edited"
        if status == "write":
            out.bars[t] = new
        items.append(SyncItem(t, s, rule.transform, status, new))
    return out, items


def updated_state(items: list[SyncItem], state: dict) -> dict:
    bars = dict(state.get("bars", {}))
    for it in items:
        if it.status in ("same", "write"):
            bars[str(it.bar)] = bar_hash(it.new)
    return {"bars": bars}
