"""노멀 덜어내기(이지): 부분집합, 간격 규칙, 빈 마디 없음, 반복 마디 일관성, 홀드 정리, 로그, 실제 곡 결과."""
from fractions import Fraction

import pytest

from chartporter.chart_io import read_chart, to_bytes
from chartporter.model import expected_main, group_of
from chartporter.paths import charts_dir
from chartporter.thin import ThinParams, onset_times, thin
from chartporter.validate import validate

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0).replace("Hard", "Normal") + "#000:13:0000;\n#000:14:0000;\n"


def make(lines: str):
    return chart_from_text(BASE + lines)[0]


def onsets_with_lane(c):
    return {(b, p, l) for b, bar in c.bars.items() for p, l, ch in bar.notes() if ch in "12"}


# ---------------- 합성 채보 ----------------

def test_never_adds_notes_and_keeps_lanes():
    n = make("#001:01:10101111;\n#001:02:01010000;\n#002:13:10001000;\n#002:14:00100010;")
    e = thin(n, 120).chart
    assert onsets_with_lane(e) <= onsets_with_lane(n)


def test_gap_rule_depends_on_bpm():
    # 8분 쌍 (0, 1/8): 155 BPM 에서는 8분 = 0.194초 → 남고, 195 BPM 에서는 0.154초 → 하나 빠짐
    n = make("#001:01:11000000;\n#001:02:00001000;\n#002:13:10000000;\n#002:14:00000000;")
    slow = thin(n, 155).chart
    fast = thin(n, 195).chart
    assert Fraction(1, 8) in slow.bars[1].cells[1]
    assert Fraction(1, 8) not in fast.bars[1].cells[1] and Fraction(0) in fast.bars[1].cells[1]
    gaps = sorted(onset_times(fast))
    assert all(float(b - a) * 240 / 195 >= 0.19 for a, b in zip(gaps, gaps[1:]))


def test_odd_sixteenths_and_chords_removed():
    n = make("#001:01:1000000000100000;\n#001:02:1000000000000000;\n#002:13:10000000;\n#002:14:00000000;")
    e = thin(n, 120).chart
    times = [(p, l) for p, l, c in e.bars[1].notes()]
    assert len([t for t in times if t[0] == 0]) == 1                  # 동시치기 → 하나
    assert all((p * 8).denominator == 1 for p, _l in times)          # 16분 엇박 없음


def test_no_empty_bar_and_density_cap():
    # 노멀 8분 꽉 찬 마디(8개) → 상한 3.33/마디로 줄되 빈 마디는 없음
    n = make("#001:01:10101010;\n#001:02:01010101;\n#002:13:10000000;\n#002:14:00000000;")
    e = thin(n, 100, ThinParams(target_per_bar=2.0)).chart
    assert all(e.bars[b].has_notes() for b in (1, 2))
    assert len(onset_times(e)) <= 4


def test_identical_normal_bars_give_identical_easy_bars():
    row = "#{b:03d}:01:10111010;\n#{b:03d}:02:01000101;\n#{b1:03d}:13:10100000;\n#{b1:03d}:14:00001010;\n"
    n = make("".join(row.format(b=b, b1=b + 1) for b in (1, 3, 5, 7)))
    e = thin(n, 150, ThinParams(target_per_bar=2.5)).chart
    assert e.bars[1].cells == e.bars[3].cells == e.bars[5].cells == e.bars[7].cells
    assert e.bars[2].cells == e.bars[4].cells == e.bars[6].cells


def test_open_hold_closed_and_inner_note_shortens_hold():
    # 레인1 홀드 0→끝 없음 (게임 버그) → 7/8 에서 닫힘. 레인2 3/4 탭이 홀드 안 → 홀드를 5/8 에서 끝냄
    n = make("#001:01:20000000;\n#001:02:00000010;\n#002:13:10000000;\n#002:14:00000000;")
    res = thin(n, 120)
    cells = res.chart.bars[1].cells[1]
    assert cells.get(Fraction(0)) == "2" and cells.get(Fraction(5, 8)) == "4"
    assert not [i for i in validate(res.chart, bpm=120) if i.severity in ("FATAL", "ERROR")]
    assert {e.origin for e in res.log} >= {"HOLD_END_78", "HOLD_SHORTEN"}


def test_release_end_becomes_hold_end_and_main_line_kept():
    n = make("#001:01:20005000;\n#001:02:00000000;\n#002:13:00000000;\n#002:14:10000000;")
    e = thin(n, 120).chart
    assert e.bars[1].cells[1][Fraction(1, 2)] == "4"
    for b, bar in e.bars.items():
        if b > 0:
            assert bar.groups_used() == [expected_main(b)[0]]
            assert all(group_of(l) == expected_main(b)[0] for _p, l, _c in bar.notes())


def test_every_removed_onset_is_logged_and_output_deterministic():
    n = make("#001:01:11101011;\n#001:02:00010100;\n#002:13:11000110;\n#002:14:00111001;")
    a, b = thin(n, 180), thin(n, 180)
    assert to_bytes(a.chart, True) == to_bytes(b.chart, True)
    gone = onsets_with_lane(n) - onsets_with_lane(a.chart)
    logged = {(e.bar, e.pos, e.lane) for e in a.log}
    assert gone <= logged


# ---------------- 실제 곡 ----------------

def _pair(sid):
    n_p, e_p = charts_dir() / f"Chart_{sid:04d}_Normal.txt", charts_dir() / f"Chart_{sid:04d}_Easy.txt"
    if not n_p.exists():
        pytest.skip(f"{sid}번 노멀 없음")
    return read_chart(n_p)[0], (read_chart(e_p)[0] if e_p.exists() else None)


def test_song2_matches_user_easy():
    normal, easy = _pair(2)
    gen, ref = onset_times(thin(normal, 155).chart), onset_times(easy)
    tp = len(gen & ref)
    assert 2 * tp / (len(gen) + len(ref)) >= 0.83
    assert 3.0 <= len(gen) / 66 <= 3.6


def test_song1_easy_is_valid_subset():
    normal, _ = _pair(1)
    res = thin(normal, 195)
    e = res.chart
    assert not [i for i in validate(e, bpm=195) if i.severity in ("FATAL", "ERROR", "WARN")]
    assert onsets_with_lane(e) <= onsets_with_lane(normal)
    ts = sorted(onset_times(e))
    assert all(float(b - a) * 240 / 195 >= 0.19 - 1e-9 for a, b in zip(ts, ts[1:]))
    assert all(e.bars[b].has_notes() for b in range(1, normal.last_bar() + 1) if normal.bars[b].has_notes())
    assert 260 <= len(ts) <= 330


def test_short_hold_becomes_tap_only_when_fast():
    # 1/8 마디 홀드: 155 BPM = 0.194초 → 홀드 유지, 195 BPM = 0.154초 → 탭
    n = make("#001:01:24000000;\n#001:02:00001000;\n#002:13:10000000;\n#002:14:00000000;")
    assert thin(n, 155).chart.bars[1].cells[1] == {Fraction(0): "2", Fraction(1, 8): "4"}
    fast = thin(n, 195)
    assert fast.chart.bars[1].cells[1] == {Fraction(0): "1"}
    assert [e for e in fast.log if e.origin == "HOLD_TO_TAP" and "짧은 홀드" in e.reason]
