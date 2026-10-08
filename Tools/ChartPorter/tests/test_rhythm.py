"""리듬 변주: 감지기(당김음·16분·셋잇단), 하드 16분 음형·셋잇단, 노멀 픽업·당김음, 이지 당김음 — 규칙과 실제 곡."""
from collections import Counter
from fractions import Fraction

import pytest

from chartporter.align import quarter_chart
from chartporter.audio import estimate_offset, features_for
from chartporter.chart_io import read_chart, to_bytes
from chartporter.cli import UsageError, _make_porter, _song_audio
from chartporter.easy import generate_easy
from chartporter.normal import generate_normal
from chartporter.paths import charts_dir
from chartporter.rhythm import antic_cells, make_ctx, triplet_bars
from chartporter.thin import onset_times
from chartporter.validate import validate


def _song(sid):
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError:
        pytest.skip(f"{sid}번 음원 없음")
    normal, _ = read_chart(cfg.base_path())
    last = cfg.region_last_bar or normal.last_bar()
    feat = features_for(audio)
    off = estimate_offset(feat, quarter_chart(last), bpm).offset
    return cfg, so, audio, bpm, normal, feat, off, last


def test_antic_detector_finds_human_like_counts_on_song6():
    *_, bpm, normal, feat, off, last = _song(6)
    ctx = make_ctx(feat, bpm, off, last)
    n = sum(len(antic_cells(ctx, b, 40.0, in_bar=False)) for b in range(1, last + 1))
    assert 25 <= n <= 50                                    # 분석: A ≥ 40 은 36칸, 사람 노멀 당김음 39


def test_triplet_detector_only_song1_bar16():
    *_, bpm, normal, feat, off, last = _song(1)
    tb = triplet_bars(make_ctx(feat, bpm, off, last), range(1, last + 1))
    assert set(tb) == {16}


@pytest.fixture(scope="module")
def hard7():
    cfg, so, audio, bpm, normal, *_ = _song(7)
    return _make_porter(cfg, so, audio, bpm, normal).run(), bpm, (cfg, so, audio, normal)


def test_hard_16th_figures_are_playable(hard7):
    res, bpm, (cfg, so, audio, normal) = hard7
    r16 = [b for b, v in res.bars.items() if v.get("rhythm") == "16"]
    assert 5 <= len(r16) <= round(0.18 * normal.last_bar()) + 1
    assert not [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR") or i.code in ("W01", "W02", "W15")]
    for b in r16:
        bar = res.chart.bars[b]
        assert bar.min_length() % 16 == 0 or any(e.origin == "RHYTHM_SHIFT16_MOVE" for e in res.log if e.bar == b)
    again = _make_porter(cfg, so, audio, bpm, normal).run()
    assert to_bytes(res.chart, True) == to_bytes(again.chart, True)


def test_rhythm_moves_are_logged_with_source(hard7):
    res, *_ = hard7
    for e in res.log:
        if e.origin == "RHYTHM_SHIFT16_MOVE" and e.char == "1":
            assert any(x.origin == "RHYTHM_SHIFT16_MOVE" and x.char == "0" and x.bar == e.bar for x in res.log)


def test_triplet_written_on_song1_bar16():
    cfg, so, audio, bpm, normal, *_ = _song(1)
    res = _make_porter(cfg, so, audio, bpm, normal).run()
    bar = res.chart.bars[16]
    tri = [q for q, _l, c in bar.notes() if c in "12" and (q * 12).denominator == 1 and (q * 8).denominator != 1]
    assert tri and bar.min_length() % 12 == 0
    assert not [i for i in validate(res.chart, bpm=bpm) if i.code in ("W01", "W02") and i.bar == 16]


def test_normal_rhythm_adds_keep_easy_and_caps():
    cfg, so, audio, bpm, normal, feat, off, last = _song(7)
    e = generate_easy(feat, bpm, off, last, 7, cfg=cfg)
    n = generate_normal(e.chart, feat, bpm, off, 7, cfg=cfg)
    assert onset_times(e.chart) <= onset_times(n.chart)
    c = Counter(x.origin for x in n.log)
    assert c["NORMAL_PICKUP16"] <= round(0.2 * last)
    assert not [i for i in validate(n.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]


def test_easy_antic_respects_gap_and_one_per_bar():
    cfg, so, audio, bpm, normal, feat, off, last = _song(5)
    e = generate_easy(feat, bpm, off, last, 5, cfg=cfg)
    ants = [x for x in e.log if x.origin == "EASY_ANTIC" and x.char == "1"]
    assert ants and max(Counter(x.bar for x in ants).values()) == 1
    ts = sorted(onset_times(e.chart))
    assert all(float(b - a) * 240 / bpm >= 0.19 - 1e-9 for a, b in zip(ts, ts[1:]))
    for x in ants:
        lane_cells = {l: e.chart.bars[x.bar].cells[l] for l in (1, 2, 3, 4)}
        assert any(lane_cells[l].get(x.pos) == "1" for l in lane_cells)
