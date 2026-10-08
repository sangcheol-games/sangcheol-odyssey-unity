"""포팅 결과를 사용자 하드와 비교 (eval).

기준 곡(사용자가 직접 찍은 노멀/하드)의 노멀을 포팅해 사용자 하드와 맞춰 본다.
  추가 노트  : 노멀에 없던 시각(마디, 위치)에 생긴 노트 — 시각 기준 precision/recall, 레인까지 같은지
  기믹 마디  : 다른 그룹을 쓴 마디 — precision/recall, 같은 마디에서 형태(위치 집합)가 같은지
  밀도       : 8마디 창별 노트 시작 수 차이 (노멀 대비 비율)
  홀드       : 끝 없는 노멀 홀드를 7/8 에서 닫은 마디가 같은지
16분 홀수 칸 사용자 노트는 확인 전이라 비교에서 뺀다.
기준선: 노멀 그대로 / 같은 수만큼 소리 있는 빈 8분 칸을 무작위(결정적)로 채운 것.
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass
from fractions import Fraction

from .diff import diff_charts
from .model import LANES, Chart, expected_main, lanes_of
from .timing import is_odd_sixteenth

ONSET = "125"


def onset_cells(c: Chart) -> dict[tuple[int, Fraction], set[int]]:
    out: dict[tuple[int, Fraction], set[int]] = {}
    for b, bar in c.bars.items():
        if b <= 0:
            continue
        for p, l, ch in bar.notes():
            if ch in ONSET and not is_odd_sixteenth(p):
                out.setdefault((b, p), set()).add(l)
    return out


def added(normal: Chart, c: Chart) -> dict[tuple[int, Fraction], set[int]]:
    n = onset_cells(normal)
    return {k: v for k, v in onset_cells(c).items() if k not in n}


def prf(tp: int, n_pred: int, n_true: int) -> tuple[float, float, float]:
    p = tp / n_pred if n_pred else 0.0
    r = tp / n_true if n_true else 0.0
    return p, r, (2 * p * r / (p + r) if p + r else 0.0)


def gimmick_bars(normal: Chart, c: Chart) -> dict[int, frozenset]:
    d = diff_charts(normal, c)
    out = {}
    for b, bd in d.bars.items():
        g = bd.gimmick
        if g.get("extra_group") is not None:
            pat = frozenset(p for p, _l, ch in g["extra_pattern"] if ch in ONSET and not is_odd_sixteenth(Fraction(p)))
            if pat:                     # 16분 홀수 칸 노트뿐인 마디는 확인 전이라 기믹으로 세지 않음
                out[b] = pat
    return out


def closed_78(normal: Chart, c: Chart) -> set[int]:
    return {h.bar for h in diff_charts(normal, c).holds if h.kind == "end_closed_78"}


def score(normal: Chart, gen: Chart, user: Chart) -> dict:
    ga, ua = added(normal, gen), added(normal, user)
    tp_t = len(set(ga) & set(ua))
    tp_l = sum(1 for k in set(ga) & set(ua) if ga[k] & ua[k])
    gg, ug = gimmick_bars(normal, gen), gimmick_bars(normal, user)
    tp_g = len(set(gg) & set(ug))
    same_shape = sum(1 for b in set(gg) & set(ug) if gg[b] == ug[b])
    n_on = lambda c, bs: sum(len(v) for (b, _p), v in onset_cells(c).items() if b in bs)
    last = max(normal.bars)
    win_err = []
    for a in range(1, last + 1, 8):
        bs = set(range(a, a + 8))
        nn = n_on(normal, bs)
        if nn:
            win_err.append(abs(n_on(gen, bs) - n_on(user, bs)) / nn)
    c_g, c_u = closed_78(normal, gen), closed_78(normal, user)
    from .uturn import find_uturns
    uf = lambda c: sum(1 for u in find_uturns(c) if u.front)
    p, r, f = prf(tp_t, len(ga), len(ua))
    pg, rg, fg = prf(tp_g, len(gg), len(ug))
    return {
        "onsets": {"normal": sum(len(v) for v in onset_cells(normal).values()),
                   "generated": sum(len(v) for v in onset_cells(gen).values()),
                   "user": sum(len(v) for v in onset_cells(user).values())},
        "added": {"generated": len(ga), "user": len(ua), "precision": round(p, 3), "recall": round(r, 3), "f1": round(f, 3),
                  "lane_match_of_hits": round(tp_l / tp_t, 3) if tp_t else 0.0},
        "gimmick": {"generated": len(gg), "user": len(ug), "precision": round(pg, 3), "recall": round(rg, 3), "f1": round(fg, 3),
                    "same_shape_of_hits": round(same_shape / tp_g, 3) if tp_g else 0.0},
        "density_window_mae": round(sum(win_err) / len(win_err), 3) if win_err else 0.0,
        "hold_close_78": {"generated": len(c_g), "user": len(c_u), "agree": len(c_g & c_u)},
        "uturn_front": {"generated": uf(gen), "user": uf(user)},
    }


def _h(*xs) -> int:
    return int(hashlib.sha1(repr(xs).encode()).hexdigest()[:8], 16)


def random_fill(normal: Chart, gen: Chart, porter_percentile) -> Chart:
    """생성 결과와 마디별 추가 수가 같도록, 소리 있는(≥P50) 빈 8분 칸을 결정적 무작위로 채운 기준선."""
    out = normal.copy()
    per_bar: dict[int, int] = {}
    for (b, _p) in added(normal, gen):
        per_bar[b] = per_bar.get(b, 0) + 1
    for b, k in per_bar.items():
        bar = out.bars[b]
        g, d = expected_main(b)
        if bar.dirs[g] is None:
            bar.dirs[g] = d
        used = {p for l in LANES for p, c in bar.cells[l].items() if c in ONSET}      # 생성기와 같은 점유 기준
        cells = [Fraction(i, 8) for i in range(8) if Fraction(i, 8) not in used and porter_percentile(b, Fraction(i, 8)) >= 50]
        cells.sort(key=lambda p: _h(b, p))
        placed = 0
        for p in cells:
            if placed >= k:
                break
            lanes = [l for l in lanes_of(g) if not any(st <= p <= (e[0] if e else 1) for st, e in bar.holds(l))]
            if not lanes:
                continue
            bar.cells[lanes[_h(b, p, 1) % len(lanes)]][p] = "1"
            placed += 1
    return out
