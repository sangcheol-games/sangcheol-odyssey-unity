"""채보 텍스트 읽기/쓰기.

읽기는 게임 파서(Assets/Scripts/Game/ChartParser.cs)와 같은 규칙으로 줄을 분류하고,
쓰기는 에디터 저장(Assets/Scripts/ChartEditor/Data/EditorChartConverter.ToChartText)과
같은 형식을 만든다:
  헤더 6줄(TITLE, ARTIST, DIFFICULTY, LEVEL, BPM, NOTES) + 빈 줄,
  '#%03d:%d%d:%s;' 마디 오름차순 · 레인 1→4, 방향이 정해진 그룹은 두 레인 모두,
  마디당 시퀀스 길이 하나(4 이상), RTL 은 화면 순서(판정 순서의 역순), CRLF, BOM 없는 UTF-8.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from fractions import Fraction
from math import lcm
from pathlib import Path

from .model import (
    DIFFICULTIES, GROUPS, LANES, LTR, MAX_LENGTH, RTL, Bar, Chart, Header,
    expected_main, group_of, lanes_of,
)

_DATA_LIKE = re.compile(r"^#+\s*[+-]?\d+\s*:")   # '#마디:' 로 시작하면 데이터 줄로 의도된 것
_DOTNET_INT = re.compile(r"[ \t\n\v\f\r]*[+-]?[0-9]+[ \t\n\v\f\r]*")
_BOMS = (
    (b"\xef\xbb\xbf", "utf-8"), (b"\xff\xfe\x00\x00", "utf-32-le"), (b"\x00\x00\xfe\xff", "utf-32-be"),
    (b"\xff\xfe", "utf-16-le"), (b"\xfe\xff", "utf-16-be"),
)


def dotnet_int(text: str) -> int | None:
    """C# int.Parse / int.TryParse 가 받아들이는 형태만 정수로 인정 (ASCII 숫자, 앞뒤 공백, 부호, Int32 범위)."""
    if not _DOTNET_INT.fullmatch(text):
        return None
    v = int(text.strip())
    return v if -2**31 <= v < 2**31 else None


def decode_chart_bytes(data: bytes) -> tuple[str, str | None, bool]:
    """(텍스트, BOM 인코딩, 깨진 바이트 여부). File.ReadAllText 처럼 BOM 으로 인코딩을 고르고 없으면 UTF-8."""
    for bom, enc in _BOMS:
        if data.startswith(bom):
            return data[len(bom):].decode(enc, errors="replace"), enc, False
    try:
        return data.decode("utf-8"), None, False
    except UnicodeDecodeError:
        return data.decode("utf-8", errors="replace"), None, True


@dataclass
class RawLine:
    lineno: int        # 원문 기준 줄 번호 (1부터)
    text: str
    bar: int
    channel: int
    lane: int
    seq: str


@dataclass
class RawChart:
    """파일 원문과 줄 분류 결과. 깨진 파일도 검사할 수 있도록 의미 해석 전 단계로 둔다."""
    data: bytes
    bom: str | None                            # BOM 이 있으면 그 인코딩
    eol: str                                   # 가장 많이 쓰인 줄바꿈
    decode_errors: bool = False                # UTF-8 로 읽을 수 없는 바이트가 있었음
    header_lines: list[tuple[int, str, str]] = field(default_factory=list)   # (lineno, KEY, value)
    data_lines: list[RawLine] = field(default_factory=list)                 # 파일 순서 그대로
    ignored: list[tuple[int, str, str]] = field(default_factory=list)       # (lineno, text, reason)


def parse_raw(data: bytes) -> RawChart:
    text, bom, bad = decode_chart_bytes(data)
    crlf, lf = text.count("\r\n"), text.count("\n")
    eol = "\r\n" if crlf and crlf >= lf - crlf else "\n"
    raw = RawChart(data=data, bom=bom, eol=eol, decode_errors=bad)

    for lineno, line in enumerate(re.split(r"\r\n|\r|\n", text), start=1):
        if line == "":
            continue
        if not line.startswith("#"):
            raw.ignored.append((lineno, line, "'#'로 시작하지 않아 무시됨"))
            continue
        if ":" not in line or not line.endswith(";"):
            # ChartParser 는 이 줄을 헤더로 취급해 버린다 (';' 누락, ';' 뒤 공백·주석 등).
            if _DATA_LIKE.match(line):
                why = "';' 뒤에 다른 문자" if ";" in line else "';' 로 끝나지 않음"
                raw.ignored.append((lineno, line, f"{why} → 게임이 이 데이터 줄을 무시함"))
                continue
            content = line.lstrip("#").strip()
            sp = content.find(" ")
            if sp < 0:
                raw.ignored.append((lineno, line, "값 없는 헤더"))
                continue
            raw.header_lines.append((lineno, content[:sp].upper(), content[sp + 1:].strip()))
            continue
        content = line.lstrip("#").rstrip(";")
        parts = content.split(":")
        if len(parts) < 3:
            raw.ignored.append((lineno, line, "':' 구분 부족 → 게임이 이 데이터 줄을 무시함"))
            continue
        bar = dotnet_int(parts[0])
        if bar is None or len(parts[1]) < 2:
            raw.ignored.append((lineno, line, "마디/채널/레인 해석 실패 → 게임이 이 데이터 줄을 무시함"))
            continue
        channel = ord(parts[1][0]) - 48   # C#: channelLaneStr[0] - '0'
        lane = ord(parts[1][1]) - 48
        raw.data_lines.append(RawLine(lineno, line, bar, channel, lane, parts[2]))
    return raw


def read_raw(path: str | Path) -> RawChart:
    return parse_raw(Path(path).read_bytes())


def _parse_int(value: str, default: int) -> int:
    v = dotnet_int(value)
    return default if v is None else v


def parse_difficulty(value: str) -> str | None:
    """C# Enum.TryParse<Difficulty>(value, ignoreCase: true): 이름 또는 숫자, 쉼표로 이으면 OR."""
    total = 0
    for part in value.split(","):
        part = part.strip()
        num = dotnet_int(part)
        if num is not None:
            total |= num
            continue
        idx = [i for i, d in enumerate(DIFFICULTIES) if d.lower() == part.lower()]
        if not idx:
            return None
        total |= idx[0]
    return DIFFICULTIES[total] if 0 <= total < len(DIFFICULTIES) else str(total)


def header_from_raw(raw: RawChart) -> Header:
    """에디터 FromChartText 와 같은 해석 (잘못된 값은 에디터 기본값). #NOTES 는 게임처럼 파싱 성공 시에만 덮어씀."""
    h = Header()
    for _, key, value in raw.header_lines:
        if key == "TITLE":
            h.title = value
        elif key == "ARTIST":
            h.artist = value
        elif key == "DIFFICULTY":
            h.raw_difficulty = value
            parsed = parse_difficulty(value)
            if parsed is not None:
                h.difficulty = parsed
        elif key == "LEVEL":
            h.level = _parse_int(value, h.level)
        elif key == "BPM":
            h.bpm = _parse_int(value, h.bpm)
        elif key == "NOTES":
            v = dotnet_int(value)
            if v is not None:
                h.notes = v
    return h


def to_chart(raw: RawChart, path: str | None = None) -> Chart:
    """줄 단위 원문 → 마디 모델. 중복 줄·그룹 내 방향 혼합 같은 충돌은 검증기가 raw 로 따로 잡는다."""
    chart = Chart(header=header_from_raw(raw), path=path)
    lengths: dict[int, list[int]] = {}
    for rl in raw.data_lines:
        if rl.lane not in LANES:
            chart.unrepresentable.append(f"{rl.lineno}번째 줄 '{rl.text}': 레인 {rl.lane}")
            continue
        bar = chart.bars.get(rl.bar)
        if bar is None:
            bar = chart.bars[rl.bar] = Bar(number=rl.bar, length=4)
        if rl.seq:
            lengths.setdefault(rl.bar, []).append(len(rl.seq))
        direction = LTR if rl.channel == 0 else RTL   # ChartParser: channel == 0 만 LTR
        g = group_of(rl.lane)
        if bar.dirs[g] is None:
            bar.dirs[g] = direction
        judged = rl.seq if direction == LTR else rl.seq[::-1]
        n = len(judged)
        for i, c in enumerate(judged):
            if c != "0":
                bar.cells[rl.lane][Fraction(i, n)] = c
    for b, ls in lengths.items():
        n = 1
        for x in ls:
            n = lcm(n, x)
        chart.bars[b].length = n
    return chart


def read_chart(path: str | Path) -> tuple[Chart, RawChart]:
    raw = read_raw(path)
    return to_chart(raw, str(path)), raw


class ChartWriteError(ValueError):
    pass


def empty_bar(number: int) -> Bar:
    """노멀 교대 규칙대로 방향만 정한 빈 마디 (빠진 마디 채우기용). 000마디는 13/14."""
    g, d = expected_main(number) if number > 0 else (1, RTL)
    bar = Bar(number=number, length=4)
    bar.dirs[g] = d
    return bar


def format_bar(number: int) -> str:
    """C# '{0:D3}' 과 같게 (음수는 부호 뒤 3자리)."""
    return f"-{-number:03d}" if number < 0 else f"{number:03d}"


def bar_output_length(bar: Bar) -> int:
    n = bar.min_length()
    if n < 4:   # 에디터는 마디를 4분할로 만들고 짧은 줄을 앞칸에 그대로 복사함 → 4의 배수로 늘려 타이밍 보존
        n = lcm(n, 4)
    return n


def to_text(chart: Chart, fill_gaps: bool = False) -> str:
    """에디터 ToChartText 와 같은 형식. fill_gaps=True 면 0..마지막 마디 사이 빠진 마디를 빈 줄로 채운다."""
    if chart.unrepresentable:
        raise ChartWriteError("에디터 형식으로 쓸 수 없는 줄: " + "; ".join(chart.unrepresentable))
    h = chart.header
    out = [
        f"#TITLE {h.title}",
        f"#ARTIST {h.artist}",
        f"#DIFFICULTY {h.difficulty}",
        f"#LEVEL {h.level}",
        f"#BPM {h.bpm}",
        f"#NOTES {chart.count_notes()}",
        "",
    ]
    bars = dict(chart.bars)
    if fill_gaps:
        for n in range(0, chart.last_bar() + 1):
            if n not in bars:
                bars[n] = empty_bar(n)
    for number in sorted(bars):
        bar = bars[number]
        n = bar_output_length(bar)
        if n > MAX_LENGTH:
            raise ChartWriteError(f"{number:03d}마디: 필요한 분할 수 {n} 가 에디터 상한 {MAX_LENGTH} 를 넘음")
        for lane in LANES:
            d = bar.dirs[group_of(lane)]
            if d is None:
                if bar.cells[lane]:
                    raise ChartWriteError(f"{number:03d}마디 레인 {lane}: 방향 없는 그룹에 노트가 있음")
                continue
            seq = ["0"] * n
            for pos, c in bar.cells[lane].items():
                idx = pos * n
                if idx.denominator != 1 or not 0 <= idx < n:
                    raise ChartWriteError(f"{number:03d}마디 레인 {lane}: 위치 {pos} 를 {n}분할로 표현할 수 없음")
                seq[int(idx)] = c
            if d == RTL:
                seq.reverse()
            out.append(f"#{format_bar(number)}:{d}{lane}:{''.join(seq)};")
    return "\r\n".join(out) + "\r\n"


def to_bytes(chart: Chart, fill_gaps: bool = False) -> bytes:
    return to_text(chart, fill_gaps=fill_gaps).encode("utf-8")


def write_chart(chart: Chart, path: str | Path, fill_gaps: bool = True) -> None:
    Path(path).write_bytes(to_bytes(chart, fill_gaps=fill_gaps))


def group_lines(bar: Bar) -> dict[int, tuple[int, ...]]:
    """그룹별로 실제로 쓰이는 레인."""
    return {g: lanes_of(g) for g in GROUPS if bar.dirs[g] is not None}
