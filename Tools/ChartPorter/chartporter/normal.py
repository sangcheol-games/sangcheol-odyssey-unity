"""이지 → 노멀 (사용자 스타일, 2번 기준) — 신규 곡용. 기반은 사용자가 고친(설치된) 이지.

사용자 노멀(2번)은 이지 노트의 96%를 그대로 두고, 추가 노트의 88%를 이지 노트에서 1/8 거리에 넣는다.
70% 는 '4분 간격으로 붙은 두 이지 노트 사이의 8분'이다. 추가 위치는 음원 크기와 거의 무관하다(AUC .50).
  1. 이지의 노트·레인·홀드·마디 길이는 그대로 둔다 (끝 없는 홀드만 7/8 에서 닫음)
  2. 후보: 이웃한 이지 노트 간격이 1/4 면 그 가운데 8분. 위치별 가중치 1/8 .79, 5/8 .68, 3/8 .46, 7/8 .41 (2번에서 나눠진 비율)
     간격이 1/2 면 가운데 박 .22. 1/8 간격은 채우지 않는다. 앞 노트가 홀드면 ×0.1 (2번: 홀드 뒤 간격은 1/14 만 나눔)
  3. 거름망은 '소리 없는 칸 제외'만 (sound_ok). 하드 포팅의 P50 기준은 쓰지 않는다 (사용자 추가 노트의 55%가 P50 아래)
     같은 가중치끼리는 화성 중역 세기(0.05×z)로만 순서를 정한다
  4. 양: 마디당 target_per_bar(4.9 — 1·2번 사용자 노멀 4.82/4.91)에서 이지 수를 뺀 만큼, 노멀/이지 ×1.3–1.6 안, 마디당 최대 4
  5. 레인: 앞 노트의 반대 57%, 같은 레인 43% (2번, 곡·마디·위치 해시). 막히면 다른 레인
  6. 같은 레인 간격 ≥ max(8분, 126ms)·동시치기 없음·홀드 안 금지 (place.Placer). 검증 경고가 나면 추가 노트 중 점수 낮은 것부터 뺀다
  7. 복사 규칙(sections copy_of)이 있으면 대상 마디 = 그 마디의 이지 + 원본 마디에서 추가한 노트(변환). 대상 마디의 이지를
     사용자가 고쳤으면 그대로 남는다. 대상 마디에는 따로 추가하지 않는다 (양은 대상이 아닌 마디 비율만큼)
  8. 리듬 변주 (더하기만): 음원 당김음 점수 A ≥ 40 인 엇박을 먼저 채우고(1·4·5·6번 노멀 AUC .80–.89),
     16분 정점 마디(비슷한 마디 묶음 단위)에 다음 노트 앞 16분 픽업(마디당 1개, 곡 전체 마디의 20% 이하),
     셋잇단으로 들리는 박에는 1/2 대신 더 큰 1/3·2/3 칸
새 홀드·기믹·다른 그룹은 넣지 않는다 (사용자 노멀: 이지 홀드 78% 유지, 다른 그룹 0).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, cell_value
from .easy import SOUND_ENVS, _h, make_ctx
from .holds import close_unterminated_holds
from .model import LANES, Chart, cross_lane, expected_main, group_of, mirror_lane
from .place import Placer
from .port import LogEntry, PortResult

EIGHTH = Fraction(1, 8)
QUARTER = Fraction(1, 4)
HALF = Fraction(1, 2)


@dataclass
class NormalParams:
    target_per_bar: float = 4.9
    ratio_min: float = 1.3
    ratio_max: float = 1.6
    max_add_per_bar: int = 4
    mid_prior: dict[int, float] = field(default_factory=lambda: {1: .79, 5: .68, 3: .46, 7: .41})
    half_gap_prior: float = 0.22
    after_hold_factor: float = 0.1
    tie_w: float = 0.05
    mirror_p: float = 0.57
    sound_db: float = 20.0
    sound_pct: float = 10.0
    antic_threshold: float = 40.0
    pickup_rate: float = 0.2


@dataclass
class Cand:
    bar: int
    pos: Fraction
    prev_lane: int
    score: float
    why: str
    origin: str = "NORMAL_ADD"

    @property
    def t(self) -> Fraction:
        return self.bar + self.pos


def _onsets(chart: Chart) -> list[tuple[Fraction, int, Fraction, int, str]]:
    return sorted((b + q, b, q, l, c) for b, bar in chart.bars.items() if b > 0 for q, l, c in bar.notes() if c in "12")


def antic_candidates(easy: Chart, params: NormalParams, rctx) -> list[Cand]:
    """음원 당김음 점수 A 가 높은 빈 엇박 칸 (앞 노트 레인 기준)."""
    from .rhythm import antic_score
    ons = _onsets(easy)
    occupied = {t for t, *_ in ons}
    out = []
    for b in range(1, easy.last_bar() + 1):
        for k in (1, 3, 5, 7):
            pos = Fraction(k, 8)
            if b + pos in occupied:
                continue
            a = antic_score(rctx, b, pos)
            if a < params.antic_threshold:
                continue
            prev = [x for x in ons if x[0] < b + pos]
            if not prev:
                continue
            out.append(Cand(b, pos, prev[-1][3], 0.8 + 0.002 * a, f"당김음 엇박 (음원 A={a:.0f})", "NORMAL_ANTIC"))
    return out


def candidates(easy: Chart, params: NormalParams, ctx=None) -> list[Cand]:
    """이웃한 이지 노트 사이의 후보 칸과 점수. ctx(음원)가 없으면 거름망·동점 처리 없이."""
    p = params
    last = easy.last_bar()
    ons = _onsets(easy)
    occupied = {t for t, *_ in ons}
    out = []
    if ctx is not None:
        hm = np.array([cell_value(ctx.feat, "harm_mid", b, Fraction(k, 8), ctx.bpm, ctx.offset)
                       for b in range(1, last + 1) for k in range(8)])
        z_mean, z_std = float(hm.mean()), float(hm.std()) or 1.0
    for (ta, ba, qa, la, ca), (tc, *_rest) in zip(ons, ons[1:]):
        gap = tc - ta
        if gap == QUARTER:
            m = ta + EIGHTH
            k = int((m - int(m)) * 8)
            prior = p.mid_prior.get(k)
            why = f"4분 간격 사이 8분 ({m - int(m)} 가중치 {prior})"
        elif gap == HALF:
            m = ta + QUARTER
            prior = p.half_gap_prior
            why = f"2분 간격 사이 박 (가중치 {prior})"
        else:
            continue
        if prior is None or m in occupied:
            continue
        b, pos = int(m), m - int(m)
        if b < 1 or b > last:
            continue
        if ca == "2":
            prior *= p.after_hold_factor
            why += f", 앞 노트가 홀드 ×{p.after_hold_factor}"
        score = prior
        if ctx is not None:
            sound = (ctx.rms(b, pos) >= ctx.rms_med - p.sound_db and max(ctx.p(e, b, pos) for e in SOUND_ENVS) >= p.sound_pct)
            if not sound:
                continue
            z = (cell_value(ctx.feat, "harm_mid", b, pos, ctx.bpm, ctx.offset) - z_mean) / z_std
            score += p.tie_w * z
        out.append(Cand(b, pos, la, score, why))
    return out


def generate_normal(easy: Chart, feat: AudioFeatures | None, bpm: float, offset: float, song_id: int,
                    params: NormalParams | None = None, title: str | None = None, artist: str | None = None,
                    level: int | None = None, cfg=None) -> PortResult:
    p = params or NormalParams()
    normal = easy.copy()
    normal.path = None
    normal.header.difficulty = "Normal"
    if title is not None:
        normal.header.title = title
    if artist is not None:
        normal.header.artist = artist
    normal.header.level = level if level is not None else normal.header.level
    log: list[LogEntry] = []
    notes: dict[int, dict] = {b: {"flags": []} for b in normal.bars}
    for f in close_unterminated_holds(normal):
        log.append(LogEntry(f.bar, f.lane, f.start, "2", "HOLD_END_78" if f.action == "close_78" else "HOLD_TO_TAP", None, f.detail))
        if f.action == "manual":
            notes.setdefault(f.bar, {"flags": []})["flags"].append("HOLD_MANUAL")
    last = normal.last_bar()
    ctx = make_ctx(feat, bpm, offset, last) if feat is not None else None
    pairs = []
    if cfg is not None:
        from .sync import ordered_pairs, rules_from_config
        fixed = set(cfg.locked_bars) | set(cfg.frozen_bars) | set(cfg.keep_normal_bars)
        pairs = [(t, s, r) for t, s, r in ordered_pairs(rules_from_config(cfg)) if t not in fixed and t in normal.bars and s in normal.bars]
    targets = {t for t, _s, _r in pairs}
    cands = [c for c in candidates(normal, p, ctx) if c.bar not in targets]
    rctx = None
    if feat is not None:
        from .rhythm import make_ctx as rhythm_ctx
        rctx = rhythm_ctx(feat, bpm, offset, last)
        best = {(c.bar, c.pos): c for c in cands}
        for c in antic_candidates(normal, p, rctx):
            if c.bar not in targets and ((c.bar, c.pos) not in best or best[(c.bar, c.pos)].score < c.score):
                best[(c.bar, c.pos)] = c
        cands = list(best.values())
    ons = _onsets(normal)
    n_easy = len(ons)
    want = p.target_per_bar * last - n_easy
    n_add = int(round(min(max(want, (p.ratio_min - 1) * n_easy), (p.ratio_max - 1) * n_easy)))
    if targets and n_easy:
        n_add = int(round(n_add * sum(1 for x in ons if x[1] not in targets) / n_easy))
    placer = Placer(lambda: normal, bpm)
    per: dict[int, int] = {}
    added: dict[tuple, LogEntry] = {}
    for c in sorted(cands, key=lambda c: (-c.score, c.t)):
        if len(added) >= n_add:
            break
        if per.get(c.bar, 0) >= p.max_add_per_bar:
            continue
        g = expected_main(c.bar)[0]
        used = normal.bars[c.bar].groups_used()
        if used and g not in used:
            g = used[0]                                   # 사용자가 이지에서 그룹을 바꾼 마디는 그 그룹을 따른다
        base = c.prev_lane if group_of(c.prev_lane) == g else cross_lane(c.prev_lane)
        first = mirror_lane(base) if _h(song_id, c.bar, c.pos, "nlane") < p.mirror_p else base
        for lane in (first, mirror_lane(first)):
            if placer.can_place(c.bar, lane, c.pos) is None:
                bar = normal.bars[c.bar]
                if bar.dirs[g] is None:
                    bar.dirs[g] = expected_main(c.bar)[1]
                bar.cells[lane][c.pos] = "1"
                bar.length = bar.min_length()             # 4칸 마디에 8분을 넣으면 8칸으로
                e = LogEntry(c.bar, lane, c.pos, "1", c.origin, round(c.score, 3), c.why,
                             ctx.p("mid", c.bar, c.pos) if ctx is not None else None)
                log.append(e)
                added[(c.bar, lane, c.pos)] = e
                per[c.bar] = per.get(c.bar, 0) + 1
                break
    if rctx is not None:
        _rhythm_adds(normal, rctx, p, placer, targets, added, log, notes, song_id)
    # 복사: 원본 마디에서 추가한 노트를 대상 마디로 (사슬 A→A'→A'' 는 앞에서부터)
    from .sync import map_lane
    for t, s, rule in pairs:
        notes.setdefault(t, {"flags": []})["copy"] = (s, rule.transform)
        src_adds = sorted((k for k in added if k[0] == s), key=lambda k: (k[2], k[1]))
        g = expected_main(t)[0]
        for _sb, lane, pos in src_adds:
            nl = map_lane(lane, rule.transform)
            if (nl, pos) in rule.drop:
                continue
            if placer.can_place(t, nl, pos) is None:
                bar = normal.bars[t]
                if bar.dirs[group_of(nl)] is None:
                    bar.dirs[group_of(nl)] = expected_main(t)[1] if group_of(nl) == g else 1 - expected_main(t)[1]
                bar.cells[nl][pos] = "1"
                bar.length = bar.min_length()
                e = LogEntry(t, nl, pos, "1", "COPY_ADDED", added[(s, lane, pos)].score, f"{s:03d}마디 추가 노트 복사 ({rule.transform})")
                log.append(e)
                added[(t, nl, pos)] = e
            else:
                log.append(LogEntry(t, nl, pos, "0", "COPY_SKIP", None, f"{s:03d}마디 추가 노트를 놓을 수 없음 (대상 마디 이지와 충돌)"))
    placer.repair(dict(added), lambda v, code: log.append(
        LogEntry(v.bar, v.lane, v.pos, v.char, "REMOVED", v.score, f"검증 {code} 때문에 뺌")))
    for b, n in per.items():
        notes.setdefault(b, {"flags": []})["added"] = n
    normal.header.notes = normal.count_notes()
    res = PortResult(chart=normal, log=log, bars=notes, offset=offset)
    res.bars[0] = {"flags": [], "n_add_target": n_add, "n_candidates": len(cands)}
    return res


def _rhythm_adds(normal: Chart, rctx, p: NormalParams, placer, targets: set[int], added: dict, log: list, notes: dict, song_id: int) -> None:
    """노멀 리듬 변주 (더하기만): 16분 픽업(묶음 단위, 마디당 1개), 셋잇단 박의 1/3·2/3 칸."""
    from .rhythm import clusters, odd16_peaks, triplet_bars
    bars = [b for b in range(1, normal.last_bar() + 1) if b not in targets and b in normal.bars]
    peaks = {b: odd16_peaks(rctx, b) for b in bars}
    cand = [b for b in bars if peaks[b]]
    cl = clusters(rctx, cand)
    groups: dict[int, list[int]] = {}
    for b in cand:
        groups.setdefault(cl[b], []).append(b)
    cap = round(p.pickup_rate * len(bars))
    used = 0
    on_at = lambda b, q: [(l, c) for l in LANES for qq, c in normal.bars[b].cells[l].items() if qq == q and c in "12"]
    for g in sorted(groups.values(), key=lambda g: (-float(np.mean([max(x for _q, x in peaks[b]) for b in g])), g[0])):
        if used >= cap:
            break
        for b in g:
            for q, pc in sorted(peaks[b], key=lambda x: (-x[1], x[0])):
                nxt = on_at(b, q + Fraction(1, 16)) if q + Fraction(1, 16) < 1 else []
                if len(nxt) != 1 or on_at(b, q):
                    continue
                lane = mirror_lane(nxt[0][0])
                if placer.can_place(b, lane, q) is None:
                    bar = normal.bars[b]
                    bar.cells[lane][q] = "1"
                    bar.length = bar.min_length()
                    e = LogEntry(b, lane, q, "1", "NORMAL_PICKUP16", round(pc / 100, 3), f"16분 픽업 (음원 16분 정점 P{pc:.0f})")
                    log.append(e)
                    added[(b, lane, q)] = e
                    notes.setdefault(b, {"flags": []})["rhythm"] = "16"
                    used += 1
                    break
    for b, js in triplet_bars(rctx, bars).items():
        for j in js:
            s0 = Fraction(j, 4)
            head = on_at(b, s0)
            if len(head) != 1 or on_at(b, s0 + Fraction(1, 8)):
                continue
            t1, t2 = s0 + Fraction(1, 12), s0 + Fraction(2, 12)
            q = t1 if rctx.pm(b, t1) >= rctx.pm(b, t2) else t2
            lane = mirror_lane(head[0][0])
            if placer.can_place(b, lane, q) is None:
                bar = normal.bars[b]
                bar.cells[lane][q] = "1"
                bar.length = bar.min_length()
                e = LogEntry(b, lane, q, "1", "NORMAL_TRIPLET", None, f"{j + 1}박 셋잇단 (음원 1/3·2/3 지점이 강함)")
                log.append(e)
                added[(b, lane, q)] = e
                notes.setdefault(b, {"flags": []})["rhythm"] = "triplet"

