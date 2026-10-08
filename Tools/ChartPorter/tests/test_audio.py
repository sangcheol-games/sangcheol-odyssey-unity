"""합성 클릭 트랙으로 마디↔시간 매핑, 오프셋 추정, 확인 목록, 미리듣기를 검증한다."""
from fractions import Fraction

import numpy as np
import pytest
import soundfile as sf

from chartporter.audio import SR, compute_features, estimate_offset, grid_values
from chartporter.listen import build_checklist, render_preview
from chartporter.timing import audio_time

from conftest import HEADER, chart_from_text

BPM = 120   # 마디 2초, 8분 0.25초, 16분 0.125초
BARS = 12


def _burst(rng, dur=0.012):
    n = int(SR * dur)
    return (rng.standard_normal(n) * np.exp(-np.arange(n) / (n / 4))).astype(np.float32)


def synth(times, total=BARS * 2.0 + 1.0, seed=0):
    rng = np.random.default_rng(seed)
    y = (rng.standard_normal(int(SR * total)) * 0.002).astype(np.float32)   # 아주 작은 잡음 바닥
    for t in times:
        i = int(round(t * SR))
        b = _burst(rng)
        y[i:i + len(b)] += b[: max(0, len(y) - i)]
    return y


def chart_with(lines: str):
    body = "#000:13:0000;\n#000:14:0000;\n" + lines
    return chart_from_text(HEADER.format(n=0) + body)[0]


def quarter_chart():
    rows = []
    for b in range(1, BARS + 1):
        if b % 2:
            rows += [f"#{b:03d}:01:10101010;", f"#{b:03d}:02:00000000;"]
        else:
            rows += [f"#{b:03d}:13:00000000;", f"#{b:03d}:14:01010101;"]   # RTL: 판정 0,2,4,6/8
    return chart_with("\n".join(rows))


@pytest.fixture(scope="module")
def clicks():
    chart = quarter_chart()
    times = [audio_time(b, Fraction(k, 4), BPM) for b in range(1, BARS + 1) for k in range(4)]
    return chart, times


def test_bar_one_starts_at_audio_zero():
    assert audio_time(1, 0, BPM) == 0.0
    assert audio_time(2, Fraction(1, 4), BPM) == pytest.approx(2.5)


def test_offset_estimate_tracks_injected_shift(clicks):
    chart, times = clicks
    f0 = compute_features(synth(times))
    f1 = compute_features(synth([t + 0.020 for t in times]))
    e0, e1 = estimate_offset(f0, chart, BPM), estimate_offset(f1, chart, BPM)
    assert -0.05 <= e0.offset <= 0.0             # STFT 프레이밍 편향은 음수 쪽 수십 ms 이내
    assert e1.offset - e0.offset == pytest.approx(0.020, abs=0.003)
    assert e0.sharpness > 2


def test_grid_values_separate_clicked_and_empty_cells(clicks):
    chart, times = clicks
    feat = compute_features(synth(times))
    off = estimate_offset(feat, chart, BPM).offset
    v = grid_values(feat, "mid", range(2, BARS + 1), 8, BPM, off)   # 1마디 0 지점은 보정 후 음원 시작보다 앞이라 제외
    clicked, empty = v[:, 0::2], v[:, 1::2]
    assert clicked.min() > 3 * empty.max()


def test_onset_peaks_line_up_with_clicks(clicks):
    chart, times = clicks
    feat = compute_features(synth(times))
    off = estimate_offset(feat, chart, BPM).offset
    for t in times[4:20]:
        assert abs(feat.nearest_onset(t + off)) <= 0.010


def test_checklist_marks_shifted_and_onbeat_offgrid_notes(clicks):
    _chart, times = clicks
    # 소리: 4분 + 1마디 3/16 지점(엇박 소리). 채보: 1마디 3/16(소리 있음), 3마디 7/16(가장 가까운 소리는 8/16 → 한 칸 뒤)
    sound = times + [audio_time(1, Fraction(3, 16), BPM)]
    feat = compute_features(synth(sound))
    rows = []
    for b in range(1, BARS + 1):
        if b % 2:
            seq = "1010101010101010"
            if b == 1:
                seq = "0001000000000000"
            elif b == 3:
                seq = "0000000100000000"
            rows += [f"#{b:03d}:01:{seq};", f"#{b:03d}:02:0000000000000000;"]
        else:
            rows += [f"#{b:03d}:13:0000;", f"#{b:03d}:14:0101;"]
    chart = chart_with("\n".join(rows))
    off = estimate_offset(feat, quarter_chart(), BPM).offset
    items = {(it.bar, it.pos): it for it in build_checklist(chart, feat, BPM, off)}
    assert items[(1, Fraction(3, 16))].shift == 0 and "있음" in items[(1, Fraction(3, 16))].suggestion
    assert items[(3, Fraction(7, 16))].shift == +1


def test_preview_renders_expected_length(tmp_path, clicks):
    chart, times = clicks
    wav = tmp_path / "song.wav"
    sf.write(str(wav), synth(times), SR)
    out = render_preview(wav, chart, BPM, (2, 3), tmp_path / "clip.wav", pad=0.5)
    y, sr = sf.read(str(out))
    assert sr == SR
    assert len(y) == pytest.approx((2 * 2.0 + 2 * 0.5) * SR, abs=2)
