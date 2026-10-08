"""난이도별 설치: 예전 상태 파일 읽기, 편집된 파일 보호, 검사 실패 시 거부."""
import json

import pytest

from chartporter import fmt as fmt_mod
from chartporter.install import InstallError, install_chart, load_install_state, sha_of

from conftest import HEADER

GOOD = (HEADER.format(n=1).replace("Hard", "Easy") + "#000:13:0000;\n#000:14:0000;\n#001:01:1000;\n#001:02:0000;\n").replace("\n", "\r\n")
BAD = (HEADER.format(n=1).replace("Hard", "Easy") + "#000:13:0000;\n#000:14:0000;\n#002:01:1000;\n#002:02:0000;\n").replace("\n", "\r\n")  # 001 빠짐 → F01


@pytest.fixture
def paths(tmp_path, monkeypatch):
    monkeypatch.setattr(fmt_mod, "out_dir", lambda sid=None: tmp_path / "out" / (f"{sid:04d}" if sid else "misc"))
    src, dst, state = tmp_path / "src" / "Chart_0009_Easy.txt", tmp_path / "Assets" / "Chart_0009_Easy.txt", tmp_path / "state.json"
    src.parent.mkdir()
    dst.parent.mkdir()
    return src, dst, state


def test_legacy_flat_state_is_read_as_hard(tmp_path):
    p = tmp_path / "s.json"
    p.write_text(json.dumps({"sha": "abc", "source": "x"}), encoding="utf-8")
    assert load_install_state(p) == {"Hard": {"sha": "abc", "source": "x"}}


def test_new_file_installs_and_records_per_difficulty(paths):
    src, dst, state = paths
    src.write_bytes(GOOD.encode())
    assert install_chart(src, dst, state, "Easy") is None
    st = json.loads(state.read_text(encoding="utf-8"))
    assert st["Easy"]["sha"] == sha_of(GOOD.encode()) and dst.read_bytes() == GOOD.encode()


def test_existing_unknown_file_needs_force_and_is_backed_up(paths):
    src, dst, state = paths
    src.write_bytes(GOOD.encode())
    dst.write_bytes(b"teammate chart")
    with pytest.raises(InstallError):
        install_chart(src, dst, state, "Easy")
    saved = install_chart(src, dst, state, "Easy", force=True)
    assert saved is not None and saved.read_bytes() == b"teammate chart"


def test_reinstall_over_own_install_is_allowed(paths):
    src, dst, state = paths
    src.write_bytes(GOOD.encode())
    install_chart(src, dst, state, "Easy")
    assert install_chart(src, dst, state, "Easy") is not None      # 지난 설치 그대로 → 백업 후 덮어씀


def test_invalid_draft_is_refused(paths):
    src, dst, state = paths
    src.write_bytes(BAD.encode())
    with pytest.raises(InstallError):
        install_chart(src, dst, state, "Easy")
    assert not dst.exists()
