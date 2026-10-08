"""음원 기반 이지: 규칙(메인 라인, 박 위만, 홀드는 마디 안·0.19초 이상), 결정성, 2번 블라인드 합격 기준, 생성 전 확인 항목."""
from fractions import Fraction

import pytest

from chartporter import cli, config, paths
from chartporter.align import quarter_chart
from chartporter.audio import estimate_offset, features_for
from chartporter.chart_io import read_chart, to_bytes
from chartporter.cli import UsageError, _gen_ready, _song_audio
from chartporter.easy import alternation, generate_easy
from chartporter.model import expected_main, group_of
from chartporter.paths import charts_dir
from chartporter.thin import onset_times
from chartporter.validate import validate


def _gen(sid):
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError:
        pytest.skip(f"{sid}번 음원 없음")
    feat = features_for(audio)
    real, _ = read_chart(charts_dir() / f"Chart_{sid:04d}_Easy.txt")
    last = real.last_bar()
    off = estimate_offset(feat, quarter_chart(last), bpm).offset
    return generate_easy(feat, bpm, off, last, sid), real, bpm, (feat, off, last)


@pytest.fixture(scope="module")
def song2():
    return _gen(2)


def test_rules_hold(song2):
    res, _real, bpm, _ = song2
    c = res.chart
    assert not [i for i in validate(c, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    for b, bar in c.bars.items():
        if b == 0:
            continue
        assert bar.groups_used() == [expected_main(b)[0]]
        onsets = [(q, l) for q, l, ch in bar.notes() if ch in "12"]
        antic = {e.pos for e in res.log if e.bar == b and e.origin == "EASY_ANTIC"}
        assert all(group_of(l) == expected_main(b)[0] for _q, l in onsets)
        assert all((q * 4).denominator == 1 or q in antic for q, _l in onsets)  # 박 위만 (엇박은 당김음으로 당긴 칸만)
        assert len([q for q, _l in onsets if q in antic]) <= 1
        assert len({q for q, _l in onsets}) == len(onsets)                       # 동시치기 없음
        for lane in (1, 2, 3, 4):
            for s, end in bar.holds(lane):
                assert end is not None and end[0] <= Fraction(7, 8)               # 마디 안에서 닫힘
                assert float(end[0] - s) * 240 / bpm >= 0.19 - 1e-9


def test_deterministic(song2):
    res, _real, bpm, (feat, off, last) = song2
    again = generate_easy(feat, bpm, off, last, 2)
    assert to_bytes(res.chart, True) == to_bytes(again.chart, True)


def test_song2_blind_acceptance(song2):
    res, real, _bpm, (_f, _o, last) = song2
    gen, ref = onset_times(res.chart), onset_times(real)
    f1 = 2 * len(gen & ref) / (len(gen) + len(ref))
    holds = sum(1 for bar in res.chart.bars.values() for _q, _l, c in bar.notes() if c == "2") / len(gen)
    sw, top = alternation(res.chart)
    assert f1 >= 0.74
    assert 3.0 <= len(gen) / last <= 3.6
    assert 0.17 <= holds <= 0.27
    assert 0.56 <= sw <= 0.76 and 0.40 <= top <= 0.60


def test_fast_song_has_no_short_holds_or_eighth_gaps():
    res, _real, bpm, _ = _gen(1)                         # 195 BPM: 8분 = 0.154초
    ts = sorted(onset_times(res.chart))
    assert all(float(b - a) * 240 / bpm >= 0.19 for a, b in zip(ts, ts[1:]))
    assert not [i for i in validate(res.chart, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]


def test_break_and_sync_flags(song2):
    res, *_ = song2
    for b, v in res.bars.items():
        if "BREAK" in v.get("flags", []):
            assert len([1 for _q, _l, c in res.chart.bars[b].notes() if c in "12"]) <= 1
    assert res.bars[0]["sync_candidates"]                      # 2번은 당김음 후보가 있음
    assert all((q * 8).denominator == 1 and (q * 4).denominator != 1 for _b, q, _a, _c in res.bars[0]["sync_candidates"])


# ---------------- 생성 전 확인 ----------------

def test_gen_requires_align_downbeat_and_region(tmp_path, monkeypatch):
    monkeypatch.setattr(paths, "out_dir", lambda sid=None: tmp_path / "out" / (f"{sid:04d}" if sid else ""))
    cfg = config.SongConfig(id=9100)
    assert "align" in _gen_ready(cfg)
    d = tmp_path / "out" / "9100"
    d.mkdir(parents=True)
    (d / "align.json").write_text('{"ok": false}', encoding="utf-8")
    assert "중지" in _gen_ready(cfg)
    (d / "align.json").write_text('{"ok": true}', encoding="utf-8")
    assert "downbeat_ok" in _gen_ready(cfg)
    cfg.downbeat_ok = True
    assert "last_bar" in _gen_ready(cfg)
    cfg.region_last_bar = 60
    assert _gen_ready(cfg) is None


def test_new_song_end_to_end_align_then_gen(tmp_path, monkeypatch):
    """MusicSO 없는 신규 곡: songs 설정만으로 align → (사람 확인 기록) → gen 이지 초안."""
    audio = paths.music_audio_dir() / "Una Alarm.ogg"
    if not audio.exists():
        pytest.skip("2번 음원 없음")
    monkeypatch.setattr(config, "songs_dir", lambda: tmp_path / "songs")
    monkeypatch.setattr(cli, "music_so_for", lambda sid: None)
    monkeypatch.setattr(paths, "out_dir", lambda sid=None: tmp_path / "out" / (f"{sid:04d}" if sid else ""))
    (tmp_path / "songs").mkdir()
    y = tmp_path / "songs" / "9200.yaml"
    y.write_text(f"id: 9200\ntitle: 시험곡\nartist: 누군가\nbpm: 155\naudio: '{audio.as_posix()}'\nlevels: {{Easy: 3}}\n", encoding="utf-8")
    assert cli.main(["align", "9200", "--no-clips"]) == 0
    assert cli.main(["gen", "9200", "--no-clips"]) == 2                     # 아직 downbeat_ok·region 없음
    y.write_text(y.read_text(encoding="utf-8") + "downbeat_ok: true\nregion: {last_bar: 66}\n", encoding="utf-8")
    assert cli.main(["gen", "9200", "--no-clips"]) == 0
    out = tmp_path / "out" / "9200" / "gen"
    chart, raw = read_chart(out / "Chart_9200_Easy.txt")
    assert chart.header.title == "시험곡" and chart.header.level == 3 and chart.header.difficulty == "Easy"
    assert chart.last_bar() == 66
    assert not [i for i in validate(chart, raw, bpm=155) if i.severity in ("FATAL", "ERROR", "WARN")]
    assert (out / "report_Easy.md").exists() and (out / "gen_log_Easy.json").exists()
