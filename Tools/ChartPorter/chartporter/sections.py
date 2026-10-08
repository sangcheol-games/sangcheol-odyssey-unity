"""음원 + 채보로 반복 구간(A, A, A', B …) 찾기.

마디마다 8분 단위로 log-mel, 크로마(화성 65–2100Hz / 멜로디 250–1500Hz), 3대역 onset 패턴을 뽑아
두 마디를 비교한다.
  dB      : log-mel 평균 절대 차 (같은 음원 조각이면 ≈0)
  comb    : 화성 크로마 · MFCC · onset 패턴 유사도 평균 (반주가 같은지)
  mid     : 멜로디 크로마 유사도, 4마디 구문 평균, 12 조옮김 중 최대 (멜로디가 같은지)
관계 (Phase 1 분석에서 3번 곡의 알려진 12개 관계를 모두 맞춘 기준):
  IDENTICAL     dB ≤ 1.0                                    → 그대로 복사
  SAME_BACKING  comb ≥ 0.85, dB ≤ 3.5, mid ≥ 0.85            → 복사 후 반전·1–2개 변주
  SIMILAR       mid ≥ max(0.45, 곡 p90)                      → 리듬 뼈대만 참고, 다시 찍기
결과는 제안일 뿐이며, 사용자가 확인한 것만 songs/000N.yaml 의 sections 에 넣는다.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, grid_values
from .model import Chart, LANES, cross_lane, mirror_lane
from .timing import audio_time, bar_seconds

RANK = {"DIFFERENT": 0, "SIMILAR": 1, "SAME_BACKING": 2, "IDENTICAL": 3}
LABEL = {"IDENTICAL": "같음", "SAME_BACKING": "반주 같음", "SIMILAR": "비슷함(멜로디)", "DIFFERENT": "다름", "MIXED": "같음/반주 같음"}
MIN_LAG = 8          # 구간 단위 반복만 (구간 안 4마디 리프 반복은 제외)
MIN_RUN = 3
EDGE_DB = 5.0


@dataclass
class BarFeatures:
    bars: list[int]
    mel: np.ndarray          # B × 8 × N_MELS
    chroma_full: np.ndarray  # B × 8 × 12
    chroma_mid: np.ndarray   # B × 8 × 12
    onset: np.ndarray        # B × (16 × 3)
    rms_db: np.ndarray       # B


def _seg_mean(mat: np.ndarray, times: np.ndarray, t0: float, t1: float) -> np.ndarray:
    a, b = np.searchsorted(times, t0), np.searchsorted(times, t1)
    if b <= a:
        b = min(a + 1, mat.shape[1])
    return mat[:, a:b].mean(axis=1)


def bar_features(feat: AudioFeatures, last_bar: int, bpm: float, offset: float) -> BarFeatures:
    bar_s = bar_seconds(bpm)
    bars = [b for b in range(1, last_bar + 1) if audio_time(b + 1, 0, bpm) <= feat.duration + bar_s / 2]
    mel, cf, cm = [], [], []
    for b in bars:
        t0 = audio_time(b, 0, bpm, offset)
        segs = [(t0 + k * bar_s / 8, t0 + (k + 1) * bar_s / 8) for k in range(8)]
        mel.append([_seg_mean(feat.mel_db, feat.times, a, c) for a, c in segs])
        cf.append([_seg_mean(feat.chroma_full, feat.times, a, c) for a, c in segs])
        cm.append([_seg_mean(feat.chroma, feat.times, a, c) for a, c in segs])
    onset = np.concatenate([grid_values(feat, e, bars, 16, bpm, offset) for e in ("low", "mid", "high")], axis=1)
    mel = np.array(mel, dtype=np.float32)
    return BarFeatures(bars=bars, mel=mel, chroma_full=np.array(cf, dtype=np.float32), chroma_mid=np.array(cm, dtype=np.float32),
                       onset=onset.astype(np.float32), rms_db=mel.mean(axis=(1, 2)))


def _unit(x: np.ndarray, axis=-1) -> np.ndarray:
    n = np.linalg.norm(x, axis=axis, keepdims=True)
    return x / np.where(n == 0, 1, n)


def _centered(c: np.ndarray) -> np.ndarray:
    return _unit(c - c.mean(axis=-1, keepdims=True))


@dataclass
class Similarity:
    db: np.ndarray        # B × B
    comb: np.ndarray      # B × B
    mid: np.ndarray       # B × B (조옮김 최대)
    mid_shift: np.ndarray  # B × B (최대가 된 반음 이동)
    mid_phrase: np.ndarray  # B × B (4마디 구문 평균)
    mid_threshold: float


def similarity(bf: BarFeatures) -> Similarity:
    B = len(bf.bars)
    db = np.abs(bf.mel[:, None] - bf.mel[None, :]).mean(axis=(2, 3))
    cf = _centered(bf.chroma_full)
    chroma_full_by_shift = np.stack([np.einsum("ask,bsk->ab", cf, np.roll(cf, s, axis=-1)) / 8 for s in range(12)])
    from scipy.fft import dct
    mfcc = dct(bf.mel, type=2, norm="ortho", axis=-1)[:, :, 1:14].reshape(B, -1)
    mf = _unit(mfcc - mfcc.mean(axis=0))
    mfcc_sim = mf @ mf.T
    on = _unit(bf.onset)
    onset_sim = on @ on.T
    cm = _centered(bf.chroma_mid)                       # B × 8 × 12
    mids = np.stack([np.einsum("ask,bsk->ab", cm, np.roll(cm, s, axis=-1)) / 8 for s in range(12)])
    mid = mids.max(axis=0)
    shift = mids.argmax(axis=0)
    shift = np.where(mid - mids[0] < 0.3, 0, shift)      # 조옮김은 0반음보다 0.3 이상 좋을 때만
    mid = np.where(shift == 0, mids[0], mid)
    chroma_sim = np.take_along_axis(chroma_full_by_shift, shift[None], axis=0)[0]   # 화성도 같은 반음 이동으로 비교
    comb = (chroma_sim + mfcc_sim + onset_sim) / 3
    phrase = np.full_like(mid, -1.0)
    for i in range(B):
        for j in range(i + 1, B):
            best = -1.0
            for k0 in range(-3, 1):          # j 를 포함하는 4마디 창 중 최대
                ks = [k for k in range(k0, k0 + 4) if 0 <= i + k < B and 0 <= j + k < B and i + k < j + k]
                if len(ks) >= 2:
                    best = max(best, float(np.mean([mid[i + k, j + k] for k in ks])))
            phrase[i, j] = phrase[j, i] = best
    iu = np.triu_indices(B, k=MIN_LAG)
    thr = max(0.45, float(np.percentile(phrase[iu], 90))) if len(iu[0]) else 0.45
    return Similarity(db=db, comb=comb, mid=mid, mid_shift=shift, mid_phrase=phrase, mid_threshold=thr)


def classify(sim: Similarity, i: int, j: int) -> str:
    db, comb, mid = sim.db[i, j], sim.comb[i, j], sim.mid_phrase[i, j]
    if db <= 1.0:
        return "IDENTICAL"
    if comb >= 0.85 and db <= 3.5 and mid >= 0.85:
        return "SAME_BACKING"
    if mid >= sim.mid_threshold:
        return "SIMILAR"
    return "DIFFERENT"


# ---------------- 채보 비교 ----------------

def _bar_cells(chart: Chart, b: int, mirror: bool = False) -> frozenset:
    bar = chart.bars.get(b)
    if bar is None:
        return frozenset()
    m = (lambda l: mirror_lane(l)) if mirror else (lambda l: l)
    return frozenset((p, m(l), c) for l in LANES for p, c in bar.cells[l].items() if c != "0")


def chart_relation(chart: Chart, a: int, b: int) -> str:
    """S = 같음, M = 위아래 반전, C = 다른 그룹 같은 높이(홀수 간격 복사), R = 리듬만 같음,
    숫자 = 노트 시각 겹침(자카드 ×10), - = 둘 다 비어 있음."""
    ca, cb = _bar_cells(chart, a), _bar_cells(chart, b)
    if not ca and not cb:
        return "-"
    if ca == cb:
        return "S"
    if _bar_cells(chart, a, mirror=True) == cb:
        return "M"
    if frozenset((p, cross_lane(l), c) for p, l, c in ca) == cb:
        return "C"
    ta, tb = {p for p, _l, c in ca if c in "125"}, {p for p, _l, c in cb if c in "125"}
    if ta and ta == tb:
        return "R"
    union = ta | tb
    return str(round(len(ta & tb) / len(union) * 10)) if union else "-"


# ---------------- 관계 찾기 ----------------

@dataclass
class Relation:
    target: tuple[int, int]
    source: tuple[int, int]
    klass: str                     # 구간 안 가장 많은 관계
    per_bar: list[str]
    transpose: int = 0
    edges: list[int] = field(default_factory=list)       # 복사에서 뺄 끝 마디 (target 마디 번호)
    chart: list[str] = field(default_factory=list)       # 마디별 노멀 채보 관계
    db: float = 0.0
    comb: float = 0.0
    mid: float = 0.0

    @property
    def lag(self) -> int:
        return self.target[0] - self.source[0]

    @property
    def reused_pattern_bars(self) -> list[int]:
        """노멀 채보는 같은데(S/M) 음원은 다른 마디 (팀원이 패턴만 재사용한 경우)."""
        return [self.target[0] + k for k, (c, r) in enumerate(zip(self.per_bar, self.chart)) if c == "DIFFERENT" and r in ("S", "M")]


def find_relations(bf: BarFeatures, sim: Similarity, chart: Chart | None = None) -> list[Relation]:
    B = len(bf.bars)
    best: dict[int, tuple[int, int, int]] = {}   # j → (rank, run_len, i)
    for lag in range(MIN_LAG, B):
        cls = [classify(sim, j - lag, j) for j in range(lag, B)]
        j = 0
        while j < len(cls):
            if RANK[cls[j]] == 0:
                j += 1
                continue
            k = j
            while k < len(cls) and RANK[cls[k]] > 0:
                k += 1
            run = range(j + lag, k + lag)   # target 인덱스
            if len(run) >= MIN_RUN:
                for t in run:
                    r = RANK[cls[t - lag]]
                    cand = (r, len(run), lag)   # 같은 등급이면 긴 구간, 그다음 먼 원본(더 앞의 베이스)
                    if t not in best or cand > best[t]:
                        best[t] = cand
            j = k
    # 원본이 이미 다른 마디의 복사본(같음/반주 같음)이면 맨 앞 원본으로 따라간다 (098 ← 050 ← 002 → 098 ← 002)
    def root(t: int, lag: int) -> int:
        src = t - lag
        seen = 0
        while src in best and best[src][0] >= RANK["SAME_BACKING"] and seen < B:
            src -= best[src][2]
            seen += 1
        return t - src

    lag_of = {t: (root(t, v[2]) if v[0] >= RANK["SAME_BACKING"] else v[2]) for t, v in best.items()}
    cls_of = {t: classify(sim, t - lag_of[t], t) for t in best}
    # 같은 lag · 같은 관계인 연속 마디를 묶는다
    rels: list[Relation] = []
    bn = bf.bars
    t = 0
    while t < B:
        if t not in best or cls_of[t] == "DIFFERENT":
            t += 1
            continue
        lag, kl = lag_of[t], cls_of[t]
        u = t
        while u + 1 < B and u + 1 in best and lag_of[u + 1] == lag and cls_of[u + 1] == kl:
            u += 1
        idx = list(range(t, u + 1))
        # 반음 이동 s 는 '대상을 s 만큼 돌리면 원본과 같음' → 대상이 원본보다 (−s) 반음 높음 (−6..+5 로 표시)
        shifts = [((-int(sim.mid_shift[x - lag, x])) % 12 + 6) % 12 - 6 for x in idx]
        transpose = max(set(shifts), key=shifts.count)
        rel = Relation(target=(bn[t], bn[u]), source=(bn[t - lag], bn[u - lag]), klass=kl, per_bar=[kl] * len(idx),
                       transpose=transpose if shifts.count(transpose) > len(shifts) / 2 else 0,
                       db=float(np.mean([sim.db[x - lag, x] for x in idx])),
                       comb=float(np.mean([sim.comb[x - lag, x] for x in idx])),
                       mid=float(np.mean([sim.mid_phrase[x - lag, x] for x in idx])))
        if chart is not None:
            rel.chart = [chart_relation(chart, bn[x - lag], bn[x]) for x in idx]
        rels.append(rel)
        t = u + 1
    return rels


def novelty_boundaries(sim: Similarity, bars: list[int], half: int = 4, min_gap: int = 4) -> list[int]:
    """Foote novelty: 종합 유사도 행렬 대각선 위 체커보드 커널. 변화가 큰 마디(구간 시작)를 고른다."""
    S = (sim.comb + 1) / 2 - np.clip(sim.db / 20.0, 0, 1)
    B = len(bars)
    k = np.ones((2 * half, 2 * half))
    k[:half, half:] = k[half:, :half] = -1
    nov = np.zeros(B)
    for i in range(half, B - half + 1):
        nov[i] = float((S[i - half:i + half, i - half:i + half] * k).sum())
    thr = float(np.median(nov[nov != 0]) + np.std(nov[nov != 0])) if np.any(nov) else 0
    peaks = []
    for i in np.argsort(-nov):
        if nov[i] <= thr:
            break
        if all(abs(i - j) >= min_gap for j in peaks):
            peaks.append(int(i))
    return sorted(bars[i] for i in peaks)


def runs_of(rels: list[Relation]) -> list[list[Relation]]:
    """같은 원본 간격(lag)으로 이어지는 관계 조각들을 한 묶음으로. 짧은 잡음 묶음은 버린다."""
    runs: list[list[Relation]] = []
    for r in sorted(rels, key=lambda r: r.target[0]):
        if runs and runs[-1][-1].lag == r.lag and runs[-1][-1].target[1] + 1 == r.target[0]:
            runs[-1].append(r)
        else:
            runs.append([r])
    keep = []
    for run in runs:
        n = run[-1].target[1] - run[0].target[0] + 1
        strong = any(r.klass in ("IDENTICAL", "SAME_BACKING") for r in run)
        if n >= MIN_RUN and (strong or n >= 4):
            keep.append(run)
    return keep


def section_map(bf: BarFeatures, rels: list[Relation], sim: Similarity | None = None) -> list[dict]:
    """마디 범위별 이름표. 새 소재는 음원 변화점으로 나눠 A, B, C…, 반복은 원본 마디의 이름(같음이면 그대로, 아니면 ')."""
    bars = bf.bars
    runs = runs_of(rels)
    piece_of: dict[int, Relation] = {}
    run_of: dict[int, int] = {}
    for k, run in enumerate(runs):
        for r in run:
            for b in range(r.target[0], r.target[1] + 1):
                piece_of[b], run_of[b] = r, k
    cuts = set(novelty_boundaries(sim, bars)) if sim is not None else set()
    letters = iter("ABCDEFGHIJKLMNOPQRSTUVWXYZ")
    label_of: dict[int, str] = {}
    rows: list[dict] = []
    for b in bars:
        r = piece_of.get(b)
        prev = rows[-1] if rows else None
        if r is None:
            if prev is None or prev["kind"] != "new" or b in cuts:
                prev = {"bars": [b, b], "kind": "new", "label": next(letters)}
                rows.append(prev)
            prev["bars"][1] = b
            label_of[b] = prev["label"]
            continue
        base = label_of.get(b - r.lag, "?").rstrip("'")
        exact = r.klass == "IDENTICAL"
        same_kind = prev is not None and prev["kind"] == "copy" and (
            prev["exact"] == exact or {prev["kinds"][-1], r.klass} <= {"IDENTICAL", "SAME_BACKING"})
        if prev is not None and prev["kind"] == "copy" and prev["run"] == run_of[b] and prev["base"] == base and same_kind:
            prev["bars"][1] = b
            prev["kinds"].append(r.klass)
            prev["exact"] = prev["exact"] and exact
            if r not in prev["parts"]:
                prev["parts"].append(r)
        else:
            prev = {"bars": [b, b], "kind": "copy", "run": run_of[b], "base": base, "exact": exact,
                    "kinds": [r.klass], "parts": [r], "lag": r.lag}
            rows.append(prev)
        prev["label"] = base + ("" if prev["exact"] else "'")
        label_of[b] = prev["label"]
    return rows


def format_report(song_title: str, bf: BarFeatures, sim: Similarity, rels: list[Relation]) -> str:
    rows = section_map(bf, rels, sim)
    out = [f"# {song_title} 구간 맵 제안", "",
           "음원으로 찾은 반복 관계다. **제안일 뿐이며**, 맞는 것만 `songs/000N.yaml` 의 `sections` 에 옮긴다.",
           f"기준: 같음 = log-mel 차 ≤ 1dB, 반주 같음 = 종합 ≥ 0.85·dB ≤ 3.5·멜로디 ≥ 0.85, 비슷함 = 멜로디 ≥ {sim.mid_threshold:.2f}(이 곡 기준선).",
           "새 소재는 음원 변화점으로 나눴다. 채보 열: S = 노멀이 같음, M = 위아래 반전, R = 리듬만 같음, 숫자 = 노트 시각 겹침(0–10).", "",
           "| 마디 | 이름 | 관계 | 원본 | 채보(노멀) | 참고 |", "|---|---|---|---|---|---|"]
    for row in rows:
        a, b = row["bars"]
        rng = f"{a:03d}–{b:03d}" if a != b else f"{a:03d}"
        if row["kind"] == "new":
            out.append(f"| {rng} | {row['label']} | 새 소재 | | | |")
            continue
        parts: list[Relation] = row["parts"]
        lag = row["lag"]
        kinds = row["kinds"]
        comp = ", ".join(f"{LABEL[k]} {kinds.count(k)}" for k in ("IDENTICAL", "SAME_BACKING", "SIMILAR") if k in kinds)
        chart_cols = [c for q in parts for x, c in zip(range(q.target[0], q.target[1] + 1), q.chart) if a <= x <= b]
        notes = []
        tr = sorted({q.transpose for q in parts if q.transpose})
        if tr:
            notes.append(", ".join(f"{t:+d}반음" for t in tr) + " 조옮김")
        reused = [x for q in parts for x in q.reused_pattern_bars if a <= x <= b]
        if reused:
            notes.append("노멀은 같은데 음원은 다름: " + ", ".join(f"{x:03d}" for x in reused))
        w = [q.target[1] - q.target[0] + 1 for q in parts]
        notes.append(f"dB {np.average([q.db for q in parts], weights=w):.1f} · 종합 {np.average([q.comb for q in parts], weights=w):.2f} · "
                     f"멜로디 {np.average([q.mid for q in parts], weights=w):.2f}")
        src = f"{a - lag:03d}–{b - lag:03d}" if a != b else f"{a - lag:03d}"
        out.append(f"| {rng} | {row['label']} | {comp} | {src} | {','.join(chart_cols)} | {'; '.join(notes)} |")
    return "\n".join(out) + "\n"


def proposal_yaml(rels: list[Relation]) -> list[dict]:
    """songs/000N.yaml 의 sections 형식 제안. 같음/반주 같음 관계만, 노멀 채보 관계(S=그대로, M=반전, C=다른 그룹)가 이어지는 구간으로 나눈다.
    노멀이 S/M/C 가 아닌 마디(리듬만 같음, 다름)는 복사 후보에서 빼고 'manual' 로 남긴다.
    채보가 없는 신규 곡은 마디 간격으로 정한다: 짝수 = same, 홀수 = cross (메인 라인 그룹이 바뀜)."""
    out = []
    for r in rels:
        if r.klass not in ("IDENTICAL", "SAME_BACKING"):
            continue
        lag = r.lag
        default = "cross" if lag % 2 else "same"
        cols = r.chart or ["?"] * (r.target[1] - r.target[0] + 1)
        kind_of = lambda c: default if c == "?" else {"S": "same", "M": "mirror", "C": "cross"}.get(c, "manual")
        k = 0
        while k < len(cols):
            kind = kind_of(cols[k])
            j = k
            while j + 1 < len(cols) and kind_of(cols[j + 1]) == kind:
                j += 1
            a, b = r.target[0] + k, r.target[0] + j
            entry = {"bars": f"{a}-{b}" if a != b else str(a),
                     "copy_of": f"{a - lag}-{b - lag}" if a != b else str(a - lag),
                     "transform": kind, "relation": r.klass.lower()}
            out.append(entry)
            k = j + 1
    # 원본 간격·변환이 같고 이어진 규칙은 하나로 (관계 등급은 합쳐 표시)
    merged: list[dict] = []
    for e in out:
        a, b = (int(x) for x in (e["bars"].split("-") + [e["bars"]])[:2])
        sa = int(e["copy_of"].split("-")[0])
        if merged:
            m = merged[-1]
            ma, mb = (int(x) for x in (m["bars"].split("-") + [m["bars"]])[:2])
            msa = int(m["copy_of"].split("-")[0])
            if m["transform"] == e["transform"] and mb + 1 == a and ma - msa == a - sa:
                m["bars"] = f"{ma}-{b}"
                m["copy_of"] = f"{msa}-{b - (a - sa)}"
                if e["relation"] not in m["relation"]:
                    m["relation"] += "+" + e["relation"]
                continue
        merged.append(dict(e))
    return merged
