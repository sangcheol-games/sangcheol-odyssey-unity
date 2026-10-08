"""노멀↔하드 비교와 기조 통계. 2번(사용자 노멀/하드)을 고정한 사본으로, Phase 1 손 분석 수치와 맞는지 확인한다."""
from collections import Counter
from fractions import Fraction
from pathlib import Path

import pytest

from chartporter import stats as stats_mod
from chartporter.chart_io import read_chart
from chartporter.config import SongConfig
from chartporter.diff import classify_added, diff_charts, gimmick_info

from conftest import HEADER, chart_from_text

FIX = Path(__file__).parent / "fixtures"


@pytest.fixture(scope="module")
def song2():
    n, _ = read_chart(FIX / "ref_0002_Normal.txt")
    h, _ = read_chart(FIX / "ref_0002_Hard.txt")
    return n, h, diff_charts(n, h)


def test_normal_fate_matches_hand_analysis(song2):
    _n, _h, d = song2
    c = Counter(m.kind for bd in d.bars.values() for m in bd.matches)
    assert c["kept"] + c["type_change"] == 260
    assert c["relane_cross"] == 21 and c["relane_same"] == 20
    assert c["shift"] + c["deleted"] == 23


def test_odd16_notes_are_flagged_not_dropped(song2):
    _n, _h, d = song2
    assert len(d.excluded) == 22
    assert all(m.excluded for bd in d.bars.values() for m in bd.matches if m.kind == "shift")


def test_gimmick_bars_and_templates(song2):
    _n, h, d = song2
    g = {b: bd.gimmick for b, bd in d.bars.items() if bd.gimmick.get("extra_group") is not None}
    assert len(g) == 29
    assert Counter(x["extra_dir"] for x in g.values()) == {"flipped": 19, "own": 10}
    t1 = (("1/2", 2, "1"),)
    assert sum(1 for b, x in g.items() if b % 2 == 0 and x["extra_pattern"] == t1) == 9
    assert gimmick_info(h.bars[4], 4)["extra_dir"] == "own"


def test_open_holds_closed_at_seven_eighths(song2):
    _n, _h, d = song2
    closed = sorted({x.bar for x in d.holds if x.kind == "end_closed_78"})
    assert closed == [3, 7, 19, 23, 29, 35, 39, 43, 47, 50, 55, 59, 63]


def test_added_notes_mostly_one_eighth_from_normal(song2):
    n, _h, d = song2
    added = classify_added(d, n)
    assert len(added) == 47
    assert sum(1 for _x, _k, dist in added if dist == Fraction(1, 8)) == 41


def test_matcher_classifies_simple_cases():
    base = HEADER.format(n=0) + "#000:13:0000;\n#000:14:0000;\n"
    n, _ = chart_from_text(base + "#001:01:10100000;\n#001:02:00001010;")
    h, _ = chart_from_text(base + "#001:01:10000000;\n#001:02:00101010;\n#001:13:00000000;\n#001:14:00000001;")
    d = diff_charts(n, h)
    kinds = sorted(m.kind for m in d.bars[1].matches)
    # L1@0 유지, L1@2/8 → L2@2/8 (같은 그룹 교체), L2@4/8·6/8 유지, 레인4 RTL '00000001' = 판정 0 → 새로 추가
    assert kinds.count("kept") == 3 and kinds.count("relane_same") == 1 and kinds.count("added") == 1


def test_song_stats_profile_shape(monkeypatch):
    cfg = SongConfig(id=2, title="Una Alarm", base="x", target="y")
    monkeypatch.setattr(SongConfig, "base_path", lambda self: FIX / "ref_0002_Normal.txt")
    monkeypatch.setattr(SongConfig, "target_path", lambda self: FIX / "ref_0002_Hard.txt")
    s = stats_mod.song_stats(cfg, 155)
    assert s["gimmick"]["bar_rate"] == pytest.approx(25 / 66, abs=1e-3)   # 16분 홀수 칸 노트뿐인 041·050–052 제외
    assert s["holds"]["open_closed_at_7_8"] == 13
    assert s["spacing"]["same_lane_gaps_below_8th"] == 0
    assert s["excluded_odd16_notes"] == 22
    md = stats_mod.report_markdown({"primary": "2", "songs": {"2": s}})
    assert "하드 기조 통계" in md and "×" in md
