from pathlib import Path

import pytest

from chartporter.chart_io import read_chart
from chartporter.paths import charts_dir
from chartporter.validate import simulate_bar_progress, validate

from conftest import HEADER, chart_from_text

BAR0 = "#000:13:0000;\n#000:14:0000;\n"
FIXTURES = Path(__file__).parent / "fixtures"


def codes(text, path=None, bpm=None):
    chart, raw = chart_from_text(text, path)
    return [(i.code, i.bar, i.lane) for i in validate(chart, raw, bpm=bpm)]


def has(cs, code, bar=None, lane=None):
    return any(c == code and (bar is None or b == bar) and (lane is None or l == lane) for c, b, l in cs)


def count(cs, code, bar=None, lane=None):
    return sum(1 for c, b, l in cs if c == code and (bar is None or b == bar) and (lane is None or l == lane))


def test_clean_chart_has_no_errors():
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:1000;\n#001:02:0010;\n#002:13:0000;\n#002:14:0000;")
    assert not [c for c in cs if c[0][0] in "FE"]


# ---- 마디 진행 (ChartManager.PrepareNextBar/StartCurrentBar 시뮬레이션) ----

def test_simulate_bar_progress():
    assert simulate_bar_progress([0, 0, 1, 1, 2]) == (None, [])
    assert simulate_bar_progress([0, 1, 3]) == (2, [])          # 2마디 빠짐 → 2에서 멈춤
    assert simulate_bar_progress([1, 2]) == (0, [])             # 000 없음 → 시작부터 멈춤
    assert simulate_bar_progress([0, 1, 2, 1]) == (None, [(1, 2)])  # 001 줄이 2마디 차례에 늦게 읽힘


def test_gap_bar_is_fatal():
    cs = codes(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;\n#003:01:0000;\n#003:02:0000;")
    assert has(cs, "F01", 2)


def test_missing_bar_zero_is_fatal():
    assert has(codes(HEADER.format(n=1) + "#001:01:1000;\n#001:02:0000;"), "F01", 0)


def test_swapped_bar_order_stalls_progress():
    cs = codes(HEADER.format(n=0) + BAR0 + "#002:13:0000;\n#002:14:0000;\n#001:01:0000;\n#001:02:0000;")
    assert has(cs, "F01", 1)


def test_late_line_is_fatal_f02():
    cs = codes(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#002:13:0000;\n#002:14:0000;\n#001:02:0000;")
    assert has(cs, "F02", 1) and not has(cs, "F01")


def test_empty_sequence_lines_still_count_as_bar():
    """'#002:13:;' 도 게임에서는 LaneData 가 생겨 마디가 진행된다."""
    cs = codes(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;\n#002:13:;\n#002:14:;\n#003:01:0000;\n#003:02:0000;")
    assert not has(cs, "F01")


# ---- 줄 구조 ----

def test_duplicate_lane_line_and_mixed_direction():
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:1000;\n#001:11:0010;\n#001:02:0000;")
    assert has(cs, "E01", 1, 1)
    assert has(cs, "E02", 1)


def test_mixed_lengths_in_bar():
    assert has(codes(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:00000000;"), "E07", 1)


def test_lines_shorter_than_four():
    """에디터는 마디를 4분할로 만들고 짧은 줄을 앞칸에 복사한다 → 타이밍이 바뀜."""
    assert has(codes(HEADER.format(n=1) + BAR0 + "#001:01:01;\n#001:02:00;"), "E07", 1)


@pytest.mark.parametrize("line", ["#001:01:1000", "#001:01:1000; ", "#001:01:1000; // kick", "#001:01:1000 ;x"])
def test_data_line_dropped_by_game_is_error(line):
    cs = codes(HEADER.format(n=0) + BAR0 + line + "\n#001:02:0000;")
    assert has(cs, "E10")


def test_dotnet_int_rejects_python_only_forms():
    cs = codes(HEADER.format(n=0) + BAR0 + "#0_01:01:1000;\n#001:01:0000;\n#001:02:0000;")
    assert has(cs, "E10")   # 0_01 은 int.Parse 실패 → 게임이 버리는 줄


# ---- 홀드 ----

def test_orphan_hold_end_four_is_error():
    assert has(codes(HEADER.format(n=1) + BAR0 + "#001:01:00000004;\n#001:02:00000000;"), "E04", 1, 1)


def test_orphan_release_five_is_only_warning():
    """누를 때마다 isHolding 이 켜지므로 짝 없는 5 도 누르고 떼면 판정된다 (ChartManager.TryJudgeInput)."""
    cs = codes(HEADER.format(n=1) + BAR0 + "#001:01:0050;\n#001:02:0000;")
    assert has(cs, "W12", 1, 1) and not has(cs, "E04")


def test_tap_inside_hold():
    cs = codes(HEADER.format(n=3) + BAR0 + "#001:01:00000000;\n#001:02:21000004;")
    assert count(cs, "E03", 1, 2) == 1


def test_nested_hold():
    cs = codes(HEADER.format(n=3) + BAR0 + "#001:01:00000000;\n#001:02:20002004;")
    assert count(cs, "E03", 1, 2) == 1


def test_hold_across_bar_is_error():
    """시작 마디에 4/5 없이 다음 마디로 넘어가는 홀드는 게임 버그 (E14)."""
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:0002;\n#001:02:0000;\n#002:13:0000;\n#002:14:0000;\n#003:01:0500;\n#003:02:0000;")
    assert has(cs, "E14", 1, 1)
    assert not has(cs, "E04") and not has(cs, "E13")


def test_tap_inside_cross_bar_hold_after_open_hold():
    # 열린 홀드 뒤 다음 마디의 탭: 홀드 막대는 마디 끝에서 끝나므로 '끝 없는 홀드'로 본다
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:0002;\n#001:02:0000;\n#002:13:0000;\n#002:14:0000;\n#003:01:1000;\n#003:02:0000;")
    assert has(cs, "E13", 1, 1) and not has(cs, "E03")


def test_open_hold_is_error():
    assert has(codes(HEADER.format(n=1) + BAR0 + "#001:01:0020;\n#001:02:0000;"), "E13", 1, 1)


# ---- 간격 ----

def test_same_lane_spacing_within_bar():
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:1100000000000000;\n#001:02:0000000000000000;", bpm=120)
    assert has(cs, "W01", 1, 1)


def test_same_lane_spacing_across_bars():
    # 120 BPM: 16분 = 125ms. 001 마지막 칸 → 002 첫 칸 (같은 레인 1)
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:0000000000000001;\n#001:02:0000000000000000;\n#002:01:1000;\n#002:02:0000;", bpm=120)
    assert has(cs, "W01", 2, 1)
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:0000000000000001;\n#001:02:0000000000000000;"
               "#002:13:0000;\n#002:14:0000;\n#003:01:1000;\n#003:02:0000;", bpm=120)
    assert not has(cs, "W01")


def test_hold_end_then_quick_note():
    cs = codes(HEADER.format(n=3) + BAR0 + "#001:01:2410000000000000;\n#001:02:0000000000000000;", bpm=120)
    assert has(cs, "W02", 1, 1)


# ---- 헤더 ----

def test_legacy_three_and_unknown_char():
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:2340;\n#001:02:0000;")
    assert has(cs, "E05", 1, 1)


def test_notes_mismatch_uses_judged_count():
    # 알 수 없는 문자 '6' 은 게임이 판정하지 않음 → #NOTES 1 이 맞다
    cs = codes(HEADER.format(n=1) + BAR0 + "#001:01:1060;\n#001:02:0000;")
    assert count(cs, "E06") == 1          # 에디터가 2 로 저장할 것이라는 경고만
    cs = codes(HEADER.format(n=2) + BAR0 + "#001:01:1060;\n#001:02:0000;")
    assert count(cs, "E06") == 2


def test_invalid_notes_header_keeps_earlier_value():
    text = HEADER.format(n=1) + "#NOTES abc\n" + BAR0 + "#001:01:1000;\n#001:02:0000;"
    assert not has(codes(text), "E06")


def test_header_difficulty_vs_filename():
    text = HEADER.replace("Hard", "Easy").format(n=0) + BAR0
    assert has(codes(text, path="Chart_0002_Hard.txt"), "W04")
    text = HEADER.replace("Hard", "2").format(n=0) + BAR0   # Enum.TryParse 숫자 → Hard
    assert not has(codes(text, path="Chart_0002_Hard.txt"), "W04")


# ---- 실제 채보에서 나온 문제 (수정 전 원본을 tests/fixtures 에 고정) ----

def _fixture(name):
    chart, raw = read_chart(FIXTURES / name)
    return [(i.code, i.bar, i.lane) for i in validate(chart, raw)]


def test_fixture_0006_gap_bars_stall_at_102():
    for name in ("gap_and_orphan_0006_Easy.txt", "gap_0006_Normal.txt"):
        assert has(_fixture(name), "F01", 102)


def test_fixture_0006_easy_orphan_end():
    assert has(_fixture("gap_and_orphan_0006_Easy.txt"), "E04", 60, 4)


def test_fixture_0006_normal_header():
    assert has(_fixture("gap_0006_Normal.txt"), "W04")


@pytest.mark.parametrize("name", [p.name for p in sorted(charts_dir().glob("Chart_000[2-5]_*.txt"))])
def test_real_charts_without_fatal_or_error(name):
    """끝 없는 홀드(E13/E14)는 fmt --close-holds 로 정리하는 중이라 여기서는 빼고 본다."""
    chart, raw = read_chart(charts_dir() / name)
    assert not [i for i in validate(chart, raw) if i.severity in ("FATAL", "ERROR") and i.code not in ("E13", "E14")]
