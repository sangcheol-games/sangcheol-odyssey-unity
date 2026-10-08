"""청취 확인용 산출물: 확인 목록(checklist) 과 클릭음 미리듣기(preview).

확인 목록: 8분 그리드 밖 노트(16분 홀수 칸, 셋잇단 등)마다
  그 위치·앞칸·뒷칸의 음원 세기(중역 플럭스, 곡 8분 칸 기준 백분위)를 비교해
  "강한 소리가 한 칸 앞/뒤에 있음 → 밀렸을 수 있음" 같은 제안을 붙인다. 판단은 사람이 듣고 한다.
미리듣기: 원곡 구간에 노트 클릭음을 섞은 WAV. --shift 를 주면 제안대로 옮긴 B안도 만든다.
"""
from __future__ import annotations

import csv
from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path

import numpy as np

from .audio import AudioFeatures, Percentiles, cell_value, grid_values, load_audio
from .model import Chart, is_top
from .timing import audio_time, bar_seconds, is_on_grid

ENV = "mid"


def grid_of(pos: Fraction) -> int:
    """위치가 놓인 가장 거친 그리드 (16, 24, 32, 48 …)."""
    for d in (16, 24, 32, 48, 64, 96, 128):
        if (pos * d).denominator == 1:
            return d
    return pos.denominator


MATCH_TOL = 0.030   # 이 안에 소리 시작이 있으면 '그 칸에 소리 있음'


@dataclass
class CheckItem:
    bar: int
    lane: int
    pos: Fraction
    char: str
    grid: int
    time: float           # 음원 시각 (보정 없음, 게임 기준)
    nearest_ms: float     # 가장 가까운 소리 시작까지 (ms, 음수 = 앞)
    strength: float       # 그 소리의 세기 백분위 (곡 8분 칸 기준)
    class_rank: float     # 이 칸 세기의 순위: 곡 전체의 같은 종류 칸(8분 그리드 밖 같은 분할 칸) 중 백분위
    suggestion: str
    shift: int            # B안 이동 칸 수 (-1, 0, +1)

    @property
    def pos_label(self) -> str:
        return f"{int(self.pos * self.grid)}/{self.grid}"


def build_checklist(chart: Chart, feat: AudioFeatures, bpm: float, offset: float) -> list[CheckItem]:
    """8분 그리드 밖 노트마다 가장 가까운 소리 시작(librosa 피크)을 찾아,
    그 칸에 소리가 있는지 / 정확히 한 칸 앞·뒤에만 있는지(밀렸을 가능성)를 표시한다."""
    pct = Percentiles(feat, ENV, chart.last_bar(), bpm, offset)
    bars_in_audio = [b for b in range(1, chart.last_bar() + 1) if (b - 1) * bar_seconds(bpm) < feat.duration]
    class_ref: dict[int, np.ndarray] = {}

    def class_rank(g: int, value: float) -> float:
        if g not in class_ref:
            vals = grid_values(feat, ENV, bars_in_audio, g, bpm, offset)
            off8 = [k for k in range(g) if (Fraction(k, g) * 8).denominator != 1]
            class_ref[g] = np.sort(vals[:, off8].ravel())
        ref = class_ref[g]
        return float(np.searchsorted(ref, value, side="right") / len(ref) * 100.0) if len(ref) else 0.0

    items = []
    for b in sorted(chart.bars):
        if b <= 0:
            continue
        for pos, lane, c in chart.bars[b].notes():
            if c not in "125" or is_on_grid(pos, 8):
                continue
            g = grid_of(pos)
            step_s = bar_seconds(bpm) / g
            t = audio_time(b, pos, bpm, offset)
            d = feat.nearest_onset(t, ENV)
            k = round(d / step_s) if abs(d) < 2 * step_s else 0
            strength = pct(cell_value(feat, ENV, b, pos + Fraction(k, g), bpm, offset))
            rank = class_rank(g, cell_value(feat, ENV, b, pos, bpm, offset))
            shift = 0
            if abs(d) <= MATCH_TOL:
                sug = "이 칸에 소리 시작 있음"
            elif abs(abs(d) - step_s) <= MATCH_TOL:
                shift = -1 if d < 0 else 1
                where = "앞" if shift < 0 else "뒤"
                sug = (f"이 칸엔 소리 시작이 안 잡힘, 1/{g} {where}에 있음"
                       + (" (이 칸도 같은 종류 칸 중 소리가 큰 편 → 약한 소리가 있을 수 있음)" if rank >= 75 else ""))
            else:
                sug = f"가까운 소리 시작 없음 (가장 가까운 것 {d*1000:+.0f}ms)"
            items.append(CheckItem(b, lane, pos, c, g, audio_time(b, pos, bpm), d * 1000, strength, rank, sug, shift))
    return items


def fmt_time(t: float) -> str:
    m, s = divmod(max(t, 0.0), 60)
    return f"{int(m)}:{s:06.3f}"


def write_checklist(items: list[CheckItem], chart_name: str, csv_path: Path, md_path: Path, clips: dict[str, str]) -> None:
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:   # 엑셀에서 바로 열리게 BOM
        w = csv.writer(f)
        w.writerow(["마디", "레인", "위치", "노트", "음원 시각", "가장 가까운 소리(ms)", "그 소리 세기(P)", "이 칸 순위(같은 종류 칸 중 P)", "제안", "판정(유지/이동)", "메모"])
        for it in items:
            w.writerow([f"{it.bar:03d}", it.lane, it.pos_label, it.char, fmt_time(it.time),
                        f"{it.nearest_ms:+.0f}", f"{it.strength:.0f}", f"{it.class_rank:.0f}", it.suggestion, "", ""])
    by_bar: dict[int, list[CheckItem]] = {}
    for it in items:
        by_bar.setdefault(it.bar, []).append(it)
    n_shift = sum(1 for it in items if it.shift)
    n_here = sum(1 for it in items if not it.shift and abs(it.nearest_ms) <= MATCH_TOL * 1000)
    lines = [f"# {chart_name} 확인 목록", "",
             f"8분 그리드 밖 노트 {len(items)}개 (마디 {len(by_bar)}개): 그 칸에 소리 있음 {n_here}개, "
             f"이 칸엔 안 잡히고 한 칸 앞/뒤에 있음 {n_shift}개, 나머지 {len(items) - n_here - n_shift}개.",
             "'소리 시작'은 중역(150–2500Hz) 온셋 피크(librosa)다. 8분마다 소리가 나는 곡에서는 엇박 노트 대부분이 "
             "'1/16 앞이나 뒤에 소리'로 보이므로 이것만으로 실수라고 단정할 수 없다.",
             "'칸 순위'는 곡 전체의 같은 종류 칸(8분 그리드 밖 칸) 중 이 칸의 소리 크기 백분위다. 높으면 드럼에 묻힌 약한 소리(보컬 등)가 있을 수 있다.",
             "미리듣기 A(지금 채보)와 B(가장 가까운 소리 쪽으로 옮김, 옮긴 노트는 3kHz 클릭)를 듣고 '유지/이동'을 판정한다.", "",
             "| 마디 | 노트 (레인 위치: 가장 가까운 소리 시작, 이 칸 순위) | 시각 | 제안 | 미리듣기 |", "|---|---|---|---|---|"]
    for b, its in by_bar.items():
        notes = ", ".join(f"L{it.lane} {it.pos_label}: {it.nearest_ms:+.0f}ms (칸 순위 P{it.class_rank:.0f})" for it in its)
        sugs = sorted({it.suggestion for it in its})
        lines.append(f"| {b:03d} | {notes} | {fmt_time(its[0].time)} | {'; '.join(sugs)} | {clips.get(str(b), '')} |")
    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


# ---------------- 클릭음 미리듣기 ----------------

def _click(sr: int, freq: float, dur: float = 0.035, gain: float = 1.0) -> np.ndarray:
    t = np.arange(int(sr * dur)) / sr
    return (gain * np.sin(2 * np.pi * freq * t) * np.exp(-t / (dur / 4))).astype(np.float32)


def render_preview(audio_path: Path, chart: Chart, bpm: float, bars: tuple[int, int], out_path: Path,
                   shift: dict[tuple[int, int, Fraction], Fraction] | None = None,
                   pad: float = 0.75, offset: float = 0.0, music_gain: float = 0.6) -> Path:
    """원곡(스테레오) 구간 + 노트 클릭. 위 레인(1·3)=높은 음, 아래 레인(2·4)=낮은 음, 홀드 끝=작은 소리.
    shift: (마디, 레인, 원래 위치) → 새 위치. 옮긴 노트는 다른 음색(3kHz)으로 표시."""
    import soundfile as sf
    y, sr = load_audio(audio_path, sr=None, mono=False)
    a, b = bars
    t0 = max(0.0, audio_time(a, 0, bpm) - pad)
    t1 = audio_time(b + 1, 0, bpm) + pad
    seg = y[int(t0 * sr): int(t1 * sr)].copy() * music_gain
    if seg.size == 0:
        raise ValueError("미리듣기 구간이 음원 밖임")
    clicks = np.zeros(len(seg), dtype=np.float32)
    tones = {True: _click(sr, 2000), False: _click(sr, 1200), "end": _click(sr, 900, gain=0.35), "moved": _click(sr, 3000)}
    for bn in range(a, b + 1):
        bar = chart.bars.get(bn)
        if bar is None:
            continue
        for pos, lane, c in bar.notes():
            if c not in "1245":
                continue
            new = (shift or {}).get((bn, lane, pos))
            p = new if new is not None else pos
            tone = tones["moved"] if new is not None else (tones["end"] if c == "4" else tones[is_top(lane)])
            i = int((audio_time(bn, p, bpm, offset) - t0) * sr)
            if 0 <= i < len(clicks):
                j = min(len(clicks), i + len(tone))
                clicks[i:j] += tone[: j - i]
    mix = seg + clicks[:, None] * 0.7
    peak = float(np.abs(mix).max()) or 1.0
    if peak > 0.99:
        mix = mix / peak * 0.99
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(out_path), mix, sr, subtype="PCM_16")
    return out_path


def bar_groups(bars: list[int], gap: int = 1) -> list[tuple[int, int]]:
    """[41, 50, 51, 52, 53] → [(41, 41), (50, 53)]"""
    out = []
    for b in sorted(set(bars)):
        if out and b - out[-1][1] <= gap:
            out[-1] = (out[-1][0], b)
        else:
            out.append((b, b))
    return out
