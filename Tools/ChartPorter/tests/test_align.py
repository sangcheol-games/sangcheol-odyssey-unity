"""음원 준비 검사(align)와 MusicSO 없는 신규 곡 설정: 6곡 통과, 틀린 BPM·밀린 음원·셋잇단은 중지, 대체 경로."""
import json
from dataclasses import replace

import numpy as np
import pytest

from chartporter import cli, config, paths
from chartporter.align import check_alignment, metronome_chart
from chartporter.audio import AudioFeatures, compute_features, features_for
from chartporter.cli import UsageError, _offset_for, _song_audio


def _feat(sid):
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError:
        pytest.skip(f"{sid}번 음원 없음")
    return features_for(audio), bpm


def _stops(res):
    return {c.code for c in res.checks if c.level == "STOP" and not c.passed}


@pytest.mark.parametrize("sid", [1, 2, 3, 4, 5, 6])
def test_existing_songs_pass(sid):
    feat, bpm = _feat(sid)
    res = check_alignment(feat, bpm)
    assert res.ok, _stops(res)
    assert -0.045 <= res.offset <= -0.010
    assert int(np.argmax(res.downbeat_scores)) == 0


def test_wrong_bpm_stops():
    feat, bpm = _feat(2)
    assert "A01" in _stops(check_alignment(feat, bpm + 1))


def _shifted(feat: AudioFeatures, dt: float) -> AudioFeatures:
    """음원 앞에 dt 초를 더 넣은 것처럼 (박이 dt 만큼 늦게 옴)."""
    return replace(feat, times=feat.times + dt, duration=feat.duration + dt, extra={})


def test_audio_not_starting_on_bar_one_stops():
    feat, bpm = _feat(4)
    assert "A03" in _stops(check_alignment(_shifted(feat, 0.050), bpm))          # 50ms 늦음
    # 8분(반 박) 늦음: 원래 엇박이 정박 자리에 와서 위상 범위는 통과할 수 있다 (1번은 정박·엇박 세기가 1.92 / 1.89).
    # 그래서 중지 대신 A03b 경고 + 메트로놈 확인(downbeat_ok)으로 막는다.
    res = check_alignment(_shifted(feat, 30.0 / bpm), bpm)
    warned = {c.code for c in res.checks if c.level == "WARN" and not c.passed}
    assert not res.ok or "A03b" in warned


def test_non_integer_tempo_stops():
    feat, bpm = _feat(3)
    fast = replace(feat, times=feat.times * bpm / (bpm + 0.5), duration=feat.duration * bpm / (bpm + 0.5), extra={})
    assert "A01" in _stops(check_alignment(fast, bpm))


def test_triplet_feel_stops():
    # 셔플: 박마다 0 과 2/3 박에 클릭 (8분 엇박 자리는 비어 있음)
    sr, bpm = 22050, 120
    beat = 60.0 / bpm
    y = np.zeros(int(sr * 24), dtype=np.float32)
    rng = np.random.default_rng(0)
    for t in np.arange(0.0, 23.5, beat):
        for d, g in ((0.0, 1.0), (2 * beat / 3, 0.8)):
            i = int((t + d) * sr)
            y[i:i + 220] += g * rng.standard_normal(220).astype(np.float32) * np.exp(-np.arange(220) / 40.0).astype(np.float32)
    res = check_alignment(compute_features(y, sr), bpm)
    assert _stops(res) & {"A08", "A02"}


def test_metronome_chart_marks_downbeat_on_top_lane():
    c = metronome_chart(2)
    assert [(p, l) for p, l, _c in c.bars[1].notes()][0][1] == 1
    assert len(c.bars[2].notes()) == 4


# ---------------- MusicSO 없는 신규 곡 ----------------

@pytest.fixture
def new_song(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "songs_dir", lambda: tmp_path / "songs")
    monkeypatch.setattr(cli, "music_so_for", lambda sid: None)
    monkeypatch.setattr(paths, "out_dir", lambda sid=None: tmp_path / "out" / (f"{sid:04d}" if sid else ""))
    (tmp_path / "songs").mkdir()
    return tmp_path


def test_new_song_uses_yaml_bpm_and_audio(new_song):
    audio = paths.music_audio_dir() / "Una Alarm.ogg"
    if not audio.exists():
        pytest.skip("2번 음원 없음")
    (new_song / "songs" / "9001.yaml").write_text(f"id: 9001\nbpm: 155\naudio: '{audio.as_posix()}'\n", encoding="utf-8")
    cfg, so, path, bpm = _song_audio(9001)
    assert so is None and bpm == 155 and path == audio


def test_new_song_without_bpm_is_rejected(new_song):
    (new_song / "songs" / "9002.yaml").write_text("id: 9002\n", encoding="utf-8")
    with pytest.raises(UsageError):
        _song_audio(9002)


def test_offset_comes_from_align_json_when_no_chart(new_song):
    audio = paths.music_audio_dir() / "Una Alarm.ogg"
    if not audio.exists():
        pytest.skip("2번 음원 없음")
    feat = features_for(audio)
    cfg = config.SongConfig(id=9003)
    with pytest.raises(UsageError):
        _offset_for(cfg, feat, None, 155)
    d = new_song / "out" / "9003"
    d.mkdir(parents=True)
    (d / "align.json").write_text(json.dumps({"bpm": 155, "offset_ms": -31.0, "sha": feat.sha}), encoding="utf-8")
    assert abs(_offset_for(cfg, feat, None, 155) + 0.031) < 1e-9
    (d / "align.json").write_text(json.dumps({"bpm": 154, "offset_ms": -31.0, "sha": feat.sha}), encoding="utf-8")
    with pytest.raises(UsageError):                     # BPM 이 다르면 쓰지 않음
        _offset_for(cfg, feat, None, 155)


def test_region_and_levels_parse(new_song):
    (new_song / "songs" / "9004.yaml").write_text("id: 9004\nbpm: 150\ndownbeat_ok: true\nregion: {last_bar: 80}\nlevels: {Easy: 3}\n", encoding="utf-8")
    cfg = config.load_song(9004)
    assert cfg.bpm == 150 and cfg.downbeat_ok and cfg.region_last_bar == 80 and cfg.levels == {"Easy": 3}
    (new_song / "songs" / "9005.yaml").write_text("id: 9005\nregion: {first: 2}\n", encoding="utf-8")
    with pytest.raises(config.ConfigError):
        config.load_song(9005)
