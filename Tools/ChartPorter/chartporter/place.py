"""노트 배치 규칙과 충돌 정리 (Placer) — 하드 포팅(port)과 이지·노멀 생성기가 함께 쓴다.

- 같은 레인 간격 ≥ min_gap (기본 max(8분, 126ms)), 동시치기 금지(옵션), 같은 레인 홀드 안 금지
- conflicts(): 검증기와 같은 기준의 충돌 쌍 (W01 / W02 / E03)
- repair(): 충돌 쌍 안의 '생성한 노트' 중 점수 낮은 것을 뺀다. 원래 있던 노트끼리의 충돌은 건드리지 않는다.
채보는 함수로 받는다 — 생성기가 작업 중 채보를 새 객체로 바꿔도(port 의 복사 단계) 따라간다.
"""
from __future__ import annotations

from fractions import Fraction
from typing import Callable

from .model import LANES, Chart
from .timing import JUDGE_UMM, bar_seconds, song_time

ONSET = "125"


class Placer:
    def __init__(self, get_chart: Callable[[], Chart], bpm: float, min_gap: float | None = None):
        self._get_chart = get_chart
        self.bpm = bpm
        self.bar_s = bar_seconds(bpm)
        self.min_gap = max(self.bar_s / 8, JUDGE_UMM) if min_gap is None else min_gap

    @property
    def chart(self) -> Chart:
        return self._get_chart()

    # ---------- 조회 ----------
    def onset_positions(self, b: int) -> set[Fraction]:
        bar = self.chart.bars.get(b)
        return {p for l in LANES for p, c in (bar.cells[l].items() if bar else []) if c in ONSET}

    def lane_items(self, lane: int, around: int):
        """이 레인의 (절대 시각, 문자, 마디, 위치) — around 마디 앞뒤 1마디."""
        out = []
        for b in (around - 1, around, around + 1):
            bar = self.chart.bars.get(b)
            if bar:
                out += [(song_time(b, p, self.bpm), c, b, p) for p, c in bar.cells[lane].items() if c in "1245"]
        return sorted(out)

    def lane_sequence(self, lane: int) -> list[tuple[Fraction, int, Fraction, str]]:
        """이 레인의 노트를 마디를 넘어 시간 순으로: (절대 위치, 마디, 위치, 문자). 판정 큐와 같은 순서."""
        return sorted((b + p, b, p, c) for b, bar in self.chart.bars.items() for p, c in bar.cells[lane].items() if c in "1245")

    def hold_spans(self, lane: int) -> list[tuple[Fraction, Fraction, bool]]:
        """(시작, 끝, 끝이 실제 4/5 인지) — 절대 위치. '2' 는 같은 레인 다음 노트가 4/5 면 거기까지(마디를 넘어도),
        아니면 그 마디 끝까지 그려지는 끝 없는 홀드."""
        seq = self.lane_sequence(lane)
        out = []
        for i, (t, b, _p, c) in enumerate(seq):
            if c != "2":
                continue
            nxt = seq[i + 1] if i + 1 < len(seq) else None
            if nxt is not None and nxt[3] in "45":
                out.append((t, nxt[0], True))
            else:
                out.append((t, Fraction(b + 1), False))
        return out

    def inside_hold(self, b: int, lane: int, pos: Fraction) -> bool:
        t = b + pos
        return any(s <= t <= e for s, e, _ in self.hold_spans(lane))

    def can_place(self, b: int, lane: int, pos: Fraction, allow_same_time: bool = False) -> str | None:
        """놓을 수 없으면 이유, 놓을 수 있으면 None."""
        bar = self.chart.bars.get(b)
        if bar is None:
            return "마디 없음"
        if pos in bar.cells[lane]:
            return "칸 사용 중"
        if not allow_same_time and pos in self.onset_positions(b):
            return "같은 시각 노트 있음(동시치기 금지)"
        if self.inside_hold(b, lane, pos):
            return "같은 레인 홀드 안"
        t = song_time(b, pos, self.bpm)
        for tt, c, _bb, _pp in self.lane_items(lane, b):
            if tt < t and t - tt < self.min_gap - 1e-9 and c in "1245":
                return "같은 레인 앞 노트와 간격 부족"
            if tt > t and tt - t < self.min_gap - 1e-9 and c in "12":
                return "같은 레인 뒤 노트와 간격 부족"
        return None

    # ---------- 정리 ----------
    def conflicts(self) -> list[tuple[str, tuple, tuple]]:
        """검증기와 같은 기준의 충돌 쌍: (코드, 앞 노트 키, 뒤 노트 키). 키 = (마디, 레인, 위치)."""
        out = []
        for lane in LANES:
            seq = self.lane_sequence(lane)
            for (t0, b0, p0, c0), (t1, b1, p1, c1) in zip(seq, seq[1:]):
                gap = song_time(b1, p1, self.bpm) - song_time(b0, p0, self.bpm)
                if c1 in "12" and gap < JUDGE_UMM and c0 in "145":
                    out.append(("W01" if c0 == "1" else "W02", (b0, lane, p0), (b1, lane, p1)))
            for s, e, _paired in self.hold_spans(lane):
                for t, b, p, c in seq:
                    if s < t < e and c in "12":
                        sb = int(s // 1)
                        out.append(("E03", (sb, lane, s - sb), (b, lane, p)))
        return out

    def repair(self, removable: dict, on_remove: Callable[[object, str], None], max_steps: int = 500) -> None:
        """removable: (마디, 레인, 위치) → 생성 기록(.char, .score, .bar 를 가진 객체). 충돌 쌍에 생성 노트가 있으면
        그중 점수 낮은 것(같으면 뒤 마디)을 빼고 on_remove(기록, 코드) 를 부른다."""
        chart = self.chart
        for _ in range(max_steps):
            todo = [(code, a, b) for code, a, b in self.conflicts()
                    if (a in removable and chart.bars[a[0]].cells[a[1]].get(a[2]) == removable[a].char)
                    or (b in removable and chart.bars[b[0]].cells[b[1]].get(b[2]) == removable[b].char)]
            if not todo:
                return
            code, a, b = todo[0]
            cands = [removable[k] for k in (a, b) if k in removable]
            victim = min(cands, key=lambda e: (e.score if e.score is not None else 0.0, -e.bar))
            key = (victim.bar, victim.lane, victim.pos)
            del chart.bars[victim.bar].cells[victim.lane][victim.pos]
            del removable[key]
            on_remove(victim, code)
