"""노멀 덜어내기 → 이지 (thin).

사용자 노멀이 이미 있는 곡(1번)의 이지를 만든다. 노멀에서 노트를 빼기만 하고 새로 넣지 않는다
(2번 사용자: 이지 노트의 96%가 노멀에 그대로 있음 = 이지 ⊂ 노멀).

규칙 (2번 사용자 이지·노멀 쌍에서 잰 값):
  1. 끝 없는 홀드는 7/8 에서 닫는다 (게임 버그). 노멀 파일은 건드리지 않고 메모리에서만
  2. 8분 격자 밖(16분 엇박·셋잇단)은 뺀다 (사용자 이지는 8분 격자만)
  3. 동시치기는 하나만 남긴다 (직전 노트와 위아래가 바뀌는 쪽)
  4. 이웃 노트(모든 레인) 간격이 min_gap_s(0.19초) 미만이면 점수 낮은 쪽을 뺀다
     — 2번 이지의 최소 간격이 8분(0.194초). 155 BPM 이하에서만 8분 연타가 남는다 (사용자 결정)
  5. 마디당 target_per_bar(3.33, 2번 이지)를 넘으면 점수가 가장 낮은 무리부터 통째로 뺀다
     (순위가 아니라 점수 무리 단위라 같은 노멀 마디는 같은 이지 마디가 된다). 마디마다 최소 1노트
  6. 점수 = 8분 위치별 '이지가 노멀 노트를 남긴 비율' (2번)
       × inserted_factor  노멀이 4분 사이에 끼워 넣은 8분 (양옆 ±1/8 에 노트)
       + sync_bonus       다음 정박이 빈 엇박 (당김음 — 사용자 이지는 실제 소리 위치를 지킴)
  7. 남긴 노트는 노멀의 레인·홀드를 그대로 쓴다. 홀드 안에 다른 레인 노트가 남으면
     그 노트 1/8 앞에서 홀드를 끝내고, 그럴 자리가 없으면 탭으로 바꾼다 (2번 이지: 홀드 중 다른 노트 2곳뿐)
  8. min_hold_s(0.19초) 보다 짧은 홀드는 탭으로 (2번 이지의 가장 짧은 홀드 = 8분 0.194초, 사용자 결정)
  9. 릴리즈 끝 '5' 는 '4' 로 (2번 이지에는 '5' 가 없음). 마디 길이는 노멀 길이(8 초과면 8)
모든 뺀·바꾼 노트는 log 에 이유를 남긴다.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction
from math import lcm

from .holds import close_unterminated_holds
from .model import LANES, Bar, Chart, Header, expected_main, group_of, is_top
from .port import LogEntry, PortResult

EIGHTH = Fraction(1, 8)
ONE = Fraction(1)


@dataclass
class ThinParams:
    # 2번: 노멀의 8분 위치 k(0..7) 노트를 이지가 남긴 비율
    keep_by_k8: dict[int, float] = field(default_factory=lambda: {0: .96, 1: .12, 2: .88, 3: .34, 4: .75, 5: .11, 6: .93, 7: .54})
    inserted_factor: float = 0.3
    sync_bonus: float = 0.3
    min_gap_s: float = 0.19
    min_hold_s: float = 0.19
    target_per_bar: float = 3.33
    max_length: int = 8


@dataclass
class Unit:
    """판정 단위: 탭 하나 또는 홀드 하나(시작 + 끝)."""
    bar: int
    lane: int
    pos: Fraction
    char: str                       # '1' 탭, '2' 홀드 시작
    end: tuple[Fraction, str] | None = None
    score: float = 0.0
    why: str = ""
    tie: float = 0.0                # 간격 규칙에서 점수가 같을 때만 쓰는 소리 세기 백분위

    @property
    def t(self) -> Fraction:
        return self.bar + self.pos

    @property
    def key(self) -> tuple[int, int, Fraction]:
        return (self.bar, self.lane, self.pos)


def k8_of(pos: Fraction) -> int | None:
    x = pos * 8
    return int(x) if x.denominator == 1 else None


def _units(chart: Chart) -> tuple[list[Unit], list[tuple[int, int, Fraction, str]]]:
    """노트를 판정 단위로 묶는다. 짝 없는 끝(4/5)은 따로 돌려준다."""
    units, orphans = [], []
    for b in sorted(chart.bars):
        bar = chart.bars[b]
        for lane in LANES:
            ends = set()
            for s, end in bar.holds(lane):
                units.append(Unit(b, lane, s, "2", end))
                if end is not None:
                    ends.add(end[0])
            for p, c in bar.cells[lane].items():
                if c == "1":
                    units.append(Unit(b, lane, p, "1"))
                elif c in "45" and p not in ends:
                    orphans.append((b, lane, p, c))
    units.sort(key=lambda u: (u.t, u.lane))
    return units, orphans


def score_units(units: list[Unit], params: ThinParams, tie=None) -> None:
    """점수는 노멀 전체(덜어내기 전) 기준이라 마디 내용이 같으면 점수도 같다."""
    times = {u.t for u in units}
    for u in units:
        k = k8_of(u.pos)
        if k is None:
            u.score, u.why = -1.0, "8분 격자 밖"
            continue
        s = params.keep_by_k8[k]
        why = [f"위치 {u.pos} 기본 {s:.2f}"]
        if k % 2 == 1:
            if u.t - EIGHTH in times and u.t + EIGHTH in times:
                s *= params.inserted_factor
                why.append(f"4분 사이에 끼운 8분 ×{params.inserted_factor}")
            if u.t + EIGHTH not in times:
                s += params.sync_bonus
                why.append(f"다음 정박이 빈 당김음 +{params.sync_bonus}")
        u.score, u.why = s, ", ".join(why)
        if tie is not None:
            u.tie = float(tie(u))


def thin(normal: Chart, bpm: float, params: ThinParams | None = None, tie=None,
         level: int | None = None) -> PortResult:
    p = params or ThinParams()
    work = normal.copy()
    log: list[LogEntry] = []
    notes: dict[int, dict] = {}

    def flag(b: int, name: str) -> None:
        n = notes.setdefault(b, {"flags": []})
        if name not in n["flags"]:
            n["flags"].append(name)

    # 1. 끝 없는 홀드 닫기 (메모리에서만)
    for f in close_unterminated_holds(work):
        if f.action == "manual":
            work.bars[f.bar].cells[f.lane][f.start] = "1"
            log.append(LogEntry(f.bar, f.lane, f.start, "1", "HOLD_TO_TAP", None, f"끝 없는 홀드 — {f.detail} → 탭"))
            flag(f.bar, "HOLD_MANUAL")
        else:
            log.append(LogEntry(f.bar, f.lane, f.start, "2" if f.action == "close_78" else "1",
                                "HOLD_END_78" if f.action == "close_78" else "HOLD_TO_TAP", None, f"끝 없는 홀드 — {f.detail}"))

    units, orphans = _units(work)
    for b, lane, q, c in orphans:
        log.append(LogEntry(b, lane, q, c, "THIN_ORPHAN", None, "짝 없는 홀드 끝 — 뺌"))
    score_units(units, p, tie)
    keep = [u for u in units if u.bar > 0]

    def drop(u: Unit, origin: str, reason: str) -> None:
        keep.remove(u)
        log.append(LogEntry(u.bar, u.lane, u.pos, u.char, origin, round(u.score, 3), reason))

    # 2. 8분 격자 밖
    for u in [u for u in keep if k8_of(u.pos) is None]:
        drop(u, "THIN_GRID", "8분 격자 밖 (이지는 8분 격자만)")

    # 3. 동시치기 → 하나 (직전에 남긴 노트와 위아래가 바뀌는 쪽, 같으면 점수·위 레인)
    prev_top: bool | None = None
    i = 0
    keep.sort(key=lambda u: (u.t, u.lane))
    while i < len(keep):
        same = [u for u in keep if u.t == keep[i].t]
        if len(same) > 1:
            best = sorted(same, key=lambda u: (prev_top is not None and is_top(u.lane) == prev_top, -u.score, u.lane))[0]
            for u in same:
                if u is not best:
                    drop(u, "THIN_CHORD", f"동시치기 → 레인{best.lane} 하나만 남김")
            same = [best]
        prev_top = is_top(same[0].lane)
        i = keep.index(same[0]) + 1

    # 4. 간격 규칙 (모든 레인)
    bar_s = 240.0 / float(bpm)
    changed = True
    while changed:
        changed = False
        keep.sort(key=lambda u: (u.t, u.lane))
        for a, c in zip(keep, keep[1:]):
            if float(c.t - a.t) * bar_s < p.min_gap_s - 1e-9:
                lo = a if (a.score, a.tie, -a.t) < (c.score, c.tie, -c.t) else c
                other = c if lo is a else a
                drop(lo, "THIN_GAP", f"이웃 노트({other.bar:03d}:{other.pos} 레인{other.lane})와 "
                                     f"{float(c.t - a.t) * bar_s:.3f}초 < {p.min_gap_s}초")
                changed = True
                break

    # 5. 밀도 상한 (점수 무리 단위, 마디마다 최소 1노트)
    music_bars = sorted({u.bar for u in units if u.bar > 0})
    target = int(p.target_per_bar * len(music_bars))
    for s in sorted({round(u.score, 6) for u in keep}):
        if len(keep) <= target:
            break
        for u in sorted([u for u in keep if round(u.score, 6) == s], key=lambda u: (u.t, u.lane)):
            if sum(1 for x in keep if x.bar == u.bar) > 1:
                drop(u, "THIN_DENSITY", f"마디당 {p.target_per_bar} 상한 (점수 {s:.2f} 무리)")

    # 6. 홀드 안에 남은 다른 레인 노트 → 홀드를 그 1/8 앞에서 끝내거나 탭으로
    kept_t = sorted(keep, key=lambda u: (u.t, u.lane))
    for u in kept_t:
        if u.char != "2":
            continue
        e = u.end[0] if u.end else ONE
        inner = [x for x in kept_t if x.bar == u.bar and u.pos < x.pos < e and x is not u]
        if not inner:
            continue
        first = min(x.pos for x in inner)
        new_end = first - EIGHTH
        if new_end > u.pos:
            u.end = (new_end, "4")
            log.append(LogEntry(u.bar, u.lane, u.pos, "2", "HOLD_SHORTEN", None, f"홀드 안 다른 레인 노트({first}) → {new_end} 에서 끝"))
        else:
            u.char, u.end = "1", None
            log.append(LogEntry(u.bar, u.lane, u.pos, "1", "HOLD_TO_TAP", None, f"홀드 안 다른 레인 노트({first}) → 탭"))

    # 7. 너무 짧은 홀드 → 탭
    for u in kept_t:
        if u.char == "2" and u.end is not None and float(u.end[0] - u.pos) * bar_s < p.min_hold_s - 1e-9:
            log.append(LogEntry(u.bar, u.lane, u.pos, "1", "HOLD_TO_TAP", None,
                                f"짧은 홀드 {float(u.end[0] - u.pos) * bar_s:.3f}초 < {p.min_hold_s}초 → 탭"))
            u.char, u.end = "1", None

    # 8. 이지 채보 만들기
    easy = Chart(header=Header(title=normal.header.title, artist=normal.header.artist, difficulty="Easy",
                               level=level if level is not None else 1, bpm=normal.header.bpm))
    by_bar: dict[int, list[Unit]] = {}
    for u in keep:
        by_bar.setdefault(u.bar, []).append(u)
    for b in range(0, normal.last_bar() + 1):
        src = normal.bars.get(b)
        bar = Bar(b, length=4)
        g, d = expected_main(b) if b > 0 else (1, 1)
        if src is not None and b > 0:
            used = src.groups_used()
            if used and used != [g]:
                flag(b, "NOT_MAIN")
                g = used[0]
            if src.dirs.get(g) is not None:
                d = src.dirs[g]
        bar.dirs[g] = d
        for u in by_bar.get(b, []):
            if group_of(u.lane) != g:
                flag(b, "NOT_MAIN")
                bar.dirs[group_of(u.lane)] = src.dirs[group_of(u.lane)] if src else d
            bar.cells[u.lane][u.pos] = u.char
            if u.end is not None:
                ec = u.end[1]
                if ec == "5":
                    log.append(LogEntry(b, u.lane, u.end[0], "4", "RELEASE_TO_END", None, "릴리즈 끝 '5' → '4' (사용자 이지에는 '5' 없음)"))
                    ec = "4"
                bar.cells[u.lane][u.end[0]] = ec
        base = min(src.length, p.max_length) if src is not None and b > 0 else 4
        n = base
        for lane in LANES:
            for q in bar.cells[lane]:
                n = lcm(n, q.denominator)
        bar.length = n
        easy.bars[b] = bar
        if b > 0 and len(by_bar.get(b, [])) == 1 and len([u for u in units if u.bar == b]) > 1:
            flag(b, "SPARSE")
    easy.header.notes = easy.count_notes()
    for b in {e.bar for e in log if e.origin in ("THIN_CHORD", "HOLD_SHORTEN", "HOLD_TO_TAP")}:
        flag(b, "CHANGED_HOLD_OR_CHORD")
    for b, n in notes.items():
        n.setdefault("flags", [])
    return PortResult(chart=easy, log=log, bars=notes, offset=0.0)


def onset_times(chart: Chart) -> set[Fraction]:
    return {b + p for b, bar in chart.bars.items() for p, _l, c in bar.notes() if c in "12"}
