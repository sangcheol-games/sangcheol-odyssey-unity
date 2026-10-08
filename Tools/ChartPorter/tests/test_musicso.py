from pathlib import Path

from chartporter.cli import slot_issues
from chartporter.config import parse_bars
from chartporter.musicso import MusicSO, _yaml_scalar, load_music_so, music_so_for
from chartporter.paths import music_so_dir


def test_music_so_0001_slots_and_decoded_audio():
    so = music_so_for(1)
    assert so.bpm == 195
    assert so.audio_file == "サバ！サマー！サンバ！.ogg"
    assert so.chart_files["Normal"] == "Chart_0001_Normal.txt"
    assert "Chart_0001_Hard.txt" in so.chart_files.values()      # 슬롯 위치는 Inspector 에서 바뀔 수 있음
    assert so.levels["Normal"] == 7


def test_music_so_korean_audio_and_three_charts():
    assert music_so_for(4).audio_file == "일본어 못합니다.ogg"
    so = music_so_for(3)
    assert so.audio_file == "Lazy Dance.ogg"
    assert {so.chart_files[d] for d in ("Easy", "Normal", "Hard")} == {
        "Chart_0003_Easy.txt", "Chart_0003_Normal.txt", "Chart_0003_Hard.txt"}


def test_yaml_scalar_forms():
    assert _yaml_scalar('"\\u30B5.ogg"') == "サ.ogg"
    assert _yaml_scalar("'[Remix] A.ogg'") == "[Remix] A.ogg"
    assert _yaml_scalar("Lazy Dance.ogg") == "Lazy Dance.ogg"
    assert _yaml_scalar("") == ""


def _patched_asset(tmp_path: Path, src: int, edit) -> MusicSO:
    text = (music_so_dir() / f"MusicSO_{src:04d}.asset").read_text(encoding="utf-8")
    p = tmp_path / "MusicSO_9999.asset"
    p.write_text(edit(text), encoding="utf-8")
    return load_music_so(p)


def test_null_chart_entry_does_not_crash(tmp_path):
    # Odin 이 None 으로 둔 슬롯은 '$v / Entry: 6 / Data: ' 로 저장된다
    def edit(t):
        i = t.index("Name: chartFile")
        j = t.index("Entry: 10", i)
        return t[:j] + "Entry: 6\n      Data: \n" + t[t.index("\n", t.index("Data:", j)) + 1:]
    so = _patched_asset(tmp_path, 2, edit)
    assert None in so.chart_files.values()


def test_null_reference_entry_keeps_positions(tmp_path):
    """ReferencedUnityObjects 첫 항목이 빈 참조({fileID: 0})면 그 슬롯만 None 이고 나머지는 그대로여야 한다."""
    def edit(t):
        i = t.index("ReferencedUnityObjects:")
        j = t.index("- {", i)
        k = t.index("\n", j)
        return t[:j] + "- {fileID: 0}" + t[k:]
    original = music_so_for(2).chart_files
    first = next(d for d, name in original.items() if name == "Chart_0002_Normal.txt")
    so = _patched_asset(tmp_path, 2, edit)
    assert so.chart_files == {**original, first: None}


def test_slot_issues():
    so = MusicSO(path=Path("MusicSO_x.asset"), levels={"Easy": 3, "Normal": 5, "Hard": -1, "Extreme": -1},
                 chart_files={"Normal": "A.txt", "Extreme": "B.txt"})
    codes = [(i.code, i.message.split(" ")[0]) for i in slot_issues(so, exists=lambda n: True)]
    assert ("E11", "Easy") in codes          # 선택 가능한데 채보 없음
    assert ("W10", "Extreme") in codes       # 채보는 있는데 선택 불가
    assert not any(d == "Normal" for _, d in codes)


def test_parse_bars():
    assert parse_bars("3-5,7-9") == {3, 4, 5, 7, 8, 9}
    assert parse_bars(["1", 4]) == {1, 4}
    assert parse_bars(None) == set()
