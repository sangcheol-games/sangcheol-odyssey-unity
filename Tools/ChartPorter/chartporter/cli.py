"""ChartPorter 명령줄.

사용: python -m chartporter check [곡번호|채보경로 ...] [--all] [-v] [--json PATH]
종료 코드: 0 = FATAL/ERROR 없음, 1 = FATAL 또는 ERROR 있음, 2 = 사용법 오류(대상 없음 등)
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from fractions import Fraction
from pathlib import Path

from .chart_io import read_chart
from .config import ConfigError, load_song
from .model import DIFFICULTIES, Chart
from .musicso import MusicSO, all_music_so, music_so_for
from .paths import charts_dir, rel, repo_root
from .timing import is_on_grid
from .validate import SEVERITIES, Issue, is_alternation_break, summarize, validate

ISSUE_LIMIT = 12  # 같은 코드의 이슈는 기본적으로 이만큼만 출력


class UsageError(Exception):
    pass


def chart_stats(chart: Chart) -> dict:
    lengths = Counter(b.length for n, b in chart.bars.items() if n > 0)
    onsets = [(n, p) for n, b in chart.bars.items() for p, _l, c in b.notes() if c in "125"]
    holds = open_holds = 0
    for b in chart.bars.values():
        for lane in b.cells:
            for _start, end in b.holds(lane):
                holds += 1
                open_holds += end is None
    bars = [n for n in chart.bars if n > 0]
    return {
        "bars": chart.last_bar(),
        "notes": chart.count_notes(),
        "onsets": len(onsets),
        "subdivisions": dict(sorted(lengths.items())),
        "holds": holds,
        "open_holds": open_holds,
        "on_8th_grid": round(sum(is_on_grid(p, 8) for _, p in onsets) / len(onsets), 3) if onsets else None,
        "alternation_breaks": sum(is_alternation_break(chart.bars[n]) for n in bars),
    }


def so_links(sos: list[MusicSO]) -> dict[str, list[tuple[str, str, int | None]]]:
    """채보 파일 이름 → [(MusicSO 이름, 난이도 슬롯, 레벨)]"""
    out: dict[str, list] = {}
    for so in sos:
        for diff, name in so.chart_files.items():
            if name:
                out.setdefault(name, []).append((so.path.stem, diff, so.levels.get(diff)))
    return out


def slot_issues(so: MusicSO, exists=lambda name: (charts_dir() / name).exists()) -> list[Issue]:
    """난이도 슬롯 점검: 선택 가능(레벨 != -1)한데 채보가 없으면 로딩 실패, 채보가 있는데 레벨 -1이면 고를 수 없음."""
    out = []
    for d in DIFFICULTIES:
        name = so.chart_files.get(d)
        if so.selectable(d) and not name:
            why = f"guid {so.unresolved[d]} 를 Assets/Charts 에서 찾지 못함" if d in so.unresolved else "채보가 연결되지 않음"
            out.append(Issue("E11", "ERROR", f"{d} 는 레벨 {so.levels[d]} 로 선택 가능한데 {why} → 고르면 로딩 실패"))
        elif so.selectable(d) and not exists(name):
            out.append(Issue("E11", "ERROR", f"{d} 슬롯의 채보 파일 {name} 이 없음"))
        elif name and not so.selectable(d):
            out.append(Issue("W10", "WARN", f"{d} 슬롯에 {name} 이 연결됐지만 레벨이 -1(또는 없음)이라 선택할 수 없음"))
    return out


def _print_issues(issues: list[Issue], verbose: bool) -> None:
    order = {s: i for i, s in enumerate(SEVERITIES)}
    by_code: dict[str, list[Issue]] = {}
    for it in sorted(issues, key=lambda i: (order[i.severity], i.code, i.bar or 0, i.lane or 0)):
        by_code.setdefault(it.code, []).append(it)
    for code, items in by_code.items():
        shown = items if verbose else items[:ISSUE_LIMIT]
        for it in shown:
            where = it.where()
            print(f"   [{it.severity}] {it.code} {where + ': ' if where else ''}{it.message}")
        if len(items) > len(shown):
            print(f"   [{items[0].severity}] {code} … 외 {len(items) - len(shown)}건 (-v 로 전부 보기)")


def check_file(path: Path, links: dict, verbose: bool, so_bpm: int | None = None) -> tuple[list[Issue], dict | None]:
    try:
        chart, raw = read_chart(path)
    except OSError as e:
        issue = Issue("E12", "ERROR", f"파일을 읽을 수 없음: {e}", file=rel(path))
        print(f"\n== {path.name}\n   [ERROR] E12 {issue.message}")
        return [issue], None
    issues = validate(chart, raw, bpm=so_bpm or chart.header.bpm)
    if so_bpm and chart.header.bpm != so_bpm:
        issues.append(Issue("W07", "WARN", f"#BPM {chart.header.bpm} ≠ MusicSO bpm {so_bpm} (게임은 MusicSO 값을 씀)"))
    for it in issues:
        it.file = rel(path)
    st = chart_stats(chart)
    h = chart.header
    print(f"\n== {path.name}  ({h.raw_difficulty or '?'}, LEVEL {h.level}, BPM {h.bpm}, {st['bars']}마디, 노트 {st['notes']})")
    lk = links.get(path.name)
    print("   곡 데이터: " + (", ".join(f"{so} {d} 슬롯(레벨 {lv})" for so, d, lv in lk) if lk else "어느 MusicSO 에도 연결되지 않음"))
    subs = " ".join(f"{k}×{v}" for k, v in st["subdivisions"].items())
    grid = f"{st['on_8th_grid']*100:.0f}%" if st["on_8th_grid"] is not None else "-"
    print(f"   분할 {subs} | 홀드 {st['holds']}(끝 없는 홀드 {st['open_holds']}) | 8분 그리드 {grid} | 교대 규칙 밖 마디 {st['alternation_breaks']}")
    print("   " + " · ".join(f"{k} {v}" for k, v in summarize(issues).items()))
    _print_issues(issues, verbose)
    return issues, st


def check_song(song_id: int, sos: list[MusicSO], links: dict, verbose: bool) -> list[Issue]:
    issues: list[Issue] = []
    song = f"{song_id:04d}"
    try:
        cfg = load_song(song_id)
    except ConfigError as e:
        cfg = None
        issues.append(Issue("C01", "ERROR", f"곡 설정 오류: {e}", file=f"Tools/ChartPorter/songs/{song}.yaml"))
    so = music_so_for(song_id)
    charts = [charts_dir() / f"Chart_{song}_{d}.txt" for d in DIFFICULTIES]
    charts = [p for p in charts if p.exists()]
    if so is None and not charts:
        raise UsageError(f"곡 {song}: MusicSO 도 채보 파일도 없음")
    print(f"\n######## 곡 {song} {cfg.title if cfg else ''}".rstrip())
    for it in issues:
        print(f"   [{it.severity}] {it.code} {it.message}")
    so_bpm = None
    if so is None:
        print("   MusicSO 없음")
    else:
        so_bpm = so.bpm
        slots = ", ".join(f"{d}={so.chart_files.get(d) or '-'}(레벨 {so.levels.get(d, '-')})" for d in DIFFICULTIES)
        print(f"   {so.path.name}: bpm {so.bpm}, 음원 '{so.audio_file}'")
        print(f"   슬롯: {slots}")
        song_issues = slot_issues(so)
        if not so.audio_file:
            song_issues.append(Issue("I12", "INFO", "MusicSO 에 음원 경로가 없음 (무음 곡으로 처리됨)"))
        elif cfg is not None and cfg.audio == "none":
            print("   음원: 사용 안 함 (songs 설정 audio: none)")
        else:
            audio = cfg.audio_path(so.audio_file) if cfg is not None else None
            if audio is not None and not audio.exists():
                song_issues.append(Issue("W08", "WARN", f"음원 파일 없음: {rel(audio)}"))
            others = [o.path.stem for o in sos if o.audio_file and o.audio_file == so.audio_file and o.path != so.path]
            if others:
                song_issues.append(Issue("W09", "WARN", f"같은 음원을 다른 곡 데이터도 가리킴: {', '.join(others)}"))
        for it in song_issues:
            it.file = rel(so.path)
            print(f"   [{it.severity}] {it.code} {it.message}")
        issues += song_issues
    stats = {}
    for p in charts:
        iss, st = check_file(p, links, verbose, so_bpm)
        issues += iss
        if st:
            stats[p.stem.rsplit("_", 1)[-1]] = st
    if "Normal" in stats and "Hard" in stats and stats["Normal"]["onsets"]:
        n, h = stats["Normal"], stats["Hard"]
        print(f"\n   노멀→하드 지문: 노트 ×{h['notes']/n['notes']:.2f}, 노트 시작 ×{h['onsets']/n['onsets']:.2f}, "
              f"교대 규칙 밖 마디 {h['alternation_breaks']}/{h['bars']} ({h['alternation_breaks']/max(1, h['bars'])*100:.0f}%)")
    return issues


def resolve_chart_path(target: str) -> Path:
    """경로 그대로 → Assets/Charts 기준 → 저장소 루트 기준 순서로 찾는다."""
    for p in (Path(target), charts_dir() / target, repo_root() / target):
        if p.is_file():
            return p
    raise UsageError(f"채보 파일을 찾을 수 없음: {target}")


def cmd_check(args) -> int:
    sos = all_music_so()
    links = so_links(sos)
    all_issues: list[Issue] = []
    targets = list(args.targets)
    if args.all or not targets:
        ids = sorted({int(p.stem.split("_")[1]) for p in charts_dir().glob("Chart_*_*.txt") if p.stem.split("_")[1].isdigit()})
        targets = [str(i) for i in ids]
    try:
        for t in targets:
            if t.isdigit():
                all_issues += check_song(int(t), sos, links, args.verbose)
            else:
                all_issues += check_file(resolve_chart_path(t), links, args.verbose)[0]
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    s = summarize(all_issues)
    print("\n합계: " + " · ".join(f"{k} {v}" for k, v in s.items()))
    if args.json:
        Path(args.json).write_text(json.dumps([vars(i) for i in all_issues], ensure_ascii=False, indent=1), encoding="utf-8")
    return 1 if s["FATAL"] or s["ERROR"] else 0


def _chart_targets(targets: list[str]) -> list[Path]:
    out = []
    for t in targets:
        if t.isdigit():
            found = sorted(charts_dir().glob(f"Chart_{int(t):04d}_*.txt"))
            if not found:
                raise UsageError(f"곡 {int(t):04d} 의 채보 파일이 없음")
            out += found
        else:
            out.append(resolve_chart_path(t))
    return out


def cmd_fmt(args) -> int:
    from .fmt import format_chart, install, staging_path
    try:
        paths = _chart_targets(args.targets)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    failed = 0
    for path in paths:
        res = format_chart(path, fill_gaps=not args.no_fill_gaps, fix_header=not args.no_fix_header, orphan_end=args.orphan_end,
                           close_holds=args.close_holds)
        if not res.changed:
            print(f"== {path.name}: 고칠 것 없음")
            continue
        print(f"== {path.name}: " + "; ".join(res.changes))
        print(res.diff())
        if args.apply:
            try:
                saved = install(res)
                print(f"   → 적용함 (백업: {rel(saved)}). 에디터에서 이 채보를 다시 불러온 뒤 편집할 것.")
            except RuntimeError as e:
                print(f"   → 적용 안 함: {e}", file=sys.stderr)
                failed += 1
        else:
            staged = staging_path(path, "fmt")
            staged.parent.mkdir(parents=True, exist_ok=True)
            staged.write_bytes(res.after)
            print(f"   → 수정안: {rel(staged)} (적용하려면 --apply)")
    return 1 if failed else 0


def _song_audio(song_id: int):
    """(설정, MusicSO 또는 None, 음원 경로, bpm). MusicSO 가 없으면 songs 설정의 bpm·audio 를 쓴다 (신규 곡).
    둘 다 있으면 BPM 이 같아야 한다. 음원이 없으면 UsageError."""
    cfg = load_song(song_id)
    so = music_so_for(song_id)
    if so is None:
        if cfg.bpm is None or cfg.audio in ("auto", "none"):
            raise UsageError(f"곡 {song_id:04d}: MusicSO 없음 — 신규 곡이면 songs/{song_id:04d}.yaml 에 bpm 과 audio(음원 경로)를 적을 것")
        audio = cfg.audio_path(None)
        if audio is None or not audio.exists():
            raise UsageError(f"곡 {song_id:04d}: 음원 파일 없음 ({cfg.audio})")
        return cfg, None, audio, cfg.bpm
    if cfg.bpm is not None and cfg.bpm != so.bpm:
        raise UsageError(f"곡 {song_id:04d}: songs 설정 bpm {cfg.bpm} 과 MusicSO bpm {so.bpm} 이 다름")
    audio = cfg.audio_path(so.audio_file)
    if audio is None or not audio.exists():
        raise UsageError(f"곡 {song_id:04d}: 음원 파일 없음 ({so.audio_file})")
    return cfg, so, audio, so.bpm


def _align_offset(cfg, feat, bpm) -> float | None:
    """out/000N/align.json 의 분석 오프셋 (같은 음원·BPM 일 때만)."""
    from .paths import out_dir
    p = out_dir(cfg.id) / "align.json"
    if not p.exists():
        return None
    d = json.loads(p.read_text(encoding="utf-8"))
    if d.get("bpm") != bpm or (feat is not None and d.get("sha") not in (None, feat.sha)):
        return None
    return float(d["offset_ms"]) / 1000.0


def _offset_for(cfg, feat, base_chart, bpm) -> float:
    """분석 오프셋: songs 설정 offset_ms → (신규 곡이거나 기준 채보가 없으면) align.json → 기준 채보 노트 위치에서 추정.
    기존 곡은 align 을 돌려도 채보 기준 값을 계속 쓴다 (포팅 결과가 바뀌지 않게). 두 값 차이는 6곡 모두 2ms 이내."""
    from .audio import estimate_offset
    if cfg.offset_ms is not None:
        return float(cfg.offset_ms) / 1000.0
    a = _align_offset(cfg, feat, bpm)
    has_chart = base_chart is not None and any(bar.has_notes() for b, bar in base_chart.bars.items() if b > 0)
    if a is not None and (not has_chart or music_so_for(cfg.id) is None):
        return a
    if has_chart:
        return estimate_offset(feat, base_chart, bpm).offset
    raise UsageError(f"곡 {cfg.id:04d}: 분석 오프셋을 정할 채보가 없음 — 먼저 align {cfg.id}")


def cmd_align(args) -> int:
    """신규 곡 음원 준비 검사: 정수 BPM·1마디 = 0초·첫 박을 검증 → out/000N/align.md, align.json, 메트로놈 클립."""
    from .align import check_alignment, metronome_chart, report_markdown
    from .audio import features_for
    from .listen import render_preview
    from .paths import out_dir
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError, ConfigError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    feat = features_for(audio)
    res = check_alignment(feat, int(bpm))
    d = out_dir(cfg.id)
    d.mkdir(parents=True, exist_ok=True)
    clips = []
    if not args.no_clips:
        n = res.last_bar_proposal
        met = metronome_chart(max(n, 8))
        mid = max(1, n // 2 - 3)
        for a, b in ((1, min(8, n)), (mid, min(mid + 7, n))):
            out = d / "clips" / f"align_metronome_{a:03d}-{b:03d}.wav"
            render_preview(audio, met, bpm, (a, b), out)
            clips.append(rel(out))
    data = res.to_json()
    data.update({"audio": audio.name, "sha": feat.sha})
    (d / "align.json").write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    md = report_markdown(f"{cfg.id}번 {cfg.title}", res, audio.name, clips)
    (d / "align.md").write_text(md, encoding="utf-8")
    print(md.split("## 8마디")[0].rstrip())
    print(f"\n→ {rel(d / 'align.md')}\n→ {rel(d / 'align.json')}")
    if not res.ok:
        return 1
    missing = [k for k, v in (("downbeat_ok: true", cfg.downbeat_ok), ("region.last_bar", cfg.region_last_bar)) if not v]
    if missing and not cfg.chart_path("Easy").exists():      # 이지부터 새로 만들 곡에만 안내
        print(f"   다음 단계 전에 songs/{cfg.id:04d}.yaml 에 적을 것: " + ", ".join(missing))
    return 0


def cmd_analyze(args) -> int:
    from .audio import estimate_offset, features_for
    from .paths import out_dir
    rc = 0
    for t in args.songs:
        try:
            cfg, so, audio, bpm = _song_audio(int(t))
        except UsageError as e:
            print(f"오류: {e}", file=sys.stderr)
            rc = 2
            continue
        feat = features_for(audio, use_cache=not args.no_cache)
        base, _ = read_chart(cfg.base_path())
        est = estimate_offset(feat, base, bpm)
        last = base.last_bar()
        chart_end = last * 240.0 / bpm
        print(f"\n######## 곡 {cfg.id:04d} {cfg.title} — {audio.name} ({feat.duration:.2f}초, BPM {bpm})")
        print(f"   채보 {last}마디 = 음원 {chart_end:.2f}초 지점까지 (음원 길이 대비 {chart_end - feat.duration:+.2f}초)")
        print(f"   분석 오프셋 {est.offset*1000:+.0f}ms (노멀 노트 위치 세기 ×{est.score:.2f}, 뾰족함 {est.sharpness:.2f})"
              + (f" — songs 설정값 {cfg.offset_ms}ms 사용 중" if cfg.offset_ms is not None else ""))
        spread = [w[2] for w in est.windows]
        if spread:
            print("   구간별: " + ", ".join(f"{a:03d}-{b:03d} {o*1000:+.0f}" for a, b, o in est.windows))
            if max(spread) - min(spread) > 0.010:
                print(f"   [WARN] 구간별 오프셋 차이 {(max(spread)-min(spread))*1000:.0f}ms > 10ms (템포 흔들림이나 채보 타이밍 문제 확인)")
        d = out_dir(cfg.id)
        d.mkdir(parents=True, exist_ok=True)
        (d / "analysis.json").write_text(json.dumps({
            "song": cfg.id, "audio": audio.name, "sha": feat.sha, "duration": feat.duration, "bpm": bpm,
            "offset_ms": round(est.offset * 1000, 1), "score": est.score, "sharpness": est.sharpness,
            "windows": [{"bars": [a, b], "offset_ms": round(o * 1000, 1)} for a, b, o in est.windows],
        }, ensure_ascii=False, indent=1), encoding="utf-8")
    return rc


def cmd_checklist(args) -> int:
    from .audio import features_for
    from .listen import bar_groups, build_checklist, render_preview, write_checklist
    from .paths import out_dir
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    diff = args.chart or ("Hard" if cfg.role == "reference" else "Normal")
    path = charts_dir() / f"Chart_{cfg.id:04d}_{diff}.txt"
    if not path.exists():
        print(f"오류: {path.name} 없음", file=sys.stderr)
        return 2
    chart, _ = read_chart(path)
    base, _ = read_chart(cfg.base_path())
    feat = features_for(audio)
    offset = _offset_for(cfg, feat, base, bpm)
    items = build_checklist(chart, feat, bpm, offset)
    d = out_dir(cfg.id)
    clips: dict[str, str] = {}
    if not args.no_clips and items:
        for a, b in bar_groups([it.bar for it in items]):
            shift = {(it.bar, it.lane, it.pos): it.pos + Fraction(it.shift, it.grid) for it in items if a <= it.bar <= b and it.shift}
            name = f"{path.stem}_{a:03d}-{b:03d}"
            render_preview(audio, chart, bpm, (a, b), d / "clips" / f"{name}_A.wav")
            label = f"clips/{name}_A.wav"
            if shift:
                render_preview(audio, chart, bpm, (a, b), d / "clips" / f"{name}_B.wav", shift=shift)
                label += f", clips/{name}_B.wav"
            for bn in range(a, b + 1):
                clips[str(bn)] = label
    csv_path, md_path = d / f"checklist_{path.stem}.csv", d / f"checklist_{path.stem}.md"
    write_checklist(items, path.name, csv_path, md_path, clips)
    flagged = sum(1 for it in items if it.shift)
    print(f"{path.name}: 8분 그리드 밖 노트 {len(items)}개, 그중 '이 칸엔 소리 시작이 안 잡히고 한 칸 앞·뒤에 있음' {flagged}개 (분석 오프셋 {offset*1000:+.0f}ms)")
    print(f"   → {rel(md_path)}\n   → {rel(csv_path)}" + (f"\n   → 미리듣기 {rel(d / 'clips')}/ (A=지금 채보, B=제안대로 옮긴 노트를 3kHz 클릭으로)" if clips else ""))
    return 0


def cmd_preview(args) -> int:
    from .config import parse_bars
    from .listen import render_preview
    from .paths import out_dir
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
        bars = sorted(parse_bars(args.bars))
        if not bars:
            raise UsageError("--bars 가 비어 있음")
    except (UsageError, ValueError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    path = charts_dir() / f"Chart_{cfg.id:04d}_{args.chart}.txt"
    chart, _ = read_chart(path)
    out = out_dir(cfg.id) / "clips" / f"{path.stem}_{bars[0]:03d}-{bars[-1]:03d}.wav"
    render_preview(audio, chart, bpm, (bars[0], bars[-1]), out, offset=args.offset_ms / 1000.0)
    print(f"→ {rel(out)}  (위 레인 2kHz, 아래 레인 1.2kHz, 홀드 끝 작은 900Hz)")
    return 0


def cmd_stats(args) -> int:
    from .config import songs_dir
    from .paths import out_dir
    from .stats import build_profile, report_markdown, write_profile
    ids = [int(s) for s in args.songs] if args.songs else sorted(
        int(p.stem) for p in songs_dir().glob("*.yaml") if load_song(int(p.stem)).role == "reference")
    if not ids:
        print("오류: 기준 곡(role: reference)이 없음", file=sys.stderr)
        return 2
    primary = int(args.primary) if args.primary else ids[0]
    profile = build_profile(ids, primary, lambda sid: music_so_for(sid).bpm)
    path = write_profile(profile)
    md = report_markdown(profile)
    rp = out_dir() / "style_report.md"
    rp.parent.mkdir(parents=True, exist_ok=True)
    rp.write_text(md, encoding="utf-8")
    print(md)
    print(f"→ {rel(path)}\n→ {rel(rp)}")
    return 0


def cmd_sections(args) -> int:
    import yaml
    from .audio import features_for
    from .paths import out_dir
    from .sections import bar_features, find_relations, format_report, proposal_yaml, similarity
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    normal, _ = read_chart(cfg.base_path()) if cfg.base_path().exists() else (None, None)
    feat = features_for(audio)
    try:
        offset = _offset_for(cfg, feat, normal, bpm)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    last = normal.last_bar() if normal is not None else (cfg.region_last_bar or int(feat.duration / (240.0 / bpm)))
    bf = bar_features(feat, last, bpm, offset)
    sim = similarity(bf)
    rels = find_relations(bf, sim, normal)            # 채보가 없으면 음원만으로 (변환은 마디 간격으로: 짝수 same, 홀수 cross)
    title = cfg.title or (so.audio_file if so is not None else audio.name)
    md = format_report(f"{cfg.id}번 {title}", bf, sim, rels)
    d = out_dir(cfg.id)
    d.mkdir(parents=True, exist_ok=True)
    (d / "sections.md").write_text(md, encoding="utf-8")
    prop = proposal_yaml(rels)
    (d / "sections.proposed.yaml").write_text(
        "# 음원 기준 복사 규칙 제안 (같음/반주 같음만). 맞는 것만 songs/%04d.yaml 의 sections 로 옮길 것.\n"
        "# transform 은 노멀 채보 관계를 따름: same(그대로) / mirror(반전) / cross(홀수 간격, 다른 그룹) / manual(노멀이 달라 직접 판단)\n"
        "# 채보가 없는 신규 곡은 마디 간격으로 정함: 짝수 same, 홀수 cross\n" % cfg.id
        + yaml.safe_dump({"sections": prop}, allow_unicode=True, sort_keys=False), encoding="utf-8")
    print(md)
    print(f"→ {rel(d / 'sections.md')}\n→ {rel(d / 'sections.proposed.yaml')} (복사 규칙 후보 {len(prop)}개)")
    return 0


def cmd_sync(args) -> int:
    from .fmt import FormatResult, install
    from .sync import RuleError, load_state, plan_sync, save_state, updated_state
    from .chart_io import to_bytes
    try:
        cfg = load_song(int(args.song))
    except ConfigError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    diff = args.diff
    target_path = cfg.target_path() if diff == "Hard" else cfg.chart_path(diff)
    if not target_path.exists():
        print(f"오류: {rel(target_path)} 없음", file=sys.stderr)
        return 2
    target, _ = read_chart(target_path)
    # 첫 sync 판단용 기준: 하드는 노멀(노멀 그대로인 마디 = 아직 손대지 않음). 이지·노멀은 설치 때 기록한 상태만 쓴다
    normal, _ = read_chart(cfg.base_path()) if diff == "Hard" and cfg.base_path().exists() else (None, None)
    state = load_state(cfg.id, diff)
    try:
        new, items = plan_sync(cfg, target, normal, state, force=args.force)
    except RuleError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    if not items:
        print("복사 규칙(sections 의 copy_of)이 없음")
        return 0
    by = {}
    for it in items:
        by.setdefault(it.status, []).append(it)
    names = {"same": "이미 같음", "write": "새로 복사", "user_edited": "직접 고친 것으로 보여 건너뜀", "locked": "잠금(locked/frozen)"}
    print(f"== {target_path.name}: 규칙 대상 {len(items)}마디")
    for st in ("write", "user_edited", "locked", "same"):
        if st in by:
            print(f"   {names[st]} {len(by[st])}: " + ", ".join(f"{i.bar:03d}←{i.source:03d}{ {'mirror': '(반전)', 'cross': '(다른 그룹)'}.get(i.transform, '') }" for i in by[st]))
    if "user_edited" in by:
        print("   → 이 마디를 계속 직접 관리하려면 songs 설정 locked_bars 에 넣거나 규칙에서 빼고, 덮어쓰려면 --force")
    res = FormatResult(path=target_path, chart=new, before=target_path.read_bytes(), after=to_bytes(new))
    if not res.changed:
        save_state(cfg.id, updated_state(items, state), diff)
        print("   바꿀 마디 없음")
        return 0
    print(res.diff())
    if not args.apply:
        print("   → 미리보기만 함 (적용하려면 --apply)")
        return 0
    try:
        saved = install(res)
    except RuntimeError as e:
        print(f"   → 적용 안 함: {e}", file=sys.stderr)
        return 1
    save_state(cfg.id, updated_state(items, state), diff)
    print(f"   → 적용함 (백업: {rel(saved)}). 에디터에서 이 채보를 다시 불러온 뒤 편집할 것.")
    return 0


def read_source_bytes(spec: str) -> bytes:
    """'git:<커밋>:<경로>' 또는 저장소 기준 경로."""
    import subprocess
    if spec.startswith("git:"):
        _, rev, path = spec.split(":", 2)
        return subprocess.run(["git", "-C", str(repo_root()), "show", f"{rev}:{path}"], capture_output=True, check=True).stdout
    return (repo_root() / spec).read_bytes()


def normal_edit_cells(cfg, normal) -> list[tuple]:
    """finish_normal_source 를 같은 규칙으로 옮긴 결과와 지금 노멀이 다른 칸 = 사용자가 리타이밍 때 직접 고친 칸."""
    from fractions import Fraction as _F
    from .chart_io import parse_raw, to_chart
    from .retime import compare, shift_chart
    if not (cfg.finish_normal_source and cfg.finish_retime):
        return []
    old = to_chart(parse_raw(read_source_bytes(cfg.finish_normal_source)))
    shifted, _ = shift_chart(old, _F(cfg.finish_retime))
    return compare(shifted, normal)[2]


def load_finish_source(cfg):
    """mode finish 의 손작업 하드: 'git:<커밋>:<경로>' 이면 git 에서, 아니면 파일에서 읽고 finish_retime 을 적용.
    반환: (시간 이동한 채보, 넘어간 노트 목록) 또는 (None, [])"""
    import subprocess
    from fractions import Fraction as _F
    from .chart_io import parse_raw, to_chart
    from .retime import shift_chart
    if cfg.mode != "finish":
        return None, []
    spec = cfg.finish_source or rel(cfg.target_path())
    chart = to_chart(parse_raw(read_source_bytes(spec)), spec)
    if cfg.finish_retime:
        return shift_chart(chart, _F(cfg.finish_retime))
    return chart, []


def _make_porter(cfg, so, audio, bpm, base_chart, use_rules: bool = True):
    from .audio import features_for
    from .port import Porter, PortParams
    from .stats import PROFILE_PATH, load_profile
    params = PortParams.from_profile(load_profile()) if PROFILE_PATH.exists() else PortParams()
    feat = features_for(audio) if audio is not None else None
    offset = _offset_for(cfg, feat, base_chart, bpm) if feat is not None else 0.0
    if not use_rules:
        from dataclasses import replace
        cfg = replace(cfg, sections=[])
    frozen, carried = load_finish_source(cfg)
    return Porter(base_chart, feat, bpm, offset, cfg, params, frozen_source=frozen, frozen_carried=carried)


def cmd_port(args) -> int:
    from .chart_io import to_bytes
    from .paths import out_dir
    from .report import log_json, report_markdown
    from .validate import validate
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    normal, raw = read_chart(cfg.base_path())
    # 끝 없는 홀드(E13/E14)는 생성기가 하드 쪽에서 닫으므로 원본 노멀에 남아 있어도 진행 (노멀은 사용자 결정으로 그대로 둠)
    bad = [i for i in validate(normal, raw, bpm=bpm) if i.severity in ("FATAL", "ERROR") and i.code not in ("E13", "E14")]
    if bad:
        print(f"오류: {cfg.base_path().name} 에 FATAL/ERROR {len(bad)}건 — 먼저 check/fmt 로 고칠 것", file=sys.stderr)
        return 1
    from .sync import RuleError
    try:
        porter = _make_porter(cfg, so, audio, bpm, normal)
        res = porter.run()
    except RuleError as e:
        print(f"오류: songs/{cfg.id:04d}.yaml 복사 규칙 — {e}", file=sys.stderr)
        return 2
    hard = res.chart
    if cfg.mode == "finish":
        for b, l, p, before, after in normal_edit_cells(cfg, normal):
            # 노멀에서 지우거나 옮긴 칸(before≠0)에 손작업 하드도 노트가 있을 때만 표시
            if b in cfg.frozen_bars and before not in ("0", "4") and any(
                    hard.bars[b].cells[x].get(p, "0") in "125" and hard.bars[b].cells[x].get(p, "0") != "0" for x in (1, 2, 3, 4)):
                n = res.bars.setdefault(b, {"flags": []})
                if "NORMAL_EDIT" not in n["flags"]:
                    n["flags"].append("NORMAL_EDIT")
                n.setdefault("normal_edits", []).append(f"L{l}@{p}: {before}→{after}")
    if args.level is not None or cfg.levels.get("Hard") is not None:
        hard.header.level = args.level if args.level is not None else cfg.levels["Hard"]
    issues = [i for i in validate(hard, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    d = out_dir(cfg.id) / "port"
    d.mkdir(parents=True, exist_ok=True)
    chart_out = d / cfg.target_path().name
    chart_out.write_bytes(to_bytes(hard, fill_gaps=True))
    (d / "port_log.json").write_text(log_json(res), encoding="utf-8")
    md = report_markdown(f"{cfg.id}번 {cfg.title}", normal, res, bpm, has_rules=bool(porter.copy_targets))
    rbars = sorted(b for b, v in res.bars.items() if v.get("rhythm"))
    if rbars and audio is not None and not getattr(args, "no_clips", False):
        from .listen import bar_groups, render_preview
        clips = []
        for a, b in bar_groups(rbars)[:8]:
            out = d / "clips" / f"rhythm_{a:03d}-{b:03d}.wav"
            render_preview(audio, hard, bpm, (a, b), out)
            clips.append(rel(out))
        md += "\n## 리듬 변주 미리듣기\n\n" + "\n".join(f"- `{c}`" for c in clips) + "\n"
    (d / "report.md").write_text(md, encoding="utf-8")
    print(md.split("## 마디별 변경")[0].rstrip())
    if issues:
        print("\n검사: " + ", ".join(f"{i.code} {i.where()} {i.message}" for i in issues[:10]))
    print(f"\n→ 초안 {rel(chart_out)}\n→ 리포트 {rel(d / 'report.md')}\n→ 근거 {rel(d / 'port_log.json')}")
    print("   Assets/Charts 에는 아직 쓰지 않았음. 검토 후 설치는 별도로 진행.")
    return 0


def cmd_install(args) -> int:
    """out/000N/ 의 초안을 Assets/Charts 에 설치 (하드 = port/, 이지·노멀 = gen/). 이미 있는 파일은 백업하고,
    지난 설치 뒤 바뀐 파일(에디터 저장, 팀원 채보 등)이면 --force 없이는 덮어쓰지 않는다."""
    from .chart_io import read_chart as _read
    from .install import InstallError, install_chart
    from .paths import out_dir
    from .sync import bar_hash, load_state, rules_from_config, save_state
    try:
        cfg = load_song(int(args.song))
    except ConfigError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    diff = args.diff
    if diff == "Hard":
        src, dst = out_dir(cfg.id) / "port" / cfg.target_path().name, cfg.target_path()
        how = f"port {cfg.id}"
    else:
        dst = cfg.chart_path(diff)
        src = out_dir(cfg.id) / "gen" / dst.name
        how = f"thin {cfg.id}" if diff == "Easy" else f"gen {cfg.id} --diff {diff}"
    if not src.exists():
        print(f"오류: {rel(src)} 없음 — 먼저 {how}", file=sys.stderr)
        return 2
    try:
        saved = install_chart(src, dst, out_dir(cfg.id) / "install_state.json", diff, force=args.force, source_label=rel(src))
    except InstallError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 1
    if saved is not None:
        print(f"   기존 파일 백업: {rel(saved)}")
    chart, _ = _read(dst)
    # 복사 규칙 대상 마디는 도구가 쓴 것으로 기록 → 이후 sync --diff 가 사용자 편집과 구분 (난이도별 상태)
    targets = {t for r in rules_from_config(cfg) for t in r.targets}
    if targets:
        st = load_state(cfg.id, diff)
        st.setdefault("bars", {}).update({str(t): bar_hash(chart.bars.get(t)) for t in targets})
        save_state(cfg.id, st, diff)
    print(f"→ 설치함: {rel(dst)} (노트 {chart.count_notes()})")
    so = music_so_for(cfg.id)
    linked = [(d, so.levels.get(d)) for d, name in (so.chart_files.items() if so else []) if name == dst.name]
    if linked:
        print("   이미 연결됨: " + ", ".join(f"MusicSO_{cfg.id:04d} {d} 슬롯(레벨 {lv})" for d, lv in linked))
    else:
        print(f"   Unity 에디터가 .meta 를 만든 뒤, MusicSO_{cfg.id:04d} 의 chartFile {diff} 슬롯 연결과 level 을 Inspector 에서 설정할 것.")
    print("   ChartEditor 로 열 때는 파일을 새로 불러온 뒤 편집할 것 (S 저장이 메모리 내용으로 덮어씀).")
    return 0


def _thin_check_line(params) -> str | None:
    """같은 규칙으로 2번 노멀을 덜어내 실제 2번 이지(사용자)와 비교한 한 줄."""
    from .thin import onset_times, thin
    n2, e2 = charts_dir() / "Chart_0002_Normal.txt", charts_dir() / "Chart_0002_Easy.txt"
    so2 = music_so_for(2)
    if not (n2.exists() and e2.exists() and so2):
        return None
    normal, _ = read_chart(n2)
    real, _ = read_chart(e2)
    gen = onset_times(thin(normal, so2.bpm, params).chart)
    ref = onset_times(real)
    tp = len(gen & ref)
    return (f"같은 규칙으로 2번 노멀을 덜어내면 실제 2번 이지(사용자)와 노트 시작 위치 P {tp / max(1, len(gen)):.2f} "
            f"R {tp / max(1, len(ref)):.2f} F1 {2 * tp / max(1, len(gen) + len(ref)):.2f} (규칙을 2번에서 잰 값이라 다소 낙관적)")


def cmd_thin(args) -> int:
    """사용자 노멀에서 노트를 빼서 이지 초안을 만든다 → out/000N/gen/ (Assets 에는 쓰지 않음)."""
    from .audio import features_for
    from .chart_io import to_bytes
    from .listen import render_preview
    from .paths import out_dir
    from .report import log_json, thin_report_markdown
    from .thin import ThinParams, thin
    from .validate import validate
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    normal, raw = read_chart(cfg.base_path())
    bad = [i for i in validate(normal, raw, bpm=bpm) if i.severity in ("FATAL", "ERROR") and i.code not in ("E13", "E14")]
    if bad:
        print(f"오류: {cfg.base_path().name} 에 FATAL/ERROR {len(bad)}건 — 먼저 check/fmt 로 고칠 것", file=sys.stderr)
        return 1
    params = ThinParams(min_gap_s=args.min_gap, min_hold_s=args.min_hold, target_per_bar=args.per_bar)
    tie = None
    if audio is not None:
        from .audio import Percentiles, cell_value
        feat = features_for(audio)
        offset = _offset_for(cfg, feat, normal, bpm)
        pct = Percentiles(feat, "harm_mid", normal.last_bar(), bpm, offset)
        tie = lambda u: pct(cell_value(feat, "harm_mid", u.bar, u.pos, bpm, offset))
    so_level = so.levels.get("Easy") if so else None
    level = args.level if args.level is not None else cfg.levels.get("Easy") or (so_level if so_level and so_level > 0 else None)
    res = thin(normal, bpm, params, tie=tie, level=level)
    easy = res.chart
    issues = [i for i in validate(easy, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    d = out_dir(cfg.id) / "gen"
    d.mkdir(parents=True, exist_ok=True)
    out = d / cfg.chart_path("Easy").name
    out.write_bytes(to_bytes(easy, fill_gaps=True))
    (d / "gen_log_Easy.json").write_text(log_json(res), encoding="utf-8")
    md = thin_report_markdown(f"{cfg.id}번 {cfg.title}", normal, res, bpm, params, _thin_check_line(params))
    (d / "report_Easy.md").write_text(md, encoding="utf-8")
    print(md.split("## 마디별 변경")[0].rstrip())
    if issues:
        print("\n검사: " + ", ".join(f"{i.code} {i.where()} {i.message}" for i in issues[:10]))
    if audio is not None and not args.no_clips:
        wav = render_preview(audio, easy, bpm, (1, easy.last_bar()), d / "clips" / f"{out.stem}_full.wav")
        print(f"→ 미리듣기 {rel(wav)} (곡 전체, 위 레인 2kHz · 아래 레인 1.2kHz · 홀드 끝 작은 900Hz)")
    print(f"→ 초안 {rel(out)}\n→ 리포트 {rel(d / 'report_Easy.md')}\n→ 근거 {rel(d / 'gen_log_Easy.json')}")
    print(f"   Assets/Charts 에는 아직 쓰지 않았음. 검토 후 install {cfg.id} --diff Easy")
    return 0


def _gen_ready(cfg) -> str | None:
    """신규 곡 생성 전 사람 확인 항목. 문제가 있으면 안내 문구."""
    from .paths import out_dir
    ap = out_dir(cfg.id) / "align.json"
    if cfg.offset_ms is None and not ap.exists():
        return f"먼저 align {cfg.id} 를 돌릴 것"
    if ap.exists() and not json.loads(ap.read_text(encoding="utf-8")).get("ok", False):
        return f"align 검사에 중지 항목이 있음 — {rel(out_dir(cfg.id) / 'align.md')} 를 보고 음원을 먼저 맞출 것"
    if not cfg.downbeat_ok:
        return f"메트로놈 클립을 듣고 첫 박이 맞으면 songs/{cfg.id:04d}.yaml 에 downbeat_ok: true 를 적을 것"
    if not cfg.region_last_bar:
        return f"songs/{cfg.id:04d}.yaml 에 채보 마지막 마디를 region: {{last_bar: N}} 으로 적을 것 (align.md 의 제안 참고)"
    return None


def cmd_gen(args) -> int:
    """신규 곡 생성: --diff Easy = 음원 기반 이지 → out/000N/gen/ (Assets 에는 쓰지 않음)."""
    from .audio import features_for
    from .chart_io import to_bytes
    from .easy import EasyParams, generate_easy
    from .listen import render_preview
    from .paths import out_dir
    from .report import easy_report_markdown, log_json
    from .validate import validate
    try:
        cfg, so, audio, bpm = _song_audio(int(args.song))
    except (UsageError, ValueError, ConfigError) as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    if args.diff == "Normal":
        return _gen_normal(args, cfg, so, audio, bpm)
    why = _gen_ready(cfg)
    if why:
        print(f"오류: {why}", file=sys.stderr)
        return 2
    feat = features_for(audio)
    try:
        offset = _offset_for(cfg, feat, None, bpm)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    params = EasyParams(target_per_bar=args.per_bar) if args.per_bar is not None else EasyParams()
    title = cfg.title or (so.title_key if so is not None and hasattr(so, "title_key") else "")
    from .sync import RuleError
    try:
        res = generate_easy(feat, bpm, offset, cfg.region_last_bar, cfg.id, params, title=title, artist=cfg.artist,
                            level=args.level if args.level is not None else cfg.levels.get("Easy"), cfg=cfg)
    except RuleError as e:
        print(f"오류: songs/{cfg.id:04d}.yaml 복사 규칙 — {e}", file=sys.stderr)
        return 2
    easy = res.chart
    issues = [i for i in validate(easy, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    d = out_dir(cfg.id) / "gen"
    d.mkdir(parents=True, exist_ok=True)
    out = d / cfg.chart_path("Easy").name
    out.write_bytes(to_bytes(easy, fill_gaps=True))
    (d / "gen_log_Easy.json").write_text(log_json(res), encoding="utf-8")
    md = easy_report_markdown(f"{cfg.id}번 {cfg.title}", res, bpm, params)
    (d / "report_Easy.md").write_text(md, encoding="utf-8")
    print(md.split("## 당김음 후보")[0].rstrip())
    if issues:
        print("\n검사: " + ", ".join(f"{i.code} {i.where()} {i.message}" for i in issues[:10]))
    if not args.no_clips:
        wav = render_preview(audio, easy, bpm, (1, easy.last_bar()), d / "clips" / f"{out.stem}_full.wav")
        print(f"→ 미리듣기 {rel(wav)}")
    print(f"→ 초안 {rel(out)}\n→ 리포트 {rel(d / 'report_Easy.md')}\n→ 근거 {rel(d / 'gen_log_Easy.json')}")
    if cfg.chart_path("Easy").exists():
        print(f"   주의: {rel(cfg.chart_path('Easy'))} 가 이미 있음 — 설치하려면 install {cfg.id} --diff Easy --force (백업함)")
    print(f"   Assets/Charts 에는 아직 쓰지 않았음. ChartEditor 로 고칠 거면 install {cfg.id} --diff Easy 후 에디터에서 불러와 편집")
    return 0


def _gen_normal(args, cfg, so, audio, bpm) -> int:
    """설치된(사용자가 고친) 이지를 바탕으로 노멀 초안 → out/000N/gen/Chart_000N_Normal.txt."""
    from .audio import features_for
    from .chart_io import to_bytes
    from .listen import render_preview
    from .normal import NormalParams, generate_normal
    from .paths import out_dir
    from .report import log_json, normal_report_markdown
    from .validate import validate
    e_path = cfg.chart_path("Easy")
    if not e_path.exists():
        print(f"오류: {rel(e_path)} 없음 — 먼저 gen {cfg.id} 와 install {cfg.id} --diff Easy (노멀은 설치된 이지를 바탕으로 만든다)", file=sys.stderr)
        return 2
    easy, raw = read_chart(e_path)
    bad = [i for i in validate(easy, raw, bpm=bpm) if i.severity in ("FATAL", "ERROR") and i.code not in ("E13", "E14")]
    if bad:
        print(f"오류: {e_path.name} 에 FATAL/ERROR {len(bad)}건 — 먼저 고칠 것 ({', '.join(i.code for i in bad[:5])})", file=sys.stderr)
        return 1
    feat = features_for(audio)
    try:
        offset = _offset_for(cfg, feat, easy, bpm)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    params = NormalParams(target_per_bar=args.per_bar) if args.per_bar is not None else NormalParams()
    from .sync import RuleError
    try:
        res = generate_normal(easy, feat, bpm, offset, cfg.id, params,
                              level=args.level if args.level is not None else cfg.levels.get("Normal"), cfg=cfg)
    except RuleError as e:
        print(f"오류: songs/{cfg.id:04d}.yaml 복사 규칙 — {e}", file=sys.stderr)
        return 2
    normal = res.chart
    issues = [i for i in validate(normal, bpm=bpm) if i.severity in ("FATAL", "ERROR", "WARN")]
    d = out_dir(cfg.id) / "gen"
    d.mkdir(parents=True, exist_ok=True)
    out = d / cfg.chart_path("Normal").name
    out.write_bytes(to_bytes(normal, fill_gaps=True))
    (d / "gen_log_Normal.json").write_text(log_json(res), encoding="utf-8")
    md = normal_report_markdown(f"{cfg.id}번 {cfg.title}", easy, res, bpm, params, base_note=f"기반 이지: `{rel(e_path)}`.")
    (d / "report_Normal.md").write_text(md, encoding="utf-8")
    print(md.split("## 마디별 변경")[0].rstrip())
    if issues:
        print("\n검사: " + ", ".join(f"{i.code} {i.where()} {i.message}" for i in issues[:10]))
    if not args.no_clips:
        wav = render_preview(audio, normal, bpm, (1, normal.last_bar()), d / "clips" / f"{out.stem}_full.wav")
        print(f"→ 미리듣기 {rel(wav)}")
    print(f"→ 초안 {rel(out)}\n→ 리포트 {rel(d / 'report_Normal.md')}\n→ 근거 {rel(d / 'gen_log_Normal.json')}")
    if cfg.chart_path("Normal").exists():
        print(f"   주의: {rel(cfg.chart_path('Normal'))} 가 이미 있음 — 설치하려면 install {cfg.id} --diff Normal --force (백업함)")
    print(f"   Assets/Charts 에는 아직 쓰지 않았음. 검토 후 install {cfg.id} --diff Normal, 그 뒤 하드는 port {cfg.id}")
    return 0


def _random_prf(pool, n: int, ref: set, tag: str, draws: int = 50) -> dict:
    """pool 에서 n 개를 무작위로 고른 결과의 평균 P/R/F1 (키 해시로 결정적, draws 번 평균)."""
    from .easy import _h
    pool = sorted(pool)
    ps, rs, fs = [], [], []
    for k in range(draws):
        pick = set(sorted(pool, key=lambda t: _h(tag, k, t))[:n])
        tp = len(pick & ref)
        ps.append(tp / max(1, len(pick)))
        rs.append(tp / max(1, len(ref)))
        fs.append(2 * tp / max(1, len(pick) + len(ref)))
    return {"n": min(n, len(pool)), "precision": round(sum(ps) / draws, 3), "recall": round(sum(rs) / draws, 3), "f1": round(sum(fs) / draws, 3)}


def _evalgen_normal(sid: int) -> dict | None:
    """B1: 실제 이지 → 노멀을 실제 노멀과 비교. B2: 음원 기반 이지 → 노멀을 실제 노멀과 비교 (이지를 사람이 고치지 않은 경우)."""
    from .align import quarter_chart
    from .audio import estimate_offset, features_for
    from .easy import _h, generate_easy
    from .normal import NormalParams, candidates, generate_normal
    from .sync import rules_from_config
    from .thin import onset_times
    e_p, n_p = charts_dir() / f"Chart_{sid:04d}_Easy.txt", charts_dir() / f"Chart_{sid:04d}_Normal.txt"
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return None
    if not (e_p.exists() and n_p.exists()):
        print(f"오류: 곡 {sid:04d} — 이지·노멀 채보가 모두 있어야 함", file=sys.stderr)
        return None
    easy, _ = read_chart(e_p)
    real, _ = read_chart(n_p)
    last = real.last_bar()
    feat = features_for(audio)
    offset = estimate_offset(feat, quarter_chart(last), bpm).offset
    res = generate_normal(easy, feat, bpm, offset, sid)
    gen, ref, e_t = onset_times(res.chart), onset_times(real), onset_times(easy)
    add_g, add_r = gen - e_t, ref - e_t
    pool = {c.t for c in candidates(easy, NormalParams())}
    copies = {t for r in rules_from_config(cfg) for t in r.targets}
    nc = lambda S: {t for t in S if int(t) not in copies}

    def prf(a, b):
        tp = len(a & b)
        return {"n": len(a), "precision": round(tp / max(1, len(a)), 3), "recall": round(tp / max(1, len(b)), 3),
                "f1": round(2 * tp / max(1, len(a) + len(b)), 3)}
    hits = sum(1 for b, bar in res.chart.bars.items() for q, l, c in bar.notes()
               if c in "12" and b + q in add_g and real.bars.get(b) is not None and real.bars[b].cells[l].get(q) in ("1", "2"))
    out = {"b1_all": prf(gen, ref), "b1_added": prf(add_g, add_r), "random_added": _random_prf(pool, len(add_g), add_r, f"n{sid}"),
           "noncopy_added": prf(nc(add_g), nc(add_r)), "noncopy_random": _random_prf(nc(pool), len(nc(add_g)), nc(add_r), f"nc{sid}"),
           "easy_kept": round(len(e_t & gen) / max(1, len(e_t)), 3),
           "lane_match_of_added_hits": round(hits / max(1, len(add_g & add_r)), 3),
           "per_bar": {"generated": round(len(gen) / last, 2), "real": round(len(ref) / last, 2)}}
    eg = generate_easy(feat, bpm, offset, last, sid).chart
    out["b2_all"] = prf(onset_times(generate_normal(eg, feat, bpm, offset, sid).chart), ref)
    return out


def _auc(pos: list[float], neg: list[float]) -> float | None:
    if not pos or not neg:
        return None
    import numpy as np
    allv = np.array(pos + neg)
    ranks = allv.argsort().argsort() + 1.0
    # 같은 값은 평균 순위
    from collections import defaultdict
    by = defaultdict(list)
    for v, r in zip(allv, ranks):
        by[v].append(r)
    avg = {v: sum(rs) / len(rs) for v, rs in by.items()}
    rp = sum(avg[v] for v in pos)
    return (rp - len(pos) * (len(pos) + 1) / 2) / (len(pos) * len(neg))


def _evalgen_rhythm(sid: int) -> dict | None:
    """리듬 감지기를 사람·팀원·생성 채보로 다시 잼: 당김음 칸 AUC, 16분 마디 AUC, 셋잇단 마디."""
    from .align import quarter_chart
    from .audio import estimate_offset, features_for
    from .paths import out_dir
    from .rhythm import antic_score, make_ctx, odd16_peaks, triplet_bars
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return None
    charts = {d: read_chart(charts_dir() / f"Chart_{sid:04d}_{d}.txt")[0] for d in ("Easy", "Normal", "Hard")
              if (charts_dir() / f"Chart_{sid:04d}_{d}.txt").exists()}
    if not charts:
        print(f"오류: 곡 {sid:04d} 채보 없음", file=sys.stderr)
        return None
    last = max(c.last_bar() for c in charts.values())
    feat = features_for(audio)
    off = estimate_offset(feat, quarter_chart(last), bpm).offset
    ctx = make_ctx(feat, bpm, off, last)
    a_score = {(b, k): antic_score(ctx, b, Fraction(k, 8)) for b in range(1, last + 1) for k in (1, 3, 5, 7)}
    p16 = {b: max([p for _q, p in odd16_peaks(ctx, b)], default=0.0) for b in range(1, last + 1)}
    out = {"triplet_bars": sorted(triplet_bars(ctx, range(1, last + 1)))}
    print(f"\n######## 곡 {sid:04d}: 리듬 감지기 ↔ 채보 (AUC, 0.5 = 무작위)")
    for d, c in charts.items():
        on = {b + q for b, bar in c.bars.items() for q, _l, ch in bar.notes() if ch in "12"}
        lab = {(b, k): (b + Fraction(k, 8) in on and b + Fraction(k + 1, 8) not in on) for (b, k) in a_score}
        auc_a = _auc([a_score[x] for x in lab if lab[x]], [a_score[x] for x in lab if not lab[x]])
        from .timing import is_odd_sixteenth
        b16 = {b: any(is_odd_sixteenth(q) for q, _l, ch in c.bars[b].notes() if ch in "12") for b in p16 if b in c.bars}
        auc_16 = _auc([p16[b] for b in b16 if b16[b]], [p16[b] for b in b16 if not b16[b]])
        n_a, n_16 = sum(lab.values()), sum(b16.values())
        out[d] = {"antic_auc": auc_a, "antic_n": n_a, "odd16_bar_auc": auc_16, "odd16_bars": n_16}
        fmt = lambda v: "—" if v is None else f"{v:.2f}"
        print(f"   {d}: 당김음 {n_a}칸 AUC {fmt(auc_a)} | 16분 엇박 마디 {n_16} AUC {fmt(auc_16)}")
    print(f"   셋잇단으로 들리는 마디: {', '.join(f'{b:03d}' for b in out['triplet_bars']) or '없음'}")
    d = out_dir(sid)
    d.mkdir(parents=True, exist_ok=True)
    (d / "evalgen_rhythm.json").write_text(json.dumps(out, ensure_ascii=False, indent=1, default=str), encoding="utf-8")
    return out


def _evalgen_chain(sid: int) -> dict | None:
    """전체 연결: 음원→이지→노멀→하드를 실제 하드(사용자)와 비교. 단계별로 실제 채보로 바꿔 넣어 차이가 어디서 생기는지 본다."""
    from .align import quarter_chart
    from .audio import estimate_offset, features_for
    from .easy import generate_easy
    from .normal import generate_normal
    from .port import Porter, PortParams
    from .stats import PROFILE_PATH, load_profile
    from .thin import onset_times
    from .uturn import find_uturns
    from .validate import validate
    paths_ = {d: charts_dir() / f"Chart_{sid:04d}_{d}.txt" for d in ("Easy", "Normal", "Hard")}
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return None
    if not all(p_.exists() for p_ in paths_.values()):
        print(f"오류: 곡 {sid:04d} — 이지·노멀·하드가 모두 있어야 함", file=sys.stderr)
        return None
    real = {d: read_chart(p_)[0] for d, p_ in paths_.items()}
    last = real["Normal"].last_bar()
    feat = features_for(audio)
    offset = estimate_offset(feat, quarter_chart(last), bpm).offset
    params = PortParams.from_profile(load_profile()) if PROFILE_PATH.exists() else PortParams()
    hard_of = lambda n: Porter(n, feat, bpm, offset, cfg, params).run().chart
    e_gen = generate_easy(feat, bpm, offset, last, sid, cfg=cfg).chart
    n_gen = generate_normal(e_gen, feat, bpm, offset, sid, cfg=cfg).chart
    n_from_real_e = generate_normal(real["Easy"], feat, bpm, offset, sid, cfg=cfg).chart
    chains = {"audio": (e_gen, n_gen, hard_of(n_gen)), "real_easy": (real["Easy"], n_from_real_e, hard_of(n_from_real_e)),
              "real_normal": (real["Easy"], real["Normal"], hard_of(real["Normal"]))}
    ref = {d: onset_times(c) for d, c in real.items()}

    def f1(a, b):
        tp = len(a & b)
        return round(2 * tp / max(1, len(a) + len(b)), 3)
    gim = lambda c: sum(1 for b, bar in c.bars.items() if b > 0 and len(bar.groups_used()) > 1)
    out = {}
    for k, (e, n, h) in chains.items():
        out[k] = {"easy_f1": f1(onset_times(e), ref["Easy"]), "normal_f1": f1(onset_times(n), ref["Normal"]),
                  "hard_f1": f1(onset_times(h), ref["Hard"]), "hard_per_bar": round(len(onset_times(h)) / last, 2),
                  "gimmick_bars": gim(h), "uturn_front": sum(1 for u in find_uturns(h) if u.front),
                  "errors": sum(1 for i in validate(h, bpm=bpm) if i.severity in ("FATAL", "ERROR"))}
    out["real"] = {"hard_per_bar": round(len(ref["Hard"]) / last, 2), "gimmick_bars": gim(real["Hard"]),
                   "uturn_front": sum(1 for u in find_uturns(real["Hard"]) if u.front)}
    return out


def _evalgen_easy(sid: int) -> dict | None:
    """음원 기반 이지를 블라인드로 만들어(노트 위치는 채보를 보지 않음) 실제 이지와 비교."""
    from .align import quarter_chart
    from .audio import estimate_offset, features_for
    from .chart_io import to_bytes
    from .easy import EasyParams, _h, alternation, generate_easy, make_ctx, rhythm_patterns
    from .paths import out_dir
    from .thin import onset_times
    e_p = charts_dir() / f"Chart_{sid:04d}_Easy.txt"
    try:
        cfg, so, audio, bpm = _song_audio(sid)
    except UsageError as e:
        print(f"오류: {e}", file=sys.stderr)
        return None
    if not e_p.exists():
        print(f"오류: {e_p.name} 없음", file=sys.stderr)
        return None
    real, _ = read_chart(e_p)
    last = real.last_bar()
    feat = features_for(audio)
    offset = estimate_offset(feat, quarter_chart(last), bpm).offset          # 채보를 보지 않는 오프셋 (align 과 같음)
    params = EasyParams()
    res = generate_easy(feat, bpm, offset, last, sid, params)
    gen, ref = onset_times(res.chart), onset_times(real)
    ctx = make_ctx(feat, bpm, offset, last)
    ok_beats = {b + Fraction(k, 4) for b in range(1, last + 1) for k in range(4)
                if ctx.rms(b, Fraction(k, 4)) >= ctx.rms_med - params.sound_db
                and max(ctx.p(e, b, Fraction(k, 4)) for e in ("mid", "low", "high", "harm_mid")) >= params.sound_pct}
    one_three = {t for t in ok_beats if (t - int(t)) in (0, Fraction(1, 2))}

    def prf(a):
        tp = len(a & ref)
        return {"n": len(a), "precision": round(tp / max(1, len(a)), 3), "recall": round(tp / max(1, len(ref)), 3),
                "f1": round(2 * tp / max(1, len(a) + len(ref)), 3)}
    holds = lambda c: sum(1 for bar in c.bars.values() for _p, _l, ch in bar.notes() if ch == "2")
    hits = sum(1 for b, bar in res.chart.bars.items() for q, l, c in bar.notes()
               if c in "12" and real.bars.get(b) is not None and real.bars[b].cells[l].get(q) in ("1", "2"))
    sw, top = alternation(res.chart)
    rsw, rtop = alternation(real)
    d = out_dir(sid) / "gen"
    d.mkdir(parents=True, exist_ok=True)
    (d / "evalgen_Easy_audio.txt").write_bytes(to_bytes(res.chart, fill_gaps=True))
    return {"gen": prf(gen), "all_beats": prf(ok_beats), "beats_1_3": prf(one_three), "random_same_n": _random_prf(ok_beats, len(gen), ref, f"e{sid}"),
            "per_bar": {"generated": round(len(gen) / last, 2), "real": round(len(ref) / last, 2)},
            "hold_share": {"generated": round(holds(res.chart) / max(1, len(gen)), 3), "real": round(holds(real) / max(1, len(ref)), 3)},
            "alternation": {"generated": round(sw, 3), "real": round(rsw, 3)}, "top_share": {"generated": round(top, 3), "real": round(rtop, 3)},
            "patterns": {"generated": len(rhythm_patterns(res.chart)), "real": len(rhythm_patterns(real))},
            "lane_match_of_hits": round(hits / max(1, len(gen & ref)), 3), "offset_ms": round(offset * 1000, 1)}


def cmd_evalgen(args) -> int:
    """생성기 평가. thin: 노멀을 덜어내 실제 이지와 비교 (노멀·이지가 모두 있는 곡)."""
    from .paths import out_dir
    from .thin import ThinParams, k8_of, onset_times, thin
    rc = 0
    if args.stage == "rhythm":
        for t in args.songs:
            if _evalgen_rhythm(int(t)) is None:
                rc = 2
        return rc
    if args.stage == "chain":
        from .paths import out_dir
        for t in args.songs:
            sid = int(t)
            s = _evalgen_chain(sid)
            if s is None:
                rc = 2
                continue
            print(f"\n######## 곡 {sid:04d}: 전체 연결 ↔ 실제 채보 (노트 시작 F1, 복사 규칙은 songs 설정)")
            print("   | 시작 | 이지 F1 | 노멀 F1 | 하드 F1 | 하드 마디당 | 기믹 마디 | 유턴 앞쪽 | 오류 |")
            for k, label in (("audio", "음원부터"), ("real_easy", "실제 이지부터"), ("real_normal", "실제 노멀부터 (= port)")):
                v = s[k]
                print(f"   | {label} | {v['easy_f1']:.2f} | {v['normal_f1']:.2f} | {v['hard_f1']:.2f} | {v['hard_per_bar']} | {v['gimmick_bars']} | {v['uturn_front']} | {v['errors']} |")
            r = s["real"]
            print(f"   | 실제 하드 | | | | {r['hard_per_bar']} | {r['gimmick_bars']} | {r['uturn_front']} | |")
            (out_dir(sid) / "evalgen_chain.json").write_text(json.dumps(s, ensure_ascii=False, indent=1), encoding="utf-8")
        return rc
    if args.stage == "normal":
        from .paths import out_dir
        for t in args.songs:
            sid = int(t)
            s = _evalgen_normal(sid)
            if s is None:
                rc = 2
                continue
            who = {1: " — 이지는 thin 으로 만든 것, 노멀은 사용자", 2: " — 사용자 이지·노멀 (가중치는 이 곡에서 잼)"}.get(sid, " — 팀원 이지·노멀 (스타일이 달라 참고용)")
            print(f"\n######## 곡 {sid:04d}: 이지 기반 노멀 ↔ 실제 노멀{who}")
            for k, label in (("b1_all", "B1 실제 이지 → 노멀, 전체 노트"), ("b1_added", "B1 추가 노트"), ("random_added", "기준선: 후보 칸 중 같은 수 무작위 (50번 평균)"),
                             ("noncopy_added", "복사 대상이 아닌 마디의 추가 노트"), ("noncopy_random", "  그 기준선 (무작위)"),
                             ("b2_all", "B2 음원 기반 이지 → 노멀, 전체 노트")):
                v = s[k]
                print(f"   {label}: {v['n']}개 — P {v['precision']:.2f} R {v['recall']:.2f} F1 {v['f1']:.2f}")
            print(f"   이지 유지 {s['easy_kept']:.2f}, 추가 노트가 맞은 칸 중 레인까지 같음 {s['lane_match_of_added_hits']:.2f}, "
                  f"마디당 생성 {s['per_bar']['generated']} / 실제 {s['per_bar']['real']}")
            if sid == 2:
                acc = [("추가 노트 F1 ≥ 0.60", s["b1_added"]["f1"] >= 0.60),
                       ("복사 아닌 마디에서 무작위보다 +0.05", s["noncopy_added"]["f1"] >= s["noncopy_random"]["f1"] + 0.05),
                       ("이지 유지 ≥ 0.95", s["easy_kept"] >= 0.95)]
                print("   합격 기준: " + ", ".join(f"{n} {'통과' if ok else '미달'}" for n, ok in acc))
                s["acceptance"] = {n: ok for n, ok in acc}
            (out_dir(sid) / "evalgen_normal.json").write_text(json.dumps(s, ensure_ascii=False, indent=1), encoding="utf-8")
        return rc
    if args.stage == "easy":
        from .paths import out_dir
        for t in args.songs:
            sid = int(t)
            s = _evalgen_easy(sid)
            if s is None:
                rc = 2
                continue
            note = " — 비교 대상은 thin 으로 만든(노멀 덜어내기) 이지" if sid == 1 else (" — 사용자 이지 (규칙의 분포 값은 이 곡에서 잼)" if sid == 2 else " — 팀원 이지 (스타일이 달라 참고용)")
            print(f"\n######## 곡 {sid:04d}: 음원 기반 이지 ↔ 실제 이지 (노트 위치는 블라인드){note}")
            for k, label in (("gen", "생성"), ("all_beats", "기준선: 소리 있는 네 박 모두"), ("beats_1_3", "기준선: 1·3박"), ("random_same_n", "기준선: 같은 수 무작위 박 (50번 평균)")):
                v = s[k]
                print(f"   {label}: {v['n']}개 — P {v['precision']:.2f} R {v['recall']:.2f} F1 {v['f1']:.2f}")
            print(f"   마디당 {s['per_bar']['generated']} / {s['per_bar']['real']}, 홀드 {s['hold_share']['generated']:.0%} / {s['hold_share']['real']:.0%}, "
                  f"위아래 바꿈 {s['alternation']['generated']:.0%} / {s['alternation']['real']:.0%}, 위 레인 {s['top_share']['generated']:.0%} / {s['top_share']['real']:.0%}, "
                  f"리듬 패턴 {s['patterns']['generated']} / {s['patterns']['real']}, 맞힌 칸 중 레인까지 같음 {s['lane_match_of_hits']:.2f}  (생성 / 실제)")
            if sid == 2:
                acc = [("F1 ≥ 0.74", s["gen"]["f1"] >= 0.74), ("마디당 3.0–3.6", 3.0 <= s["per_bar"]["generated"] <= 3.6),
                       ("홀드 17–27%", 0.17 <= s["hold_share"]["generated"] <= 0.27), ("위아래 바꿈 56–76%", 0.56 <= s["alternation"]["generated"] <= 0.76),
                       ("위 레인 40–60%", 0.40 <= s["top_share"]["generated"] <= 0.60),
                       ("리듬 패턴 ≥ 실제의 50%", s["patterns"]["generated"] >= 0.5 * s["patterns"]["real"])]
                print("   합격 기준: " + ", ".join(f"{n} {'통과' if ok else '미달'}" for n, ok in acc))
                s["acceptance"] = {n: ok for n, ok in acc}
            (out_dir(sid) / "evalgen_easy.json").write_text(json.dumps(s, ensure_ascii=False, indent=1), encoding="utf-8")
        return rc
    for t in args.songs:
        sid = int(t)
        so = music_so_for(sid)
        n_p, e_p = charts_dir() / f"Chart_{sid:04d}_Normal.txt", charts_dir() / f"Chart_{sid:04d}_Easy.txt"
        if so is None or not (n_p.exists() and e_p.exists()):
            print(f"오류: 곡 {sid:04d} — MusicSO 나 노멀·이지 채보가 없음", file=sys.stderr)
            rc = 2
            continue
        normal, _ = read_chart(n_p)
        real, _ = read_chart(e_p)
        res = thin(normal, so.bpm, ThinParams())
        gen, ref, nt = onset_times(res.chart), onset_times(real), onset_times(normal)
        beats = {x for x in nt if k8_of(x - int(x)) is not None and k8_of(x - int(x)) % 2 == 0}

        def prf(a):
            tp = len(a & ref)
            return {"n": len(a), "precision": round(tp / max(1, len(a)), 3), "recall": round(tp / max(1, len(ref)), 3),
                    "f1": round(2 * tp / max(1, len(a) + len(ref)), 3)}
        same_lane = sum(1 for b, bar in res.chart.bars.items() for p, l, c in bar.notes()
                        if c in "12" and real.bars.get(b) is not None and real.bars[b].cells[l].get(p) in ("1", "2"))
        bars = max(1, sum(1 for b in normal.bars if b > 0 and normal.bars[b].has_notes()))
        holds = lambda c: sum(1 for bar in c.bars.values() for _p, _l, ch in bar.notes() if ch == "2")
        s = {"thin": prf(gen), "beats_only": prf(beats), "normal_as_is": prf(nt),
             "lane_match_of_hits": round(same_lane / max(1, len(gen & ref)), 3),
             "per_bar": {"generated": round(len(gen) / bars, 2), "real": round(len(ref) / bars, 2)},
             "hold_share": {"generated": round(holds(res.chart) / max(1, len(gen)), 3), "real": round(holds(real) / max(1, len(ref)), 3)}}
        print(f"\n######## 곡 {sid:04d}: 노멀 덜어내기 ↔ 실제 이지 (노트 시작 위치)")
        for k, label in (("thin", "덜어내기"), ("beats_only", "기준선: 노멀의 정박만"), ("normal_as_is", "기준선: 노멀 그대로")):
            v = s[k]
            print(f"   {label}: {v['n']}개 — P {v['precision']:.2f} R {v['recall']:.2f} F1 {v['f1']:.2f}")
        print(f"   맞힌 칸 중 레인까지 같음 {s['lane_match_of_hits']:.2f}, 마디당 생성 {s['per_bar']['generated']} / 실제 {s['per_bar']['real']}, "
              f"홀드 비율 생성 {s['hold_share']['generated']:.0%} / 실제 {s['hold_share']['real']:.0%}")
        d = out_dir(sid)
        d.mkdir(parents=True, exist_ok=True)
        (d / "evalgen_thin.json").write_text(json.dumps(s, ensure_ascii=False, indent=1), encoding="utf-8")
    return rc


def cmd_uturns(args) -> int:
    """유턴 앞쪽 노트 목록 → out/000N/uturns_<채보>.md (직접 찍은 채보 확인용)."""
    from .listen import fmt_time
    from .paths import out_dir
    from .timing import audio_time
    from .uturn import UTURN_FRONT, find_uturns, lead_seconds
    try:
        cfg = load_song(int(args.song))
    except ConfigError as e:
        print(f"오류: {e}", file=sys.stderr)
        return 2
    path = charts_dir() / f"Chart_{cfg.id:04d}_{args.chart}.txt"
    if not path.exists():
        print(f"오류: {path.name} 없음", file=sys.stderr)
        return 2
    so = music_so_for(cfg.id)
    bpm = so.bpm if so else None
    chart, _ = read_chart(path)
    bpm = bpm or chart.header.bpm
    us = find_uturns(chart)
    bad = [u for u in us if u.front]
    lines = [f"# {path.name} 유턴 앞쪽 노트 목록", "",
             f"유턴 {len(us)}곳 (A 기믹 마디 {sum(u.kind == 'A' for u in us)}, B 다음 마디 {sum(u.kind == 'B' for u in us)}) 중 "
             f"앞쪽(0 < p ≤ {UTURN_FRONT})에 노트가 있는 곳 {len(bad)}.",
             "유턴 마디의 위치 p 노트는 나타나서 판정까지 약 2p 마디만 보인다 (p = 0 은 끝점이라 괜찮음).",
             "A = 직전 그룹을 방향 뒤집어 넣은 마디, B = 앞 마디가 방향을 뒤집어 메인 라인이 돌아오는 마디.", "",
             "| 마디 | 종류 | 그룹 | 앞 마디 → 이번 | 앞쪽 노트 (보이는 시간) | 음원 시각 |", "|---|---|---|---|---|---|"]
    for u in bad:
        d = lambda x: "LTR" if x == 0 else "RTL"
        notes = ", ".join(f"레인{l} {q} ({lead_seconds(q, bpm):.2f}초)" for l, q, _c in u.front)
        tag = " (손작업)" if u.bar in cfg.frozen_bars else ""
        lines.append(f"| {u.bar:03d}{tag} | {u.kind} | {u.group} | {d(u.prev_dir)} → {d(u.dir)} | {notes} | {fmt_time(audio_time(u.bar, 0, bpm))} |")
    out = out_dir(cfg.id) / f"uturns_{path.stem}.md"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{path.name}: 유턴 {len(us)}곳, 앞쪽 노트 있음 {len(bad)} (A {sum(u.kind == 'A' for u in bad)}, B {sum(u.kind == 'B' for u in bad)})")
    print(f"   → {rel(out)}")
    return 0


def cmd_eval(args) -> int:
    from .evaluate import random_fill, score
    from .paths import out_dir
    rc = 0
    for t in args.songs:
        try:
            cfg, so, audio, bpm = _song_audio(int(t))
        except (UsageError, ValueError) as e:
            print(f"오류: {e}", file=sys.stderr)
            rc = 2
            continue
        if not cfg.target_path().exists():
            print(f"오류: {rel(cfg.target_path())} 없음 (사용자 하드가 있는 곡만 평가할 수 있음)", file=sys.stderr)
            rc = 2
            continue
        normal, _ = read_chart(cfg.base_path())
        user, _ = read_chart(cfg.target_path())
        porter = _make_porter(cfg, so, audio, bpm, normal, use_rules=not args.no_rules)
        if args.oracle_budget:
            from .evaluate import added as _added
            per: dict[int, int] = {}
            for (b, _p) in _added(normal, user):
                per[b] = per.get(b, 0) + 1
            porter.budget_override = per
        res = porter.run()
        s = score(normal, res.chart, user)
        rnd = score(normal, random_fill(normal, res.chart, porter.percentile), user)
        base = score(normal, normal.copy(), user)
        origins = Counter(e.origin for e in res.log)
        print(f"\n######## 곡 {cfg.id:04d} {cfg.title}: 포팅 결과 ↔ 사용자 하드 (16분 홀수 칸 제외)")
        print(f"   생성 출처: " + ", ".join(f"{k} {v}" for k, v in sorted(origins.items())))
        o = s["onsets"]
        print(f"   노트 시작: 노멀 {o['normal']} → 생성 {o['generated']} / 사용자 {o['user']}")
        a, ra = s["added"], rnd["added"]
        print(f"   추가 노트: 생성 {a['generated']} / 사용자 {a['user']} — P {a['precision']:.2f} R {a['recall']:.2f} F1 {a['f1']:.2f}"
              f" (무작위 {ra['generated']}개 P {ra['precision']:.2f} R {ra['recall']:.2f}), 맞힌 칸 중 레인까지 같음 {a['lane_match_of_hits']:.2f}")
        g = s["gimmick"]
        print(f"   기믹 마디: 생성 {g['generated']} / 사용자 {g['user']} — P {g['precision']:.2f} R {g['recall']:.2f} F1 {g['f1']:.2f}, 맞힌 마디 중 형태 같음 {g['same_shape_of_hits']:.2f}")
        print(f"   8마디 창 밀도 오차: {s['density_window_mae']:.3f} (노멀 그대로 {base['density_window_mae']:.3f})")
        h = s["hold_close_78"]
        print(f"   7/8 홀드 끝: 생성 {h['generated']} / 사용자 {h['user']}, 일치 {h['agree']}")
        uf = s["uturn_front"]
        print(f"   유턴 앞쪽 노트가 있는 유턴: 생성 {uf['generated']} / 사용자 {uf['user']}")
        d = out_dir(cfg.id)
        d.mkdir(parents=True, exist_ok=True)
        (d / "eval.json").write_text(json.dumps({"port": s, "random": rnd, "normal": base}, ensure_ascii=False, indent=1), encoding="utf-8")
    return rc


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(prog="chartporter", description="노멀→하드 채보 포팅 도구")
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check", help="채보·곡 데이터 검사 (읽기 전용)")
    c.add_argument("targets", nargs="*", help="곡 번호(예: 4) 또는 채보 파일 경로/이름. 비우면 전체")
    c.add_argument("--all", action="store_true", help="모든 곡 검사")
    c.add_argument("-v", "--verbose", action="store_true", help="이슈를 전부 출력")
    c.add_argument("--json", metavar="PATH", help="이슈 목록을 JSON 으로 저장 (file 필드로 출처 구분)")
    c.set_defaults(func=cmd_check)
    f = sub.add_parser("fmt", help="기계적으로 고칠 수 있는 문제를 고친 수정안 만들기 (기본은 out/ 에만 씀)")
    f.add_argument("targets", nargs="+", help="곡 번호 또는 채보 파일 경로/이름")
    f.add_argument("--no-fill-gaps", action="store_true", help="빠진 마디를 채우지 않음")
    f.add_argument("--no-fix-header", action="store_true", help="#DIFFICULTY 를 파일 이름에 맞추지 않음")
    f.add_argument("--orphan-end", choices=("keep", "tap", "drop"), default="keep", help="짝 없는 '4' 처리 (기본 keep)")
    f.add_argument("--close-holds", action="store_true", help="끝 없는 홀드·마디를 넘는 홀드를 7/8 에서 닫음 (7/8 이후 시작은 탭)")
    f.add_argument("--apply", action="store_true", help="백업 후 Assets/Charts 에 바로 적용")
    f.set_defaults(func=cmd_fmt)
    a = sub.add_parser("analyze", help="음원 특징 계산(캐시) + 분석 오프셋 추정")
    a.add_argument("songs", nargs="+", help="곡 번호")
    a.add_argument("--no-cache", action="store_true", help="캐시를 무시하고 다시 계산")
    a.set_defaults(func=cmd_analyze)
    k = sub.add_parser("checklist", help="8분 그리드 밖 노트 확인 목록 + 클릭음 미리듣기")
    k.add_argument("song", help="곡 번호")
    k.add_argument("--chart", help="Easy/Normal/Hard (기본: 기준 곡은 Hard, 대상 곡은 Normal)")
    k.add_argument("--no-clips", action="store_true", help="미리듣기 WAV 를 만들지 않음")
    k.set_defaults(func=cmd_checklist)
    p = sub.add_parser("preview", help="원곡 구간 + 노트 클릭음 WAV")
    p.add_argument("song", help="곡 번호")
    p.add_argument("--bars", required=True, help="예: 50-53")
    p.add_argument("--chart", default="Hard", help="Easy/Normal/Hard (기본 Hard)")
    p.add_argument("--offset-ms", type=float, default=0.0, help="클릭 위치 보정 (게임 기준이면 0)")
    p.set_defaults(func=cmd_preview)
    s = sub.add_parser("stats", help="기준 곡 노멀/하드 쌍에서 하드 기조 통계 뽑기 → profiles/style_profile.json")
    s.add_argument("songs", nargs="*", help="곡 번호 (기본: songs/*.yaml 의 role: reference)")
    s.add_argument("--primary", help="주 기준 곡 (기본: 첫 곡)")
    s.set_defaults(func=cmd_stats)
    sc = sub.add_parser("sections", help="음원으로 반복 구간 맵·복사 규칙 제안 → out/000N/sections.md, sections.proposed.yaml")
    sc.add_argument("song", help="곡 번호")
    sc.set_defaults(func=cmd_sections)
    sy = sub.add_parser("sync", help="songs 설정의 1·2절 복사 규칙을 채보에 다시 적용 (기본은 미리보기)")
    sy.add_argument("song", help="곡 번호")
    sy.add_argument("--diff", choices=("Easy", "Normal", "Hard"), default="Hard", help="적용할 난이도 (기본 Hard)")
    sy.add_argument("--apply", action="store_true", help="백업 후 적용")
    sy.add_argument("--force", action="store_true", help="직접 고친 것으로 보이는 마디도 덮어씀")
    sy.set_defaults(func=cmd_sync)
    po = sub.add_parser("port", help="노멀 → 하드 초안 생성 (out/000N/port/ 에만 씀)")
    po.add_argument("song", help="곡 번호")
    po.add_argument("--level", type=int, help="초안 헤더 #LEVEL (게임은 MusicSO 레벨을 씀)")
    po.add_argument("--no-clips", action="store_true", help="리듬 변주 마디 미리듣기 WAV 를 만들지 않음")
    po.set_defaults(func=cmd_port)
    ins = sub.add_parser("install", help="out/000N/ 의 초안을 Assets/Charts 에 설치 (백업, 사용자 편집 보호)")
    ins.add_argument("song", help="곡 번호")
    ins.add_argument("--diff", choices=("Easy", "Normal", "Hard"), default="Hard", help="설치할 난이도 (기본 Hard = port 결과, Easy/Normal = gen 결과)")
    ins.add_argument("--force", action="store_true", help="지난 설치 뒤 바뀐 파일도 덮어씀 (백업은 함)")
    ins.set_defaults(func=cmd_install)
    al = sub.add_parser("align", help="신규 곡 음원 준비 검사: 정수 BPM·1마디 = 0초·첫 박 검증 → out/000N/align.md")
    al.add_argument("song", help="곡 번호 (MusicSO 가 없으면 songs 설정의 bpm·audio 사용)")
    al.add_argument("--no-clips", action="store_true", help="메트로놈 미리듣기 WAV 를 만들지 않음")
    al.set_defaults(func=cmd_align)
    th = sub.add_parser("thin", help="사용자 노멀에서 노트를 빼서 이지 초안 만들기 → out/000N/gen/")
    th.add_argument("song", help="곡 번호")
    th.add_argument("--level", type=int, help="초안 헤더 #LEVEL (기본: songs 설정 levels.Easy, 게임은 MusicSO 레벨을 씀)")
    th.add_argument("--min-gap", type=float, default=0.19, help="이웃 노트 최소 간격 초 (기본 0.19 = 2번 이지의 8분)")
    th.add_argument("--min-hold", type=float, default=0.19, help="이보다 짧은 홀드는 탭으로 (초, 기본 0.19 = 2번 이지의 가장 짧은 홀드)")
    th.add_argument("--per-bar", type=float, default=3.33, help="마디당 노트 상한 (기본 3.33 = 2번 이지)")
    th.add_argument("--no-clips", action="store_true", help="미리듣기 WAV 를 만들지 않음")
    th.set_defaults(func=cmd_thin)
    gn = sub.add_parser("gen", help="신규 곡 생성 (--diff Easy = 음원 기반 이지) → out/000N/gen/")
    gn.add_argument("song", help="곡 번호 (align, downbeat_ok, region.last_bar 가 먼저 필요)")
    gn.add_argument("--diff", choices=("Easy", "Normal"), default="Easy", help="만들 난이도")
    gn.add_argument("--level", type=int, help="초안 헤더 #LEVEL (기본: songs 설정 levels)")
    gn.add_argument("--per-bar", type=float, help="마디당 노트 목표 (기본: 이지 3.33 = 2번 이지, 노멀 4.9 = 1·2번 노멀)")
    gn.add_argument("--no-clips", action="store_true", help="미리듣기 WAV 를 만들지 않음")
    gn.set_defaults(func=cmd_gen)
    eg = sub.add_parser("evalgen", help="생성기 평가 (thin: 노멀을 덜어내 실제 이지와, easy: 음원 기반 이지를 실제 이지와 비교)")
    eg.add_argument("stage", choices=("thin", "easy", "normal", "chain", "rhythm"),
                    help="평가할 단계 (chain = 음원→이지→노멀→하드 전체, rhythm = 리듬 감지기 ↔ 채보)")
    eg.add_argument("songs", nargs="+", help="곡 번호 (노멀·이지가 모두 있는 곡)")
    eg.set_defaults(func=cmd_evalgen)
    ut = sub.add_parser("uturns", help="유턴 앞쪽 노트 목록 (out/000N/uturns_<채보>.md)")
    ut.add_argument("song", help="곡 번호")
    ut.add_argument("--chart", default="Hard", help="Easy/Normal/Hard (기본 Hard)")
    ut.set_defaults(func=cmd_uturns)
    ev = sub.add_parser("eval", help="기준 곡 노멀을 포팅해 사용자 하드와 비교 (기준선: 노멀 그대로, 무작위 채우기)")
    ev.add_argument("songs", nargs="+", help="곡 번호 (사용자 하드가 있는 곡)")
    ev.add_argument("--no-rules", action="store_true", help="songs 설정의 복사 규칙을 쓰지 않고 모든 마디를 따로 생성")
    ev.add_argument("--oracle-budget", action="store_true", help="마디별 추가 수를 사용자 하드와 같게 줘서 '어디에 넣는지'만 평가")
    ev.set_defaults(func=cmd_eval)
    args = ap.parse_args(argv)
    return args.func(args)
