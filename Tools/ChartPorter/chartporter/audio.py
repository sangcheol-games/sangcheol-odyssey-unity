"""음원 특징 추출 (librosa) 과 채보 그리드 정렬.

- 프레임 단위 포락선: 대역별 스펙트럼 플럭스(onset 세기), HPSS 화성 성분 중역 플럭스, 화성 중역 에너지, RMS dB, 중역 크로마.
- 결과는 cache/ 에 음원 해시별로 저장한다 (같은 음원이면 다시 계산하지 않음).
- 시간 규칙: 음원 시각 = (마디 - 1 + 위치) × 240/BPM + offset  (timing.audio_time)
  offset 은 분석용 보정값이다. 채보 노트 위치에서 중역 플럭스가 가장 커지는 값을 고른다 (STFT 프레이밍 편향도 함께 흡수).
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path

import numpy as np

from .model import Chart
from .paths import TOOL_DIR
from .timing import audio_time, bar_seconds

SR = 22050
N_FFT = 2048
HOP = 128                      # 5.8ms
BANDS = {"low": (30.0, 150.0), "mid": (150.0, 2500.0), "high": (5000.0, 11000.0)}
CHROMA_BAND = (250.0, 1500.0)  # 멜로디/보컬 대역
CHROMA_FULL_BAND = (65.0, 2100.0)
N_MELS = 64
FEATURE_VERSION = 2
CELL_WINDOW = 0.025            # 칸 값 = 칸 시각 ±25ms 안 최댓값


def cache_dir() -> Path:
    return TOOL_DIR / "cache"


def load_audio(path: Path, sr: int | None = SR, mono: bool = True) -> tuple[np.ndarray, int]:
    """파일 객체로 열어 Windows 의 일본어·한국어 경로 문제를 피한다."""
    import soundfile as sf
    with open(path, "rb") as f:
        y, file_sr = sf.read(f, dtype="float32", always_2d=True)
    if mono:
        y = y.mean(axis=1)
    if sr is not None and file_sr != sr:
        import librosa
        y = librosa.resample(y.T if not mono else y, orig_sr=file_sr, target_sr=sr, res_type="soxr_hq")
        y = y.T if not mono else y
        file_sr = sr
    return y, file_sr


@dataclass
class AudioFeatures:
    times: np.ndarray                     # 프레임 중심 시각 (초)
    env: dict[str, np.ndarray]            # 이름 → 프레임별 값
    chroma: np.ndarray                    # 12 × 프레임 (중역 크로마, 멜로디/보컬)
    duration: float
    chroma_full: np.ndarray | None = None  # 12 × 프레임 (65–2100Hz, 화성)
    mel_db: np.ndarray | None = None       # N_MELS × 프레임 (log-mel, dB)
    source: str = ""
    sha: str = ""
    extra: dict = field(default_factory=dict)

    def onsets(self, name: str = "mid") -> np.ndarray:
        """librosa 피크 검출로 찾은 소리 시작 시각들 (이 포락선 기준, 초)."""
        key = f"onsets_{name}"
        if key not in self.extra:
            import librosa
            self.extra[key] = librosa.onset.onset_detect(onset_envelope=self.env[name], sr=SR, hop_length=HOP,
                                                         units="time", backtrack=False)
        return self.extra[key]

    def nearest_onset(self, t: float, name: str = "mid") -> float:
        """t 에서 가장 가까운 소리 시작까지의 차이 (초, 음수 = 앞). 없으면 inf."""
        on = self.onsets(name)
        if len(on) == 0:
            return float("inf")
        i = int(np.searchsorted(on, t))
        cands = [on[j] - t for j in (i - 1, i) if 0 <= j < len(on)]
        return min(cands, key=abs)

    def at(self, name: str, t, window: float = 0.0):
        """시각 t(스칼라 또는 배열)의 값. window>0 이면 t±window 안 최댓값, 0 이면 선형 보간."""
        e = self.env[name]
        t = np.atleast_1d(np.asarray(t, dtype=float))
        if window <= 0:
            out = np.interp(t, self.times, e, left=0.0, right=0.0)
        else:
            lo = np.searchsorted(self.times, t - window, side="left")
            hi = np.searchsorted(self.times, t + window, side="right")
            out = np.array([e[a:b].max() if b > a else 0.0 for a, b in zip(lo, hi)])
        return out


def _band(freqs: np.ndarray, lo: float, hi: float) -> np.ndarray:
    return (freqs >= lo) & (freqs < hi)


def _flux(mag: np.ndarray) -> np.ndarray:
    """log 압축 스펙트럼의 양의 시간 차분 평균 (주파수 축)."""
    L = np.log1p(100.0 * mag)
    d = np.diff(L, axis=1, prepend=L[:, :1])
    return np.maximum(d, 0.0).mean(axis=0)


def compute_features(y: np.ndarray, sr: int = SR) -> AudioFeatures:
    import librosa
    S = np.abs(librosa.stft(y, n_fft=N_FFT, hop_length=HOP, center=True)).astype(np.float32)
    freqs = librosa.fft_frequencies(sr=sr, n_fft=N_FFT)
    times = librosa.frames_to_time(np.arange(S.shape[1]), sr=sr, hop_length=HOP)
    env: dict[str, np.ndarray] = {"full": _flux(S[_band(freqs, 30.0, sr / 2 + 1)])}
    for name, (lo, hi) in BANDS.items():
        env[name] = _flux(S[_band(freqs, lo, min(hi, sr / 2))])
    mid = _band(freqs, *BANDS["mid"])
    H, _P = librosa.decompose.hpss(S[mid], kernel_size=31)
    env["harm_mid"] = _flux(H)
    env["harm_mid_energy"] = H.mean(axis=0)
    env["rms_db"] = librosa.amplitude_to_db(librosa.feature.rms(S=S, frame_length=N_FFT)[0], ref=1.0, top_db=None)
    def band_chroma(lo, hi):
        masked = np.zeros_like(S)
        cb = _band(freqs, lo, hi)
        masked[cb] = S[cb]
        return librosa.feature.chroma_stft(S=masked ** 2, sr=sr, n_fft=N_FFT, tuning=0.0).astype(np.float32)
    mel = librosa.feature.melspectrogram(S=S ** 2, sr=sr, n_fft=N_FFT, n_mels=N_MELS, fmin=30.0, fmax=sr / 2)
    mel_db = librosa.power_to_db(mel, ref=1.0, top_db=None).astype(np.float32)
    return AudioFeatures(times=times, env={k: v.astype(np.float32) for k, v in env.items()},
                         chroma=band_chroma(*CHROMA_BAND), duration=len(y) / sr,
                         chroma_full=band_chroma(*CHROMA_FULL_BAND), mel_db=mel_db)


def _sha(path: Path) -> str:
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    h.update(f"v{FEATURE_VERSION}:{SR}:{N_FFT}:{HOP}".encode())
    return h.hexdigest()[:16]


def features_for(path: Path, use_cache: bool = True) -> AudioFeatures:
    """음원 파일 → 특징 (cache/<sha>.npz 재사용)."""
    path = Path(path)
    sha = _sha(path)
    cp = cache_dir() / f"{sha}.npz"
    if use_cache and cp.exists():
        z = np.load(cp)
        env = {k[4:]: z[k] for k in z.files if k.startswith("env_")}
        return AudioFeatures(times=z["times"], env=env, chroma=z["chroma"], duration=float(z["duration"]),
                             chroma_full=z["chroma_full"], mel_db=z["mel_db"], source=path.name, sha=sha)
    y, sr = load_audio(path)
    feat = compute_features(y, sr)
    feat.source, feat.sha = path.name, sha
    cache_dir().mkdir(parents=True, exist_ok=True)
    np.savez_compressed(cp, times=feat.times, chroma=feat.chroma, duration=feat.duration,
                        chroma_full=feat.chroma_full, mel_db=feat.mel_db,
                        **{f"env_{k}": v for k, v in feat.env.items()})
    return feat


# ---------------- 채보 ↔ 음원 정렬 ----------------

def note_audio_times(chart: Chart, bpm: float, chars: str = "125") -> np.ndarray:
    ts = sorted({audio_time(b, p, bpm) for b, bar in chart.bars.items() if b > 0
                 for p, _l, c in bar.notes() if c in chars})
    return np.array(ts, dtype=float)


@dataclass
class OffsetEstimate:
    offset: float                  # 초
    score: float                   # 최적 위치 평균 세기 / 곡 전체 중앙값
    sharpness: float               # 최적값과 ±30ms 밖 평균의 비
    curve: list[tuple[float, float]]
    windows: list[tuple[int, int, float]]   # (시작 마디, 끝 마디, 구간별 최적 offset)


def estimate_offset(feat: AudioFeatures, chart: Chart, bpm: float, env: str = "mid",
                    lo: float = -0.06, hi: float = 0.04, step: float = 0.001, window_bars: int = 16) -> OffsetEstimate:
    times = note_audio_times(chart, bpm)
    times = times[(times > 0) & (times < feat.duration)]
    if len(times) == 0:
        raise ValueError("음원 구간 안에 노트가 없음")
    grid = np.arange(lo, hi + step / 2, step)
    med = float(np.median(feat.env[env])) or 1e-9
    scores = np.array([feat.at(env, times + o).mean() for o in grid]) / med
    i = int(np.argmax(scores))
    far = scores[np.abs(grid - grid[i]) > 0.03]
    sharp = float(scores[i] / far.mean()) if len(far) else float("nan")
    windows = []
    bar_s = bar_seconds(bpm)
    last = max(chart.bars) if chart.bars else 0
    for start in range(1, last + 1, window_bars):
        end = min(last, start + window_bars - 1)
        sel = times[(times >= (start - 1) * bar_s) & (times < end * bar_s)]
        if len(sel) >= 8:
            s = np.array([feat.at(env, sel + o).mean() for o in grid])
            windows.append((start, end, float(grid[int(np.argmax(s))])))
    return OffsetEstimate(offset=float(grid[i]), score=float(scores[i]), sharpness=sharp,
                          curve=[(float(a), float(b)) for a, b in zip(grid, scores)], windows=windows)


def cell_value(feat: AudioFeatures, env: str, bar: int, pos: Fraction | float, bpm: float, offset: float,
               window: float = CELL_WINDOW) -> float:
    return float(feat.at(env, audio_time(bar, pos, bpm, offset), window)[0])


def grid_values(feat: AudioFeatures, env: str, bars: range | list[int], division: int, bpm: float, offset: float,
                window: float = CELL_WINDOW) -> np.ndarray:
    """마디 × 칸 값 배열 (shape: len(bars) × division)."""
    bars = list(bars)
    ts = np.array([audio_time(b, Fraction(k, division), bpm, offset) for b in bars for k in range(division)])
    return feat.at(env, ts, window).reshape(len(bars), division)


class Percentiles:
    """곡의 8분 칸 값 분포 기준 백분위 (P0–P100)."""

    def __init__(self, feat: AudioFeatures, env: str, last_bar: int, bpm: float, offset: float):
        bars = [b for b in range(1, last_bar + 1) if (b - 1) * bar_seconds(bpm) < feat.duration]
        self.ref = np.sort(grid_values(feat, env, bars, 8, bpm, offset).ravel())

    def __call__(self, value: float) -> float:
        if len(self.ref) == 0:
            return 0.0
        return float(np.searchsorted(self.ref, value, side="right") / len(self.ref) * 100.0)
