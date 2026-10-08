"""MusicSO_000N.asset 읽기 (읽기 전용).

필요한 값만 뽑는다:
  bpm, audioFilePath(YAML 스칼라: 큰따옴표 \\uXXXX, 작은따옴표, 따옴표 없음), id
  level / chartFile: Odin 직렬화 Dictionary<Difficulty, ...> 노드
      $k = 난이도 번호, $v = Entry 3(정수) / Entry 10(ReferencedUnityObjects 인덱스) / Entry 6(null)
  chartFile 의 참조 인덱스 → ReferencedUnityObjects 항목 → guid → Assets/Charts/*.meta 로 파일 이름 확인
게임 기준: 레벨이 -1 이 아니면 선택 가능(AdventureUI, MusicListUI), 채보가 없으면 로딩 실패(GameDataLoader.TryLoadChart).
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

import yaml

from .model import DIFFICULTIES
from .paths import charts_dir, music_so_dir

_NODE = re.compile(r"^\s*- Name: ?(.*)\n\s*Entry: (\d+)\n\s*Data: ?(.*)$", re.M)
ENTRY_INT, ENTRY_NULL, ENTRY_REF = 3, 6, 10


@dataclass
class MusicSO:
    path: Path
    id: int | None = None
    bpm: int | None = None
    audio_file: str | None = None
    levels: dict[str, int] = field(default_factory=dict)              # 난이도 → 레벨 (-1 = 선택 불가)
    chart_files: dict[str, str | None] = field(default_factory=dict)  # 난이도 → 채보 파일 이름 (None = 비었거나 못 찾음)
    unresolved: dict[str, str] = field(default_factory=dict)          # 난이도 → 찾지 못한 guid

    def selectable(self, difficulty: str) -> bool:
        return self.levels.get(difficulty, -1) != -1


def _yaml_scalar(value: str) -> str:
    value = value.strip()
    if not value:
        return ""
    try:
        parsed = yaml.safe_load("v: " + value)["v"]
    except yaml.YAMLError:
        return value
    return "" if parsed is None else str(parsed)


def _guid_to_chart() -> dict[str, str]:
    out = {}
    for meta in charts_dir().glob("*.meta"):
        m = re.search(r"^guid: (\w+)", meta.read_text(encoding="utf-8"), re.M)
        if m:
            out[m.group(1)] = meta.name[:-5]
    return out


def _odin_dict(nodes: list[tuple[str, int, str]], name: str) -> list[tuple[int, int, str]]:
    """Name 이 name 인 Dictionary 노드 뒤의 ($k, entry 타입, $v 원문) 목록."""
    pairs = []
    start = next((i for i, n in enumerate(nodes) if n[0] == name), None)
    if start is None:
        return pairs
    key = None
    for n_name, entry, data in nodes[start + 1:]:
        if n_name not in ("", "$k", "$v", "comparer"):
            break
        if n_name == "$k":
            key = int(data)
        elif n_name == "$v" and key is not None:
            pairs.append((key, entry, data))
            key = None
    return pairs


def load_music_so(path: str | Path) -> MusicSO:
    path = Path(path)
    text = path.read_text(encoding="utf-8")
    so = MusicSO(path=path)
    m = re.search(r"^  id: (-?\d+)", text, re.M)
    so.id = int(m.group(1)) if m else None
    m = re.search(r"^  bpm: (-?\d+)", text, re.M)
    so.bpm = int(m.group(1)) if m else None
    m = re.search(r"^  audioFilePath:(.*)$", text, re.M)
    so.audio_file = _yaml_scalar(m.group(1)) if m else None

    nodes = [(a.strip(), int(b), c.strip()) for a, b, c in _NODE.findall(text)]
    for k, entry, data in _odin_dict(nodes, "level"):
        if 0 <= k < len(DIFFICULTIES) and entry == ENTRY_INT:
            so.levels[DIFFICULTIES[k]] = int(data)

    block = re.search(r"ReferencedUnityObjects:\n((?:[ \t]*- \{.*\}\n)*)", text)
    entries = re.findall(r"^[ \t]*- (\{.*\})$", block.group(1), re.M) if block else []
    guids = [(re.search(r"guid: (\w+)", e) or [None, None])[1] for e in entries]   # 항목마다 하나 (없으면 None)
    names = _guid_to_chart()
    for k, entry, data in _odin_dict(nodes, "chartFile"):
        if not 0 <= k < len(DIFFICULTIES):
            continue
        d = DIFFICULTIES[k]
        if entry != ENTRY_REF:
            so.chart_files[d] = None
            continue
        ref = int(data)
        guid = guids[ref] if 0 <= ref < len(guids) else None
        so.chart_files[d] = names.get(guid) if guid else None
        if guid and guid not in names:
            so.unresolved[d] = guid
    return so


def music_so_for(song_id: int) -> MusicSO | None:
    p = music_so_dir() / f"MusicSO_{song_id:04d}.asset"
    return load_music_so(p) if p.exists() else None


def all_music_so() -> list[MusicSO]:
    return [load_music_so(p) for p in sorted(music_so_dir().glob("MusicSO_*.asset"))]
