"""채보 데이터 모델.

- 노트 위치는 항상 '판정 순서' 기준 Fraction [0, 1) 로 다룬다.
  RTL(채널 1) 시퀀스를 뒤집는 일은 chart_io 안에서만 한다 (LaneData.ConvertSequenceToNotes 규칙).
- 셀 문자는 원문 그대로 보관한다('1','2','4','5' 외 '3' 등도 보존) — 검증기가 따로 잡는다.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction
from math import lcm

LTR, RTL = 0, 1
LANES = (1, 2, 3, 4)
GROUPS = (0, 1)
MAX_LENGTH = 128  # 에디터 비트 입력 상한 (EditorToolbar)
HOLD_END_CHARS = "45"


def group_of(lane: int) -> int:
    """레인 1·2 = 그룹 0, 레인 3·4 = 그룹 1."""
    return 0 if lane <= 2 else 1


def lanes_of(group: int) -> tuple[int, int]:
    return (1, 2) if group == 0 else (3, 4)


def is_top(lane: int) -> bool:
    """그룹 안에서 홀수 레인(1, 3)이 위(Top), 짝수 레인(2, 4)이 아래(Bottom)."""
    return lane % 2 == 1


def mirror_lane(lane: int) -> int:
    """같은 그룹 안에서 위아래 교체 (1↔2, 3↔4)."""
    return {1: 2, 2: 1, 3: 4, 4: 3}[lane]


def cross_lane(lane: int) -> int:
    """다른 그룹의 같은 높이 레인 (1↔3, 2↔4)."""
    return {1: 3, 2: 4, 3: 1, 4: 2}[lane]


def expected_main(bar_number: int) -> tuple[int, int]:
    """노멀 교대 규칙상 (그룹, 방향). 홀수 마디 = 그룹0 LTR(01/02), 짝수 마디 = 그룹1 RTL(13/14)."""
    return (0, LTR) if bar_number % 2 == 1 else (1, RTL)


@dataclass
class Bar:
    number: int
    length: int = 4
    dirs: dict[int, int | None] = field(default_factory=lambda: {0: None, 1: None})
    cells: dict[int, dict[Fraction, str]] = field(default_factory=lambda: {l: {} for l in LANES})

    def copy(self, number: int | None = None) -> "Bar":
        return Bar(
            number=self.number if number is None else number,
            length=self.length,
            dirs=dict(self.dirs),
            cells={l: dict(c) for l, c in self.cells.items()},
        )

    def groups_used(self) -> list[int]:
        return [g for g in GROUPS if self.dirs[g] is not None]

    def has_notes(self, lane: int | None = None) -> bool:
        if lane is None:
            return any(self.cells[l] for l in LANES)
        return bool(self.cells[lane])

    def note_count(self) -> int:
        return sum(1 for l in LANES for c in self.cells[l].values() if c != "0")

    def notes(self):
        """(pos, lane, char) 를 판정 시간 → 레인 순으로."""
        out = [(p, l, c) for l in LANES for p, c in self.cells[l].items() if c != "0"]
        out.sort(key=lambda t: (t[0], t[1]))
        return out

    def holds(self, lane: int):
        """LaneData 와 같은 방식의 홀드 해석: 각 '2' 는 뒤쪽 첫 '4'/'5' 와 짝. 없으면 끝=None(마디 끝까지)."""
        items = sorted(self.cells[lane].items())
        out = []
        for i, (p, c) in enumerate(items):
            if c != "2":
                continue
            end = None
            for q, d in items[i + 1:]:
                if d in HOLD_END_CHARS:
                    end = (q, d)
                    break
            out.append((p, end))
        return out

    def min_length(self) -> int:
        """모든 위치를 표현할 수 있는 최소 공배수 길이 (현재 길이를 포함)."""
        n = self.length
        for l in LANES:
            for p in self.cells[l]:
                n = lcm(n, p.denominator)
        return n


@dataclass
class Header:
    title: str = ""
    artist: str = ""
    difficulty: str = "Normal"   # 에디터가 쓰는 enum 이름 (Easy/Normal/Hard/Extreme)
    level: int = 1
    bpm: int = 120
    notes: int | None = None      # 파일에 적힌 #NOTES (쓸 때는 항상 다시 센다)
    raw_difficulty: str | None = None  # 파일에 적힌 원문 (검증용)


DIFFICULTIES = ("Easy", "Normal", "Hard", "Extreme")


@dataclass
class Chart:
    header: Header = field(default_factory=Header)
    bars: dict[int, Bar] = field(default_factory=dict)
    path: str | None = None
    unrepresentable: list[str] = field(default_factory=list)   # 에디터 형식으로 쓸 수 없는 원문 줄 (레인 1–4 밖)

    def count_notes(self) -> int:
        """에디터 CountTotalNotes 와 같은 규칙: '0' 이 아닌 모든 칸 (에디터가 저장하는 #NOTES)."""
        return sum(b.note_count() for b in self.bars.values())

    def count_judged(self) -> int:
        """게임이 실제로 판정하는 칸 수: '1'–'5' (LaneData.GetNoteType)."""
        return sum(1 for b in self.bars.values() for l in LANES for c in b.cells[l].values() if c in "12345")

    def last_bar(self) -> int:
        return max(self.bars) if self.bars else 0

    def bar_seconds(self, bpm: float | None = None) -> float:
        return 240.0 / float(bpm or self.header.bpm)

    def copy(self) -> "Chart":
        h = Header(**vars(self.header))
        return Chart(header=h, bars={n: b.copy() for n, b in self.bars.items()}, path=self.path,
                     unrepresentable=list(self.unrepresentable))
