"""포팅 초안 검토 리포트 (markdown) 와 노트별 근거 로그 (json)."""
from __future__ import annotations

import json
from collections import Counter
from fractions import Fraction
from pathlib import Path

from .listen import fmt_time
from .model import Chart
from .port import PortResult
from .timing import audio_time

ORIGIN_KO = {
    "HOLD_END_78": "홀드 끝 7/8", "HOLD_TO_TAP": "홀드→탭", "GAPFILL": "빈 칸 채우기",
    "REMOVED": "검증 때문에 뺌", "RHYTHM_SHIFT16_MOVE": "16분으로 밀기", "RHYTHM_PICKUP16": "16분 픽업",
    "RHYTHM_TRIPLET": "셋잇단", "RHYTHM_TRIPLET_MOVE": "셋잇단으로 바꿈", "EASY_ANTIC": "당김음(뒤 박을 당김)", "NORMAL_ANTIC": "당김음 엇박",
    "NORMAL_PICKUP16": "16분 픽업", "NORMAL_TRIPLET": "셋잇단",
    "COPY": "복사(홀드 끝)", "COPY_ADDED": "복사", "FROZEN": "손작업", "FROZEN_CARRY": "손작업에서 넘어옴", "HOLD_DROPPED": "끝 없는 홀드 지움", "UTURN_DROP": "유턴 앞쪽 노트 뺌", "COPY_REMOVED": "복사로 빠짐", "UTURN_UNFLIP": "유턴 없애려고 방향 되돌림", "UTURN_UNREVERSE": "유턴 없애려고 메인 방향 되돌림",
    "THIN_GRID": "8분 격자 밖이라 뺌", "THIN_CHORD": "동시치기라 뺌", "THIN_GAP": "간격이 좁아 뺌", "THIN_DENSITY": "밀도 상한이라 뺌",
    "THIN_ORPHAN": "짝 없는 끝 뺌", "HOLD_SHORTEN": "홀드 줄임", "RELEASE_TO_END": "릴리즈 끝 5→4",
    "NORMAL_ADD": "8분 추가", "COPY_SKIP": "복사할 노트를 놓지 못함", "EASY_BEAT": "박", "EASY_HOLD": "홀드", "EASY_SWALLOW": "약한 박을 홀드로 덮음", "EASY_SILENT": "소리 없는 박 뺌", "EASY_BREAK": "쉬는 마디라 뺌",
}
def origin_ko(o: str) -> str:
    """출처 이름 → 한국어. 기믹은 GIMMICK_<형태>[_MOVE|_DIR]."""
    if o.startswith("GIMMICK_"):
        from .gimmicks import FAMILY_KO
        rest = o[len("GIMMICK_"):]
        suffix = ""
        for sfx, ko in (("_MOVE", "(옮김)"), ("_DIR", "(방향)")):
            if rest.endswith(sfx):
                rest, suffix = rest[: -len(sfx)], ko
        return f"기믹 {FAMILY_KO.get(rest, rest)}{suffix}"
    return ORIGIN_KO.get(o, o)


def rhythm_summary(bars: dict) -> list[str]:
    r16 = sorted(b for b, v in bars.items() if v.get("rhythm") == "16")
    tri = sorted(b for b, v in bars.items() if v.get("rhythm") == "triplet")
    if not r16 and not tri:
        return ["- 리듬 변주: 없음 (음원에서 16분·셋잇단 정점을 찾지 못함)"]
    return [f"- 리듬 변주: 16분 음형 {len(r16)}마디" + (f" ({', '.join(f'{b:03d}' for b in r16)})" if r16 else "")
            + f", 셋잇단 {len(tri)}마디" + (f" ({', '.join(f'{b:03d}' for b in tri)})" if tri else "")]


def gimmick_summary(bars: dict) -> list[str]:
    """기믹 형태 분포·가장 흔한 형태·같은 모양 연속 (하드 리포트)."""
    from .gimmicks import FAMILY_KO
    g = [(b, v["gimmick"], v.get("gimmick_sig")) for b, v in sorted(bars.items()) if v.get("gimmick")]
    if not g:
        return []
    fams = Counter(f for _b, f, _s in g)
    run = best = 0
    prev = None
    for _b, _f, sig in g:
        run = run + 1 if sig == prev else 1
        best, prev = max(best, run), sig
    adj = sum(1 for b, _f, _s in g if any(x in (b - 1, b + 1) for x, _ff, _ss in g))
    return ["- 기믹 형태: " + ", ".join(f"{FAMILY_KO.get(f, f)} {n}" for f, n in fams.most_common())
            + f" (가장 흔한 형태 {fams.most_common(1)[0][1] / len(g):.0%}, 같은 모양 최대 연속 {best}, 이웃 기믹과 붙은 마디 {adj}/{len(g)})"]


FLAG_KO = {
    "RECHART": "노멀이 4분할로 성긴데 음원은 촘촘함 → 다시 찍기 권장 (2번에서 사용자가 새로 찍은 형태)",
    "LISTEN": "청취 확인 대상 (songs 설정 listen_bars)",
    "LOW": "소리가 약한 칸에 넣은 노트 있음",
    "UTURN": "유턴 마디 앞쪽(0 < p ≤ 1/4)에 노트가 남음 (손작업·잠금 마디가 원인) → 직접 확인",
    "HOLD_MANUAL": "끝 없는 홀드를 자동으로 닫지 못했거나 지움 → 직접 확인",
    "CARRY_CONFLICT": "손작업 마디에서 넘어온 노트를 놓을 수 없음 (자리 충돌) → 직접 확인",
    "SPARSE": "노트가 하나만 남음 → 박이 비어 보이는지 확인",
    "ANTIC": "당김음을 넣음 (뒤 박을 엇박으로 당김) → 들어 보고 확인",
    "RTL_CHECK": "노멀 짝수 마디(RTL 줄) 방향이 아직 확인 전 → 이 기믹 위치가 음악과 맞는지 확인",
    "RHYTHM_RECHART": "음원에 16분 음형이 뚜렷한데 노트가 거의 없음 → 다시 찍기 권장",
    "CHANGED_HOLD_OR_CHORD": "홀드를 줄이거나 탭으로 바꿨거나 동시치기를 하나로 줄임",
    "NOT_MAIN": "메인 라인이 아닌 그룹을 쓰는 노멀 마디 → 직접 확인",
    "PICKUP": "첫 소리가 1마디 시작보다 늦음(못갖춘마디) → 첫 노트 위치 확인",
    "END": "마지막 마디 → 끝맺음(홀드·마무리 노트) 확인",
    "BREAK": "쉬는 마디(소리 약함) → 첫 박 홀드 하나만 넣음",
    "EMPTY": "소리가 없어 노트가 없는 마디 → 정말 비울지 확인",
    "SYNC_CANDIDATE": "당김음 후보(엇박이 다음 박보다 확실히 큼) → 엇박 노트를 넣을지 확인",
    "NORMAL_EDIT": "노멀을 리타이밍할 때 직접 고친 칸이 이 손작업 마디에 있음 → 하드도 같게 고칠지 확인",
}


def log_json(res: PortResult) -> str:
    return json.dumps([{"bar": e.bar, "lane": e.lane, "pos": str(e.pos), "char": e.char, "origin": e.origin,
                        "score": e.score, "sound_pct": e.pct, "reason": e.reason} for e in res.log], ensure_ascii=False, indent=1)


def _notes_str(chart: Chart, b: int) -> str:
    bar = chart.bars.get(b)
    if bar is None:
        return ""
    return " ".join(f"L{l}:{p}{'' if c == '1' else '(' + c + ')'}" for p, l, c in bar.notes())


def report_markdown(title: str, normal: Chart, res: PortResult, bpm: float, eval_summary: str | None = None,
                    has_rules: bool = False) -> str:
    hard = res.chart
    gone = ("REMOVED", "UTURN_DROP", "COPY_REMOVED")
    removed = {(e.bar, e.lane, e.pos) for e in res.log if e.origin in gone}
    live = [e for e in res.log if e.origin in gone or (e.bar, e.lane, e.pos) not in removed]
    by_bar: dict[int, list] = {}
    for e in live:
        by_bar.setdefault(e.bar, []).append(e)
    for b, es in by_bar.items():
        if any((e.origin == "GAPFILL" or (e.origin.startswith("GIMMICK_") and not e.origin.endswith(("_MOVE", "_DIR"))))
               and e.pct is not None and e.pct < 60 for e in es):
            res.bars.setdefault(b, {"flags": []})["flags"].append("LOW")
    origins = Counter(e.origin for e in res.log)
    n_norm = sum(1 for bar in normal.bars.values() for _p, _l, c in bar.notes() if c in "125")
    n_hard = sum(1 for bar in hard.bars.values() for _p, _l, c in bar.notes() if c in "125")
    gim = sorted(b for b, v in res.bars.items() if v.get("is_gimmick"))
    copies = sorted(b for b, v in res.bars.items() if v.get("copy"))
    flagged = sorted(b for b, v in res.bars.items() if v.get("flags"))
    out = [f"# {title} 하드 초안 검토", "",
           "자동 생성 초안이다. 우선 확인할 마디부터 듣고 ChartEditor 에서 고친다. 노트별 근거는 `port_log.json`.", "",
           "## 요약", "",
           f"- 노트 시작 {n_norm} → {n_hard} (×{n_hard / max(1, n_norm):.2f}), 전체 노트 {normal.count_notes()} → {hard.count_notes()}",
           "- 생성 내역: " + ", ".join(f"{origin_ko(k)} {v}" for k, v in sorted(origins.items())),
           f"- 기믹 마디 {len(gim)}개: " + (", ".join(f"{b:03d}" for b in gim) or "없음"),
           *gimmick_summary(res.bars),
           *rhythm_summary(res.bars),
           f"- 복사 규칙으로 채운 마디 {len(copies)}개" + (": " + ", ".join(f"{b:03d}←{res.bars[b]['copy'][0]:03d}{'(반전)' if res.bars[b]['copy'][1] == 'mirror' else ''}" for b in copies) if copies else ""),
           f"- 분석 오프셋 {res.offset * 1000:+.0f}ms (음원 정렬용, 채보에는 쓰지 않음)"]
    if not has_rules:
        out.append("- **복사 규칙이 없다.** 1·2절 반복 구간도 따로 생성돼 서로 다를 수 있다. `sections` 제안(out/000N/sections.md)을 확인해 "
                   "songs 설정 sections 에 넣고 다시 port 하면 반복 구간이 원본과 같게(또는 반전) 나온다.")
    if eval_summary:
        out += ["", eval_summary]
    out += ["", "## 먼저 볼 마디", ""]
    if flagged:
        out += ["| 마디 | 시각 | 표시 |", "|---|---|---|"]
        for b in flagged:
            fl = sorted(set(res.bars[b]["flags"]))
            extra = f" ({', '.join(res.bars[b]['normal_edits'])})" if res.bars[b].get("normal_edits") else ""
            out.append(f"| {b:03d} | {fmt_time(audio_time(b, 0, bpm))} | {'; '.join(FLAG_KO.get(f, f) for f in fl)}{extra} |")
    else:
        out.append("없음")
    out += ["", "## 마디별 변경", "", "| 마디 | 변경 | 노멀 | 하드 초안 |", "|---|---|---|---|"]
    frozen = {b for b, n in res.bars.items() if n.get("frozen")}
    for b in sorted(set(by_bar) | set(copies) | frozen):
        es = [e for e in by_bar.get(b, []) if e.origin not in ("REMOVED", "COPY", "COPY_ADDED", "FROZEN")]
        what = "; ".join(f"{origin_ko(e.origin)} L{e.lane}@{e.pos}" + (f" (소리 P{e.pct:.0f})" if e.pct is not None and not e.origin.startswith("HOLD") else "") for e in es)
        if res.bars.get(b, {}).get("frozen"):
            what = "손작업 하드 마디 그대로 (시간 이동)" + (f"; {what}" if what else "")
        if b in copies:
            src, tf = res.bars[b]["copy"]
            what = f"{src:03d}마디 {'반전 ' if tf == 'mirror' else ''}복사" + (f"; {what}" if what else "")
        out.append(f"| {b:03d} | {what} | {_notes_str(normal, b)} | {_notes_str(hard, b)} |")
    return "\n".join(out) + "\n"


def thin_report_markdown(title: str, normal: Chart, res: PortResult, bpm: float, params, check_line: str | None = None) -> str:
    """노멀 덜어내기(이지) 리포트."""
    from .thin import k8_of, onset_times
    easy = res.chart
    n_t, e_t = onset_times(normal), onset_times(easy)
    bars = max(1, sum(1 for b in normal.bars if b > 0 and normal.bars[b].has_notes()))
    first = min(n_t) if n_t else 0
    span = max(1e-9, float((max(n_t) - first)) * 240.0 / bpm) if n_t else 1.0
    origins = Counter(e.origin for e in res.log)
    kn = Counter(k8_of(t - int(t)) for t in n_t)
    ke = Counter(k8_of(t - int(t)) for t in e_t)
    holds = sum(1 for bar in easy.bars.values() for _p, _l, c in bar.notes() if c == "2")
    per_bar = Counter(sum(1 for t in e_t if int(t) == b) for b in normal.bars if b > 0 and normal.bars[b].has_notes())
    flagged = sorted(b for b, v in res.bars.items() if v.get("flags"))
    out = [f"# {title} 이지 초안 검토 (노멀 덜어내기)", "",
           "사용자 노멀에서 노트를 빼기만 해서 만든 이지다 (새로 넣은 노트 없음). 노트별 근거는 `gen_log_Easy.json`.", "",
           "## 요약", "",
           f"- 노트 시작 {len(n_t)} → {len(e_t)} (×{len(e_t) / max(1, len(n_t)):.2f}), 마디당 {len(e_t) / bars:.2f}, "
           f"초당 {len(e_t) / span:.2f} (첫 노트~끝 노트 기준), 전체 노트(#NOTES) {normal.count_notes()} → {easy.count_notes()}",
           f"- 홀드 {holds}개 (노트 시작의 {holds / max(1, len(e_t)):.0%})",
           "- 마디당 노트 수 분포: " + ", ".join(f"{k}개 {v}마디" for k, v in sorted(per_bar.items())),
           "- 바꾼 내역: " + ", ".join(f"{origin_ko(k)} {v}" for k, v in sorted(origins.items())),
           f"- 규칙: 간격 ≥ {params.min_gap_s}초(이 곡 8분 = {60.0 / bpm / 2:.3f}초), 홀드 ≥ {params.min_hold_s}초(짧으면 탭), 마디당 상한 {params.target_per_bar}, "
           f"끼운 8분 ×{params.inserted_factor}, 당김음 +{params.sync_bonus}"]
    if check_line:
        out.append(f"- 검증: {check_line}")
    out += ["", "| 8분 위치 | " + " | ".join(str(Fraction(k, 8)) for k in range(8)) + " |",
            "|---|" + "---|" * 8,
            "| 노멀 | " + " | ".join(str(kn.get(k, 0)) for k in range(8)) + " |",
            "| 이지 | " + " | ".join(str(ke.get(k, 0)) for k in range(8)) + " |",
            "", "## 먼저 볼 마디", ""]
    if flagged:
        out += ["| 마디 | 시각 | 표시 |", "|---|---|---|"]
        for b in flagged:
            out.append(f"| {b:03d} | {fmt_time(audio_time(b, 0, bpm))} | {'; '.join(FLAG_KO.get(f, f) for f in sorted(set(res.bars[b]['flags'])))} |")
    else:
        out.append("없음")
    by_bar: dict[int, list] = {}
    for e in res.log:
        by_bar.setdefault(e.bar, []).append(e)
    out += ["", "## 마디별 변경", "", "| 마디 | 변경 | 노멀 | 이지 초안 |", "|---|---|---|---|"]
    for b in sorted(x for x in normal.bars if x > 0):
        es = by_bar.get(b, [])
        what = "; ".join(f"{origin_ko(e.origin)} L{e.lane}@{e.pos}" for e in es)
        out.append(f"| {b:03d} | {what} | {_notes_str(normal, b)} | {_notes_str(easy, b)} |")
    return "\n".join(out) + "\n"


def easy_report_markdown(title: str, res: PortResult, bpm: float, params, align_note: str | None = None) -> str:
    """음원 기반 이지 리포트."""
    from .easy import alternation, rhythm_patterns
    from .thin import onset_times
    easy = res.chart
    e_t = onset_times(easy)
    bars = max(1, easy.last_bar())
    holds = sum(1 for bar in easy.bars.values() for _p, _l, c in bar.notes() if c == "2")
    sw, top = alternation(easy)
    per_bar = Counter(sum(1 for t in e_t if int(t) == b) for b in range(1, bars + 1))
    origins = Counter(e.origin for e in res.log)
    sync = res.bars.get(0, {}).get("sync_candidates", [])
    # 당김음 후보는 맞히는 비율이 낮아(15%) 아래 전용 표에만 둔다
    flagged = sorted(b for b, v in res.bars.items() if b > 0 and set(v.get("flags", [])) - {"SYNC_CANDIDATE"})
    out = [f"# {title} 이지 초안 검토 (음원 기반)", "",
           "음원의 박 위에 노트를 깔고, 공격음이 약한 박을 앞 박의 홀드로 덮어 만든 이지다 (사용자 이지 2번 기준). "
           "엇박 노트는 넣지 않았다 — 아래 '당김음 후보'를 듣고 ChartEditor 에서 직접 넣는다. 노트별 근거는 `gen_log_Easy.json`.", "",
           "## 요약", "",
           f"- 노트 시작 {len(e_t)}, 마디당 {len(e_t) / bars:.2f} (목표 {params.target_per_bar}), 전체 노트(#NOTES) {easy.count_notes()}",
           f"- 홀드 {holds}개 ({holds / max(1, len(e_t)):.0%}, 목표 {params.hold_share:.0%}), 마디 안 위아래 바꿈 {sw:.0%}, 위 레인 {top:.0%}, 리듬 패턴 {len(rhythm_patterns(easy))}가지",
           "- 당김음(음원 A ≥ 60, 뒤 박을 엇박으로 당김): " + (", ".join(f"{e.bar:03d}:{e.pos}" for e in res.log if e.origin == "EASY_ANTIC" and e.char == "1") or "없음"),
           "- 마디당 노트 수 분포: " + ", ".join(f"{k}개 {v}마디" for k, v in sorted(per_bar.items())),
           "- 내역: " + ", ".join(f"{origin_ko(k)} {v}" for k, v in sorted(origins.items())),
           f"- 분석 오프셋 {res.offset * 1000:+.0f}ms (음원 정렬용, 채보에는 쓰지 않음)"]
    if align_note:
        out.append(f"- {align_note}")
    out += ["", "## 먼저 볼 마디", ""]
    if flagged:
        out += ["| 마디 | 시각 | 표시 |", "|---|---|---|"]
        for b in flagged:
            out.append(f"| {b:03d} | {fmt_time(audio_time(b, 0, bpm))} | {'; '.join(FLAG_KO.get(f, f) for f in sorted(set(res.bars[b]['flags']) - {'SYNC_CANDIDATE'}))} |")
    else:
        out.append("없음")
    out += ["", "## 당김음 후보", "",
            f"엇박 칸 소리(중역 백분위) ≥ P{params.sync_pct:.0f} 이고 다음 박보다 {params.sync_margin:.0f} 이상 큰 칸. "
            "2번에서 이 규칙으로 맞힌 비율은 15% 정도라 자동으로 넣지 않는다.", ""]
    if sync:
        out += ["| 마디 | 위치 | 시각 | 엇박 P | 다음 박 P |", "|---|---|---|---|---|"]
        out += [f"| {b:03d} | {q} | {fmt_time(audio_time(b, q, bpm))} | {a} | {c} |" for b, q, a, c in sync]
    else:
        out.append("없음")
    out += ["", "## 마디별 노트", "", "| 마디 | 이지 초안 |", "|---|---|"]
    out += [f"| {b:03d} | {_notes_str(easy, b)} |" for b in range(1, bars + 1)]
    return "\n".join(out) + "\n"


def normal_report_markdown(title: str, easy: Chart, res: PortResult, bpm: float, params, base_note: str = "") -> str:
    """이지 기반 노멀 리포트."""
    from .thin import k8_of, onset_times
    normal = res.chart
    e_t, n_t = onset_times(easy), onset_times(normal)
    bars = max(1, normal.last_bar())
    added = [e for e in res.log if e.origin in ("NORMAL_ADD", "NORMAL_ANTIC", "NORMAL_PICKUP16", "NORMAL_TRIPLET")]
    removed = {(e.bar, e.lane, e.pos) for e in res.log if e.origin == "REMOVED"}
    live = [e for e in added if (e.bar, e.lane, e.pos) not in removed]
    kp = Counter(k8_of(e.pos) for e in live)
    meta = res.bars.get(0, {})
    flagged = sorted(b for b, v in res.bars.items() if b > 0 and v.get("flags"))
    out = [f"# {title} 노멀 초안 검토 (이지 기반)", "",
           "설치된 이지를 그대로 두고, 4분 간격으로 붙은 이지 노트 사이에 8분을 끼워 만든 노멀이다 (사용자 노멀 2번 기준). "
           "노트별 근거는 `gen_log_Normal.json`." + (f" {base_note}" if base_note else ""), "",
           "## 요약", "",
           f"- 노트 시작 이지 {len(e_t)} → 노멀 {len(n_t)} (×{len(n_t) / max(1, len(e_t)):.2f}), 마디당 {len(n_t) / bars:.2f} (목표 {params.target_per_bar}), "
           f"전체 노트(#NOTES) {easy.count_notes()} → {normal.count_notes()}",
           f"- 추가 {len(live)}개 (목표 {meta.get('n_add_target', '?')}, 후보 {meta.get('n_candidates', '?')}칸), 검증 때문에 뺀 것 {len(removed)}",
           "- 추가 위치: " + ", ".join(f"{Fraction(k, 8)} {v}개" for k, v in sorted((k, v) for k, v in kp.items() if k is not None)),
           "- 리듬 변주: " + ", ".join(f"{origin_ko(o)} {sum(1 for e in live if e.origin == o)}" for o in ("NORMAL_ANTIC", "NORMAL_PICKUP16", "NORMAL_TRIPLET")),
           f"- 분석 오프셋 {res.offset * 1000:+.0f}ms (음원 정렬용, 채보에는 쓰지 않음)", "", "## 먼저 볼 마디", ""]
    if flagged:
        out += ["| 마디 | 시각 | 표시 |", "|---|---|---|"]
        for b in flagged:
            out.append(f"| {b:03d} | {fmt_time(audio_time(b, 0, bpm))} | {'; '.join(FLAG_KO.get(f, f) for f in sorted(set(res.bars[b]['flags'])))} |")
    else:
        out.append("없음")
    by_bar: dict[int, list] = {}
    for e in res.log:
        by_bar.setdefault(e.bar, []).append(e)
    out += ["", "## 마디별 변경", "", "| 마디 | 추가 | 이지 | 노멀 초안 |", "|---|---|---|---|"]
    for b in range(1, bars + 1):
        es = by_bar.get(b, [])
        what = "; ".join(f"{origin_ko(e.origin)} L{e.lane}@{e.pos}" for e in es)
        out.append(f"| {b:03d} | {what} | {_notes_str(easy, b)} | {_notes_str(normal, b)} |")
    return "\n".join(out) + "\n"

