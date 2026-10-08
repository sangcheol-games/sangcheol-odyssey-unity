"""하드 기믹 형태 (2번·3번 사용자 하드를 반반으로, 사용자 결정 2026-10-08).

기믹 = 그 마디의 메인 라인이 아닌 그룹(직전 마디 그룹)도 쓰는 마디. 형태는 사용자 하드에서 뽑았다:
  HALF1      1/2 에 다른 그룹 아래 레인 하나 (원래 방향)                       2번 6 · 3번 1
  SINGLE     3/8·5/8·3/4·1/4 중 하나 (메인 노트를 옮기거나 소리 있는 빈 칸)      2번 7 · 3번 1
  PAIR       1/2+5/8 둘, 1/2·5/8·3/4 셋 (메인 노트를 옮김)                     2번 6 · 3번 1
  RESPONSE   마디 끝 2–4노트를 다른 그룹으로 (끝 문답)                          2번 2 · 3번 7
  CALL       마디 앞 2–3노트를 다른 그룹으로 (앞 부르기)                         3번 3
  INTERLEAVE 4분·8분 간격으로 이어진 메인 노트를 한 칸 걸러 다른 그룹으로 (주고받기) 2번 3 · 3번 8
  WHOLE      마디 전체를 다른 그룹으로                                         3번 1   (gimmick_level ≥ 2)
  REVERSE    메인 라인 방향만 뒤집기                                          3번 3   (gimmick_level ≥ 2)
  CHORD      메인 탭과 같은 시각에 다른 그룹 탭 (두 그룹 동시치기)               3번 2   (gimmick_level ≥ 3)
  CALL·INTERLEAVE 는 메인 라인도 뒤집는 변형(+REV, 두 그룹이 같은 방향)이 있다 (3번 8마디, gimmick_level ≥ 2).
홀드는 시작·끝을 함께 옮긴다 (3번: 다른 그룹 노트 64개 중 14개가 홀드).
형태 선택은 키 해시 가중 추첨이다: 직전·다음 기믹과 같은 형태는 ×0.4, 같은 모양은 음악이 같을 때만, 형태별 35%·모양별 20% 상한.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from fractions import Fraction

from .model import Bar, cross_lane, lanes_of, mirror_lane

LTR, RTL = 0, 1
DEFAULT_DIR = {0: LTR, 1: RTL}
EIGHTH = Fraction(1, 8)
QUARTER = Fraction(1, 4)
HALF = Fraction(1, 2)

# 2번·3번 사용자 하드 고유 기믹 마디의 형태 비율을 반반으로 섞은 값 (기믹 분석, 짝수 13+10 / 홀수 11+15 마디)
WEIGHTS = {
    "even": {"HALF1": .281, "SINGLE": .165, "PAIR": .0, "RESPONSE": .088, "CALL": .100, "INTERLEAVE": .265, "WHOLE": .050, "REVERSE": .050},
    "odd": {"HALF1": .0, "SINGLE": .182, "PAIR": .306, "RESPONSE": .245, "CALL": .033, "INTERLEAVE": .167, "WHOLE": .0, "REVERSE": .067},
}
CHORD_WEIGHT = 0.04          # 3번 2/25
SMOOTH = 0.02                # 가산 사전값 (예시가 적은 형태도 아주 가끔)
MIN_LEVEL = {"WHOLE": 2, "REVERSE": 2, "CHORD": 3}
REV_LEVEL = 2
VARIANT_W = {"own": 0.6, "flipped": 0.4, "rev": 0.3}     # 같은 형태 안 변형 비중 (3번 원래 방향 60%, 메인 뒤집기 약 30%)
FAMILY_KO = {"HALF1": "1/2 하나", "SINGLE": "하나", "PAIR": "쌍·3연", "RESPONSE": "끝 문답", "CALL": "앞 부르기",
             "INTERLEAVE": "주고받기", "WHOLE": "마디 통째", "REVERSE": "메인 뒤집기", "CHORD": "두 그룹 동시치기"}


@dataclass
class MainNote:
    pos: Fraction
    lane: int
    char: str                                   # '1' 탭, '2' 홀드 시작
    end: tuple[Fraction, str] | None = None


@dataclass
class Op:
    src: int | None          # 옮기는 원래 레인 (None = 새로 추가)
    dst: int
    pos: Fraction
    char: str = "1"
    end: tuple[Fraction, str] | None = None
    gate: float = 0.0        # 추가 노트 소리 기준 (백분위)
    chord: bool = False      # 같은 시각 노트 허용


@dataclass
class Plan:
    family: str
    ops: list[Op]
    other_dir: int | None             # 다른 그룹 방향 (None = 쓰지 않음)
    main_dir: int | None = None       # 메인 그룹 새 방향 (None = 그대로)
    drop_main: bool = False           # WHOLE: 메인 그룹을 비움
    variant: str = "own"
    sig: tuple = field(default_factory=tuple)


def main_notes(bar: Bar, g: int) -> list[MainNote]:
    out = []
    for lane in lanes_of(g):
        ends = set()
        for s, end in bar.holds(lane):
            out.append(MainNote(s, lane, "2", end))
            if end is not None:
                ends.add(end[0])
        for p, c in bar.cells[lane].items():
            if c == "1":
                out.append(MainNote(p, lane, "1"))
    return sorted(out, key=lambda m: (m.pos, m.lane))


def _dir(o: int, variant: str) -> int:
    return DEFAULT_DIR[o] if variant in ("own", "rev") else 1 - DEFAULT_DIR[o]


def build_plans(bar: Bar, b: int, g: int, pct, level: int, onsets_any: set[Fraction]) -> dict[str, list[Plan]]:
    """마디 b 에서 가능한 형태별 계획. pct(pos) = 그 칸 소리 백분위. onsets_any = 이 마디 모든 그룹의 노트 시작 위치."""
    o = 1 - g
    top_o, bot_o = lanes_of(o)
    d_main = bar.dirs.get(g)
    if d_main is None:
        return {}
    mains = main_notes(bar, g)
    at: dict[Fraction, list[MainNote]] = {}
    for m in mains:
        at.setdefault(m.pos, []).append(m)
    out: dict[str, list[Plan]] = {}

    def add(fam: str, plan: Plan) -> None:
        out.setdefault(fam, []).append(plan)

    def move(m: MainNote, dst: int | None = None) -> Op:
        return Op(m.lane, dst if dst is not None else cross_lane(m.lane), m.pos, m.char, m.end)

    def move_or_add(pos: Fraction, dst: int, gate: float) -> Op | None:
        ms = at.get(pos, [])
        if len(ms) > 1:
            return None
        if ms:
            return move(ms[0], dst)
        if pos in onsets_any or pct(pos) < gate:
            return None
        return Op(None, dst, pos, "1", None, gate)

    def h_of(op: Op) -> str:
        return "T" if op.dst % 2 == 1 else "B"

    # HALF1 (원래 방향)
    op = move_or_add(HALF, bot_o, 50.0)
    if op:
        add("HALF1", Plan("HALF1", [op], DEFAULT_DIR[o], variant="own", sig=("HALF1", HALF, "B")))
    # SINGLE
    for p in (Fraction(3, 8), Fraction(5, 8), Fraction(3, 4), QUARTER):
        for variant in ("flipped", "own"):
            ms = at.get(p, [])
            dst = cross_lane(ms[0].lane) if len(ms) == 1 else (bot_o if p >= HALF else top_o)
            op = move_or_add(p, dst, 50.0)
            if op:
                add("SINGLE", Plan("SINGLE", [op], _dir(o, variant), variant=variant, sig=("SINGLE", p, h_of(op), variant)))
    # PAIR (1/2+5/8, 3/8+1/2) · MIDRUN (1/2·5/8·3/4)
    for poss, heights in (((HALF, HALF + EIGHTH), (bot_o, top_o)), ((Fraction(3, 8), HALF), (top_o, bot_o)),
                          ((HALF, HALF + EIGHTH, Fraction(3, 4)), (top_o, bot_o, top_o))):
        ops = [move_or_add(p, dst, 50.0 if i == 0 else 30.0) for i, (p, dst) in enumerate(zip(poss, heights))]
        if all(ops) and sum(1 for x in ops if x.src is not None) >= (2 if len(poss) == 3 else 1):
            for variant in ("flipped", "own"):
                add("PAIR", Plan("PAIR", ops, _dir(o, variant), variant=variant,
                                 sig=("PAIR", poss, tuple(h_of(x) for x in ops), variant)))
    # RESPONSE (끝 문답)
    for s in (HALF, HALF + EIGHTH, Fraction(3, 4)):
        tail = [m for m in mains if m.pos >= s]
        head = [m for m in mains if m.pos < s]
        if len(tail) < 2 or not head or any(len(at[m.pos]) > 1 for m in tail):
            continue
        ops = [move(m) for m in tail]
        poss = sorted(m.pos for m in tail)
        for a, c in zip(poss, poss[1:]):                     # 4분 간격 사이 8분 하나를 채워 이어 줌 (3번: 추가 노트 중앙값 P54)
            mid = a + EIGHTH
            if c - a == QUARTER and mid not in onsets_any and pct(mid) >= 30.0:
                prev = next(x for x in ops if x.pos == a)
                ops.append(Op(None, mirror_lane(prev.dst), mid, "1", None, 30.0))
                break
        ops.sort(key=lambda x: x.pos)
        for variant in ("own", "flipped"):
            add("RESPONSE", Plan("RESPONSE", ops, _dir(o, variant), variant=variant,
                                 sig=("RESPONSE", tuple(x.pos for x in ops), variant)))
    # CALL (앞 부르기)
    for e in (Fraction(3, 8), QUARTER):
        head = [m for m in mains if m.pos <= e]
        tail = [m for m in mains if m.pos > e]
        if len(head) < 2 or not tail or any(len(at[m.pos]) > 1 for m in head) or any(m.end and m.end[0] > e for m in head):
            continue
        ops = [move(m) for m in head]
        add("CALL", Plan("CALL", ops, _dir(o, "own"), variant="own", sig=("CALL", tuple(x.pos for x in ops), "own")))
        if level >= REV_LEVEL:
            add("CALL", Plan("CALL", ops, _dir(o, "rev"), main_dir=1 - d_main, variant="rev",
                             sig=("CALL", tuple(x.pos for x in ops), "rev")))
    # INTERLEAVE (주고받기)
    singles = [m for m in mains if len(at[m.pos]) == 1]
    for step in (EIGHTH, QUARTER):
        run: list[MainNote] = []
        runs = []
        for m in singles:
            if run and m.pos - run[-1].pos == step and not (run[-1].end and run[-1].end[0] > run[-1].pos + step / 2):
                run.append(m)
            else:
                if len(run) >= 3:
                    runs.append(run)
                run = [m]
        if len(run) >= 3:
            runs.append(run)
        for r in runs:
            picked = r[0::2]
            if len(picked) >= len(mains):
                continue
            ops = [move(m) for m in picked]
            for variant in ("own", "flipped") + (("rev",) if level >= REV_LEVEL else ()):
                add("INTERLEAVE", Plan("INTERLEAVE", ops, _dir(o, variant), main_dir=(1 - d_main) if variant == "rev" else None,
                                       variant=variant, sig=("INTERLEAVE", step, tuple(x.pos for x in ops), variant)))
    # WHOLE · REVERSE · CHORD
    if level >= MIN_LEVEL["WHOLE"] and len(mains) >= 2 and all(len(v) == 1 for v in at.values()):
        add("WHOLE", Plan("WHOLE", [move(m) for m in mains], DEFAULT_DIR[o], drop_main=True, variant="own", sig=("WHOLE",)))
    if level >= MIN_LEVEL["REVERSE"] and mains:
        add("REVERSE", Plan("REVERSE", [], None, main_dir=1 - d_main, variant="rev", sig=("REVERSE",)))
    if level >= MIN_LEVEL["CHORD"]:
        for p in (HALF, Fraction(3, 4), QUARTER):
            ms = at.get(p, [])
            if len(ms) == 1 and ms[0].char == "1" and pct(p) >= 50.0:
                op = Op(None, cross_lane(mirror_lane(ms[0].lane)), p, "1", None, 50.0, chord=True)
                add("CHORD", Plan("CHORD", [op], DEFAULT_DIR[o], variant="own", sig=("CHORD", p)))
    return out


def family_weights(parity: str, level: int, override: dict | None = None) -> dict[str, float]:
    if override is not None:
        return dict(override)
    w = {f: v + SMOOTH for f, v in WEIGHTS[parity].items() if level >= MIN_LEVEL.get(f, 1)}
    if level >= MIN_LEVEL["CHORD"]:
        w["CHORD"] = CHORD_WEIGHT
    return w


def weighted_order(plans: list[Plan], key) -> list[Plan]:
    """변형 비중을 반영한 결정적 가중 셔플 (지수 키: -ln(u)/w)."""
    return sorted(plans, key=lambda p: (-math.log(max(key(p), 1e-12)) / VARIANT_W.get(p.variant, 0.5), p.sig))


def draw(weights: dict[str, float], u: float) -> str | None:
    tot = sum(weights.values())
    if tot <= 0:
        return None
    acc = 0.0
    for f in sorted(weights):
        acc += weights[f] / tot
        if u < acc:
            return f
    return sorted(weights)[-1]
