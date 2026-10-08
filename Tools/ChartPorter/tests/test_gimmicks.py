"""하드 기믹 형태(2번·3번 반반): 형태별 계획, 홀드 통째 옮기기, 메인 라인 바꾸기, 동시치기, 다양성, 실제 곡 불변 조건."""
from collections import Counter
from fractions import Fraction

import pytest

from chartporter.chart_io import read_chart, to_bytes
from chartporter.cli import UsageError, _make_porter, _song_audio
from chartporter.config import SongConfig
from chartporter.gimmicks import build_plans, main_notes
from chartporter.model import LTR, RTL
from chartporter.port import Porter, PortParams
from chartporter.uturn import find_uturns
from chartporter.validate import validate

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0).replace("Hard", "Normal") + "#000:13:0000;\n#000:14:0000;\n"


def make(lines: str):
    return chart_from_text(BASE + lines)[0]


def plans_for(c, b, level=3):
    bar = c.bars[b]
    g = 0 if b % 2 else 1
    ons = {q for q, _l, ch in bar.notes() if ch in "125"}
    return build_plans(bar, b, g, lambda q: 100.0, level, ons)


# 3마디(홀수, 메인 그룹0 LTR): 탭 0·1/8·1/4·1/2·5/8 + 레인1 홀드 3/4→7/8
ODD = ("#001:01:10000000;\n#001:02:00000000;\n#002:13:10000000;\n#002:14:00000000;\n"
       "#003:01:10100024;\n#003:02:01001100;\n#004:13:10000000;\n#004:14:00000000;")


def test_families_present_and_main_line_variants_need_level():
    c = make(ODD)
    p3 = plans_for(c, 3, level=3)
    assert {"RESPONSE", "CALL", "INTERLEAVE", "WHOLE", "REVERSE", "PAIR"} <= set(p3)
    p1 = plans_for(c, 3, level=1)
    assert not {"WHOLE", "REVERSE", "CHORD"} & set(p1)
    assert not [p for ps in p1.values() for p in ps if p.main_dir is not None]


def test_response_moves_tail_and_hold_as_unit():
    c = make(ODD)
    p = Porter(c, None, 150, 0.0, SongConfig(id=1), PortParams(gimmick_rate=0.0))
    plan = next(x for x in plans_for(c, 3)["RESPONSE"] if x.variant == "own" and x.ops[0].pos == Fraction(1, 2))
    assert p.apply_plan(3, plan)
    bar = p.hard.bars[3]
    assert bar.dirs[1] == RTL and bar.dirs[0] == LTR
    assert bar.cells[3].get(Fraction(3, 4)) == "2" and bar.cells[3].get(Fraction(7, 8)) == "4"   # 레인1 홀드 → 레인3 (같은 높이)
    assert Fraction(3, 4) not in bar.cells[1] and Fraction(7, 8) not in bar.cells[2]
    assert all(q < Fraction(1, 2) for l in (1, 2) for q in bar.cells[l])                          # 메인은 앞부분만
    p.hard.header.notes = p.hard.count_notes()
    assert not [i for i in validate(p.hard, bpm=150) if i.severity in ("FATAL", "ERROR")]


def test_whole_and_reverse_change_main_line():
    c = make(ODD)
    p = Porter(c, None, 150, 0.0, SongConfig(id=1), PortParams(gimmick_rate=0.0))
    assert p.apply_plan(3, plans_for(c, 3)["WHOLE"][0])
    assert p.hard.bars[3].dirs[0] is None and not any(p.hard.bars[3].cells[l] for l in (1, 2))
    q = Porter(c, None, 150, 0.0, SongConfig(id=1), PortParams(gimmick_rate=0.0))
    assert q.apply_plan(3, plans_for(c, 3)["REVERSE"][0]) and q.hard.bars[3].dirs[0] == RTL


def test_chord_only_through_chord_family():
    c = make("#001:01:10000000;\n#001:02:00000000;\n#002:13:10000000;\n#002:14:00000000;\n"
             "#003:01:10001000;\n#003:02:00100010;\n#004:13:10000000;\n#004:14:00000000;")
    p = Porter(c, None, 150, 0.0, SongConfig(id=1), PortParams(gimmick_rate=0.0))
    chord = plans_for(c, 3)["CHORD"][0]
    assert p.apply_plan(3, chord)
    ons = Counter(q for q, _l, ch in p.hard.bars[3].notes() if ch in "12")
    assert max(ons.values()) == 2
    assert [e for e in p.log if e.origin == "GIMMICK_CHORD"]


def test_uturn_front_rejected_for_own_plan_next_to_reversed_bar():
    # 2마디(짝수) 메인 그룹1 을 LTR 로 뒤집어 둔 상태 → 3마디에서 그룹1 을 원래 방향(RTL)으로 쓰면 유턴,
    # 3마디 그룹1 앞쪽(1/8)에 노트가 생기는 계획은 거부
    c = make("#001:01:10000000;\n#001:02:00000000;\n#002:03:10000000;\n#002:04:00000000;\n"
             "#003:01:11100000;\n#003:02:00001000;\n#004:13:10000000;\n#004:14:00000000;")
    p = Porter(c, None, 150, 0.0, SongConfig(id=1), PortParams(gimmick_rate=0.0))
    call = [x for x in plans_for(c, 3)["CALL"] if x.variant == "own"]
    assert call and not any(p.apply_plan(3, x) for x in call)
    assert p.hard.bars[3].dirs[1] is None


def _song(sid):
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError:
        pytest.skip(f"{sid}번 음원 없음")
    normal, _ = read_chart(cfg.base_path())
    return cfg, so, audio, bpm, normal


@pytest.mark.parametrize("sid", [7, 4, 6])
def test_variety_invariants_on_real_songs(sid):
    cfg, so, audio, bpm, normal = _song(sid)
    res = _make_porter(cfg, so, audio, bpm, normal).run()
    again = _make_porter(cfg, so, audio, bpm, normal).run()
    assert to_bytes(res.chart, True) == to_bytes(again.chart, True)
    g = [(b, v["gimmick"], v["gimmick_sig"]) for b, v in sorted(res.bars.items()) if v.get("gimmick")]
    fams = Counter(f for _b, f, _s in g)
    assert fams["HALF1"] <= 0.25 * len(g)
    assert len(fams) >= 6
    run = best = 0
    prev = None
    for _b, _f, s in g:
        run = run + 1 if s == prev else 1
        best, prev = max(best, run), s
    assert best <= 2
    assert not [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR") or i.code in ("W01", "W15")]
    assert not [u for u in find_uturns(res.chart) if u.front]


def test_main_notes_reads_holds():
    c = make(ODD)
    ms = main_notes(c.bars[3], 0)
    assert [(m.pos, m.char) for m in ms if m.char == "2"] == [(Fraction(3, 4), "2")]
    assert next(m for m in ms if m.char == "2").end == (Fraction(7, 8), "4")
