"""음원 기반 리듬 변주 감지 (당김음·16분·셋잇단).

감지기 (기존 곡으로 잰 값, 리듬 분석 2026-10-08):
  ANTIC   8분 당김음 점수 A = 엇박 칸 백분위 − 다음 박 백분위 (중역·화성 중역 평균).
          1·4·5·6번 노멀에서 사람이 넣은 당김음과 AUC .80–.89. A ≥ 40 이면 사람 수와 비슷하게 골라짐, A ≥ 60 은 정밀도 .61–.89
  ODD16   16분 홀수 칸 중역 백분위 ≥ 50 이고 양옆 8분 칸보다 큼 (1번 노멀 16분 마디 정밀도 .38, 기준 .13)
  TRIPLET 박의 1/3·2/3 지점(±10ms 점값)이 그 박의 1/4·1/2·3/4 지점보다 모두 크고 중앙값 ×0.8 이상 → 그 박은 셋잇단.
          7곡 중 1번 16마디만 걸림 (드럼 셋잇단만 잡힘, 보컬 셋잇단은 못 잡음)
음형 묶음: 16칸 음원 모양(중역)이 비슷한 마디(코사인 ≥ 0.9)를 묶어 같은 묶음은 같이 결정한다 (7번 리프가 마디마다 달라지지 않게).
"""
from __future__ import annotations

from dataclasses import dataclass
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, Percentiles, cell_value
from .timing import audio_time

EIGHTH = Fraction(1, 8)
SIXTEENTH = Fraction(1, 16)
ANTIC_EASY = 60.0
ANTIC_NORMAL = 40.0
ODD16_PCT = 50.0
TRIPLET_MED = 0.8
CLUSTER_COS = 0.9


@dataclass
class RhythmCtx:
    feat: AudioFeatures
    bpm: float
    offset: float
    last_bar: int
    pct_mid: Percentiles
    pct_harm: Percentiles
    med_mid: float

    def raw(self, env: str, b: int, pos: Fraction, window: float = 0.025) -> float:
        return cell_value(self.feat, env, b, pos, self.bpm, self.offset, window)

    def pm(self, b: int, pos: Fraction) -> float:
        return self.pct_mid(self.raw("mid", b, pos))

    def both(self, b: int, pos: Fraction) -> float:
        return (self.pm(b, pos) + self.pct_harm(self.raw("harm_mid", b, pos))) / 2


def make_ctx(feat: AudioFeatures, bpm: float, offset: float, last_bar: int) -> RhythmCtx:
    return RhythmCtx(feat, bpm, offset, last_bar, Percentiles(feat, "mid", last_bar, bpm, offset),
                     Percentiles(feat, "harm_mid", last_bar, bpm, offset), float(np.median(feat.env["mid"])) or 1e-9)


def _next_beat(b: int, pos: Fraction) -> tuple[int, Fraction]:
    q = pos + EIGHTH
    return (b + 1, Fraction(0)) if q >= 1 else (b, q)


def antic_score(ctx: RhythmCtx, b: int, pos: Fraction) -> float:
    """엇박 8분 칸 pos 의 당김음 점수 A (엇박이 다음 박보다 얼마나 큰지)."""
    nb, nq = _next_beat(b, pos)
    if nb > ctx.last_bar:
        return 0.0
    return ctx.both(b, pos) - ctx.both(nb, nq)


def antic_cells(ctx: RhythmCtx, b: int, threshold: float, in_bar: bool = True) -> list[tuple[Fraction, float]]:
    """마디 b 의 당김음 후보 [(엇박 위치, A)] — in_bar 면 다음 박이 같은 마디인 1/8·3/8·5/8 만."""
    out = []
    for k in ((1, 3, 5) if in_bar else (1, 3, 5, 7)):
        pos = Fraction(k, 8)
        a = antic_score(ctx, b, pos)
        if a >= threshold:
            out.append((pos, a))
    return out


def odd16_peaks(ctx: RhythmCtx, b: int) -> list[tuple[Fraction, float]]:
    """16분 홀수 칸 정점 [(위치, 백분위)]: 백분위 ≥ 50 이고 양옆 8분 칸보다 큼."""
    out = []
    for k in range(1, 16, 2):
        pos = Fraction(k, 16)
        v = ctx.raw("mid", b, pos)
        left = ctx.raw("mid", b, pos - SIXTEENTH)
        nb, nq = (b + 1, Fraction(0)) if pos + SIXTEENTH >= 1 else (b, pos + SIXTEENTH)
        right = ctx.raw("mid", nb, nq) if nb <= ctx.last_bar else 0.0
        p = ctx.pct_mid(v)
        if p >= ODD16_PCT and v > left and v > right:
            out.append((pos, p))
    return out


def triplet_beats(ctx: RhythmCtx, b: int) -> list[int]:
    """셋잇단으로 들리는 박 번호(0–3)."""
    out = []
    t = lambda q: float(ctx.feat.at("mid", audio_time(b, q, ctx.bpm, ctx.offset), 0.010)[0])
    for j in range(4):
        s = Fraction(j, 4)
        t1, t2 = t(s + Fraction(1, 12)), t(s + Fraction(2, 12))
        duple = [t(s + Fraction(1, 16)), t(s + EIGHTH), t(s + Fraction(3, 16))]
        if min(t1, t2) > max(duple) and min(t1, t2) >= TRIPLET_MED * ctx.med_mid:
            out.append(j)
    return out


def triplet_bars(ctx: RhythmCtx, bars) -> dict[int, list[int]]:
    """셋잇단 마디(셋잇단 박이 2개 이상) → 박 번호들."""
    out = {}
    for b in bars:
        js = triplet_beats(ctx, b)
        if len(js) >= 2:
            out[b] = js
    return out


def profile16(ctx: RhythmCtx, b: int) -> np.ndarray:
    return np.array([ctx.raw("mid", b, Fraction(k, 16)) for k in range(16)], dtype=float)


def clusters(ctx: RhythmCtx, bars: list[int], cos: float = CLUSTER_COS) -> dict[int, int]:
    """16칸 모양이 비슷한 마디 묶음: 마디 → 묶음 번호 (가장 앞 마디 번호)."""
    vec = {}
    for b in bars:
        v = profile16(ctx, b)
        v = v - v.mean()
        n = np.linalg.norm(v)
        vec[b] = v / n if n > 0 else v
    parent = {b: b for b in bars}

    def root(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for i, a in enumerate(bars):
        for c in bars[i + 1:]:
            if float(vec[a] @ vec[c]) >= cos:
                ra, rc = root(a), root(c)
                if ra != rc:
                    parent[max(ra, rc)] = min(ra, rc)
    return {b: root(b) for b in bars}
