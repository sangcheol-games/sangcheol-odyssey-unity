"""끝 없는 홀드 정리.

같은 마디 안에 끝(4/5)이 없는 홀드(시작 '2')는 게임 버그를 일으킨다:
  - 끝 없는 홀드 (E13): 마디 끝까지 그려지고 끝 판정이 없음
  - 마디를 넘는 홀드 (E14): 끝이 다음 마디 이후에 있음
정리 규칙 (2번 사용자 하드의 규칙: 끝 없는 홀드는 7/8 에 끝, 13/13):
  - 시작이 7/8 전이고 그 사이 같은 레인에 다른 노트가 없으면 → 7/8 에 '4'
  - 시작이 7/8 이후면 → 탭 '1'
  - 마디를 넘던 끝(다음 마디의 4/5)은 짝이 없어지므로 지운다
  - 시작과 7/8 사이에 같은 레인 노트가 있으면 자동으로 고치지 않고 'manual' 로 보고
"""
from __future__ import annotations

from dataclasses import dataclass
from fractions import Fraction

from .model import LANES, Chart

SEVEN8 = Fraction(7, 8)


@dataclass
class HoldFix:
    bar: int
    lane: int
    start: Fraction
    action: str          # close_78 | to_tap | manual
    detail: str = ""


def unterminated_holds(chart: Chart) -> list[tuple[int, int, Fraction, tuple | None]]:
    """(마디, 레인, 시작, 마디를 넘는 끝 (마디, 위치, 문자) 또는 None)"""
    out = []
    for lane in LANES:
        seq = sorted((b + p, b, p, c) for b, bar in chart.bars.items() for p, c in bar.cells[lane].items() if c in "1245")
        for i, (_t, b, p, c) in enumerate(seq):
            if c != "2":
                continue
            later_in_bar = [(q, d) for _tt, bb, q, d in seq[i + 1:] if bb == b]
            if later_in_bar and later_in_bar[0][1] in "45":
                continue                                  # 같은 마디 안에서 끝남
            nxt = seq[i + 1] if i + 1 < len(seq) else None
            cross = (nxt[1], nxt[2], nxt[3]) if nxt is not None and nxt[1] != b and nxt[3] in "45" else None
            out.append((b, lane, p, cross))
    return out


def close_unterminated_holds(chart: Chart, bars: set[int] | None = None) -> list[HoldFix]:
    """chart 를 제자리에서 고치고 고친 내역을 돌려준다. bars 가 있으면 그 마디의 홀드만."""
    fixes = []
    for b, lane, s, cross in unterminated_holds(chart):
        if bars is not None and b not in bars:
            continue
        cells = chart.bars[b].cells[lane]
        between = [q for q, d in cells.items() if s < q <= SEVEN8 and d != "0"]
        if s < SEVEN8 and not between:
            cells[SEVEN8] = "4"
            action, detail = "close_78", "7/8 에 끝(4) 추가"
        elif s >= SEVEN8:
            cells[s] = "1"
            action, detail = "to_tap", "7/8 이후 시작 → 탭"
        else:
            fixes.append(HoldFix(b, lane, s, "manual", f"시작과 7/8 사이에 같은 레인 노트({', '.join(str(q) for q in sorted(between))})가 있어 직접 확인 필요"))
            continue
        if cross is not None:
            cb, cp, cc = cross
            chart.bars[cb].cells[lane].pop(cp, None)
            detail += f", 다음 마디 끝({cb:03d}:{cp} '{cc}') 삭제"
        fixes.append(HoldFix(b, lane, s, action, detail))
    return fixes
