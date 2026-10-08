"""곡별 설정 (songs/000N.yaml)."""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import yaml

from .paths import charts_dir, music_audio_dir, repo_root, songs_dir


def parse_bars(spec: str | int | list | None) -> set[int]:
    """'3-5,7-13' → {3,4,5,7,...,13}. 정수나 리스트도 받는다."""
    if spec is None or spec == "":
        return set()
    if isinstance(spec, int):
        return {spec}
    if isinstance(spec, list):
        out: set[int] = set()
        for s in spec:
            out |= parse_bars(s)
        return out
    out = set()
    for part in str(spec).split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.update(range(int(a), int(b) + 1))
        else:
            out.add(int(part))
    return out


@dataclass
class SongConfig:
    id: int
    title: str = ""
    artist: str = ""               # 신규 곡 헤더 #ARTIST (MusicSO 가 없을 때)
    bpm: int | None = None         # 신규 곡: MusicSO 가 없을 때 쓰는 정수 BPM (있으면 서로 같아야 함)
    downbeat_ok: bool = False      # align 메트로놈을 듣고 '1마디 시작 = 음원 0초' 를 사람이 확인했는지
    region_last_bar: int | None = None   # 채보 마지막 마디 (align 제안을 사람이 확정)
    role: str = "target"           # target = 포팅 대상, reference = 기조 학습용(사용자 노멀/하드 쌍)
    audio: str = "auto"            # auto = MusicSO 의 audioFilePath, none = 음원 없음, 그 외 = 경로
    offset_ms: float | None = None
    base: str | None = None        # 포팅 입력 (보통 Normal)
    target: str | None = None      # 포팅 출력 (Hard)
    mode: str = "port"             # port | finish
    frozen_bars: set[int] = field(default_factory=set)
    locked_bars: set[int] = field(default_factory=set)
    listen_bars: set[int] = field(default_factory=set)
    keep_normal_bars: set[int] = field(default_factory=set)  # 하드에서도 일부러 노멀과 같게 둔 마디
    rtl_suspect: bool = False      # 노멀 짝수 마디(RTL 줄) 방향이 의심됨 → 그 마디 기믹에 '방향 확인' 표시 (5번)
    gimmick_level: int = 3         # 하드 기믹 범위: 1 = 노트 넘기기, 2 = + 메인 라인 뒤집기·통째, 3 = + 두 그룹 동시치기 (사용자 결정)
    density_scale: float = 1.0     # 추가 노트 양 배율 (1.0 = 2번 기조, 2.0 = 그 두 배)
    finish_source: str | None = None   # mode: finish — 손작업 하드 원본 (경로 또는 'git:<커밋>:<경로>')
    finish_retime: str | None = None   # 원본에 먼저 적용할 시간 이동 (예: "-1/8")
    finish_normal_source: str | None = None   # 시간 이동 전 노멀 (사용자가 직접 고친 칸 찾기용)
    levels: dict = field(default_factory=dict)   # 생성 채보 헤더 #LEVEL (예: {Easy: 3}); 게임은 MusicSO 레벨을 씀
    sections: list = field(default_factory=list)
    decisions: list = field(default_factory=list)
    notes: str = ""
    path: Path | None = None

    def chart_path(self, difficulty: str) -> Path:
        return charts_dir() / f"Chart_{self.id:04d}_{difficulty}.txt"

    def base_path(self) -> Path:
        return repo_root() / self.base if self.base else self.chart_path("Normal")

    def target_path(self) -> Path:
        return repo_root() / self.target if self.target else self.chart_path("Hard")

    def audio_path(self, so_audio: str | None) -> Path | None:
        """auto = MusicSO 의 음원(StreamingAssets/Music), 그 외 = 그 폴더 기준 상대 경로나 절대 경로."""
        if self.audio == "none":
            return None
        name = so_audio if self.audio == "auto" else self.audio
        return music_audio_dir() / name if name else None


class ConfigError(ValueError):
    pass


def load_song(song_id: int) -> SongConfig:
    """songs/000N.yaml 을 읽는다. 없으면 기본값, 형식이 틀리면 ConfigError (파일·키 이름 포함)."""
    p = songs_dir() / f"{song_id:04d}.yaml"
    if not p.exists():
        return SongConfig(id=song_id)
    try:
        d = yaml.safe_load(p.read_text(encoding="utf-8")) or {}
    except yaml.YAMLError as e:
        raise ConfigError(f"{p.name}: YAML 해석 실패 ({e})") from e
    if not isinstance(d, dict):
        raise ConfigError(f"{p.name}: 최상위가 키-값 형식이 아님")

    def bars(key: str) -> set[int]:
        try:
            return parse_bars(d.get(key))
        except ValueError as e:
            raise ConfigError(f"{p.name}: {key} 해석 실패 ({d.get(key)!r})") from e

    region = d.get("region") or {}
    if not isinstance(region, dict) or any(k != "last_bar" for k in region):
        raise ConfigError(f"{p.name}: region 은 {{last_bar: 98}} 형식이어야 함 ({region!r})")
    levels = d.get("levels") or {}
    if not isinstance(levels, dict) or any(k not in ("Easy", "Normal", "Hard", "Extreme") for k in levels):
        raise ConfigError(f"{p.name}: levels 는 {{Easy: 3, Normal: 5, ...}} 형식이어야 함 ({levels!r})")
    try:
        return SongConfig(
            id=int(d.get("id", song_id)),
            title=str(d.get("title") or ""),
            artist=str(d.get("artist") or ""),
            bpm=(int(d["bpm"]) if d.get("bpm") is not None else None),
            downbeat_ok=bool(d.get("downbeat_ok", False)),
            region_last_bar=(int(region["last_bar"]) if region.get("last_bar") is not None else None),
            role=str(d.get("role") or "target"),
            audio=str(d.get("audio") or "auto"),
            offset_ms=d.get("offset_ms"),
            base=d.get("base"),
            target=d.get("target"),
            mode=str(d.get("mode") or "port"),
            frozen_bars=bars("frozen_bars"),
            locked_bars=bars("locked_bars"),
            listen_bars=bars("listen_bars"),
            keep_normal_bars=bars("keep_normal_bars"),
            rtl_suspect=bool(d.get("rtl_suspect", False)),
            gimmick_level=int(d.get("gimmick_level", 3)),
            density_scale=float(d.get("density_scale", 1.0)),
            finish_source=d.get("finish_source"),
            finish_retime=(str(d["finish_retime"]) if d.get("finish_retime") is not None else None),
            finish_normal_source=d.get("finish_normal_source"),
            levels={k: int(v) for k, v in levels.items()},
            sections=d.get("sections") or [],
            decisions=d.get("decisions") or [],
            notes=str(d.get("notes") or ""),
            path=p,
        )
    except (TypeError, ValueError) as e:
        if isinstance(e, ConfigError):
            raise
        raise ConfigError(f"{p.name}: 값 형식 오류 ({e})") from e
