"""유턴 앞쪽 노트 규칙: 판정, 검사(W15), 생성기의 예방·한 개 빼기·방향 되돌리기, 손작업 마디는 표시만."""
from fractions import Fraction

from chartporter.config import SongConfig
from chartporter.model import LTR, RTL
from chartporter.port import Porter, PortParams
from chartporter.uturn import find_uturns, lead_seconds
from chartporter.validate import validate

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0) + "#000:13:0000;\n#000:14:0000;\n"


def make(lines: str):
    return chart_from_text(BASE + lines)[0]


def porter(c, rate=1.0, fams=None, **cfg):
    return Porter(c, None, 160, 0.0, SongConfig(id=9999, **cfg), PortParams(gimmick_rate=rate, family_weights=fams))


def pair_plan(variant: str):
    """3마디(홀수): 1/2 메인 탭(레인2)을 다른 그룹 아래 레인(4)으로 옮기고 5/8 위 레인(3) 추가."""
    from chartporter.gimmicks import Op, Plan
    other_dir = LTR if variant == "flipped" else RTL
    return Plan("PAIR", [Op(2, 4, Fraction(1, 2)), Op(None, 3, Fraction(5, 8), gate=30.0)], other_dir, variant=variant,
                sig=("PAIR", variant))


# ---------------- 판정 ----------------

def test_uturn_detected_from_line_direction_even_if_empty():
    # 1마디 그룹1 빈 줄(LTR, 03/04) → 2마디 그룹1 RTL(13/14): 유턴 (빈 줄도 판정선)
    c = make("#001:01:1000;\n#001:02:0000;\n#001:03:0000;\n#001:04:0000;\n#002:13:00000010;\n#002:14:00000000;")
    u = find_uturns(c)
    assert [(x.bar, x.group, x.kind) for x in u] == [(2, 1, "B")]
    assert u[0].front == [(3, Fraction(1, 8), "1")]          # RTL '00000010' = 판정 1/8


def test_front_zone_excludes_zero_and_after_quarter():
    c = make("#001:01:1000;\n#001:02:0000;\n#001:03:0000;\n#001:04:0000;\n"
             "#002:13:0000000000100001;\n#002:14:0000001000000000;")   # 판정 0(끝점), 5/16, 9/16
    assert find_uturns(c)[0].front == []
    c = make("#001:01:1000;\n#001:02:0000;\n#001:03:0000;\n#001:04:0000;\n#002:13:00000100;\n#002:14:00000000;")
    assert find_uturns(c)[0].front == [(3, Fraction(1, 4), "1")]        # 1/4 는 앞쪽에 포함


def test_validator_w15_and_lead_time():
    c = make("#001:01:1000;\n#001:02:0000;\n#001:03:0000;\n#001:04:0000;\n#002:13:00000010;\n#002:14:00000000;")
    assert [i for i in validate(c, bpm=160) if i.code == "W15" and i.bar == 2]
    assert abs(lead_seconds(Fraction(1, 8), 180) - 0.333) < 0.01


# ---------------- 생성기 ----------------

def _t2_ready(next_bar_lane3: str):
    """홀수 3마디: 레인2 1/2 탭(T2 대상). 짝수 4마디: 그룹1 메인 노트 next_bar_lane3 (RTL 화면 순서).
    1마디에는 기믹이 들어가지 않으므로 1·2마디는 채움용."""
    return make("#001:01:10000000;\n#001:02:00000000;\n#002:13:00000001;\n#002:14:00000000;\n"
                "#003:01:10000000;\n#003:02:00001000;\n" + f"#004:13:{next_bar_lane3};\n#004:14:00000000;")


def test_flipped_plan_rejected_when_next_bar_has_two_front_notes():
    c = _t2_ready("00000110")                      # 판정 1/8, 1/4 → 앞쪽 2개
    p = porter(c, rate=0.0)
    assert not p.apply_plan(3, pair_plan("flipped"))                 # 뒤집으면 4마디 유턴 앞쪽 노트 2개 → 거부
    assert p.hard.bars[3].dirs[1] is None                            # 되돌림: 다른 그룹 안 씀
    assert p.hard.bars[3].cells[2].get(Fraction(1, 2)) == "1"       # 옮겼던 노트도 원위치
    assert p.apply_plan(3, pair_plan("own"))                         # 원래 방향은 유턴이 없어 됨
    res = porter(c, fams={"PAIR": 1.0}).run()
    assert not [u for u in find_uturns(res.chart) if u.front]
    assert res.bars[3].get("gimmick") == "PAIR" and res.chart.bars[3].dirs[1] == RTL


def test_single_front_note_is_dropped_and_uturn_kept():
    c = _t2_ready("00000010")                      # 판정 1/8 하나
    p = porter(c, rate=0.0)
    assert p.apply_plan(3, pair_plan("flipped"))
    assert p.hard.bars[3].dirs[1] == LTR             # 뒤집음 유지 → 4마디는 유턴
    assert Fraction(1, 8) not in p.hard.bars[4].cells[3]
    assert [e for e in p.log if e.origin == "UTURN_DROP" and e.bar == 4]


def test_final_pass_unflips_generator_gimmick():
    c = _t2_ready("00000110")
    p = porter(c, rate=0.0)
    # 기믹을 직접 뒤집어 넣은 상태를 만든다 (복사 등으로 남은 경우를 흉내)
    p.hard.bars[3].dirs[1] = LTR
    p.hard.bars[3].cells[4][Fraction(1, 2)] = "1"
    del p.hard.bars[3].cells[2][Fraction(1, 2)]
    p.fix_uturn_fronts()
    assert p.hard.bars[3].dirs[1] == RTL
    assert [e for e in p.log if e.origin == "UTURN_UNFLIP"]
    assert not [u for u in find_uturns(p.hard) if u.front]


def test_gapfill_skips_front_of_uturn_bar():
    c = make("#001:01:10000000;\n#001:02:00000000;\n#001:03:00000000;\n#001:04:00001000;\n"   # 그룹1 LTR 줄(기믹 자리)
             "#002:13:10000000;\n#002:14:00000000;")
    p = porter(c, rate=0.0)
    p.budget_override = {1: 0, 2: 3}
    res = p.run()
    assert not [e for e in res.log if e.origin == "GAPFILL" and e.bar == 2 and 0 < e.pos <= Fraction(1, 4)]


def test_frozen_cause_is_only_flagged():
    c = _t2_ready("00000110")
    p = porter(c, rate=0.0, frozen_bars={3})
    p.hard.bars[3].dirs[1] = LTR                     # 손작업 마디가 뒤집어 놓은 경우
    p.hard.bars[3].cells[4][Fraction(1, 2)] = "1"
    p.fix_uturn_fronts()
    assert p.hard.bars[3].dirs[1] == LTR and "UTURN" in p.notes[4]["flags"]
    assert len(p.hard.bars[4].cells[3]) == 2


def test_flip_kept_when_next_bar_has_no_front_notes():
    c = _t2_ready("00000001")                      # 판정 0 (끝점) 뿐 → 유턴이어도 괜찮음
    p = porter(c, rate=0.0)
    assert p.apply_plan(3, pair_plan("flipped")) and p.hard.bars[3].dirs[1] == LTR
    assert not [e for e in p.log if e.origin.startswith("UTURN")]
