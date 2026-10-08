"""마디/위치 ↔ 시간 변환.

게임: 노트 곡 시각 = bar * 240/BPM + pos * 240/BPM (ChartParser.cs, LaneData.cs),
음원은 1마디 리드인 뒤에 시작 (ChartManager.StartMusic(barDuration)) → 음원 0초 = 1마디 시작.
"""
from __future__ import annotations

from fractions import Fraction

JUDGE_UMM = 0.126  # 가장 넓은 판정 창 (Domain/Service/Constants.cs)


def bar_seconds(bpm: float) -> float:
    return 240.0 / float(bpm)


def song_time(bar: int, pos: Fraction | float, bpm: float) -> float:
    """곡 시계 기준 시각 (0 = 게임 시작, 1마디 = 리드인 끝)."""
    return (bar + float(pos)) * bar_seconds(bpm)


def audio_time(bar: int, pos: Fraction | float, bpm: float, offset_s: float = 0.0) -> float:
    """음원 파일 기준 시각. offset_s 는 분석용 보정값(파일에는 쓰지 않음)."""
    return (bar - 1 + float(pos)) * bar_seconds(bpm) + offset_s


def is_on_grid(pos: Fraction, division: int) -> bool:
    return (pos * division).denominator == 1


def is_odd_sixteenth(pos: Fraction) -> bool:
    """16분 그리드의 홀수 칸 (8분 그리드 밖 16분)."""
    x = pos * 16
    return x.denominator == 1 and int(x) % 2 == 1
