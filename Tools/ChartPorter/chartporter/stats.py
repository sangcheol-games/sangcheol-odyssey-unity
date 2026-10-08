"""사용자 노멀/하드 쌍에서 하드 기조 통계(style profile) 뽑기.

기준 곡(songs/*.yaml 의 role: reference)마다 diff 를 돌려 수치를 모은다.
- 16분 홀수 칸 하드 노트는 학습에서 뺀다 (청취 확인 목록 대상).
- sections 로 정해진 복사 대상 마디(1·2절 복사본)는 패턴 통계에서 뺀다 (같은 패턴 중복 집계 방지).
결과: profiles/style_profile.json (곡별 수치 + primary = 주 기준 곡)
"""
from __future__ import annotations

import json
from collections import Counter
from fractions import Fraction
from pathlib import Path
from statistics import mean, median

from .chart_io import read_chart
from .config import SongConfig, load_song, parse_bars
from .diff import ONSET, ChartDiff, classify_added, diff_charts, grid_class
from .model import Chart, group_of, lanes_of, mirror_lane
from .paths import TOOL_DIR
from .timing import bar_seconds, song_time

PROFILE_PATH = TOOL_DIR / "profiles" / "style_profile.json"
SHORT_HOLD = Fraction(1, 8)


def copy_target_bars(cfg: SongConfig) -> set[int]:
    out: set[int] = set()
    for s in cfg.sections or []:
        if isinstance(s, dict) and s.get("copy_of") is not None:
            out |= parse_bars(s.get("bars"))
    return out


def _frac(c: Counter, total: int | None = None) -> dict[str, float]:
    total = total if total is not None else sum(c.values())
    return {str(k): round(v / total, 3) for k, v in sorted(c.items(), key=lambda kv: -kv[1])} if total else {}


def _onset_count(chart: Chart, bars: list[int]) -> int:
    return sum(1 for b in bars if b in chart.bars for _p, _l, c in chart.bars[b].notes() if c in ONSET)


def lane_relation_of_added(d: ChartDiff, hard: Chart) -> Counter:
    """추가 노트가 같은 마디·같은 그룹의 직전 노트와 같은 레인인지(same), 위아래 교대인지(alternate)."""
    c: Counter = Counter()
    for bd in d.bars.values():
        bar = hard.bars.get(bd.bar)
        if bar is None:
            continue
        for m in bd.matches:
            if m.kind != "added" or m.excluded:
                continue
            g = group_of(m.hard.lane)
            prev = [(p, l) for p, l, ch in bar.notes() if ch in ONSET and group_of(l) == g and p < m.hard.pos]
            if not prev:
                c["first_in_group"] += 1
            else:
                _p, l = max(prev)
                c["same" if l == m.hard.lane else "alternate"] += 1
    return c


def same_lane_gaps(chart: Chart, bpm: float, bars: set[int]) -> list[float]:
    gaps = []
    for lane in (1, 2, 3, 4):
        ts = sorted(song_time(b, p, bpm) for b, bar in chart.bars.items() if b in bars for p, c in bar.cells[lane].items() if c in "12")
        gaps += [b - a for a, b in zip(ts, ts[1:])]
    return gaps


def song_stats(cfg: SongConfig, bpm: float) -> dict:
    normal, _ = read_chart(cfg.base_path())
    hard, _ = read_chart(cfg.target_path())
    copies = copy_target_bars(cfg)
    all_bars = sorted(b for b in set(normal.bars) | set(hard.bars) if b > 0)
    bars = [b for b in all_bars if b not in copies]
    d = diff_charts(normal, hard, bars)
    matches = [m for bd in d.bars.values() for m in bd.matches]
    fate = Counter(m.kind for m in matches if m.normal is not None and not m.excluded)
    n_normal = sum(fate.values())
    added = classify_added(d, normal)
    n_added = len(added)

    # 밀도: 노멀 분할(4 / 8 …)별 하드 노트 시작 배율
    by_len: dict[int, list[int]] = {}
    for b in bars:
        bd = d.bars[b]
        nn = sum(1 for m in bd.matches if m.normal is not None)
        hh = sum(1 for m in bd.matches if m.hard is not None and not m.excluded)
        by_len.setdefault(bd.normal_len, [0, 0])
        by_len[bd.normal_len][0] += nn
        by_len[bd.normal_len][1] += hh
    ratio_by_len = {str(k): round(v[1] / v[0], 3) for k, v in sorted(by_len.items()) if v[0]}

    # 기믹
    from .timing import is_odd_sixteenth

    def _verified(g: dict) -> bool:
        """16분 홀수 칸(확인 전) 노트를 빼고도 다른 그룹 노트가 남는 기믹인가."""
        if g.get("main_dropped"):
            return True
        pat = [p for p, _l, c in g.get("extra_pattern", ()) if c in ONSET and not is_odd_sixteenth(Fraction(p))]
        return g.get("extra_group") is not None and bool(pat)

    gbars = {b: bd.gimmick for b, bd in d.bars.items() if _verified(bd.gimmick)}
    added_per_bar = Counter(m.hard.bar for m in matches if m.kind == "added" and not m.excluded)
    normal_per_bar = Counter(m.normal.bar for m in matches if m.normal is not None)
    gim_added = [added_per_bar.get(b, 0) for b in gbars]
    non_added = [added_per_bar.get(b, 0) for b in bars if b not in gbars]
    templates = Counter()
    extra_pos = Counter()
    for b, g in gbars.items():
        if "extra_pattern" in g:
            pat = tuple(x for x in g["extra_pattern"] if not is_odd_sixteenth(Fraction(x[0])))
            templates[(b % 2, g["extra_dir"], pat)] += 1
            for p, _l, c in pat:
                if c in ONSET:
                    extra_pos[str(Fraction(p))] += 1

    # 홀드
    hk = Counter(h.kind for h in d.holds)
    short = [h for h in d.holds if h.kind != "new" and h.length is not None and h.length <= SHORT_HOLD]
    open_normal = [h for h in d.holds if h.kind != "new" and h.normal_end is None]

    from .uturn import find_uturns
    ut = [u for u in find_uturns(hard) if u.bar in set(bars)]
    gaps = same_lane_gaps(hard, bpm, set(bars))
    eighth = bar_seconds(bpm) / 8
    return {
        "song": cfg.id,
        "title": cfg.title,
        "bars_used": len(bars),
        "bars_excluded_as_copies": sorted(copies),
        "excluded_odd16_notes": len(d.excluded),
        "density": {
            "normal_onsets": _onset_count(normal, bars),
            "hard_onsets": _onset_count(hard, bars) - len(d.excluded),
            "onset_ratio": round((_onset_count(hard, bars) - len(d.excluded)) / max(1, _onset_count(normal, bars)), 3),
            "note_ratio": round(sum(hard.bars[b].note_count() for b in bars if b in hard.bars)
                                / max(1, sum(normal.bars[b].note_count() for b in bars if b in normal.bars)), 3),
            "ratio_by_normal_subdivision": ratio_by_len,
        },
        "bar_classes": dict(Counter(bd.klass for bd in d.bars.values())),
        "normal_fate": {"counts": dict(fate), "share": _frac(fate, n_normal)},
        "added": {
            "count": n_added,
            "class_share": _frac(Counter(k for _n, k, _d in added)),
            "grid_share": _frac(Counter(grid_class(n.pos) for n, _k, _d in added)),
            "distance_to_normal_16ths": dict(Counter(str(x * 16) for _n, _k, x in added if x is not None)),
            "lane_relation": dict(lane_relation_of_added(d, hard)),
        },
        "gimmick": {
            "bars": sorted(gbars),
            "bar_rate": round(len(gbars) / max(1, len(bars)), 3),
            "extra_dir": dict(Counter(g.get("extra_dir") for g in gbars.values() if g.get("extra_dir"))),
            "main_reversed_bars": sorted(b for b, bd in d.bars.items() if bd.gimmick.get("main_reversed")),
            "main_dropped_bars": sorted(b for b, g in gbars.items() if g.get("main_dropped")),
            "both_group_chord_bars": sorted(b for b, g in gbars.items() if g.get("both_group_chord")),
            "extra_notes_per_bar": dict(Counter(g.get("extra_notes", 0) for g in gbars.values())),
            "extra_note_positions": dict(extra_pos.most_common()),
            "added_per_bar": {"gimmick": round(mean(gim_added), 2) if gim_added else 0,
                              "other": round(mean(non_added), 2) if non_added else 0},
            "normal_onsets_per_bar": {"gimmick": round(mean(normal_per_bar.get(b, 0) for b in gbars), 2) if gbars else 0,
                                      "other": round(mean(normal_per_bar.get(b, 0) for b in bars if b not in gbars), 2)},
            "top_templates": [{"bar_parity": "odd" if k[0] else "even", "extra_dir": k[1],
                               "cells": [list(x) for x in k[2]], "count": v} for k, v in templates.most_common(8)],
        },
        "holds": {
            "changes": dict(hk),
            "open_normal_holds": len(open_normal),
            "open_closed_at_7_8": sum(1 for h in open_normal if h.kind == "end_closed_78"),
            "open_hold_fate": [{"bar": h.bar, "lane": h.lane, "start": str(h.start), "fate": h.kind} for h in open_normal],
            "short_normal_holds": len(short),
            "short_to_tap": sum(1 for h in short if h.kind == "to_tap"),
            "short_kept": sum(1 for h in short if h.kind in ("kept", "end_changed", "end_closed_78", "relaned")),
        },
        "uturns": {
            "A": sum(1 for u in ut if u.kind == "A"), "B": sum(1 for u in ut if u.kind == "B"),
            "A_front": sum(1 for u in ut if u.kind == "A" and u.front), "B_front": sum(1 for u in ut if u.kind == "B" and u.front),
        },
        "spacing": {
            "same_lane_min_gap_ms": round(min(gaps) * 1000) if gaps else None,
            "same_lane_gaps_below_8th": sum(1 for g in gaps if g < eighth - 1e-9),
            "same_lane_gaps_total": len(gaps),
        },
    }


def build_profile(song_ids: list[int], primary: int, bpm_of) -> dict:
    songs = {}
    for sid in song_ids:
        cfg = load_song(sid)
        songs[str(sid)] = song_stats(cfg, bpm_of(sid))
    return {"version": 1, "primary": str(primary), "songs": songs}


def write_profile(profile: dict, path: Path = PROFILE_PATH) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(profile, ensure_ascii=False, indent=1), encoding="utf-8")
    return path


def load_profile(path: Path = PROFILE_PATH) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


# ---------------- 사람이 읽는 리포트 ----------------

def _pct(x: float) -> str:
    return f"{x * 100:.0f}%"


def report_markdown(profile: dict) -> str:
    P = profile["primary"]
    ids = [P] + [k for k in profile["songs"] if k != P]
    S = profile["songs"]
    head = "| 항목 | " + " | ".join(f"{k}번 {S[k]['title']}{' (주 기준)' if k == P else ''}" for k in ids) + " |"
    sep = "|---|" + "---|" * len(ids)
    rows = []

    def row(name, f):
        rows.append(f"| {name} | " + " | ".join(f(S[k]) for k in ids) + " |")

    row("학습에 쓴 마디 (복사본 제외)", lambda s: f"{s['bars_used']}")
    row("제외한 16분 홀수 칸 노트", lambda s: f"{s['excluded_odd16_notes']}")
    row("노트 시작 배율 (하드/노멀)", lambda s: f"×{s['density']['onset_ratio']}")
    row("노트 배율 (홀드 끝 포함)", lambda s: f"×{s['density']['note_ratio']}")
    row("노멀 분할별 배율", lambda s: ", ".join(f"{k}분할 ×{v}" for k, v in s["density"]["ratio_by_normal_subdivision"].items()))
    row("노멀 노트 유지", lambda s: _pct(s["normal_fate"]["share"].get("kept", 0) + s["normal_fate"]["share"].get("type_change", 0)))
    row("같은 그룹 위아래 교체", lambda s: _pct(s["normal_fate"]["share"].get("relane_same", 0)))
    row("다른 그룹으로 이동", lambda s: _pct(s["normal_fate"]["share"].get("relane_cross", 0)))
    row("삭제", lambda s: _pct(s["normal_fate"]["share"].get("deleted", 0)))
    row("새로 추가한 노트", lambda s: f"{s['added']['count']}")
    row("추가 위치", lambda s: ", ".join(f"{ {'gapfill': '빈 칸 채우기', 'extend': '앞뒤로 늘림', 'same_time': '노멀과 같은 시각', 'isolated': '노멀 없는 마디'}.get(k, k)} {_pct(v)}" for k, v in s["added"]["class_share"].items()))
    row("추가 노트 그리드", lambda s: ", ".join(f"{ {'beat': '정박', '8th': '8분 뒷박', '16th': '16분'}.get(k, k)} {_pct(v)}" for k, v in s["added"]["grid_share"].items()))
    row("노멀 노트와 거리(16분 칸)", lambda s: ", ".join(f"{k}칸 {v}" for k, v in sorted(s["added"]["distance_to_normal_16ths"].items(), key=lambda kv: Fraction(kv[0]))))
    row("추가 노트 레인 (직전 노트 대비)", lambda s: ", ".join(f"{ {'same': '같은 레인', 'alternate': '위아래 교대', 'first_in_group': '그룹 첫 노트'}.get(k, k)} {v}" for k, v in s["added"]["lane_relation"].items()))
    row("기믹 마디 비율", lambda s: f"{_pct(s['gimmick']['bar_rate'])} ({len(s['gimmick']['bars'])}마디)")
    row("추가 그룹 방향 (원래/뒤집음)", lambda s: f"{s['gimmick']['extra_dir'].get('own', 0)} / {s['gimmick']['extra_dir'].get('flipped', 0)}")
    row("메인 그룹 반전 / 메인 없음 / 두 그룹 동시치기", lambda s: f"{len(s['gimmick']['main_reversed_bars'])} / {len(s['gimmick']['main_dropped_bars'])} / {len(s['gimmick']['both_group_chord_bars'])}")
    row("마디당 추가 노트 (기믹 마디 / 그 외)", lambda s: f"{s['gimmick']['added_per_bar']['gimmick']} / {s['gimmick']['added_per_bar']['other']}")
    row("마디당 노멀 노트 (기믹 마디 / 그 외)", lambda s: f"{s['gimmick']['normal_onsets_per_bar']['gimmick']} / {s['gimmick']['normal_onsets_per_bar']['other']}")
    row("끝 없는 노멀 홀드 → 7/8 끝", lambda s: f"{s['holds']['open_closed_at_7_8']}/{s['holds']['open_normal_holds']}")
    row("짧은 홀드(≤1/8) → 탭", lambda s: f"{s['holds']['short_to_tap']}/{s['holds']['short_normal_holds']}")
    row("홀드 변화", lambda s: ", ".join(f"{k} {v}" for k, v in s["holds"]["changes"].items()))
    row("유턴 (A 기믹 마디 / B 다음 마디), 앞쪽 노트 있음", lambda s: f"A {s.get('uturns', {}).get('A', 0)}({s.get('uturns', {}).get('A_front', 0)}) / B {s.get('uturns', {}).get('B', 0)}({s.get('uturns', {}).get('B_front', 0)})")
    row("같은 레인 최소 간격", lambda s: f"{s['spacing']['same_lane_min_gap_ms']}ms (8분 미만 {s['spacing']['same_lane_gaps_below_8th']}/{s['spacing']['same_lane_gaps_total']})")

    tpl = S[P]["gimmick"]["top_templates"]
    tlines = [f"- {t['count']}회: {t['bar_parity']} 마디, 추가 그룹 방향 {t['extra_dir']}, 칸 {', '.join(f'레인{l} {p} \'{c}\'' for p, l, c in t['cells'])}" for t in tpl]
    return "\n".join([
        "# 하드 기조 통계", "",
        f"주 기준은 {P}번이다. 다른 곡은 참고로만 쓴다. 16분 홀수 칸 하드 노트는 청취 확인 전이라 학습에서 뺐다. "
        "1·2절 복사본 마디도 같은 패턴을 두 번 세지 않도록 뺐다.", "",
        head, sep, *rows, "",
        f"## {P}번 기믹 템플릿 (자주 쓴 순)", "", *tlines, "",
    ])
