"""M5 리뷰에서 확인된 문제들의 회귀 테스트 (음원 없이 합성 채보로)."""
from collections import Counter
from fractions import Fraction

import pytest

from chartporter.config import SongConfig
from chartporter.evaluate import added, random_fill
from chartporter.port import Porter, PortParams
from chartporter.sync import RuleError, SyncRule, ordered_pairs, plan_sync
from chartporter.validate import validate

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0) + "#000:13:0000;\n#000:14:0000;\n"


def chart(rows: dict[int, tuple[str, str]], bpm: int = 160):
    lines = []
    for b in sorted(rows):
        a, c = rows[b]
        if b % 2:
            lines += [f"#{b:03d}:01:{a};", f"#{b:03d}:02:{c};"]
        else:
            lines += [f"#{b:03d}:13:{a};", f"#{b:03d}:14:{c};"]
    c, _ = chart_from_text(BASE.replace("#BPM 120", f"#BPM {bpm}") + "\n".join(lines))
    return c


def porter(normal, bpm=160, **cfg):
    params = PortParams(gimmick_rate=0.0)
    return Porter(normal, None, bpm, 0.0, SongConfig(id=9999, **cfg), params)


def errors(c, bpm=160):
    return [i for i in validate(c, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]


def test_cross_bar_hold_is_closed_in_its_own_bar():
    # 1마디 레인1 3/4 에서 시작해 3마디 1/8 에서 끝나는 홀드 (마디를 넘는 홀드 = 게임 버그)
    n = chart({1: ("00000020", "00000000"), 2: ("0000", "0000"), 3: ("04000000", "00000000")})
    assert [i for i in validate(n, bpm=160) if i.code == "E14"]
    p = porter(n)
    p.budget_override = {1: 2, 2: 0, 3: 3}
    res = p.run()
    assert res.chart.bars[1].cells[1][Fraction(7, 8)] == "4"
    assert Fraction(1, 8) not in res.chart.bars[3].cells[1]                  # 넘어가던 끝은 지움
    assert not [i for i in errors(res.chart) if i.code in ("E04", "E03", "E13", "E14")]


def test_hold_to_tap_respects_spacing():
    # 195 BPM: 15/16 시작 홀드를 탭으로 바꾸면 다음 마디 0 의 같은 레인 탭과 77ms → 바꾸지 않음
    n = chart({1: ("0000000000000002", "0000000000000000"), 2: ("0000", "0000"), 3: ("1000", "0000")}, bpm=195)
    lines = BASE.replace("#BPM 120", "#BPM 195") + "#001:01:0000000000000002;\n#001:02:0000000000000000;\n#002:01:1000;\n#002:02:0000;"
    n, _ = chart_from_text(lines)
    res = porter(n, bpm=195).run()
    assert not [e for e in res.log if e.origin == "HOLD_TO_TAP"]
    assert [e for e in res.log if e.origin == "HOLD_DROPPED"]               # 탭으로 두면 77ms → 지움
    assert not [i for i in errors(res.chart, 195) if i.code in ("W01", "E13", "E14")]


def test_budget_cap_spills_to_other_bars():
    n = chart({b: ("1010", "0101") for b in (1, 3, 5)} | {b: ("1010", "0101") for b in (2, 4, 6)})
    p = porter(n)
    p.p = PortParams(ratio_by_len={4: 1.5}, gimmick_rate=0.0, max_add_per_bar=3)
    p.dense_cells = lambda b: 8 if b == 1 else 0          # 1마디에 몰리게
    bud = p.budgets([1, 2, 3, 4, 5, 6])
    assert sum(bud.values()) == 12 and max(bud.values()) <= 3


def test_repair_leaves_normal_only_conflicts_and_keeps_far_notes():
    # 노멀 자체에 16분 간격 탭(W01) — 생성 노트와 무관하면 그대로 둔다
    n = chart({1: ("1100000000000000", "0000000000000000"), 2: ("0000", "0000")}, bpm=200)
    p = porter(n, bpm=200)
    p.budget_override = {1: 1, 2: 0}
    res = p.run()
    assert not [e for e in res.log if e.origin == "REMOVED"]


def test_keep_normal_bars_are_not_overwritten_by_copy():
    n = chart({1: ("10000000", "00100000"), 2: ("0000", "0000"), 3: ("10000000", "00000000")})
    res = porter(n, keep_normal_bars={3}, sections=[{"bars": "3", "copy_of": "1", "transform": "same"}]).run()
    assert res.chart.bars[3].cells == n.bars[3].cells


def test_backward_copy_chain_uses_final_source():
    r = [SyncRule([10], [20]), SyncRule([20], [30])]
    order = [t for t, _s, _r in ordered_pairs(r)]
    assert order.index(20) < order.index(10)
    with pytest.raises(RuleError):
        ordered_pairs([SyncRule([5], [3]), SyncRule([5], [2])])
    with pytest.raises(RuleError):
        ordered_pairs([SyncRule([5], [6]), SyncRule([6], [5])])
    assert len(ordered_pairs([SyncRule([5], [3]), SyncRule([4, 5], [2, 3])])) == 2   # 같은 원본 중복은 허용


def test_copy_cells_are_logged_and_stale_entries_dropped():
    n = chart({1: ("00100000", "00000000"), 2: ("0000", "0000"), 3: ("00100000", "00000000")})
    p = porter(n, sections=[{"bars": "3", "copy_of": "1", "transform": "mirror"}])
    res = p.run()
    for (b, l, pos) in [(3, l, q) for l in (1, 2) for q in res.chart.bars[3].cells[l]]:
        if n.bars[3].cells[l].get(pos) != res.chart.bars[3].cells[l][pos]:
            assert any(e.bar == b and e.lane == l and e.pos == pos and e.origin.startswith("COPY") for e in res.log)
    assert not [e for e in res.log if e.bar == 3 and not e.origin.startswith("COPY")]


def test_random_baseline_matches_count():
    n = chart({1: ("10000004", "00200000"), 2: ("0000", "0000")})
    g = n.copy()
    g.bars[1].cells[1][Fraction(1, 8)] = "1"
    g.bars[1].cells[2][Fraction(5, 8)] = "1"
    r = random_fill(n, g, lambda b, p: 100.0)
    assert len(added(n, r)) == len(added(n, g)) == 2
