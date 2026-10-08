from fractions import Fraction

import pytest

from chartporter.chart_io import (
    ChartWriteError, dotnet_int, format_bar, parse_difficulty, parse_raw, read_chart, to_bytes, to_chart, to_text,
)
from chartporter.model import LTR, RTL, Bar, Chart
from chartporter.paths import charts_dir

from conftest import HEADER, chart_from_text

REAL_CHARTS = sorted(charts_dir().glob("Chart_*.txt"))
BAR0 = "#000:13:0000;\n#000:14:0000;\n"


def _lf(b: bytes) -> bytes:
    return b.replace(b"\r\n", b"\n")


@pytest.mark.parametrize("path", REAL_CHARTS, ids=lambda p: p.name)
def test_editor_round_trip(path):
    """에디터가 저장한 실제 채보는 읽고 다시 쓰면 같아야 한다 (줄바꿈은 git autocrlf 설정에 따라 다를 수 있어 정규화)."""
    chart, _ = read_chart(path)
    assert _lf(to_bytes(chart)) == _lf(path.read_bytes())


def test_writer_always_uses_crlf_without_bom():
    chart, _ = chart_from_text(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;")
    data = to_bytes(chart)
    assert data.endswith(b";\r\n") and b"\n" not in data.replace(b"\r\n", b"")
    assert not data.startswith(b"\xef\xbb\xbf")


def test_rtl_positions_are_judgment_order():
    chart, _ = chart_from_text(HEADER.format(n=2) + BAR0 + "#001:13:00000001;\n#001:14:10000000;")
    bar = chart.bars[1]
    assert bar.dirs == {0: None, 1: RTL}
    # RTL: 화면 맨 오른쪽(마지막 문자)이 판정 0, 맨 왼쪽이 마지막 판정
    assert bar.cells[3] == {Fraction(0): "1"}
    assert bar.cells[4] == {Fraction(7, 8): "1"}


def test_rtl_hold_pairs_in_judgment_order():
    # 화면 순서 '00400200' 은 판정 순서 '00200400' → 2 가 먼저, 4 가 뒤
    chart, _ = chart_from_text(HEADER.format(n=2) + BAR0 + "#001:13:00400200;\n#001:14:00000000;")
    assert chart.bars[1].holds(3) == [(Fraction(2, 8), (Fraction(5, 8), "4"))]


def test_writer_uses_lcm_length():
    chart = Chart()
    chart.bars[0] = Bar(number=0, dirs={0: None, 1: RTL})
    b = Bar(number=1, length=4)
    b.dirs[0] = LTR
    b.cells[1][Fraction(1, 16)] = "1"
    b.cells[2][Fraction(1, 24)] = "1"
    chart.bars[1] = b
    lines = to_text(chart).split("\r\n")
    seq1 = [l for l in lines if l.startswith("#001:01:")][0].split(":")[2].rstrip(";")
    seq2 = [l for l in lines if l.startswith("#001:02:")][0].split(":")[2].rstrip(";")
    assert len(seq1) == len(seq2) == 48
    assert seq1.index("1") == 3 and seq2.index("1") == 2


def test_writer_rejects_length_over_128():
    chart = Chart()
    chart.bars[0] = Bar(number=0, dirs={0: None, 1: RTL})
    b = Bar(number=1, length=4)
    b.dirs[0] = LTR
    b.cells[1][Fraction(1, 128)] = "1"
    b.cells[2][Fraction(1, 3)] = "1"
    chart.bars[1] = b
    with pytest.raises(ChartWriteError):
        to_text(chart)


def test_writer_expands_lines_shorter_than_four():
    chart, _ = chart_from_text(HEADER.format(n=2) + BAR0 + "#001:01:01;\n#001:02:00;\n#002:13:10;\n#002:14:00;")
    text = to_text(chart)
    assert "#001:01:0010;" in text      # 1/2 지점 유지 (에디터처럼 앞칸 복사가 아님)
    assert "#002:13:0100;" in text      # RTL '10' = 판정 1/2 → 4분할 화면 순서 0100 (에디터는 1000 = 3/4 로 바꿔 버림)


def test_fill_gaps_follows_alternation():
    chart, _ = chart_from_text(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;\n#004:13:0000;\n#004:14:0000;")
    text = to_text(chart, fill_gaps=True)
    assert "#002:13:0000;" in text and "#002:14:0000;" in text
    assert "#003:01:0000;" in text and "#003:02:0000;" in text
    assert "#002:0" not in text


def test_empty_sequence_line_keeps_bar_and_direction():
    chart, _ = chart_from_text(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;\n#002:01:;\n#002:02:;")
    assert 2 in chart.bars and chart.bars[2].dirs[0] == LTR
    assert "#002:01:0000;" in to_text(chart)


def test_notes_header_is_recounted():
    chart, _ = chart_from_text(HEADER.format(n=99) + BAR0 + "#001:01:1200;\n#001:02:0004;")
    assert "#NOTES 3\r\n" in to_text(chart)


def test_invalid_notes_keeps_earlier_value():
    chart, _ = chart_from_text(HEADER.format(n=5) + "#NOTES abc\n" + BAR0)
    assert chart.header.notes == 5


def test_trailing_data_with_whitespace_is_reported_as_ignored():
    chart, raw = chart_from_text(HEADER.format(n=1) + BAR0 + "#001:01:1000; ")
    assert 1 not in chart.bars
    assert any("무시" in reason for _, _, reason in raw.ignored)


def test_out_of_range_lane_refuses_to_write():
    chart, _ = chart_from_text(HEADER.format(n=1) + BAR0 + "#001:01:1000;\n#001:02:0000;\n#001:15:00000000;")
    with pytest.raises(ChartWriteError):
        to_text(chart)


def test_utf16_and_invalid_utf8_are_decoded():
    text = (HEADER.format(n=0) + BAR0).replace("\n", "\r\n")
    raw = parse_raw(b"\xff\xfe" + text.encode("utf-16-le"))
    assert raw.bom == "utf-16-le" and to_chart(raw).header.title == "T"
    raw = parse_raw(text.replace("#TITLE T", "#TITLE \udcb0").encode("utf-8", "surrogateescape"))
    assert raw.decode_errors


def test_dotnet_int_and_difficulty_parsing():
    assert dotnet_int(" 001 ") == 1 and dotnet_int("-3") == -3
    assert dotnet_int("0_01") is None and dotnet_int("１") is None and dotnet_int("99999999999") is None
    assert parse_difficulty("hard") == "Hard" and parse_difficulty("2") == "Hard"
    assert parse_difficulty("Hard, Easy") == "Hard" and parse_difficulty("Normal, Hard") == "Extreme"
    assert parse_difficulty("Expert") is None


def test_negative_bar_format_matches_csharp_d3():
    assert format_bar(-1) == "-001" and format_bar(7) == "007" and format_bar(1234) == "1234"
