"""마디 단위 시간 이동 (retime).

1번 노멀은 커밋 1886759 에서 −1/8 마디 당겨졌다. 그때 쓴 규칙을 그대로 따른다:
  - 모든 노트를 shift 만큼 옮긴다 (마디를 넘어가면 앞 마디 끝으로).
  - 앞 마디로 넘어간 노트는 다른 그룹의 같은 높이 레인(±2)으로 — 앞 마디는 교대 규칙상 다른 그룹을 쓰므로.
  - 마디 처음(0)에서 시작하는 홀드는 그 자리에 둔다 (끝만 당겨져 홀드가 짧아짐).
옛 1번 노멀(1886759^)에 이 규칙을 적용하면 지금 노멀 554노트 중 대부분이 그대로 나온다 (tests 참고).
"""
from __future__ import annotations

from fractions import Fraction

from .model import LANES, Bar, Chart, cross_lane, expected_main, group_of


def shift_chart(chart: Chart, shift: Fraction = Fraction(-1, 8), keep_hold_start_at_zero: bool = True,
                cross_lane_on_carry: bool = True) -> tuple[Chart, list[tuple[int, int, Fraction, int, int, Fraction, str]]]:
    """(새 채보, 마디를 넘어간 노트 목록 [(원래 마디, 레인, 위치, 새 마디, 새 레인, 새 위치, 문자)])"""
    out = chart.copy()
    for bar in out.bars.values():
        bar.cells = {l: {} for l in LANES}
    carried = []
    for b in sorted(chart.bars):
        src = chart.bars[b]
        for lane in LANES:
            for p, c in src.cells[lane].items():
                np_ = p + shift
                if keep_hold_start_at_zero and c == "2" and p == 0 and shift < 0:
                    np_ = p
                nb, nl = b, lane
                while np_ < 0:
                    nb, np_ = nb - 1, np_ + 1
                    if cross_lane_on_carry:
                        nl = cross_lane(nl)
                while np_ >= 1:
                    nb, np_ = nb + 1, np_ - 1
                    if cross_lane_on_carry:
                        nl = cross_lane(nl)
                if nb < 1:
                    continue                      # 000마디 이전으로는 옮기지 않음
                if nb not in out.bars:
                    out.bars[nb] = Bar(number=nb)
                tgt = out.bars[nb]
                g = group_of(nl)
                if tgt.dirs[g] is None:
                    eg, ed = expected_main(nb)
                    tgt.dirs[g] = ed if eg == g else src.dirs[group_of(lane)]
                tgt.cells[nl][np_] = c
                if nb != b:
                    carried.append((b, lane, p, nb, nl, np_, c))
    for bar in out.bars.values():
        bar.length = max(bar.length, bar.min_length())
    return out, carried


def compare(a: Chart, b: Chart) -> tuple[int, int, list[tuple[int, int, Fraction, str, str]]]:
    """(같은 칸 수, b 의 노트 수, 다른 칸 [(마디, 레인, 위치, a 문자, b 문자)])"""
    same, diff = 0, []
    keys = {(n, l, p) for c in (a, b) for n, bar in c.bars.items() for l in LANES for p in bar.cells[l]}
    for n, l, p in sorted(keys):
        ca = a.bars[n].cells[l].get(p, "0") if n in a.bars else "0"
        cb = b.bars[n].cells[l].get(p, "0") if n in b.bars else "0"
        if ca == cb and ca != "0":
            same += 1
        elif ca != cb:
            diff.append((n, l, p, ca, cb))
    return same, b.count_notes(), diff
