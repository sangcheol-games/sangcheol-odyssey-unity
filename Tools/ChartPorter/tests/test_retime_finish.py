"""1번 하드 마무리: 리타이밍 규칙(골든 546/554)과 mode finish (손작업 마디 그대로)."""
import subprocess
from fractions import Fraction

import pytest

from chartporter.chart_io import parse_raw, read_chart, to_chart
from chartporter.cli import UsageError, _make_porter, _song_audio, load_finish_source
from chartporter.config import load_song
from chartporter.model import LANES
from chartporter.paths import charts_dir, repo_root
from chartporter.retime import compare, shift_chart
from chartporter.validate import validate

from conftest import HEADER, chart_from_text


def _git(spec):
    try:
        return subprocess.run(["git", "-C", str(repo_root()), "show", spec], capture_output=True, check=True).stdout
    except (subprocess.CalledProcessError, FileNotFoundError):
        pytest.skip("git 기록 없음")


def test_shift_carries_to_previous_bar_cross_lane_and_keeps_hold_at_zero():
    c, _ = chart_from_text(HEADER.format(n=0) + "#000:13:0000;\n#000:14:0000;\n#001:01:10000000;\n#001:02:00000000;"
                           "\n#002:13:00000000;\n#002:14:00000000;\n#003:01:20400001;\n#003:02:10000000;")
    out, carried = shift_chart(c, Fraction(-1, 8))
    assert out.bars[3].cells[1][Fraction(0)] == "2"            # 0 에서 시작하는 홀드는 그대로
    assert out.bars[3].cells[1][Fraction(1, 8)] == "4"         # 끝은 당겨짐
    assert out.bars[2].cells[4][Fraction(7, 8)] == "1"         # 3마디 레인2 의 0 → 2마디 레인4 의 7/8
    assert (3, 2, Fraction(0), 2, 4, Fraction(7, 8), "1") in carried
    assert out.bars[1].cells[1] == {} and 0 not in out.bars or not out.bars[0].has_notes()   # 1마디 0 은 000마디로 가지 않음


def test_retime_rule_reproduces_users_normal_retiming():
    old = to_chart(parse_raw(_git("1886759^:Assets/Charts/Chart_0001_Normal.txt")))
    cur, _ = read_chart(charts_dir() / "Chart_0001_Normal.txt")
    same, total, diff = compare(shift_chart(old, Fraction(-1, 8))[0], cur)
    assert same >= 546 and total == cur.count_notes()
    assert {b for b, *_ in diff} <= {2, 5, 20, 68, 69, 90, 93}      # 나머지는 사용자가 직접 고친 마디


def test_finish_mode_keeps_handmade_bars():
    try:
        cfg, so, audio, bpm = _song_audio(1)
    except UsageError:
        pytest.skip("1번 음원 없음")
    _git("1886759:Assets/Charts/Chart_0001_Hard.txt")
    normal, _ = read_chart(cfg.base_path())
    src, _ = load_finish_source(cfg)
    from chartporter.holds import close_unterminated_holds
    close_unterminated_holds(src, set(cfg.frozen_bars))      # 손작업 마디의 끝 없는 홀드도 닫는다 (게임 버그)
    res = _make_porter(cfg, so, audio, bpm, normal).run()
    cells = lambda bar: {(l, p, c) for l in LANES for p, c in bar.cells[l].items()}
    for b in cfg.frozen_bars:
        assert cells(res.chart.bars[b]) == cells(src.bars[b])
    bad = [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    # 유턴 앞쪽 노트(W15)는 손작업 마디가 원인일 때만 남는다 (사용자가 직접 고칠 목록)
    assert all(i.code == "W15" and (i.bar in cfg.frozen_bars or i.bar - 1 in cfg.frozen_bars) for i in bad)
