"""포팅 엔진: 실제 음원(캐시)으로 2번·4번을 포팅해 지켜야 할 성질을 확인한다."""
from collections import Counter
from dataclasses import replace
from fractions import Fraction

import pytest

from chartporter.chart_io import read_chart, to_bytes
from chartporter.cli import UsageError, _make_porter, _song_audio
from chartporter.evaluate import random_fill, score
from chartporter.timing import is_odd_sixteenth, is_on_grid
from chartporter.validate import validate


def _setup(sid):
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError:
        pytest.skip(f"{sid}번 음원 없음")
    normal, _ = read_chart(cfg.base_path())
    return cfg, so, audio, bpm, normal


@pytest.fixture(scope="module")
def song4():
    cfg, so, audio, bpm, normal = _setup(4)
    return cfg, so, audio, bpm, normal, _make_porter(cfg, so, audio, bpm, normal).run()


def _onsets(c):
    return [(b, p, l) for b, bar in c.bars.items() for p, l, ch in bar.notes() if ch in "125"]


def test_output_is_valid_and_deterministic(song4):
    cfg, so, audio, bpm, normal, res = song4
    again = _make_porter(cfg, so, audio, bpm, normal).run()
    assert to_bytes(res.chart, True) == to_bytes(again.chart, True)
    bad = [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    assert not bad


def test_never_adds_chords_or_odd_sixteenths(song4):
    *_, normal, res = song4
    chords = lambda c: {k for k, v in Counter((b, p) for b, p, _l in _onsets(c)).items() if v > 1}
    by_chord = {(e.bar, e.pos) for e in res.log if e.origin == "GIMMICK_CHORD"}     # 두 그룹 동시치기 형태만 허용 (사용자 결정)
    assert not (chords(res.chart) - chords(normal) - by_chord)
    new = {(b, p) for b, p, _l in _onsets(res.chart)} - {(b, p) for b, p, _l in _onsets(normal)}
    rhythm = {(e.bar, e.pos) for e in res.log if e.origin.startswith("RHYTHM_")}         # 16분·셋잇단은 리듬 변주로만 (음원 정점)
    assert not any(is_odd_sixteenth(p) for b, p in new - rhythm)
    assert all(is_on_grid(p, 8) for b, p in new - rhythm)


def test_keeps_all_normal_onsets_except_gimmick_moves(song4):
    *_, normal, res = song4
    moved = {(e.bar, e.pos) for e in res.log if e.origin.endswith("_MOVE") or e.origin in ("UTURN_DROP", "COPY_REMOVED")}
    lost = {(b, p) for b, p, _l in _onsets(normal)} - {(b, p) for b, p, _l in _onsets(res.chart)}
    assert lost <= moved


def test_every_change_is_logged(song4):
    *_, normal, res = song4
    logged = {(e.bar, e.lane, e.pos) for e in res.log}
    for b, bar in res.chart.bars.items():
        for p, l, c in bar.notes():
            if normal.bars.get(b) is None or normal.bars[b].cells[l].get(p) != c:
                assert (b, l, p) in logged


def test_density_scale_increases_adds():
    cfg, so, audio, bpm, normal = _setup(4)
    base = _make_porter(cfg, so, audio, bpm, normal).run()
    more = _make_porter(replace(cfg, density_scale=2.0), so, audio, bpm, normal).run()
    count = lambda r: sum(1 for e in r.log if e.origin == "GAPFILL")
    assert count(more) > count(base)


def test_keep_normal_bars_untouched():
    cfg, so, audio, bpm, normal = _setup(4)
    res = _make_porter(replace(cfg, keep_normal_bars={5, 6, 7}), so, audio, bpm, normal).run()
    for b in (5, 6, 7):
        assert res.chart.bars[b].cells == normal.bars[b].cells


def test_copy_rules_are_applied_on_song2():
    cfg, so, audio, bpm, normal = _setup(2)
    res = _make_porter(cfg, so, audio, bpm, normal).run()
    for t, s in zip(range(34, 41), range(2, 9)):        # songs/0002.yaml: 34-40 ← 2-8 그대로
        assert res.chart.bars[t].cells == res.chart.bars[s].cells


def test_placement_beats_random_on_song2():
    cfg, so, audio, bpm, normal = _setup(2)
    user, _ = read_chart(cfg.target_path())
    porter = _make_porter(cfg, so, audio, bpm, normal)
    res = porter.run()
    s = score(normal, res.chart, user)
    r = score(normal, random_fill(normal, res.chart, porter.percentile), user)
    assert s["added"]["precision"] > r["added"]["precision"] + 0.1
    assert s["gimmick"]["f1"] >= 0.55
    # 기믹이 홀드를 다른 그룹으로 통째로 옮기면 레인이 달라져 하나쯤 안 맞을 수 있음 (2번 14곳 중 13곳)
    assert s["hold_close_78"]["agree"] >= s["hold_close_78"]["user"] - 1 >= 12
