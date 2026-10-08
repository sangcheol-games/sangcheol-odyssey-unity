"""채보 검증 규칙.

심각도:
  FATAL  게임이 멈추거나 끝나지 않음
  ERROR  판정이 깨지거나 노트가 무시됨 / 에디터가 파일을 망가뜨림
  WARN   플레이에 위험하거나 의도 확인이 필요한 형태
  INFO   통계성 정보
근거 코드: ChartParser.cs, LaneData.cs, ChartManager.cs, EditorChartConverter.cs, ScoreManager.cs
"""
from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass
from fractions import Fraction

from .chart_io import RawChart
from .model import HOLD_END_CHARS, LANES, LTR, MAX_LENGTH, RTL, Chart, expected_main, group_of
from .timing import JUDGE_UMM, is_odd_sixteenth, is_on_grid, song_time

SEVERITIES = ("FATAL", "ERROR", "WARN", "INFO")


@dataclass
class Issue:
    code: str
    severity: str
    message: str
    bar: int | None = None
    lane: int | None = None
    file: str | None = None

    def where(self) -> str:
        s = "" if self.bar is None else f"{self.bar:03d}마디"
        if self.lane is not None:
            s += f" 레인{self.lane}"
        return s


def difficulty_from_filename(path: str | None) -> str | None:
    if not path:
        return None
    from .model import DIFFICULTIES
    stem = path.replace("\\", "/").rsplit("/", 1)[-1].rsplit(".", 1)[0]
    last = stem.rsplit("_", 1)[-1]
    for d in DIFFICULTIES:
        if d.lower() == last.lower():
            return d
    return None


def simulate_bar_progress(bars_in_file_order: list[int]) -> tuple[int | None, list[tuple[int, int]]]:
    """ChartManager.PrepareNextBar/StartCurrentBar 진행 흉내.

    현재 마디 k(0부터)마다 큐 앞쪽에서 bar <= k 인 줄을 모두 소비한다. 하나도 소비하지 못했는데
    남은 줄이 있으면 k 에서 진행이 멈추고 게임이 끝나지 않는다.
    반환: (멈춘 마디 또는 None, [(늦게 소비된 줄의 마디, 소비된 시점 k)])
    """
    i, k, late = 0, 0, []
    n = len(bars_in_file_order)
    while i < n:
        loaded = 0
        while i < n and bars_in_file_order[i] <= k:
            if bars_in_file_order[i] < k:
                late.append((bars_in_file_order[i], k))
            i += 1
            loaded += 1
        if loaded == 0:
            return k, late
        k += 1
    return None, late


def validate(chart: Chart, raw: RawChart | None = None, bpm: float | None = None) -> list[Issue]:
    issues: list[Issue] = []
    add = lambda *a, **k: issues.append(Issue(*a, **k))
    bpm = float(bpm or chart.header.bpm)

    # ---------- 원문 기준 규칙 ----------
    if raw is not None:
        if raw.decode_errors:
            add("W13", "WARN", "UTF-8 로 읽을 수 없는 바이트가 있음 (제목 등 글자가 깨짐)")
        for lineno, text, reason in raw.ignored:
            sev = "ERROR" if "데이터 줄" in reason else "INFO"
            add("E10" if sev == "ERROR" else "I10", sev, f"{lineno}번째 줄 '{text}': {reason}")

        stuck, late = simulate_bar_progress([rl.bar for rl in raw.data_lines])
        if stuck is not None:
            what = "000마디 줄이 없어 시작부터" if stuck == 0 else "이 마디를 채울 줄이 없어 여기서"
            add("F01", "FATAL", f"{what} 진행이 멈추고 게임이 끝나지 않음 (빠진 마디이거나 마디 순서가 뒤바뀜)", stuck)
        for b, k in sorted(set(late)):
            add("F02", "FATAL", f"마디 순서가 뒤바뀐 줄: {k:03d}마디 차례에 늦게 읽혀 그 노트는 이미 지난 시각 → 전부 Miss", b)

        per_lane = Counter((rl.bar, rl.lane) for rl in raw.data_lines)
        for (b, l), cnt in sorted(per_lane.items()):
            if cnt > 1:
                add("E01", "ERROR", f"같은 마디·레인 줄이 {cnt}개 → 한 레인 큐에 섞여 판정이 꼬임", b, l)
        per_group = defaultdict(set)
        lengths = defaultdict(list)
        lanes_written = defaultdict(set)
        for rl in raw.data_lines:
            if rl.lane not in LANES:
                add("E09", "ERROR", f"레인 번호 {rl.lane} → 게임에서 IndexOutOfRange", rl.bar)
                continue
            if rl.channel not in (0, 1):
                add("W06", "WARN", f"채널 {rl.channel} (0 외에는 모두 RTL 로 처리됨)", rl.bar, rl.lane)
            per_group[(rl.bar, group_of(rl.lane))].add(LTR if rl.channel == 0 else RTL)
            lengths[rl.bar].append(len(rl.seq))
            lanes_written[(rl.bar, group_of(rl.lane))].add(rl.lane)
        for (b, g), ds in sorted(per_group.items()):
            if len(ds) > 1:
                add("E02", "ERROR", f"그룹 {g} 안에서 방향이 섞임 → 판정선 하나가 한쪽 레인 노트와 반대로 움직임", b)
        for b, ls in sorted(lengths.items()):
            beat = max(4, max(ls))   # 에디터가 불러온 뒤의 마디 분할 수 (EditorBarData 기본 4, 큰 쪽으로만 늘어남)
            odd = sorted({n for n in ls if n != beat})
            if odd:
                add("E07", "ERROR", f"시퀀스 길이 {sorted(set(ls))} → 에디터로 불러오면 마디가 {beat}분할이 되고 길이 {odd} 줄은 앞칸에 그대로 복사돼 타이밍이 바뀜", b)
            if beat > MAX_LENGTH:
                add("E08", "ERROR", f"분할 수 {beat} > {MAX_LENGTH} (에디터 입력 상한)", b)
        for (b, g), ls in sorted(lanes_written.items()):
            if len(ls) == 1:
                add("I05", "INFO", f"그룹 {g} 의 한 레인만 적힘 (에디터 저장 시 다른 레인이 0으로 추가됨)", b)
    elif chart.bars:
        for n in range(0, chart.last_bar() + 1):
            if n not in chart.bars:
                add("F01", "FATAL", "줄이 하나도 없는 마디 → 게임 진행이 여기서 멈추고 끝나지 않음", n)
                break

    # ---------- 문자 ----------
    for bnum in sorted(chart.bars):
        bar = chart.bars[bnum]
        for lane in LANES:
            for pos, c in sorted(bar.cells[lane].items()):
                if c not in "012345":
                    add("E05", "ERROR", f"알 수 없는 문자 '{c}' 위치 {pos} (게임은 무시, 에디터는 노트로 셈)", bnum, lane)
                elif c == "3":
                    add("E05", "ERROR", f"레거시 '3'(Holding) 위치 {pos}", bnum, lane)

    # ---------- 홀드: 레인별 판정 큐는 마디를 넘어 이어진다 (ChartManager.ActivateGhostNotes) ----------
    for lane in LANES:
        items = sorted((b, p, c) for b, bar in chart.bars.items() for p, c in bar.cells[lane].items() if c in "1245")
        open_hold: tuple[int, Fraction] | None = None
        for b, p, c in items:
            if c in "12" and open_hold is not None and open_hold[0] != b:
                add("E13", "ERROR", f"끝 없는 홀드 {open_hold[1]} (같은 마디에 4/5 없음 → 마디 끝까지 그려지고 끝 판정 없음, 게임 버그 발생)", open_hold[0], lane)
                open_hold = None
            if c == "2":
                if open_hold is not None:
                    add("E03", "ERROR", f"홀드({open_hold[1]}) 안에 다시 '2' 위치 {p} → 같은 레인을 떼고 다시 눌러야 함", b, lane)
                open_hold = (b, p)
            elif c == "1":
                if open_hold is not None:
                    add("E03", "ERROR", f"홀드({open_hold[1]}) 중 같은 레인 탭 {p} → 홀드 판정이 탭에 막힘", b, lane)
            else:  # '4' / '5'
                if open_hold is None:
                    if c == "4":
                        add("E04", "ERROR", f"짝 없는 '4' 위치 {p}: 보이지 않는 홀드 끝이라 그 레인을 누르고 있을 때만 판정 → 보통은 Miss", b, lane)
                    else:
                        add("W12", "WARN", f"짝 없는 '5' 위치 {p}: 홀드 막대 없는 릴리즈 노트 (누르고 제때 떼야 판정)", b, lane)
                elif open_hold[0] != b:
                    add("E14", "ERROR", f"마디를 넘는 홀드 ({open_hold[0]:03d}:{open_hold[1]} → {b:03d}:{p}): 시작 마디에 4/5 없음 → 게임 버그 발생", open_hold[0], lane)
                open_hold = None
        if open_hold is not None:
            add("E13", "ERROR", f"끝 없는 홀드 {open_hold[1]} (같은 마디에 4/5 없음 → 마디 끝까지 그려지고 끝 판정 없음, 게임 버그 발생)", open_hold[0], lane)

    # ---------- 같은 레인 간격 ----------
    for lane in LANES:
        q = sorted((song_time(b, p, bpm), c, b, p) for b, bar in chart.bars.items() for p, c in bar.cells[lane].items() if c in "1245")
        for (t0, c0, b0, p0), (t1, c1, b1, p1) in zip(q, q[1:]):
            gap = t1 - t0
            if c1 not in "12" or gap >= JUDGE_UMM:
                continue
            if c0 == "1":
                add("W01", "WARN", f"같은 레인 노트 간격 {gap*1000:.0f}ms < 126ms ({b0:03d}:{p0} → {b1:03d}:{p1}) — 앞 노트를 놓치면 다음 노트를 제때 눌러도 앞 노트로 판정됨", b1, lane)
            elif c0 in HOLD_END_CHARS:
                add("W02", "WARN", f"홀드 끝 직후 {gap*1000:.0f}ms 만에 같은 레인 노트 ({b0:03d}:{p0} → {b1:03d}:{p1})", b1, lane)

    # ---------- 유턴 앞쪽 노트 (같은 그룹을 연달아 반대 방향으로 쓸 때 0 < p ≤ 1/4 노트는 거의 바로 활성화) ----------
    from .uturn import find_uturns, lead_seconds
    for u in find_uturns(chart):
        if not u.front:
            continue
        what = "기믹 마디(직전 그룹을 반대 방향으로)" if u.kind == "A" else "메인 라인이 돌아오는 마디"
        notes = ", ".join(f"레인{l} {p}({lead_seconds(p, bpm) * 1000:.0f}ms)" for l, p, _c in u.front)
        add("W15", "WARN", f"유턴 앞쪽 노트 — {what}, 그룹{u.group} {u.bar - 1:03d}마디 {'LTR' if u.prev_dir == LTR else 'RTL'} → "
                           f"{'LTR' if u.dir == LTR else 'RTL'}: {notes} (괄호 = 보이는 시간)", u.bar)

    # ---------- 헤더 ----------
    judged, editor_count = chart.count_judged(), chart.count_notes()
    if chart.header.notes is None:
        add("E06", "ERROR", f"#NOTES 없음(또는 숫자가 아님) → 모든 점수가 0 (실제 판정 노트 {judged})")
    elif chart.header.notes != judged:
        add("E06", "ERROR", f"#NOTES {chart.header.notes} ≠ 판정 노트 수 {judged} → 점수/올퍼펙트 계산이 틀어짐")
    if editor_count != judged:
        add("E06", "ERROR", f"에디터는 #NOTES 를 {editor_count} 로 저장하지만 게임이 판정하는 노트는 {judged} (알 수 없는 문자 때문)")
    fdiff = difficulty_from_filename(chart.path)
    if fdiff and chart.header.raw_difficulty is not None and chart.header.difficulty != fdiff:
        add("W04", "WARN", f"#DIFFICULTY {chart.header.raw_difficulty} 인데 파일 이름은 {fdiff}")

    # ---------- 통계성 정보 ----------
    off8 = odd16 = 0
    alt_breaks = []
    for bnum in sorted(chart.bars):
        bar = chart.bars[bnum]
        for pos, lane, c in bar.notes():
            if c in "125":
                if not is_on_grid(pos, 8):
                    off8 += 1
                if is_odd_sixteenth(pos):
                    odd16 += 1
        if bnum > 0 and is_alternation_break(bar):
            alt_breaks.append(bnum)
    if off8:
        add("I02", "INFO", f"8분 그리드 밖 노트 {off8}개 (그중 16분 홀수 칸 {odd16}개)")
    if alt_breaks:
        add("I01", "INFO", f"교대 규칙(홀수 01/02, 짝수 13/14)을 벗어난 마디 {len(alt_breaks)}개")
    return issues


def is_alternation_break(bar) -> bool:
    """노멀 교대 규칙과 다른 마디인가 (다른 그룹 사용, 또는 메인 그룹 방향 반전)."""
    g, d = expected_main(bar.number)
    if any(x != g for x in bar.groups_used()):
        return True
    return bar.dirs[g] is not None and bar.dirs[g] != d


def summarize(issues: list[Issue]) -> dict[str, int]:
    c = Counter(i.severity for i in issues)
    return {s: c.get(s, 0) for s in SEVERITIES}
