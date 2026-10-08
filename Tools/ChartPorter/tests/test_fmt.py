import shutil
from pathlib import Path

import pytest

from chartporter import fmt as fmt_mod
from chartporter.chart_io import read_chart
from chartporter.validate import validate

FIXTURES = Path(__file__).parent / "fixtures"


@pytest.fixture
def work(tmp_path, monkeypatch):
    monkeypatch.setattr(fmt_mod, "out_dir", lambda sid=None: tmp_path / "out")
    return tmp_path


def _copy(work, name, as_name):
    dst = work / as_name
    shutil.copy2(FIXTURES / name, dst)
    return dst


def test_fmt_fills_gaps_fixes_header_and_orphan(work):
    p = _copy(work, "gap_and_orphan_0006_Easy.txt", "Chart_0006_Easy.txt")
    res = fmt_mod.format_chart(p, orphan_end="tap")
    text = res.after.decode()
    assert "#102:13:0000;\r\n#102:14:0000;\r\n#103:01:0000;\r\n#103:02:0000;" in text
    assert "#060:14:0100;" in text
    assert res.path.read_bytes() == res.before      # 수정안 만들기는 원본을 건드리지 않음


def test_fmt_keep_leaves_orphan_and_only_fixes_safe_things(work):
    p = _copy(work, "gap_0006_Normal.txt", "Chart_0006_Normal.txt")
    res = fmt_mod.format_chart(p)
    assert "#DIFFICULTY Normal" in res.after.decode()
    assert any("빠진 마디" in c for c in res.changes)


def test_install_backs_up_and_reduces_errors(work):
    p = _copy(work, "gap_and_orphan_0006_Easy.txt", "Chart_0006_Easy.txt")
    res = fmt_mod.format_chart(p, orphan_end="tap")
    saved = fmt_mod.install(res)
    assert saved.read_bytes() == res.before
    chart, raw = read_chart(p)
    assert not [i for i in validate(chart, raw) if i.severity in ("FATAL", "ERROR")]


def test_install_refuses_if_file_changed_after_reading(work):
    p = _copy(work, "gap_0006_Normal.txt", "Chart_0006_Normal.txt")
    res = fmt_mod.format_chart(p)
    p.write_bytes(p.read_bytes() + b"#105:01:0000;\r\n")   # 에디터가 그 사이에 저장한 상황
    with pytest.raises(RuntimeError):
        fmt_mod.install(res)


def test_unchanged_chart_reports_no_change(work):
    p = work / "Chart_9999_Normal.txt"
    p.write_bytes(b"#TITLE T\r\n#ARTIST A\r\n#DIFFICULTY Normal\r\n#LEVEL 1\r\n#BPM 120\r\n#NOTES 1\r\n\r\n"
                  b"#000:13:0000;\r\n#000:14:0000;\r\n#001:01:1000;\r\n#001:02:0000;\r\n")
    assert not fmt_mod.format_chart(p).changed
