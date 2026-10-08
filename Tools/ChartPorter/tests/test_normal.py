"""이지 기반 노멀: 이지 유지, 4분 사이 8분 추가, 홀드 뒤는 덜 채움, 마디당 상한, 결정성, 2번 합격 기준, 신규 곡 전체 흐름."""
from fractions import Fraction

import pytest

from chartporter import cli, config, fmt, paths
from chartporter.align import quarter_chart
from chartporter.audio import estimate_offset, features_for
from chartporter.chart_io import read_chart, to_bytes
from chartporter.cli import UsageError, _song_audio
from chartporter.model import expected_main, group_of
from chartporter.normal import NormalParams, candidates, generate_normal
from chartporter.paths import charts_dir
from chartporter.thin import onset_times
from chartporter.validate import validate

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0).replace("Hard", "Easy") + "#000:13:0000;\n#000:14:0000;\n"


def make(lines: str):
    return chart_from_text(BASE + lines)[0]


def lanes_onsets(c):
    return {(b, q, l) for b, bar in c.bars.items() for q, l, ch in bar.notes() if ch in "12"}


def test_keeps_easy_and_fills_quarter_gaps_with_eighths():
    e = make("#001:01:10001000;\n#001:02:00100010;\n#002:13:10001000;\n#002:14:00100010;")
    res = generate_normal(e, None, 150, 0.0, 1, NormalParams(target_per_bar=6))
    n = res.chart
    assert lanes_onsets(e) <= lanes_onsets(n)
    new = onset_times(n) - onset_times(e)
    assert new and all((t - int(t)) * 8 % 2 == 1 for t in new)          # 모두 8분 엇박
    for b, bar in n.bars.items():
        if b > 0:
            assert all(group_of(l) == expected_main(b)[0] for _q, l, _c in bar.notes())
            assert len({q for q, _l, c in bar.notes() if c in "12"}) == len([1 for _q, _l, c in bar.notes() if c in "12"])
    assert not [i for i in validate(n, bpm=150) if i.severity in ("FATAL", "ERROR", "WARN")]


def test_gap_after_hold_is_low_priority_and_eighth_gaps_not_filled():
    # 레인1: 홀드 0→1/8, 탭 1/4 → 0 과 1/4 사이(1/8)는 홀드 뒤 간격. 레인2: 탭 1/2·5/8 (1/8 간격)
    e = make("#001:01:24100000;\n#001:02:00001100;\n#002:13:10000000;\n#002:14:00000000;")
    cs = {(c.bar, c.pos): c.score for c in candidates(e, NormalParams())}
    assert cs[(1, Fraction(1, 8))] < 0.1                                   # 홀드 뒤 간격 ×0.1
    assert cs[(1, Fraction(3, 8))] > 0.4                                   # 탭 뒤 간격 (3/8 가중치 .46)
    assert (1, Fraction(9, 16)) not in cs                                  # 1/8 간격은 채우지 않음


def test_per_bar_cap_and_ratio_bounds():
    e = make("#001:01:10101010;\n#001:02:00000000;\n#002:13:10101010;\n#002:14:00000000;")
    res = generate_normal(e, None, 120, 0.0, 1, NormalParams(target_per_bar=20, max_add_per_bar=2))
    per = {}
    for x in res.log:
        if x.origin == "NORMAL_ADD":
            per[x.bar] = per.get(x.bar, 0) + 1
    assert max(per.values()) <= 2
    assert len(onset_times(res.chart)) <= 1.6 * len(onset_times(e)) + 1e-9


def test_deterministic():
    e = make("#001:01:10001000;\n#001:02:00100010;\n#002:13:10101010;\n#002:14:00000000;")
    a, b = generate_normal(e, None, 150, 0.0, 7), generate_normal(e, None, 150, 0.0, 7)
    assert to_bytes(a.chart, True) == to_bytes(b.chart, True)


def test_song2_from_user_easy_acceptance():
    try:
        cfg, so, audio, bpm = _song_audio(2)
    except UsageError:
        pytest.skip("2번 음원 없음")
    easy, _ = read_chart(charts_dir() / "Chart_0002_Easy.txt")
    real, _ = read_chart(charts_dir() / "Chart_0002_Normal.txt")
    feat = features_for(audio)
    off = estimate_offset(feat, quarter_chart(66), bpm).offset
    res = generate_normal(easy, feat, bpm, off, 2)
    g, r, e = onset_times(res.chart), onset_times(real), onset_times(easy)
    add_g, add_r = g - e, r - e
    assert e <= g
    assert 2 * len(add_g & add_r) / (len(add_g) + len(add_r)) >= 0.60
    assert not [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]


def test_new_song_full_chain(tmp_path, monkeypatch):
    """MusicSO 없는 신규 곡: align → gen 이지 → install → gen 노멀 → install → port 하드 → install (모두 임시 폴더)."""
    audio = paths.music_audio_dir() / "Una Alarm.ogg"
    if not audio.exists():
        pytest.skip("2번 음원 없음")
    charts = tmp_path / "Charts"
    charts.mkdir()
    out = lambda sid=None: tmp_path / "out" / (f"{sid:04d}" if sid else "")
    monkeypatch.setattr(config, "songs_dir", lambda: tmp_path / "songs")
    monkeypatch.setattr(config, "charts_dir", lambda: charts)
    monkeypatch.setattr(cli, "charts_dir", lambda: charts)
    monkeypatch.setattr(cli, "music_so_for", lambda sid: None)
    monkeypatch.setattr(paths, "out_dir", out)
    monkeypatch.setattr(fmt, "out_dir", out)
    (tmp_path / "songs").mkdir()
    (tmp_path / "songs" / "9300.yaml").write_text(
        f"id: 9300\ntitle: 시험곡\nartist: 누군가\nbpm: 155\naudio: '{audio.as_posix()}'\n"
        "levels: {Easy: 3, Normal: 5, Hard: 9}\ndownbeat_ok: true\nregion: {last_bar: 66}\n", encoding="utf-8")
    assert cli.main(["align", "9300", "--no-clips"]) == 0
    assert cli.main(["gen", "9300", "--diff", "Normal", "--no-clips"]) == 2     # 이지가 아직 없음
    assert cli.main(["gen", "9300", "--no-clips"]) == 0
    assert cli.main(["install", "9300", "--diff", "Easy"]) == 0
    assert cli.main(["gen", "9300", "--diff", "Normal", "--no-clips"]) == 0
    assert cli.main(["install", "9300", "--diff", "Normal"]) == 0
    assert cli.main(["port", "9300"]) == 0
    assert cli.main(["install", "9300", "--diff", "Hard"]) == 0
    e, _ = read_chart(charts / "Chart_9300_Easy.txt")
    n, _ = read_chart(charts / "Chart_9300_Normal.txt")
    h, raw = read_chart(charts / "Chart_9300_Hard.txt")
    assert onset_times(e) <= onset_times(n)
    assert len(onset_times(e)) < len(onset_times(n)) <= len(onset_times(h))
    assert (e.header.level, n.header.level, h.header.level) == (3, 5, 9)
    for c in (e, n, h):
        assert not [i for i in validate(c, bpm=155) if i.severity in ("FATAL", "ERROR")]
