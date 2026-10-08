"""노멀 → 하드 초안 생성 (port).

규칙은 사용자 하드(2번)에서 뽑은 기조(profiles/style_profile.json)를 따른다:
  1. 홀드: 끝 없는 홀드(같은 마디에 4/5 없음)는 게임 버그를 일으키므로 남기지 않는다.
     시작이 7/8 전이면 7/8 에 '4' (2번 사용자 규칙 13/13), 7/8 이후면 탭
  2. 추가 예산: 노멀 분할별 배율(4분할 ×1.39, 8분할 ×1.05)로 곡 전체 추가 수를 정하고,
     음원 밀도(중역 P80 이상 8분 칸)가 노멀보다 큰 마디에 몰아서 배분 (마디당 최대 3)
  3. 기믹(먼저): 빈 칸 소리가 큰 마디부터(이웃 기믹 마디 보너스, 사람은 2–4마디씩 이어 씀) bar_rate 만큼,
     형태는 2번·3번 사용자 하드를 반반 섞은 가중 추첨 (gimmicks.py: HALF1·SINGLE·PAIR·RESPONSE·CALL·INTERLEAVE·
     WHOLE·REVERSE·CHORD, 메인 노트·홀드를 다른 그룹으로 옮기거나 소리 있는 칸에 추가). 같은 형태가 이어지지 않게 다양성 규칙.
     유턴 앞쪽 노트 금지, 동시치기는 CHORD 형태만, 메인 라인 바꾸기는 gimmick_level ≥ 2
  4. 빈 칸 채우기: 비어 있는 8분 칸 중 중역 소리 세기 ≥ 곡 P50 인 칸만, 점수 = z(화성 중역)+0.5·z(중역)+0.3·[노멀 노트에서 8분 거리]
     레인은 같은 그룹 직전 노트와 위아래 교대, 막히면 같은 레인
  5. 같은 레인 간격 ≥ max(8분, 126ms), 홀드 안에는 넣지 않음. 16분·셋잇단은 리듬 변주(rhythm.py)로만:
     음원 16분 정점 마디(음형 묶음 단위, 마디 18% 상한)에 약한 8분을 16분 뒤로 밀기·16분 픽업, 셋잇단 감지 박에 A-B-A 셋잇단
  6. 복사 규칙(songs 설정 sections 의 copy_of)이 있으면 원본 마디를 만든 뒤 대상 마디로 복사
  7. 검증 경고(W01/W02/E03)가 나면 점수 낮은 추가 노트부터 뺌
모든 추가·변경 노트는 log 에 출처·점수·이유를 남긴다.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, Percentiles, cell_value
from .config import SongConfig
from .model import (
    LANES, LTR, RTL, Bar, Chart, cross_lane, expected_main, group_of, lanes_of, mirror_lane,
)
from .gimmicks import FAMILY_KO, Plan, build_plans, draw, family_weights, weighted_order
from .holds import close_unterminated_holds
from .keyhash import h as _kh
from .place import Placer
from .uturn import find_uturns, in_front, is_uturn
from .sync import plan_sync, rules_from_config
from .timing import JUDGE_UMM, bar_seconds, song_time

EIGHTH = Fraction(1, 8)
HALF = Fraction(1, 2)
DEFAULT_DIR = {0: LTR, 1: RTL}
ONSET = "125"


@dataclass
class LogEntry:
    bar: int
    lane: int
    pos: Fraction
    char: str
    origin: str          # HOLD_END_78 | HOLD_TO_TAP | GIMMICK_T1 | GIMMICK_T2_MOVE | GIMMICK_T2_ADD | GAPFILL | COPY | REMOVED
    score: float | None = None
    reason: str = ""
    pct: float | None = None         # 그 칸의 소리 세기 백분위 (곡 8분 칸 기준)


@dataclass
class PortParams:
    ratio_by_len: dict[int, float] = field(default_factory=lambda: {4: 1.39, 8: 1.05})
    gimmick_rate: float = 0.44
    max_add_per_bar: int = 3
    gate_percentile: float = 50.0
    dense_percentile: float = 80.0
    family_weights: dict | None = None     # 기믹 형태 가중치 덮어쓰기 (테스트·실험용, None = 2번·3번 반반)
    neighbor_bonus: float = 15.0           # 이웃 마디가 기믹이면 선택 점수 + (백분위 단위)
    max_run: int = 4                       # 기믹 마디 연속 최대
    same_family_penalty: float = 0.4       # 이웃 기믹과 같은 형태면 가중치 ×
    family_cap: float = 0.35               # 한 형태가 기믹 마디의 이 비율을 넘지 않게
    sig_cap: float = 0.20                  # 같은 모양(형태·위치·레인·방향) 상한
    rhythm_bar_rate: float = 0.18          # 16분 음형을 넣는 마디 상한 (사람 하드 8–21%)
    rhythm_max_per_bar: int = 2

    @classmethod
    def from_profile(cls, profile: dict) -> "PortParams":
        s = profile["songs"][profile["primary"]]
        r = {int(k): float(v) for k, v in s["density"]["ratio_by_normal_subdivision"].items()}
        return cls(ratio_by_len=r, gimmick_rate=float(s["gimmick"]["bar_rate"]))


@dataclass
class PortResult:
    chart: Chart
    log: list[LogEntry]
    bars: dict[int, dict]            # 마디별 메모 (예산, 기믹, 표시)
    offset: float


class Porter:
    def __init__(self, normal: Chart, feat: AudioFeatures | None, bpm: float, offset: float,
                 cfg: SongConfig, params: PortParams, frozen_source: Chart | None = None,
                 frozen_carried: list | None = None):
        self.normal, self.feat, self.bpm, self.offset, self.cfg, self.p = normal, feat, bpm, offset, cfg, params
        self.frozen_source = frozen_source          # mode finish: 손작업 하드 (이미 시간 이동한 것)
        self.frozen_carried = frozen_carried or []  # 시간 이동으로 손작업 마디에서 다른 마디로 넘어간 노트
        self.hard = normal.copy()
        self.hard.header.difficulty = "Hard"
        self.hard.path = None
        self.log: list[LogEntry] = []
        self.notes: dict[int, dict] = {b: {"flags": []} for b in normal.bars}
        self.bar_s = bar_seconds(bpm)
        self.placer = Placer(lambda: self.hard, bpm)      # 배치 규칙·충돌 정리 (복사 단계에서 self.hard 가 바뀌어도 따라감)
        self.min_gap = self.placer.min_gap
        self.fixed = set(cfg.keep_normal_bars) | set(cfg.frozen_bars) | set(cfg.locked_bars)
        rules = rules_from_config(cfg)          # 규칙 오류는 생성 전에 RuleError 로
        self.copy_targets = {t for r in rules for t in r.targets} - self.fixed
        self.last = normal.last_bar()
        self.budget_override: dict[int, int] | None = None   # 평가용: 마디별 추가 수를 직접 지정
        if feat is not None:
            self.pct = Percentiles(feat, "mid", self.last, bpm, offset)
            cells = [(b, Fraction(k, 8)) for b in range(1, self.last + 1) for k in range(8)]
            mids = np.array([self._raw("mid", b, p) for b, p in cells])
            harms = np.array([self._raw("harm_mid", b, p) for b, p in cells])
            self._z = {"mid": (mids.mean(), mids.std() or 1.0), "harm_mid": (harms.mean(), harms.std() or 1.0)}
        else:
            self.pct = None

    # ---------- 음원 ----------
    def _raw(self, env: str, b: int, pos: Fraction) -> float:
        return cell_value(self.feat, env, b, pos, self.bpm, self.offset)

    def percentile(self, b: int, pos: Fraction) -> float:
        return self.pct(self._raw("mid", b, pos)) if self.pct else 100.0

    def z(self, env: str, b: int, pos: Fraction) -> float:
        if self.feat is None:
            return 0.0
        m, s = self._z[env]
        return (self._raw(env, b, pos) - m) / s

    def dense_cells(self, b: int) -> int:
        if self.pct is None:
            return 0
        return sum(1 for k in range(8) if self.percentile(b, Fraction(k, 8)) >= self.p.dense_percentile)

    # ---------- 채보 조회 (place.Placer 에 위임) ----------
    def onset_positions(self, b: int) -> set[Fraction]:
        return self.placer.onset_positions(b)

    def lane_items(self, lane: int, around: int):
        return self.placer.lane_items(lane, around)

    def lane_sequence(self, lane: int) -> list[tuple[Fraction, int, Fraction, str]]:
        return self.placer.lane_sequence(lane)

    def hold_spans(self, lane: int) -> list[tuple[Fraction, Fraction, bool]]:
        return self.placer.hold_spans(lane)

    def inside_hold(self, b: int, lane: int, pos: Fraction) -> bool:
        return self.placer.inside_hold(b, lane, pos)

    def can_place(self, b: int, lane: int, pos: Fraction, allow_same_time: bool = False) -> str | None:
        """놓을 수 없으면 이유, 놓을 수 있으면 None."""
        return self.placer.can_place(b, lane, pos, allow_same_time)

    def put(self, b: int, lane: int, pos: Fraction, char: str, origin: str, score: float | None, reason: str) -> None:
        self.hard.bars[b].cells[lane][pos] = char
        pct = self.percentile(b, pos) if self.pct is not None else None
        self.log.append(LogEntry(b, lane, pos, char, origin, score, reason, pct))

    # ---------- 1. 홀드 ----------
    def fix_open_holds(self, bars: set[int] | None = None) -> None:
        """끝 없는 홀드·마디를 넘는 홀드를 닫는다 (holds.close_unterminated_holds). bars=None 이면 손대지 않는 마디를 뺀 전체."""
        target = bars if bars is not None else {b for b in self.hard.bars if b > 0 and b not in self.fixed}
        for f in close_unterminated_holds(self.hard, target):
            if f.action == "close_78":
                self.log.append(LogEntry(f.bar, f.lane, Fraction(7, 8), "4", "HOLD_END_78", None, f"끝 없는 홀드({f.start}): {f.detail}"))
            elif f.action == "to_tap":
                cells = self.hard.bars[f.bar].cells[f.lane]
                del cells[f.start]
                if self.can_place(f.bar, f.lane, f.start, allow_same_time=True) is None:
                    cells[f.start] = "1"
                    self.log.append(LogEntry(f.bar, f.lane, f.start, "1", "HOLD_TO_TAP", None, f.detail))
                else:   # 탭으로 두면 같은 레인 다음 노트와 126ms 안 → 끝 없는 홀드를 남기지 않도록 지운다
                    self.log.append(LogEntry(f.bar, f.lane, f.start, "2", "HOLD_DROPPED", None, "7/8 이후 시작한 끝 없는 홀드: 탭으로 두면 다음 노트와 너무 가까워 지움"))
                    self.notes.setdefault(f.bar, {"flags": []})["flags"].append("HOLD_MANUAL")
            else:
                self.notes.setdefault(f.bar, {"flags": []})["flags"].append("HOLD_MANUAL")

    # ---------- 2. 예산 ----------
    def budgets(self, bars: list[int]) -> dict[int, int]:
        by_len: dict[int, list[int]] = {}
        for b in bars:
            by_len.setdefault(self.normal.bars[b].length, []).append(b)
        out = {b: 0 for b in bars}
        for n, group in by_len.items():
            # 16·12·24분할 노멀 마디는 8분할 비율을 쓴다 (전에는 표에 없어서 추가가 0이 됐음)
            ratio = self.p.ratio_by_len.get(n, self.p.ratio_by_len.get(8 if n > 8 else 4, 1.0))
            total = sum(len(self.onset_positions(b)) for b in group) * max(0.0, ratio - 1.0) * self.cfg.density_scale
            if total <= 0:
                continue
            cap = self.p.max_add_per_bar
            w = {b: max(0, self.dense_cells(b) - len(self.onset_positions(b))) + 0.5 for b in group}
            want = min(round(total), cap * len(group))
            # 상한에 걸린 마디의 몫은 아직 여유 있는 마디에 가중치대로 다시 나눈다
            alloc = {b: 0.0 for b in group}
            free, left = set(group), float(want)
            while left > 1e-9 and free:
                s = sum(w[b] for b in free)
                spill = 0.0
                for b in sorted(free):
                    alloc[b] += left * w[b] / s
                    if alloc[b] >= cap:
                        spill += alloc[b] - cap
                        alloc[b] = cap
                free = {b for b in free if alloc[b] < cap}
                left = spill
            base = {b: math.floor(v + 1e-9) for b, v in alloc.items()}
            rest = want - sum(base.values())
            for b in sorted(group, key=lambda b: (-(alloc[b] - base[b]), b)):
                if rest <= 0:
                    break
                if base[b] < cap:
                    base[b] += 1
                    rest -= 1
            out.update(base)
        return out

    # ---------- 3. 기믹 ----------
    def touchable(self, b: int) -> bool:
        """생성기가 노트를 빼거나 방향을 바꿔도 되는 마디 (손작업·노멀 유지·잠금·복사 대상이 아님)."""
        return b in self.hard.bars and b not in self.fixed and b not in self.copy_targets

    def eventual_bar(self, b: int):
        """최종 내용 기준 마디: 손작업 마디는 손작업 원본, 그 외는 지금 하드."""
        if self.frozen_source is not None and b in self.cfg.frozen_bars and b in self.frozen_source.bars:
            return self.frozen_source.bars[b]
        return self.hard.bars.get(b)

    def uturn_front_after(self, b: int, group: int) -> list[tuple[int, Fraction, str]]:
        """b마디에서 group 을 뒤집으면 b+1 마디(그 그룹을 원래 방향으로 쓰는 경우) 앞쪽에 걸리는 노트."""
        nxt = self.eventual_bar(b + 1)
        if nxt is None or nxt.dirs.get(group) is None or nxt.dirs[group] != DEFAULT_DIR[group]:
            return []
        return sorted(((l, q, c) for l in lanes_of(group) for q, c in nxt.cells[l].items() if c in ONSET and in_front(q)),
                      key=lambda x: (x[1], x[0]))

    def gimmick_score(self, b: int) -> float:
        """비어 있는 8분 칸들의 소리 세기 평균 (2번에서 사용자 기믹 마디를 가장 잘 가려낸 지표, AUC 0.78)."""
        used = self.onset_positions(b)
        empty = [Fraction(k, 8) for k in range(8) if Fraction(k, 8) not in used]
        return float(np.mean([self.percentile(b, p) for p in empty])) if empty else 0.0

    # 기믹 계획 적용 (gimmicks.py 가 만든 계획: 옮기기·추가·방향)
    def _hold_ok(self, b: int, lane: int, s: Fraction, e: Fraction) -> str | None:
        """홀드를 lane 에 s→e 로 놓을 수 있는지: 시작 칸 규칙 + 홀드 동안·끝 뒤 126ms 안에 같은 레인 노트 없음."""
        why = self.can_place(b, lane, s)
        if why:
            return why
        ts, te = song_time(b, s, self.bpm), song_time(b, e, self.bpm)
        for tt, _c, _bb, _pp in self.lane_items(lane, b):
            if ts < tt < te + JUDGE_UMM - 1e-9:
                return "홀드 동안·끝 직후 같은 레인 노트"
        return None

    def _uturn_check(self, b: int, groups: set[int]) -> tuple[str, tuple | None]:
        """바뀐 그룹의 유턴 앞쪽 노트: ('ok', None) / ('drop', 다음 마디 앞쪽 탭 하나) / ('bad', 이유)."""
        bar = self.hard.bars[b]
        prev, nxt = self.eventual_bar(b - 1), self.eventual_bar(b + 1)
        drop = None
        for x in sorted(groups):
            d = bar.dirs.get(x)
            if d is None:
                continue
            if prev is not None and prev.dirs.get(x) is not None and prev.dirs[x] != d:
                if any(c in ONSET and in_front(q) for l in lanes_of(x) for q, c in bar.cells[l].items()):
                    return "bad", None
            if nxt is not None and nxt.dirs.get(x) is not None and nxt.dirs[x] != d:
                front = sorted(((l, q, c) for l in lanes_of(x) for q, c in nxt.cells[l].items() if c in ONSET and in_front(q)),
                               key=lambda t: (t[1], t[0]))
                if len(front) == 1 and front[0][2] == "1" and self.touchable(b + 1) and drop is None:
                    drop = front[0]                       # 앞쪽 노트가 하나뿐이면 그것만 빼고 유턴 유지 (사용자 결정)
                elif front:
                    return "bad", None
        return ("drop", drop) if drop else ("ok", None)

    def apply_plan(self, b: int, plan: Plan) -> bool:
        g, d_main = expected_main(b)
        o = 1 - g
        bar = self.hard.bars[b]
        snap = bar.copy()

        def fail() -> bool:
            self.hard.bars[b] = snap
            return False
        for op in plan.ops:
            if op.src is not None:
                bar.cells[op.src].pop(op.pos, None)
                if op.end is not None:
                    bar.cells[op.src].pop(op.end[0], None)
        changed = set()
        if any(op.dst in lanes_of(o) for op in plan.ops):
            bar.dirs[o] = plan.other_dir
            changed.add(o)
        if plan.main_dir is not None:
            bar.dirs[g] = plan.main_dir
            changed.add(g)
        if plan.drop_main:
            if any(bar.cells[l] for l in lanes_of(g)):
                return fail()
            bar.dirs[g] = None
        for op in plan.ops:
            if op.src is None and self.percentile(b, op.pos) < op.gate:
                return fail()
            if op.char == "2" and op.end is not None:
                if self._hold_ok(b, op.dst, op.pos, op.end[0]):
                    return fail()
            elif self.can_place(b, op.dst, op.pos, allow_same_time=op.chord):
                return fail()
            bar.cells[op.dst][op.pos] = op.char
            if op.end is not None:
                bar.cells[op.dst][op.end[0]] = op.end[1]
        status, drop = self._uturn_check(b, changed)
        if status == "bad":
            return fail()
        if drop is not None:
            dl, dq, dc = drop
            del self.hard.bars[b + 1].cells[dl][dq]
            self.log.append(LogEntry(b + 1, dl, dq, dc, "UTURN_DROP", None,
                                     f"{b:03d}마디 기믹({FAMILY_KO[plan.family]})이 만든 유턴의 앞쪽 노트 하나를 뺌"))
        dir_txt = {"own": "원래 방향", "flipped": "방향 뒤집음", "rev": "메인 라인도 뒤집음"}.get(plan.variant, plan.variant)
        name = FAMILY_KO[plan.family]
        for op in plan.ops:
            if op.src is not None:
                self.log.append(LogEntry(b, op.dst, op.pos, op.char, f"GIMMICK_{plan.family}_MOVE", None,
                                         f"기믹 {name}: 레인{op.src}→{op.dst} 옮김 ({dir_txt})"))
                if op.end is not None:
                    self.log.append(LogEntry(b, op.dst, op.end[0], op.end[1], f"GIMMICK_{plan.family}_MOVE", None,
                                             f"기믹 {name}: 홀드 끝도 레인{op.dst} 로"))
            else:
                self.put(b, op.dst, op.pos, op.char, f"GIMMICK_{plan.family}", self.percentile(b, op.pos),
                         f"기믹 {name}: 레인{op.dst} {op.pos} 추가 ({dir_txt})")
        if plan.main_dir is not None or plan.drop_main:
            self.log.append(LogEntry(b, 0, Fraction(0), "0", f"GIMMICK_{plan.family}_DIR", None,
                                     f"기믹 {name}: 메인 라인 " + ("비움(마디 통째 다른 그룹)" if plan.drop_main else "방향 뒤집음")))
        self.notes.setdefault(b, {"flags": []}).update({"gimmick": plan.family, "gimmick_variant": plan.variant,
                                                         "gimmick_sig": repr(plan.sig)})
        return True

    def _similar_music(self, a: int, b: int) -> bool:
        """두 마디 음원이 같음·반주 같음인지 (같은 기믹 모양을 이어 써도 되는 경우)."""
        if self.feat is None:
            return False
        if not hasattr(self, "_sim"):
            from .sections import bar_features, similarity
            bf = bar_features(self.feat, self.last, self.bpm, self.offset)
            self._sim = (similarity(bf), {x: i for i, x in enumerate(bf.bars)})
        from .sections import classify
        sim, idx = self._sim
        if a not in idx or b not in idx:
            return False
        return classify(sim, idx[a], idx[b]) in ("IDENTICAL", "SAME_BACKING")

    def _run_len(self, b: int, chosen) -> int:
        n = 1
        x = b - 1
        while x in chosen:
            n, x = n + 1, x - 1
        x = b + 1
        while x in chosen:
            n, x = n + 1, x + 1
        return n

    def apply_gimmicks(self, bars: list[int]) -> set[int]:
        from collections import Counter
        cand = [b for b in bars if b >= 2 and self.normal.bars[b].note_count() > 0]
        n = round(self.p.gimmick_rate * len(cand))
        base = {b: self.gimmick_score(b) for b in cand}
        chosen: dict[int, Plan] = {}
        tried: set[int] = set()
        fam_n: Counter = Counter()
        sig_n: Counter = Counter()
        fam_cap = max(2, math.ceil(self.p.family_cap * n))
        sig_cap = max(2, math.ceil(self.p.sig_cap * n))
        level = self.cfg.gimmick_level

        def score(x: int) -> float:
            near = (x - 1) in chosen or (x + 1) in chosen
            if not near:
                return base[x]
            return base[x] + (self.p.neighbor_bonus if self._run_len(x, chosen) <= self.p.max_run else -self.p.neighbor_bonus)
        while len(chosen) < n:
            avail = [x for x in cand if x not in tried]
            if not avail:
                break
            b = max(avail, key=lambda x: (score(x), -x))
            tried.add(b)
            plan = self._choose_plan(b, chosen, fam_n, sig_n, fam_cap, sig_cap, level)
            if plan is not None:
                chosen[b] = plan
                fam_n[plan.family] += 1
                sig_n[plan.sig] += 1
        return set(chosen)

    def _choose_plan(self, b: int, chosen, fam_n, sig_n, fam_cap: int, sig_cap: int, level: int):
        bar = self.hard.bars[b]
        g = expected_main(b)[0]
        if bar.dirs.get(1 - g) is not None:
            return None                                   # 이미 두 그룹을 쓰는 마디 (손작업·노멀 그대로)
        plans = build_plans(bar, b, g, lambda q: self.percentile(b, q), level, self.onset_positions(b))
        weights = family_weights("odd" if b % 2 else "even", level, self.p.family_weights)
        neigh = [(x, chosen[x]) for x in (b - 1, b + 1) if x in chosen]
        for attempt in range(20):
            usable: dict[str, list[Plan]] = {}
            w: dict[str, float] = {}
            for f, ps in plans.items():
                if weights.get(f, 0.0) <= 0 or fam_n[f] >= fam_cap:
                    continue
                ok = [p for p in ps if sig_n[p.sig] < sig_cap
                      and not any(p.sig == q.sig and not self._similar_music(b, x) for x, q in neigh)]
                if not ok:
                    continue
                usable[f] = ok
                w[f] = weights[f] * (self.p.same_family_penalty if any(q.family == f for _x, q in neigh) else 1.0)
            f = draw(w, _kh(self.cfg.id, b, "family", attempt))
            if f is None:
                return None
            for p in weighted_order(usable[f], lambda p: _kh(self.cfg.id, b, repr(p.sig))):
                if self.apply_plan(b, p):
                    return p
            plans.pop(f, None)
        return None

    # ---------- 4. 빈 칸 채우기 ----------
    def gapfill(self, b: int, budget: int) -> int:
        g_main, d_main = expected_main(b)
        bar = self.hard.bars[b]
        if bar.dirs[g_main] is None:
            bar.dirs[g_main] = d_main
        normal_pos = sorted({p for l in LANES for p, c in self.normal.bars[b].cells[l].items() if c in ONSET})
        cands = []
        uturn_here = is_uturn(self.hard, b, g_main)
        for k in range(8):
            pos = Fraction(k, 8)
            if pos in self.onset_positions(b):
                continue
            if uturn_here and in_front(pos):
                continue                              # 유턴 마디 앞쪽에는 넣지 않음
            pc = self.percentile(b, pos)
            if pc < self.p.gate_percentile:
                continue
            near8 = any(abs(pos - q) == EIGHTH for q in normal_pos)
            score = self.z("harm_mid", b, pos) + 0.5 * self.z("mid", b, pos) + 0.3 * near8
            cands.append((score, pos, pc))
        placed = 0
        for score, pos, pc in sorted(cands, key=lambda x: (-x[0], x[1])):
            if placed >= budget:
                break
            prev = [(p, l) for p, l, c in bar.notes() if c in ONSET and group_of(l) == g_main and p < pos]
            lanes = lanes_of(g_main)
            first = mirror_lane(max(prev)[1]) if prev else lanes[0]
            for lane in (first, mirror_lane(first)):
                if self.can_place(b, lane, pos) is None:
                    self.put(b, lane, pos, "1", "GAPFILL", round(score, 2), f"빈 8분 칸 채우기 (소리 P{pc:.0f})")
                    placed += 1
                    break
        return placed

    # ---------- 5b. 리듬 변주 (16분·셋잇단) ----------
    def _front_blocked(self, b: int, lane: int, pos: Fraction) -> bool:
        """유턴 마디의 앞쪽 칸이면 True (그 그룹이 앞 마디와 반대 방향)."""
        g = group_of(lane)
        prev, bar = self.eventual_bar(b - 1), self.hard.bars[b]
        return (in_front(pos) and prev is not None and prev.dirs.get(g) is not None
                and bar.dirs.get(g) is not None and prev.dirs[g] != bar.dirs[g])

    def _note_at(self, b: int, pos: Fraction):
        bar = self.hard.bars.get(b)
        if bar is None:
            return None
        hits = [(l, c) for l in LANES for q, c in bar.cells[l].items() if q == pos and c in "12"]
        return hits[0] if len(hits) == 1 else None

    def _active_lanes(self, b: int) -> list[int]:
        bar = self.hard.bars[b]
        return [l for g in (0, 1) if bar.dirs.get(g) is not None for l in lanes_of(g)]

    def _rhythm16(self, b: int, peaks: list[tuple[Fraction, float]], ctx) -> int:
        """마디 b 에 16분 음형: 약한 8분을 16분 뒤 정점으로 밀기(SHIFT16) 또는 다음 노트 앞 16분 픽업(PICKUP16)."""
        bar = self.hard.bars[b]
        done = 0
        for q, pc in sorted(peaks, key=lambda x: (-x[1], x[0])):
            if done >= self.p.rhythm_max_per_bar:
                break
            if any(q in bar.cells[l] for l in LANES):
                continue
            prev8, next8 = q - Fraction(1, 16), q + Fraction(1, 16)
            src = self._note_at(b, prev8)
            if src is not None and src[1] == "1" and ctx.pm(b, prev8) < 40.0 and not self._front_blocked(b, src[0], q):
                lane = src[0]
                del bar.cells[lane][prev8]
                if self.can_place(b, lane, q) is None:
                    bar.cells[lane][q] = "1"
                    bar.length = bar.min_length()
                    self.log.append(LogEntry(b, lane, prev8, "0", "RHYTHM_SHIFT16_MOVE", None, f"{q} 로 옮김 (원래 칸)"))
                    self.log.append(LogEntry(b, lane, q, "1", "RHYTHM_SHIFT16_MOVE", None,
                                             f"약한 8분({prev8}, 중역 P{ctx.pm(b, prev8):.0f})을 16분 정점({q}, P{pc:.0f})으로 밀기"))
                    done += 1
                    continue
                bar.cells[lane][prev8] = "1"
            nxt = self._note_at(b, next8) if next8 < 1 else None
            if nxt is None:
                continue
            lanes = [mirror_lane(nxt[0])] + [l for l in self._active_lanes(b) if l not in (nxt[0], mirror_lane(nxt[0]))]
            for lane in lanes:
                if self._front_blocked(b, lane, q) or self.can_place(b, lane, q):
                    continue
                bar.cells[lane][q] = "1"
                bar.length = bar.min_length()
                self.put(b, lane, q, "1", "RHYTHM_PICKUP16", round(pc / 100, 3), f"16분 픽업 (음원 16분 정점 P{pc:.0f}, 다음 노트 {next8})")
                done += 1
                break
        return done

    def _triplet(self, b: int, beats: list[int]) -> int:
        """셋잇단으로 들리는 박: 박 노트(A) – 1/12(B, 반대 레인) – 2/12(A). 8분 가운데 노트가 있으면 1/12 로 옮김."""
        bar = self.hard.bars[b]
        done = 0
        for j in beats:
            s = Fraction(j, 4)
            head = self._note_at(b, s)
            if head is None or head[1] != "1":
                continue
            a, bl = head[0], mirror_lane(head[0])
            t1, t2, mid = s + Fraction(1, 12), s + Fraction(2, 12), s + Fraction(1, 8)
            snap = bar.copy()
            moved = self._note_at(b, mid)
            if moved is not None and moved[1] == "1":
                del bar.cells[moved[0]][mid]
            elif moved is not None:
                continue
            ok = True
            for lane, q in ((bl, t1), (a, t2)):
                if self._front_blocked(b, lane, q) or self.can_place(b, lane, q):
                    ok = False
                    break
                bar.cells[lane][q] = "1"
            if not ok:
                self.hard.bars[b] = snap
                bar = snap
                continue
            bar.length = bar.min_length()
            if moved is not None:
                self.log.append(LogEntry(b, moved[0], mid, "0", "RHYTHM_TRIPLET_MOVE", None, "셋잇단으로 바꾸며 뺀 8분 (원래 칸)"))
            self.log.append(LogEntry(b, bl, t1, "1", "RHYTHM_TRIPLET", None, f"{j + 1}박 셋잇단 (음원 1/3·2/3 지점이 강함)"))
            self.log.append(LogEntry(b, a, t2, "1", "RHYTHM_TRIPLET", None, f"{j + 1}박 셋잇단"))
            done += 1
        return done

    def apply_rhythm(self, work: list[int]) -> None:
        """음원 16분 정점이 있는 마디에 16분 음형 (비슷한 마디 묶음 단위, 마디 상한), 셋잇단 박에 셋잇단."""
        if self.feat is None or self.p.rhythm_bar_rate <= 0:
            return
        from .rhythm import clusters, make_ctx, odd16_peaks, triplet_bars
        ctx = make_ctx(self.feat, self.bpm, self.offset, self.last)
        bars = [b for b in work if b > 0 and self.notes.get(b, {}).get("gimmick") != "WHOLE"]
        peaks = {b: odd16_peaks(ctx, b) for b in bars}
        cand = [b for b in bars if peaks[b]]
        cl = clusters(ctx, cand)
        groups: dict[int, list[int]] = {}
        for b in cand:
            groups.setdefault(cl[b], []).append(b)
        cap = round(self.p.rhythm_bar_rate * len(bars))
        used = 0
        for g in sorted(groups.values(), key=lambda g: (-float(np.mean([max(p for _q, p in peaks[b]) for b in g])), g[0])):
            if used >= cap:
                break
            for b in g:
                if self._rhythm16(b, peaks[b], ctx):
                    used += 1
                    n = self.notes.setdefault(b, {"flags": []})
                    n["rhythm"] = "16"
                    n["rhythm_group"] = g[0]
        for b, js in triplet_bars(ctx, bars).items():
            if self._triplet(b, js):
                self.notes.setdefault(b, {"flags": []})["rhythm"] = "triplet"
        for b in bars:
            bar = self.hard.bars[b]
            if len(peaks.get(b, [])) >= 2 and len([1 for _q, _l, c in bar.notes() if c in "12"]) <= 1:
                self.notes.setdefault(b, {"flags": []})["flags"].append("RHYTHM_RECHART")

    # ---------- 6b. 유턴 앞쪽 정리 ----------
    def fix_uturn_fronts(self) -> None:
        """남은 유턴 앞쪽 노트: 생성기가 뒤집은 기믹 그룹이 원인이면 원래 방향으로 되돌리고(유턴 자체를 없앰),
        아니면 앞쪽 노트가 하나뿐이고 손댈 수 있는 마디일 때만 그 노트를 뺀다. 그래도 남으면 리포트에 표시."""
        rule_pairs = [(t_, s_) for r in rules_from_config(self.cfg) for t_, s_ in zip(r.targets, r.sources)]
        for _ in range(500):
            todo = [u for u in find_uturns(self.hard) if u.front and not self.notes.get(u.bar, {}).get("uturn_flagged")]
            if not todo:
                return
            u = todo[0]
            # 생성기가 메인 라인을 뒤집은 마디(REVERSE·+REV, 그 복사본 포함)가 원인이면 그 마디 메인 방향을 되돌린다
            rev = [x for x in (u.bar - 1, u.bar) if x in self.hard.bars and x not in self.fixed
                   and expected_main(x)[0] == u.group and self.hard.bars[x].dirs.get(u.group) not in (None, expected_main(x)[1])]
            if rev:
                x = rev[0]
                self.hard.bars[x].dirs[u.group] = expected_main(x)[1]
                self.log.append(LogEntry(x, 0, Fraction(0), "0", "UTURN_UNREVERSE", None,
                                         f"메인 라인 방향을 원래대로 되돌림 ({u.bar:03d}마디 유턴 앞쪽 노트 {len(u.front)}개 때문)"))
                continue
            cause = u.bar - 1 if u.kind == "B" else u.bar
            g = u.group
            cbar = self.hard.bars[cause]
            generator_flip = (cause not in self.fixed and g != expected_main(cause)[0]
                              and cbar.dirs.get(g) is not None and cbar.dirs[g] != DEFAULT_DIR[g])
            if generator_flip:
                flipped = cbar.dirs[g]
                for x in [cause] + [t_ for t_, s_ in rule_pairs if s_ == cause]:
                    xb = self.hard.bars.get(x)
                    if xb is not None and x not in self.fixed and xb.dirs.get(g) == flipped and g != expected_main(x)[0]:
                        xb.dirs[g] = DEFAULT_DIR[g]
                        for l in lanes_of(g):
                            for q, c in xb.cells[l].items():
                                self.log.append(LogEntry(x, l, q, c, "UTURN_UNFLIP", None,
                                                         f"그룹{g} 방향을 원래대로 되돌림 ({u.bar:03d}마디 유턴 앞쪽 노트 {len(u.front)}개 때문)"))
                continue
            if len(u.front) == 1 and u.front[0][2] == "1" and u.bar not in self.fixed:
                fl, fq, fc = u.front[0]
                del self.hard.bars[u.bar].cells[fl][fq]
                self.log.append(LogEntry(u.bar, fl, fq, fc, "UTURN_DROP", None, "유턴 앞쪽 노트 하나를 뺌 (유턴은 유지)"))
                continue
            n = self.notes.setdefault(u.bar, {"flags": []})
            n["uturn_flagged"] = True
            n["flags"].append("UTURN")

    # ---------- 7. 정리 ----------
    def conflicts(self) -> list[tuple[str, tuple, tuple]]:
        """검증기와 같은 기준의 충돌 쌍: (코드, 앞 노트 키, 뒤 노트 키). 키 = (마디, 레인, 위치)."""
        return self.placer.conflicts()

    def repair(self) -> None:
        """생성한 노트가 끼어 있는 충돌만, 그 쌍 안의 생성 노트 중 점수 낮은 것을 뺀다. 원래 노멀끼리의 충돌은 건드리지 않는다."""
        removable = {(e.bar, e.lane, e.pos): e for e in self.log
                     if e.origin in ("GAPFILL", "COPY_ADDED", "RHYTHM_PICKUP16", "RHYTHM_TRIPLET")
                     or (e.origin.startswith("GIMMICK") and not e.origin.endswith(("_MOVE", "_DIR")))}
        self.placer.repair(removable, lambda v, code: self.log.append(
            LogEntry(v.bar, v.lane, v.pos, v.char, "REMOVED", v.score, f"검증 {code} 때문에 뺌")))

    # ---------- mode finish: 손작업 마디 덮어쓰기 ----------
    def overlay_frozen(self) -> None:
        src = self.frozen_source
        for b in sorted(self.cfg.frozen_bars):
            if b not in src.bars:
                continue
            self.hard.bars[b] = src.bars[b].copy()
            self.log = [e for e in self.log if e.bar != b]
            nb = self.normal.bars.get(b)
            for lane in LANES:
                for p, c in self.hard.bars[b].cells[lane].items():
                    if nb is None or nb.cells[lane].get(p) != c:
                        self.log.append(LogEntry(b, lane, p, c, "FROZEN", None, "손작업 하드 마디 (시간 이동 후 그대로)"))
            self.notes.setdefault(b, {"flags": []})["frozen"] = True
        # 손작업 마디에서 넘어온 노트가 손작업이 아닌 마디에 떨어진 경우
        for ob, ol, op, nb_, nl, np_, c in self.frozen_carried:
            if ob not in self.cfg.frozen_bars or nb_ in self.cfg.frozen_bars or nb_ not in self.hard.bars:
                continue
            bar = self.hard.bars[nb_]
            if bar.cells[nl].get(np_) == c:
                continue                                  # 이미 같은 노트가 있음 (노멀 리타이밍 때 사용자가 넣은 것과 같음)
            g = group_of(nl)
            if bar.dirs[g] is None:
                bar.dirs[g] = expected_main(nb_)[1] if expected_main(nb_)[0] == g else DEFAULT_DIR[g]
            if self.can_place(nb_, nl, np_, allow_same_time=True) is None:
                self.put(nb_, nl, np_, c, "FROZEN_CARRY", None, f"{ob:03d}마디 손작업 노트가 시간 이동으로 넘어옴")
            else:
                self.notes.setdefault(nb_, {"flags": []})["flags"].append("CARRY_CONFLICT")

    # ---------- 전체 ----------
    def run(self) -> PortResult:
        self.fix_open_holds()
        work = [b for b in sorted(self.normal.bars) if b > 0 and b not in self.fixed and b not in self.copy_targets]
        budget = self.budgets(work) if self.budget_override is None else {b: self.budget_override.get(b, 0) for b in work}
        gim = self.apply_gimmicks(work)
        for b in work:
            if self.notes.get(b, {}).get("gimmick") == "WHOLE":
                continue                                  # 마디 통째 다른 그룹 — 메인 라인을 다시 채우지 않음
            used = sum(1 for e in self.log if e.bar == b and e.origin.startswith("GIMMICK") and not e.origin.endswith(("_MOVE", "_DIR")))
            left = max(0, budget[b] - used)
            placed = self.gapfill(b, left) if left else 0
            n = self.notes.setdefault(b, {"flags": []})
            n.update({"budget": budget[b], "dense": self.dense_cells(b), "gapfill": placed})
            if self.normal.bars[b].length <= 4 and self.dense_cells(b) - len(self.onset_positions(b)) >= 3:
                n["flags"].append("RECHART")
        self.apply_rhythm(work)
        if self.copy_targets:
            new, items = plan_sync(self.cfg, self.hard, self.normal, {"bars": {}}, force=True)
            self.hard = new
            copied = {it.bar for it in items if it.status in ("write", "same")}
            self.log = [e for e in self.log if e.bar not in copied]      # 덮어쓴 마디의 이전 기록은 지운다
            for it in items:
                if it.status not in ("write", "same"):
                    continue
                self.notes.setdefault(it.bar, {"flags": []})["copy"] = (it.source, it.transform)
                nb, hb = self.normal.bars.get(it.bar), self.hard.bars[it.bar]
                for lane in LANES:
                    for p, c in hb.cells[lane].items():
                        if nb is None or nb.cells[lane].get(p) != c:
                            origin = "COPY_ADDED" if c in ONSET else "COPY"
                            self.log.append(LogEntry(it.bar, lane, p, c, origin, None,
                                                     f"{it.source:03d}마디 {'반전 ' if it.transform == 'mirror' else ''}복사",
                                                     self.percentile(it.bar, p) if self.pct is not None else None))
                    if nb is not None:
                        for p, c in nb.cells[lane].items():
                            if c in ONSET and p not in hb.cells[lane]:     # 원본 마디에서 뺀 노트(유턴 앞쪽 등)가 복사로 넘어옴
                                self.log.append(LogEntry(it.bar, lane, p, c, "COPY_REMOVED", None,
                                                         f"{it.source:03d}마디 {'반전 ' if it.transform == 'mirror' else ''}복사로 빠짐"))
        if self.frozen_source is not None:
            self.overlay_frozen()
        self.fix_open_holds(set(b for b in self.hard.bars if b > 0))   # 손작업·복사·노멀 유지 마디의 끝 없는 홀드도 (게임 버그)
        self.fix_uturn_fronts()
        self.repair()
        for b in self.cfg.listen_bars:
            self.notes.setdefault(b, {"flags": []})["flags"].append("LISTEN")
        if self.cfg.rtl_suspect:
            for b, n in self.notes.items():
                if b % 2 == 0 and n.get("gimmick") in ("RESPONSE", "CALL", "INTERLEAVE", "PAIR", "SINGLE", "HALF1"):
                    n.setdefault("flags", []).append("RTL_CHECK")
        for b in gim:
            self.notes[b]["is_gimmick"] = True
        self.hard.header.notes = self.hard.count_notes()
        return PortResult(chart=self.hard, log=self.log, bars=self.notes, offset=self.offset)


def port_chart(normal: Chart, feat: AudioFeatures | None, bpm: float, offset: float, cfg: SongConfig,
               params: PortParams) -> PortResult:
    return Porter(normal, feat, bpm, offset, cfg, params).run()
