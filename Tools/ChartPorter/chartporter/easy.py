"""음원 → 이지 (사용자 스타일, 2번 기준) — 신규 곡용.

사용자 이지(2번)는 음원 크기보다 박자 위치를 따른다 (노트 칸 AUC .51–.56). 그래서 음원은 거름망과 홀드 선택에만 쓴다.
  1. 뼈대: 마디의 네 박. 소리가 없는 박은 뺀다
     (sound_ok = RMS ≥ 곡 중앙값 −20 dB 그리고 중역·저역·고역·화성 중역 중 최대 백분위 ≥ P10 — 사용자 노트가 걸리는 비율 1% 미만)
  2. 쉬는 마디(BREAK, 마디 평균 RMS ≤ 중앙값 −12 dB): 첫 박 하나를 3/4 까지 홀드
  3. 마디당 target_per_bar(3.33)가 되도록 '공격음이 약한 박'을 바로 앞 박의 홀드로 덮는다
     (2번 블라인드: 무작위로 덮기 F1 .71 → 약한 박 덮기 .75). 마디당 최대 2개, 연달아 덮지 않음.
     홀드 끝 = 다음 노트 1/8 앞 (사용자 이지 홀드의 40%), 마디 안, 7/8 까지
  4. 홀드 비율이 hold_share(22%)보다 낮으면, 뒤 소리가 이어지는 박을 1/8 홀드로 (홀드 ≥ 0.19초일 때만 = 155 BPM 이하)
  5. 엇박은 확실한 당김음만: 음원 당김음 점수 A ≥ 60 인 엇박(1/8·3/8·5/8)으로 뒤 박 탭을 당긴다
     (노트 수 그대로, 마디당 1개, 0.19초 규칙. A ≥ 60 정밀도: 1·5·6번 노멀 .61–.89). 나머지는 리포트 '당김음 후보'로
  6. 레인: 메인 라인 그룹만. 마디 첫 노트 위 71%, 위→아래 83%, 아래→위 48% (2번: 전체 바꿈 66%, 위 레인 44%). 곡·마디·위치로 정한 해시라
     한 마디를 고쳐도 다른 마디 레인이 바뀌지 않는다
  7. songs 설정의 복사 규칙(sections copy_of)이 있으면 원본 마디를 대상 마디로 그대로 복사한다 (하드와 같은 규칙)
모든 노트·뺀 박은 log 에 이유를 남긴다.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction

import numpy as np

from .audio import AudioFeatures, Percentiles, cell_value
from .model import Bar, Chart, Header, expected_main, lanes_of
from .port import LogEntry, PortResult
from .timing import audio_time

QUARTER = Fraction(1, 4)
EIGHTH = Fraction(1, 8)
SEVEN8 = Fraction(7, 8)
SOUND_ENVS = ("mid", "low", "high", "harm_mid")


@dataclass
class EasyParams:
    target_per_bar: float = 3.33       # 2번 이지 마디당 노트 (빠른 곡도 마디당 유지 — 사용자 결정)
    hold_share: float = 0.22           # 2번 이지 홀드 비율
    max_swallow_per_bar: int = 2
    min_hold_s: float = 0.19           # 2번 이지 가장 짧은 홀드(8분 0.194초)
    switch_from_top: float = 0.83      # 마디 안: 위 → 아래로 바꿀 확률 (2번 67/81)
    switch_from_bottom: float = 0.48   #          아래 → 위 (2번 35/73)
    first_top_p: float = 0.71          # 마디 첫 노트가 위 레인 (2번 47/66)
    sound_db: float = 20.0
    sound_pct: float = 10.0
    break_db: float = 12.0
    bar_length: int = 8                # 사용자 이지는 4분만 있는 마디도 8칸으로 씀
    antic_threshold: float = 60.0      # 이 이상이면 뒤 박을 엇박으로 당김 (자동)
    sync_pct: float = 70.0             # 당김음 후보: 엇박 칸 백분위 ≥ 70
    sync_margin: float = 20.0          #             그리고 다음 박보다 20 이상 큼


from .keyhash import h as _h  # noqa: E402  (예전 이름 유지)


@dataclass
class _Ctx:
    feat: AudioFeatures
    bpm: float
    offset: float
    pct: dict
    rms_med: float

    def p(self, env: str, b: int, pos: Fraction) -> float:
        return self.pct[env](cell_value(self.feat, env, b, pos, self.bpm, self.offset))

    def rms(self, b: int, pos: Fraction) -> float:
        return float(self.feat.at("rms_db", audio_time(b, pos, self.bpm, self.offset), 0.025)[0])

    def bar_rms(self, b: int) -> float:
        t0, t1 = audio_time(b, 0, self.bpm, self.offset), audio_time(b + 1, 0, self.bpm, self.offset)
        i, j = np.searchsorted(self.feat.times, t0), np.searchsorted(self.feat.times, t1)
        return float(self.feat.env["rms_db"][i:j].mean()) if j > i else -999.0


def make_ctx(feat: AudioFeatures, bpm: float, offset: float, last_bar: int) -> _Ctx:
    pct = {e: Percentiles(feat, e, last_bar, bpm, offset) for e in SOUND_ENVS + ("harm_mid_energy",)}
    t_end = audio_time(last_bar + 1, 0, bpm, offset)
    sel = feat.env["rms_db"][(feat.times >= 0) & (feat.times < t_end)]
    return _Ctx(feat, bpm, offset, pct, float(np.median(sel)) if len(sel) else 0.0)


def generate_easy(feat: AudioFeatures, bpm: float, offset: float, last_bar: int, song_id: int,
                  params: EasyParams | None = None, title: str = "", artist: str = "", level: int | None = None,
                  cfg=None) -> PortResult:
    p = params or EasyParams()
    ctx = make_ctx(feat, bpm, offset, last_bar)
    bar_s = 240.0 / bpm
    log: list[LogEntry] = []
    notes: dict[int, dict] = {b: {"flags": []} for b in range(1, last_bar + 1)}

    def flag(b: int, name: str) -> None:
        if name not in notes[b]["flags"]:
            notes[b]["flags"].append(name)

    def sound_ok(b: int, pos: Fraction) -> bool:
        return ctx.rms(b, pos) >= ctx.rms_med - p.sound_db and max(ctx.p(e, b, pos) for e in SOUND_ENVS) >= p.sound_pct

    # 마디 분류
    first = float(feat.times[int(np.argmax(feat.env["rms_db"] >= float(feat.env["rms_db"].max()) - 40.0))])
    if first > bar_s / 4:
        flag(1, "PICKUP")
    flag(last_bar, "END")
    breaks = {b for b in range(2, last_bar) if ctx.bar_rms(b) <= ctx.rms_med - p.break_db}
    for b in breaks:
        flag(b, "BREAK")

    # 1. 뼈대
    onsets: dict[int, list[Fraction]] = {}
    holds: dict[tuple[int, Fraction], Fraction] = {}       # (마디, 시작) → 끝
    for b in range(1, last_bar + 1):
        beats = []
        for k in range(4):
            pos = Fraction(k, 4)
            if sound_ok(b, pos):
                beats.append(pos)
            else:
                log.append(LogEntry(b, 0, pos, "0", "EASY_SILENT", None, "소리 없는 박 — 넣지 않음", ctx.p("mid", b, pos)))
        if not beats:
            flag(b, "EMPTY")
        if b in breaks and beats:
            s = beats[0]
            end = Fraction(3, 4)
            for q in beats[1:]:
                log.append(LogEntry(b, 0, q, "0", "EASY_BREAK", None, "쉬는 마디 — 첫 박 홀드만"))
            beats = [s]
            if end > s and float(end - s) * bar_s >= p.min_hold_s:
                holds[(b, s)] = end
        onsets[b] = beats

    # 2. 약한 박을 앞 박 홀드로 덮기
    total = sum(len(v) for v in onsets.values())
    n_drop = max(0, total - round(p.target_per_bar * last_bar))
    cands = [(b, q) for b, v in onsets.items() if b not in breaks for q in v if q > 0 and (q - QUARTER) in v]
    chosen: set[tuple[int, Fraction]] = set()
    per: dict[int, int] = {}
    for b, q in sorted(cands, key=lambda x: (ctx.p("mid", *x), x)):
        if len(chosen) >= n_drop:
            break
        if per.get(b, 0) >= p.max_swallow_per_bar or (b, q - QUARTER) in chosen or (b, q + QUARTER) in chosen:
            continue
        chosen.add((b, q))
        per[b] = per.get(b, 0) + 1
    for b, q in sorted(chosen):
        onsets[b].remove(q)
        log.append(LogEntry(b, 0, q, "0", "EASY_SWALLOW", round(ctx.p("mid", b, q), 1),
                            f"공격음 약한 박 (중역 P{ctx.p('mid', b, q):.0f}) → 앞 박 홀드로 덮음"))
        s = q - QUARTER
        nxt = [x for x in onsets[b] if x > s]
        end = min(nxt[0] - EIGHTH, SEVEN8) if nxt else SEVEN8
        if end > s and float(end - s) * bar_s >= p.min_hold_s - 1e-9:
            holds[(b, s)] = end

    # 3. 홀드 비율 맞추기 (짧은 1/8 홀드, 느린 곡만)
    n_on = sum(len(v) for v in onsets.values())
    if float(EIGHTH) * bar_s >= p.min_hold_s - 1e-9:
        extra = []
        for b, v in onsets.items():
            for i, s in enumerate(v):
                if (b, s) in holds:
                    continue
                nxt = v[i + 1] if i + 1 < len(v) else Fraction(1)
                end = min(nxt - EIGHTH, SEVEN8)
                if end > s:
                    extra.append((ctx.p("harm_mid_energy", b, s + EIGHTH), b, s, end))
        for sc, b, s, end in sorted(extra, key=lambda x: (-x[0], x[1], x[2])):
            if len(holds) >= p.hold_share * n_on:
                break
            holds[(b, s)] = end

    # 3b. 확실한 당김음: 뒤 박 탭을 엇박으로 당김 (노트 수 그대로)
    from .rhythm import antic_cells, make_ctx as rhythm_ctx
    rctx = rhythm_ctx(feat, bpm, offset, last_bar)

    def clear_before(b: int, v: list, pos: Fraction) -> bool:
        """pos 앞 노트와 0.19초가 되는지. 안 되면 그 앞 노트가 약한 탭(중역 P50 미만)일 때만 빼서 '쉼 → 엇박'으로 만든다."""
        prev = [x for x in v if x < pos]
        if not prev:
            return not (b > 1 and onsets.get(b - 1) and float(pos + 1 - onsets[b - 1][-1]) * bar_s < p.min_hold_s - 1e-9)
        q = prev[-1]
        if (b, q) in holds:
            return False
        if float(pos - q) * bar_s >= p.min_hold_s - 1e-9:
            return True
        if ctx.p("mid", b, q) >= 50.0 or len(v) < 3:
            return False
        v.remove(q)
        log.append(LogEntry(b, 0, q, "0", "EASY_ANTIC", None, f"당김음 {pos} 앞의 약한 박(중역 P{ctx.p('mid', b, q):.0f})을 뺌 — 0.19초 규칙"))
        return True

    for b in range(1, last_bar + 1):
        if b in breaks:
            continue
        for pos, a in sorted(antic_cells(rctx, b, p.antic_threshold, in_bar=False), key=lambda x: (-x[1], x[0])):
            v = onsets[b]
            if pos in v:
                continue
            if pos == SEVEN8:
                # 다음 마디 첫 박을 이번 마디 7/8 로 (사람 노멀의 당김음은 5·6번에서 대부분 7/8)
                nb = b + 1
                nv = onsets.get(nb, [])
                if nb > last_bar or nb in breaks or Fraction(0) not in nv or len(nv) < 2 or (nb, Fraction(0)) in holds:
                    continue
                if not clear_before(b, v, pos):
                    continue
                nv.remove(Fraction(0))
                v.append(pos)
                v.sort()
                log.append(LogEntry(nb, 0, Fraction(0), "0", "EASY_ANTIC", round(a, 1), "첫 박을 앞 마디 7/8 로 당김 (원래 칸)"))
                log.append(LogEntry(b, 0, pos, "1", "EASY_ANTIC", round(a, 1), f"당김음 (음원 A={a:.0f}: 7/8 이 다음 첫 박보다 큼)"))
                flag(b, "ANTIC")
                break
            beat = pos + EIGHTH
            if beat not in v or (b, beat) in holds:
                continue
            if not clear_before(b, v, pos):
                continue
            v[v.index(beat)] = pos
            v.sort()
            log.append(LogEntry(b, 0, beat, "0", "EASY_ANTIC", round(a, 1), f"뒤 박을 엇박 {pos} 로 당김 (원래 칸)"))
            log.append(LogEntry(b, 0, pos, "1", "EASY_ANTIC", round(a, 1), f"당김음 (음원 A={a:.0f}: 엇박이 뒤 박보다 큼)"))
            flag(b, "ANTIC")
            break

    # 4. 당김음 후보 (넣지 않고 표시만)
    sync = []
    for b in range(1, last_bar + 1):
        for k in (1, 3, 5, 7):
            pos = Fraction(k, 8)
            nb, npos = (b + 1, Fraction(0)) if k == 7 else (b, pos + EIGHTH)
            if nb > last_bar:
                continue
            a, c = ctx.p("mid", b, pos), ctx.p("mid", nb, npos)
            if pos in onsets.get(b, []):
                continue
            if a >= p.sync_pct and a - c >= p.sync_margin:
                sync.append((b, pos, round(a), round(c)))
                flag(b, "SYNC_CANDIDATE")

    # 5. 레인과 채보
    chart = Chart(header=Header(title=title, artist=artist, difficulty="Easy", level=level if level is not None else 1, bpm=int(bpm)))
    chart.bars[0] = Bar(0, length=4, dirs={0: None, 1: 1})
    for b in range(1, last_bar + 1):
        g, d = expected_main(b)
        top, bottom = lanes_of(g)
        bar = Bar(b, length=p.bar_length)
        bar.dirs[g] = d
        cur_top = None
        for s in onsets[b]:
            if cur_top is None:
                cur_top = _h(song_id, b, s, "first") < p.first_top_p
            elif _h(song_id, b, s, "switch") < (p.switch_from_top if cur_top else p.switch_from_bottom):
                cur_top = not cur_top
            lane = top if cur_top else bottom
            if (b, s) in holds:
                e = holds[(b, s)]
                bar.cells[lane][s] = "2"
                bar.cells[lane][e] = "4"
                log.append(LogEntry(b, lane, s, "2", "EASY_HOLD", None, f"홀드 {s}→{e}", ctx.p("mid", b, s)))
            else:
                bar.cells[lane][s] = "1"
                log.append(LogEntry(b, lane, s, "1", "EASY_BEAT", None, "박", ctx.p("mid", b, s)))
        if any(q.denominator > p.bar_length for l in (1, 2, 3, 4) for q in bar.cells[l]):
            bar.length = bar.min_length()
        chart.bars[b] = bar
    if cfg is not None:
        chart = apply_copies(cfg, chart, log, notes)
    chart.header.notes = chart.count_notes()
    res = PortResult(chart=chart, log=log, bars=notes, offset=offset)
    res.bars[0] = {"flags": [], "sync_candidates": sync}
    return res


def apply_copies(cfg, chart: Chart, log: list, notes: dict) -> Chart:
    """복사 규칙을 마디 통째로 적용 (원본이 먼저 만들어진 뒤). 잠금·손작업 마디는 건드리지 않는다."""
    from .sync import plan_sync
    new, items = plan_sync(cfg, chart, None, {"bars": {}}, force=True)
    for it in items:
        if it.status not in ("write", "same"):
            continue
        notes.setdefault(it.bar, {"flags": []})["copy"] = (it.source, it.transform)
        for i in range(len(log) - 1, -1, -1):
            if log[i].bar == it.bar:
                del log[i]
        for q, l, c in new.bars[it.bar].notes():
            log.append(LogEntry(it.bar, l, q, c, "COPY", None, f"{it.source:03d}마디 복사 ({it.transform})"))
    return new


def rhythm_patterns(chart: Chart) -> set[tuple]:
    return {tuple(sorted({q for q, _l, c in bar.notes() if c in "12"})) for b, bar in chart.bars.items() if b > 0 and bar.has_notes()}


def alternation(chart: Chart) -> tuple[float, float]:
    """(마디 안 위아래 바꿈 비율, 위 레인 비율)."""
    sw = st = top = n = 0
    for b, bar in chart.bars.items():
        seq = [l for _q, l, c in bar.notes() if c in "12"]
        for x, y in zip(seq, seq[1:]):
            if (x % 2) != (y % 2):
                sw += 1
            else:
                st += 1
        top += sum(1 for l in seq if l % 2 == 1)
        n += len(seq)
    return sw / max(1, sw + st), top / max(1, n)
