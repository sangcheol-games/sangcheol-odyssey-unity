"""유턴(같은 그룹을 연달아 쓰면서 방향이 반대) 판정.

엔진 동작 (ChartManager.SpawnNextNotes, NoteController.CheckGhostState):
  다음 마디 노트는 이번 마디 시작에 미리 생성된다. 같은 그룹 판정선이 움직이는 중이면 Hidden 으로 두고,
  판정선이 노트 위치를 20px 지나야 Ghost 가 된다. 유턴이면 다음 마디의 판정 위치 p 노트는 끝점 가까이 있어
  이번 마디 1−p 시점에야 Ghost 가 되므로 보이는 시간이 약 2p 마디뿐이다 (끝점 20px 안이면 바로 Active).
  p = 0 은 끝점이라 마디 내내 Ghost 로 보인다 → 예외.
빈 줄(0000)도 판정선을 만들므로 노트 유무가 아니라 줄 방향으로 판단한다.

종류: A = 이번 마디의 메인이 아닌 그룹(직전 그룹을 방향 뒤집어 넣은 기믹 마디)
      B = 이번 마디의 메인 그룹(앞 마디 기믹이 방향을 뒤집어 놓아 메인 라인이 돌아오는 마디)
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

from .model import Chart, expected_main, lanes_of
from .timing import bar_seconds

UTURN_FRONT = Fraction(1, 4)     # 0 < p ≤ 1/4 를 '마디 앞쪽'으로 본다 (사용자 결정)


@dataclass
class UTurn:
    bar: int
    group: int
    kind: str                        # "A" | "B"
    prev_dir: int
    dir: int
    front: list[tuple[int, Fraction, str]] = field(default_factory=list)   # (레인, 위치, 문자)


def in_front(pos: Fraction) -> bool:
    return 0 < pos <= UTURN_FRONT


def lead_seconds(pos: Fraction, bpm: float) -> float:
    """유턴 마디 위치 pos 노트가 Ghost 로 보이기 시작해 판정까지 걸리는 대략적인 시간."""
    return 2 * float(pos) * bar_seconds(bpm)


def is_uturn(chart: Chart, bar: int, group: int) -> bool:
    prev, cur = chart.bars.get(bar - 1), chart.bars.get(bar)
    if prev is None or cur is None:
        return False
    d0, d1 = prev.dirs.get(group), cur.dirs.get(group)
    return d0 is not None and d1 is not None and d0 != d1


def front_notes(chart: Chart, bar: int, group: int) -> list[tuple[int, Fraction, str]]:
    b = chart.bars.get(bar)
    if b is None:
        return []
    return sorted(((l, p, c) for l in lanes_of(group) for p, c in b.cells[l].items() if c in "125" and in_front(p)),
                  key=lambda x: (x[1], x[0]))


def find_uturns(chart: Chart) -> list[UTurn]:
    out = []
    for b in sorted(chart.bars):
        if b <= 1:
            continue
        for g in (0, 1):
            if is_uturn(chart, b, g):
                kind = "B" if g == expected_main(b)[0] else "A"
                out.append(UTurn(b, g, kind, chart.bars[b - 1].dirs[g], chart.bars[b].dirs[g], front_notes(chart, b, g)))
    return out
