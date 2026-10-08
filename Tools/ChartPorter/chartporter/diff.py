"""노멀 ↔ 하드 마디 단위 비교.

노멀의 각 노트 시작(1, 2, 5)이 하드에서 어떻게 됐는지(유지 / 같은 그룹 위아래 교체 / 다른 그룹으로 이동 /
1/16 이동 / 삭제)와, 하드에만 있는 노트가 어디에 들어갔는지(빈 칸 채우기 / 노멀 노트와 같은 시각 / 기타)를 분류한다.
마디 단위로는 기믹(두 그룹 사용, 방향, 메인 라인 제거)과 홀드 변화를 기록한다.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from fractions import Fraction

from .model import LANES, LTR, RTL, Bar, Chart, expected_main, group_of, lanes_of, mirror_lane
from .timing import is_odd_sixteenth, is_on_grid

SIXTEENTH = Fraction(1, 16)
DEFAULT_DIR = {0: LTR, 1: RTL}     # 노멀 교대 규칙에서 각 그룹의 기본 방향
ONSET = "125"


@dataclass(frozen=True)
class Note:
    bar: int
    pos: Fraction
    lane: int
    char: str

    @property
    def t(self) -> Fraction:          # 마디 단위 절대 위치
        return self.bar + self.pos


@dataclass
class NoteMatch:
    normal: Note | None
    hard: Note | None
    kind: str        # kept | type_change | relane_same | relane_cross | shift | deleted | added
    excluded: bool = False   # 하드 노트가 학습 제외 대상(16분 홀수 칸)


@dataclass
class BarDiff:
    bar: int
    matches: list[NoteMatch] = field(default_factory=list)
    klass: str = ""                  # unchanged | add_only | type_only | moved | recharted | empty
    gimmick: dict = field(default_factory=dict)
    normal_len: int = 0
    hard_len: int = 0


@dataclass
class HoldChange:
    bar: int
    lane: int
    start: Fraction
    normal_end: tuple | None
    kind: str        # kept | end_closed_78 | end_changed | to_tap | relaned | removed | new
    hard_end: tuple | None = None
    length: Fraction | None = None


@dataclass
class ChartDiff:
    bars: dict[int, BarDiff]
    holds: list[HoldChange]
    excluded: list[Note]             # 학습 제외(16분 홀수 칸 등)


def onsets(bar: Bar | None, number: int) -> list[Note]:
    if bar is None:
        return []
    return [Note(number, pos, lane, c) for pos, lane, c in bar.notes() if c in ONSET]


def _match_bar(nn: list[Note], hh: list[Note]) -> list[NoteMatch]:
    """결정적 탐욕 매칭: 같은 칸·레인 → 같은 칸 같은 그룹 → 같은 칸 다른 그룹 → ±1/16 같은 레인 → ±1/16 아무 레인."""
    out: list[NoteMatch] = []
    free_h = list(hh)
    rest_n = []

    def take(pred):
        for h in free_h:
            if pred(h):
                free_h.remove(h)
                return h
        return None

    for n in nn:
        h = take(lambda h: h.pos == n.pos and h.lane == n.lane)
        if h is not None:
            out.append(NoteMatch(n, h, "kept" if _same_type(n.char, h.char) else "type_change"))
        else:
            rest_n.append(n)
    stages = [
        ("relane_same", lambda n, h: h.pos == n.pos and h.lane == mirror_lane(n.lane)),
        ("relane_cross", lambda n, h: h.pos == n.pos and group_of(h.lane) != group_of(n.lane)),
        ("shift", lambda n, h: abs(h.pos - n.pos) == SIXTEENTH and h.lane == n.lane),
        ("shift", lambda n, h: abs(h.pos - n.pos) == SIXTEENTH),
    ]
    for kind, pred in stages:
        nxt = []
        for n in rest_n:
            h = take(lambda h, n=n: pred(n, h))
            if h is not None:
                out.append(NoteMatch(n, h, kind))
            else:
                nxt.append(n)
        rest_n = nxt
    out += [NoteMatch(n, None, "deleted") for n in rest_n]
    out += [NoteMatch(None, h, "added") for h in free_h]
    return out


def _same_type(a: str, b: str) -> bool:
    return (a == "2") == (b == "2")


def gimmick_info(bar: Bar | None, number: int) -> dict:
    """교대 규칙 대비 이 마디가 쓴 기믹. 노트가 있는 그룹만 센다 (빈 줄은 무시)."""
    if bar is None or number <= 0:
        return {}
    g_main, d_main = expected_main(number)
    used = [g for g in (0, 1) if any(bar.cells[l] for l in lanes_of(g))]
    info: dict = {}
    if g_main in used and bar.dirs[g_main] != d_main:
        info["main_reversed"] = True
    if used and g_main not in used:
        info["main_dropped"] = True
    other = 1 - g_main
    if other in used:
        info["extra_group"] = other
        info["extra_dir"] = "own" if bar.dirs[other] == DEFAULT_DIR[other] else "flipped"
        info["extra_notes"] = sum(1 for l in lanes_of(other) for c in bar.cells[l].values() if c in ONSET)
        info["extra_pattern"] = tuple(sorted((str(p), l, c) for l in lanes_of(other) for p, c in bar.cells[l].items()))
        if g_main in used:
            ts_main = {p for l in lanes_of(g_main) for p, c in bar.cells[l].items() if c in ONSET}
            ts_other = {p for l in lanes_of(other) for p, c in bar.cells[l].items() if c in ONSET}
            if ts_main & ts_other:
                info["both_group_chord"] = len(ts_main & ts_other)
    return info


def _bar_class(matches: list[NoteMatch], n_normal: int, n_hard: int) -> str:
    c = Counter(m.kind for m in matches)
    if n_normal == 0 and n_hard == 0:
        return "empty"
    kept = c["kept"] + c["type_change"]
    if kept == n_normal == n_hard and c["type_change"] == 0:
        return "unchanged"
    if kept == n_normal == n_hard:
        return "type_only"
    if c["kept"] == n_normal and c["added"] > 0 and kept + c["added"] == n_hard:
        return "add_only"
    if n_normal and kept / n_normal < 0.5:
        return "recharted"
    return "moved"


def _hold_changes(normal: Chart, hard: Chart, bars: list[int]) -> list[HoldChange]:
    out = []
    for b in bars:
        nb, hb = normal.bars.get(b), hard.bars.get(b)
        if nb is None:
            continue
        hard_holds = {}
        if hb is not None:
            for lane in LANES:
                for s, e in hb.holds(lane):
                    hard_holds[(lane, s)] = e
        used = set()
        for lane in LANES:
            for s, e in nb.holds(lane):
                length = (e[0] if e else Fraction(1)) - s
                key = (lane, s)
                hc = HoldChange(b, lane, s, e, "removed", length=length)
                if key in hard_holds:
                    he = hard_holds[key]
                    used.add(key)
                    hc.hard_end = he
                    if he == e:
                        hc.kind = "kept"
                    elif e is None and he is not None and he[0] == Fraction(7, 8) and he[1] == "4":
                        hc.kind = "end_closed_78"
                    else:
                        hc.kind = "end_changed"
                elif hb is not None and hb.cells[lane].get(s) == "1":
                    hc.kind = "to_tap"
                elif hb is not None and any((l2, s) in hard_holds and (l2, s) not in used for l2 in LANES if l2 != lane):
                    l2 = next(l2 for l2 in LANES if l2 != lane and (l2, s) in hard_holds and (l2, s) not in used)
                    used.add((l2, s))
                    hc.kind, hc.hard_end = "relaned", hard_holds[(l2, s)]
                out.append(hc)
        for (lane, s), e in hard_holds.items():
            if (lane, s) not in used and not (nb.cells[lane].get(s) == "2"):
                out.append(HoldChange(b, lane, s, None, "new", hard_end=e, length=(e[0] if e else Fraction(1)) - s))
    return out


def diff_charts(normal: Chart, hard: Chart, bars: list[int] | None = None, exclude_odd16: bool = True) -> ChartDiff:
    if bars is None:
        bars = sorted(b for b in set(normal.bars) | set(hard.bars) if b > 0)
    out: dict[int, BarDiff] = {}
    excluded: list[Note] = []
    for b in bars:
        nn = onsets(normal.bars.get(b), b)
        hh = onsets(hard.bars.get(b), b)
        bd = BarDiff(bar=b, matches=_match_bar(nn, hh))
        if exclude_odd16:   # 매칭은 그대로 하고, 하드 쪽이 16분 홀수 칸인 항목만 통계 제외로 표시
            for m in bd.matches:
                if m.hard is not None and is_odd_sixteenth(m.hard.pos):
                    m.excluded = True
                    excluded.append(m.hard)
        bd.klass = _bar_class(bd.matches, len(nn), len(hh))
        bd.gimmick = gimmick_info(hard.bars.get(b), b)
        bd.normal_len = normal.bars[b].length if b in normal.bars else 0
        bd.hard_len = hard.bars[b].length if b in hard.bars else 0
        out[b] = bd
    return ChartDiff(bars=out, holds=_hold_changes(normal, hard, bars), excluded=excluded)


# ---------------- 추가 노트 위치 분류 ----------------

def classify_added(d: ChartDiff, normal: Chart, include_excluded: bool = False) -> list[tuple[Note, str, Fraction | None]]:
    """(하드 노트, 분류, 가장 가까운 노멀 노트 시작까지 거리[마디 단위])
    분류: same_time(노멀 노트와 같은 시각, 동시치기·겹침)
          gapfill(같은 마디 안에서 앞뒤 노멀 노트 사이 빈 칸)
          extend(같은 마디의 노멀 노트 앞이나 뒤 한쪽에만 붙음)
          isolated(같은 마디에 노멀 노트가 없음)"""
    normal_by_bar = {b: sorted(p for p, _l, c in bar.notes() if c in ONSET) for b, bar in normal.bars.items() if b > 0}
    normal_ts = sorted(b + p for b, ps in normal_by_bar.items() for p in ps)
    out = []
    for bd in d.bars.values():
        for m in bd.matches:
            if m.kind != "added" or (m.excluded and not include_excluded):
                continue
            t, ps = m.hard.t, normal_by_bar.get(m.hard.bar, [])
            dist = min((abs(t - x) for x in normal_ts), default=None)
            if dist == 0:
                k = "same_time"
            elif not ps:
                k = "isolated"
            elif any(p < m.hard.pos for p in ps) and any(p > m.hard.pos for p in ps):
                k = "gapfill"
            else:
                k = "extend"
            out.append((m.hard, k, dist))
    return out


def grid_class(pos: Fraction) -> str:
    if is_on_grid(pos, 4):
        return "beat"
    if is_on_grid(pos, 8):
        return "8th"
    if is_on_grid(pos, 16):
        return "16th"
    return "other"
