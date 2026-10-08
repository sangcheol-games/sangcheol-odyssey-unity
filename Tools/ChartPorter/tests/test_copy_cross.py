"""반복 구간 복사: cross 변환(홀수 간격), 짝홀 검사, 채보 없는 곡의 제안, 이지·노멀 생성에 복사 적용, 난이도별 상태."""
from fractions import Fraction

import pytest

from chartporter import cli, config
from chartporter.audio import features_for
from chartporter.cli import UsageError, _song_audio
from chartporter.config import SongConfig
from chartporter.easy import generate_easy
from chartporter.model import LTR, RTL, expected_main
from chartporter.normal import NormalParams, generate_normal
from chartporter.sections import Relation, chart_relation, proposal_yaml
from chartporter.sync import RuleError, rules_from_config, state_path, transformed

from conftest import HEADER, chart_from_text

BASE = HEADER.format(n=0).replace("Hard", "Easy") + "#000:13:0000;\n#000:14:0000;\n"


def make(lines: str):
    return chart_from_text(BASE + lines)[0]


def test_cross_moves_to_other_group_and_keeps_own_or_flipped_meaning():
    # 3마디(홀수): 메인 그룹0 LTR + 기믹 그룹1 을 뒤집어(LTR) 쓴 마디 → 4마디(짝수)로
    c = make("#003:01:10000000;\n#003:02:00100000;\n#003:03:00001000;\n#003:04:00000000;")
    src = c.bars[3]
    assert src.dirs == {0: LTR, 1: LTR}
    out = transformed(src, 4, "cross", [])
    assert out.dirs[expected_main(4)[0]] == expected_main(4)[1]          # 메인 라인 = 그룹1 RTL
    assert out.dirs[0] == RTL                                            # 뒤집힌 기믹은 대상에서도 뒤집힌 채 (그룹0 원래 LTR)
    assert out.cells[3] == src.cells[1] and out.cells[4] == src.cells[2]   # 같은 높이 (1→3, 2→4)
    assert out.cells[1] == src.cells[3]


def test_parity_guard():
    with pytest.raises(RuleError):
        rules_from_config(SongConfig(id=1, sections=[{"bars": "10", "copy_of": "3", "transform": "same"}]))
    with pytest.raises(RuleError):
        rules_from_config(SongConfig(id=1, sections=[{"bars": "10", "copy_of": "2", "transform": "cross"}]))
    assert rules_from_config(SongConfig(id=1, sections=[{"bars": "10-11", "copy_of": "3-4", "transform": "cross"}]))


def test_chart_relation_detects_cross():
    c = make("#003:01:10000000;\n#003:02:00100000;\n#004:13:00000001;\n#004:14:00000100;")
    assert chart_relation(c, 3, 4) == "C"


def test_proposal_without_chart_uses_lag_parity():
    rel = lambda t, s: Relation(target=t, source=s, klass="IDENTICAL", per_bar=["IDENTICAL"] * (t[1] - t[0] + 1))
    props = proposal_yaml([rel((40, 43), (8, 11)), rel((61, 62), (12, 13))])
    assert {p["bars"]: p["transform"] for p in props} == {"40-43": "same", "61-62": "cross"}


def test_normal_copy_keeps_target_easy_and_adds_source_additions():
    # 3마디 ← 1마디 (same). 대상 이지(3마디)는 사용자가 1/2 노트를 3/4 로 옮겼다고 가정
    e = make("#001:01:10001000;\n#001:02:00100010;\n#002:13:10000000;\n#002:14:00000000;\n"
             "#003:01:10000010;\n#003:02:00100000;\n#004:13:10000000;\n#004:14:00000000;")
    cfg = SongConfig(id=1, sections=[{"bars": "3", "copy_of": "1", "transform": "same"}])
    res = generate_normal(e, None, 150, 0.0, 1, NormalParams(target_per_bar=6), cfg=cfg)
    n = res.chart
    assert {(q, l) for q, l, c in e.bars[3].notes()} <= {(q, l) for q, l, c in n.bars[3].notes()}   # 대상 이지 그대로
    src_adds = {(x.lane, x.pos) for x in res.log if x.origin == "NORMAL_ADD" and x.bar == 1}
    copied = {(x.lane, x.pos) for x in res.log if x.origin == "COPY_ADDED" and x.bar == 3}
    skipped = {(x.lane, x.pos) for x in res.log if x.origin == "COPY_SKIP" and x.bar == 3}
    assert src_adds and copied | skipped == src_adds
    assert not [x for x in res.log if x.origin == "NORMAL_ADD" and x.bar == 3]          # 대상 마디는 따로 추가하지 않음


def test_easy_copies_are_identical_to_source():
    try:
        cfg, so, audio, bpm = _song_audio(2)
    except UsageError:
        pytest.skip("2번 음원 없음")
    feat = features_for(audio)
    res = generate_easy(feat, bpm, -0.031, 66, 2, cfg=cfg)
    for r in rules_from_config(cfg):
        for t, s in zip(r.targets, r.sources):
            assert res.chart.bars[t].cells == res.chart.bars[s].cells                    # 2번 규칙은 모두 same
    assert {x.origin for x in res.log if x.bar in {t for r in rules_from_config(cfg) for t in r.targets}} == {"COPY"}


def test_sync_state_per_difficulty(tmp_path, monkeypatch):
    from chartporter import sync
    monkeypatch.setattr(sync, "out_dir", lambda sid=None: tmp_path / f"{sid:04d}")
    assert state_path(7).name == "sync_state.json"
    assert state_path(7, "Easy").name == "sync_state_Easy.json"
    sync.save_state(7, {"bars": {"3": "x"}}, "Normal")
    assert sync.load_state(7, "Normal") == {"bars": {"3": "x"}} and sync.load_state(7) == {"bars": {}}
