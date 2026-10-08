"""신규 곡 음원 준비 검사 (align).

BPM·위상·첫 박을 음원에서 '찾지' 않고, 주어진 정수 BPM 과 '1마디 시작 = 음원 0초' 가정을 '검증'한다.
(기본 librosa 템포 추정은 기존 6곡 중 4곡에서 박자를 잘못 잡지만, 아래 검증은 6곡 모두 통과한다.)

게임 규칙: MusicSO 의 BPM 은 정수이고 곡별 오프셋이 없다. 1마디가 음원 0초에서 시작한다.
그래서 음원이 이 가정에 맞지 않으면 채보로는 고칠 수 없고, 음원 앞부분을 패딩·트림해야 한다(도구는 음원을 고치지 않음).

검사 (STOP = 다음 단계로 가면 안 됨, WARN = 사람 확인, INFO = 참고):
  A01 BPM      : 8분 빗(comb) 맞춤을 B±2 에서 0.01 간격으로 훑은 정점이 B ±0.02 안      (6곡: ±0.01)
  A02 배속     : 4분 격자 기준 8분 엇박 세기 ≥ 1.4×중앙값 → BPM 이 실제의 두 배가 아님   (6곡: ≥1.77)
  A02b 16분    : 16분 엇박 > 1.25× 면 경고 — 16분 소리(하이햇 등)가 강하거나 BPM 이 절반일 수 있음 (6곡: ≤1.09, 7번 1.29)
                (BPM 을 절반으로 보면 16분 성분이 오히려 더 강해지므로 보통은 16분 소리가 실제로 있는 곡)
  A08 셋잇단   : 1/3·2/3 박 세기 < 8분 엇박 세기                                     (6곡: 0.84–0.95 < 1.77+)
  A03 위상     : 4분 격자 채보로 분석 오프셋을 구해 −45…−10 ms 안 (STFT 편향 −36ms 포함)  (6곡: −18…−32)
                + 정박 세기 ≥ 8분 엇박 세기가 아니면 반 박 밀렸을 수 있다고 경고
  A04 흔들림   : 8마디 창별 오프셋의 기울기 ≤ 10 ms/100마디 (넘으면 템포가 변함 → 중지), 범위 > 15 ms 면 경고
  A05 첫 박    : 박 앞뒤 1박 멜 스펙트럼 변화가 1박에서 가장 커야 함. 여유 < 1.05 면 경고 (5번 1.03).
                어떤 경우든 메트로놈 클립을 듣고 songs 설정에 downbeat_ok: true 를 적어야 다음 단계로 간다
  A06 길이     : 음원 길이 / 마디 길이가 정수나 .5 에 가까운지 (DAW 에서 BPM 으로 만든 음원인지) — 참고
  A07 구간     : 마지막 마디 제안 = 마디 평균 RMS ≥ 곡 중앙값 −20 dB 인 마지막 마디 (6곡 중 2곡 1마디 차이 → 사람 확정)
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, estimate_offset
from .model import LTR, Bar, Chart

ENV = "mid"
BPM_TOL = 0.02
OFF8_MIN, OFF16_MAX = 1.4, 1.25
OFFSET_RANGE = (-0.045, -0.010)
DRIFT_SLOPE_MAX = 10.0       # ms / 100 마디
DRIFT_RANGE_WARN = 15.0      # ms
DOWNBEAT_MARGIN_WARN = 1.05
REGION_DB = 20.0


@dataclass
class Check:
    code: str
    level: str            # STOP | WARN | INFO
    passed: bool
    message: str


@dataclass
class AlignResult:
    bpm: int
    sweep_peak: float
    offset: float
    duration: float
    strengths: dict[str, float]
    windows: list[tuple[int, float]]
    downbeat_scores: list[float]
    downbeat_margin: float
    last_bar_proposal: int
    first_sound: float
    checks: list[Check] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not any(c.level == "STOP" and not c.passed for c in self.checks)

    @property
    def bar_s(self) -> float:
        return 240.0 / self.bpm

    def to_json(self) -> dict:
        return {"bpm": self.bpm, "sweep_peak": round(self.sweep_peak, 3), "offset_ms": round(self.offset * 1000, 1),
                "duration": round(self.duration, 3), "duration_bars": round(self.duration / self.bar_s, 3),
                "strengths": {k: round(v, 3) for k, v in self.strengths.items()},
                "windows": [{"bar": b, "offset_ms": round(o * 1000, 1)} for b, o in self.windows],
                "downbeat_scores": [round(x, 3) for x in self.downbeat_scores], "downbeat_margin": round(self.downbeat_margin, 3),
                "last_bar_proposal": self.last_bar_proposal, "first_sound": round(self.first_sound, 3), "ok": self.ok,
                "checks": [{"code": c.code, "level": c.level, "passed": c.passed, "message": c.message} for c in self.checks]}


def _med(feat: AudioFeatures, env: str = ENV) -> float:
    return float(np.median(feat.env[env])) or 1e-9


def bpm_sweep(feat: AudioFeatures, bpm: float, span: float = 2.0, step: float = 0.01, env: str = ENV,
              phase_step: float = 0.002) -> tuple[float, list[tuple[float, float]]]:
    """8분 빗 세기(위상 최대)를 bpm±span 에서 훑는다 → (정점 BPM, [(BPM, 세기/중앙값)])."""
    med = _med(feat, env)
    curve = []
    for c in np.arange(bpm - span, bpm + span + step / 2, step):
        period = 30.0 / c
        phases = np.arange(0.0, period, phase_step)
        k = np.arange(0.0, feat.duration - period, period)
        t = phases[:, None] + k[None, :]
        vals = feat.at(env, t.ravel()).reshape(t.shape).mean(axis=1)
        curve.append((float(c), float(vals.max()) / med))
    best = max(curve, key=lambda x: x[1])
    return best[0], curve


def quarter_chart(n_bars: int) -> Chart:
    """1..n 마디에 4분마다 탭 — 분석 오프셋을 구하는 데만 쓴다."""
    ch = Chart()
    for b in range(1, n_bars + 1):
        bar = Bar(b)
        bar.dirs[0] = LTR
        for k in range(4):
            bar.cells[1][Fraction(k, 4)] = "1"
        ch.bars[b] = bar
    return ch


def metronome_chart(n_bars: int) -> Chart:
    """미리듣기용: 1박은 위 레인(높은 클릭), 나머지 박은 아래 레인(낮은 클릭)."""
    ch = Chart()
    for b in range(1, n_bars + 1):
        bar = Bar(b)
        bar.dirs[0] = LTR
        bar.cells[1][Fraction(0)] = "1"
        for k in (1, 2, 3):
            bar.cells[2][Fraction(k, 4)] = "1"
        ch.bars[b] = bar
    return ch


def _strengths(feat: AudioFeatures, bpm: float, offset: float) -> dict[str, float]:
    beat = 60.0 / bpm
    on_t = np.arange(offset, feat.duration - beat, beat)
    on_t = on_t[on_t > 0]
    med = _med(feat)
    s = lambda d: float(feat.at(ENV, on_t + d).mean()) / med
    return {"on": s(0.0), "off8": s(beat / 2), "off16": (s(beat / 4) + s(3 * beat / 4)) / 2,
            "triplet": (s(beat / 3) + s(2 * beat / 3)) / 2}


def _windows(feat: AudioFeatures, bpm: float, offset: float, n_bars: int, size: int = 8) -> list[tuple[int, float]]:
    bar_s, beat = 240.0 / bpm, 60.0 / bpm
    grid = np.arange(-0.04, 0.0405, 0.001)
    out = []
    for start in range(1, n_bars + 1, size):
        end = min(n_bars, start + size - 1)
        tt = np.array([(b - 1) * bar_s + k * beat for b in range(start, end + 1) for k in range(4)])
        if len(tt) < 16:
            continue
        sc = [float(feat.at(ENV, tt + offset + g).mean()) for g in grid]
        out.append((start, float(offset + grid[int(np.argmax(sc))])))
    return out


def _downbeat(feat: AudioFeatures, bpm: float, offset: float, n_bars: int) -> list[float]:
    """박 위치별(1–4박) 평균 멜 변화: 박 뒤 1박 평균 − 박 앞 1박 평균의 |차| 를 멜 대역 평균."""
    if feat.mel_db is None:
        return [0.0, 0.0, 0.0, 0.0]
    bar_s, beat = 240.0 / bpm, 60.0 / bpm
    fr, mel = feat.times, feat.mel_db

    def seg(a: float, b: float):
        i, j = np.searchsorted(fr, a), np.searchsorted(fr, b)
        return mel[:, i:j].mean(axis=1) if j > i else None
    acc: list[list[float]] = [[], [], [], []]
    for b in range(1, n_bars):
        for k in range(4):
            t = (b - 1) * bar_s + k * beat + offset
            x, y = seg(t - beat, t), seg(t, t + beat)
            if x is not None and y is not None:
                acc[k].append(float(np.abs(y - x).mean()))
    return [float(np.mean(v)) if v else 0.0 for v in acc]


def _region(feat: AudioFeatures, bpm: float) -> tuple[int, float]:
    bar_s = 240.0 / bpm
    rms, fr = feat.env["rms_db"], feat.times
    med = float(np.median(rms))
    last = 1
    for b in range(1, int(np.ceil(feat.duration / bar_s)) + 1):
        i, j = np.searchsorted(fr, (b - 1) * bar_s), np.searchsorted(fr, b * bar_s)
        if j > i and float(rms[i:j].mean()) >= med - REGION_DB:
            last = b
    first = float(fr[int(np.argmax(rms >= float(rms.max()) - 40.0))])
    return last, first


def check_alignment(feat: AudioFeatures, bpm: int) -> AlignResult:
    bar_s = 240.0 / bpm
    n_bars = max(1, int(feat.duration / bar_s))
    peak, _curve = bpm_sweep(feat, bpm)
    est = estimate_offset(feat, quarter_chart(n_bars), bpm)
    offset = est.offset
    st = _strengths(feat, bpm, offset)
    wins = _windows(feat, bpm, offset, n_bars)
    db = _downbeat(feat, bpm, offset, n_bars)
    others = max(db[1:]) or 1e-9
    margin = db[0] / others
    last, first = _region(feat, bpm)
    res = AlignResult(bpm=bpm, sweep_peak=peak, offset=offset, duration=feat.duration, strengths=st, windows=wins,
                      downbeat_scores=db, downbeat_margin=margin, last_bar_proposal=last, first_sound=first)
    add = lambda code, level, ok, msg: res.checks.append(Check(code, level, bool(ok), msg))

    add("A01", "STOP", abs(peak - bpm) <= BPM_TOL + 1e-9,
        f"BPM {bpm} 검증: 8분 빗 정점 {peak:.2f}" + ("" if abs(peak - bpm) <= BPM_TOL + 1e-9 else
        f" — {bpm} 과 {abs(peak - bpm):.2f} 차이. BPM 이 틀렸거나 정수가 아님(게임은 정수 BPM 만)"))
    add("A02", "STOP", st["off8"] >= OFF8_MIN, f"배속: 8분 엇박 ×{st['off8']:.2f} (≥{OFF8_MIN})"
        + ("" if st["off8"] >= OFF8_MIN else " — 8분 엇박이 약함: BPM 이 실제의 두 배일 수 있음"))
    add("A02b", "WARN", st["off16"] <= OFF16_MAX, f"16분 엇박 ×{st['off16']:.2f} (≤{OFF16_MAX}, 8분 엇박 대비 {st['off16'] / max(1e-9, st['off8']):.2f})"
        + ("" if st["off16"] <= OFF16_MAX else " — 16분 소리(하이햇 등)가 강함. BPM 을 절반으로 볼 가능성도 있으니 메트로놈으로 확인"))
    add("A08", "STOP", st["triplet"] < st["off8"], f"셋잇단·셔플: 1/3·2/3 박 ×{st['triplet']:.2f} vs 8분 엇박 ×{st['off8']:.2f}"
        + ("" if st["triplet"] < st["off8"] else " — 셋잇단 느낌이 강해 8분 격자로 찍기 어려움"))
    lo, hi = OFFSET_RANGE
    edge = abs(offset - (-0.06)) < 1e-9 or abs(offset - 0.04) < 1e-9
    ok3 = lo <= offset <= hi and not edge
    add("A03", "STOP", ok3, f"위상: 분석 오프셋 {offset * 1000:+.0f} ms (기존 곡 −18…−32, 허용 {lo * 1000:.0f}…{hi * 1000:.0f})"
        + ("" if ok3 else " — 1마디가 음원 0초에서 시작하지 않음. 음원 앞부분을 패딩·트림해 박을 0초에 맞출 것"))
    add("A03b", "WARN", st["on"] >= st["off8"], f"정박 ×{st['on']:.2f} vs 8분 엇박 ×{st['off8']:.2f}"
        + ("" if st["on"] >= st["off8"] else " — 엇박이 더 강함: 반 박 밀렸을 수 있으니 메트로놈으로 확인"))
    if len(wins) >= 3:
        xs = np.array([w[0] for w in wins], dtype=float)
        ys = np.array([w[1] for w in wins]) * 1000
        slope = float(np.polyfit(xs, ys, 1)[0] * 100)
        rng = float(ys.max() - ys.min())
    else:
        slope, rng = 0.0, 0.0
    add("A04", "STOP", abs(slope) <= DRIFT_SLOPE_MAX, f"템포 흔들림: 8마디 창 오프셋 기울기 {slope:+.1f} ms/100마디 (≤{DRIFT_SLOPE_MAX:.0f})"
        + ("" if abs(slope) <= DRIFT_SLOPE_MAX else " — 템포가 변하거나 BPM 이 조금 틀림"))
    add("A04b", "WARN", rng <= DRIFT_RANGE_WARN, f"창별 오프셋 범위 {rng:.0f} ms" + ("" if rng <= DRIFT_RANGE_WARN else " — 구간마다 박이 흔들림(녹음 템포 흔들림·잡음), 듣고 확인"))
    top = int(np.argmax(db))
    add("A05", "WARN", top == 0 and margin >= DOWNBEAT_MARGIN_WARN,
        f"첫 박: 박별 멜 변화 {', '.join(f'{x:.2f}' for x in db)} (1박/다른 박 최대 = {margin:.2f})"
        + ("" if top == 0 else f" — {top + 1}박이 가장 큼: 마디 시작이 {top}박 밀렸을 수 있음")
        + ("" if top != 0 or margin >= DOWNBEAT_MARGIN_WARN else " — 차이가 작음"))
    frac = (feat.duration / bar_s) % 0.5
    add("A06", "INFO", min(frac, 0.5 - frac) < 0.01, f"음원 길이 {feat.duration:.2f}초 = {feat.duration / bar_s:.3f}마디"
        + ("" if min(frac, 0.5 - frac) < 0.01 else " — 마디에 딱 맞지 않음 (DAW 에서 이 BPM 으로 만든 음원이 아닐 수 있음)"))
    pickup = first > bar_s / 4
    add("A07", "INFO", True, f"구간 제안: 1–{last}마디 (마지막 = RMS 가 곡 중앙값 −{REGION_DB:.0f}dB 이상인 마지막 마디, 1마디 차이 날 수 있음). "
        f"첫 소리 {first:.2f}초" + (" — 1마디 시작보다 1/4 마디 넘게 늦음(못갖춘마디)" if pickup else ""))
    return res


def report_markdown(title: str, res: AlignResult, audio_name: str, clips: list[str]) -> str:
    mark = {True: "통과", False: "실패"}
    out = [f"# {title} 음원 준비 검사 (align)", "",
           f"- 음원 `{audio_name}`, BPM {res.bpm}, 길이 {res.duration:.2f}초 ({res.duration / res.bar_s:.2f}마디)",
           f"- 분석 오프셋 {res.offset * 1000:+.0f} ms — 음원 특징을 읽을 때만 쓴다 (채보 시각은 바꾸지 않음)",
           f"- 결과: **{'다음 단계로 갈 수 있음' if res.ok else '중지 — 아래 실패 항목을 먼저 해결'}**", "",
           "| 코드 | 종류 | 결과 | 내용 |", "|---|---|---|---|"]
    for c in res.checks:
        out.append(f"| {c.code} | {c.level} | {mark[c.passed] if c.level != 'INFO' else '참고'} | {c.message} |")
    out += ["", "## 사람이 확인할 것", "",
            "1. 메트로놈 클립을 듣는다. 높은 클릭(2kHz)이 마디 첫 박, 낮은 클릭(1.2kHz)이 나머지 박이다.",
            "   클릭이 음악의 박과 맞고 높은 클릭이 마디 시작에 오면 `songs/000N.yaml` 에 `downbeat_ok: true` 를 적는다.",
            f"2. 마지막 마디를 정해 `region: {{last_bar: {res.last_bar_proposal}}}` 처럼 적는다 (제안 {res.last_bar_proposal}). "
            "기존 곡은 소리가 끝나기 1–3마디 전에 채보가 끝난다.",
            "3. 박이 0초에 맞지 않으면(A03 실패) 음원 앞부분을 패딩·트림하고 다시 align 한다. 도구는 음원을 고치지 않는다.", ""]
    if clips:
        out += ["미리듣기: " + ", ".join(f"`{c}`" for c in clips), ""]
    out += ["## 8마디 창별 오프셋", "", " ".join(f"{b:03d}:{o * 1000:+.0f}" for b, o in res.windows), ""]
    return "\n".join(out)
