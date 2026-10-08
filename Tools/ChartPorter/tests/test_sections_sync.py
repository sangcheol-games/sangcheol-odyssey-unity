"""구간 찾기(실제 3번 음원, 알려진 구간 맵과 비교)와 복사 규칙 재적용(sync)의 3-way 판단."""
from fractions import Fraction

import pytest

from chartporter.audio import estimate_offset, features_for
from chartporter.chart_io import read_chart
from chartporter.config import SongConfig
from chartporter.musicso import music_so_for
from chartporter.paths import charts_dir, music_audio_dir
from chartporter.sections import bar_features, find_relations, proposal_yaml, similarity
from chartporter.sync import bar_hash, plan_sync, rules_from_config, updated_state

from conftest import HEADER, chart_from_text


# ---------------- sections: 3번 Lazy Dance ----------------

@pytest.fixture(scope="module")
def lazy():
    so = music_so_for(3)
    audio = music_audio_dir() / so.audio_file
    if not audio.exists():
        pytest.skip("3번 음원 없음")
    feat = features_for(audio)
    normal, _ = read_chart(charts_dir() / "Chart_0003_Normal.txt")
    off = estimate_offset(feat, normal, so.bpm).offset
    bf = bar_features(feat, normal.last_bar(), so.bpm, off)
    sim = similarity(bf)
    return normal, bf, sim, find_relations(bf, sim, normal)


def _covered(rels, target, lag, classes):
    bars = {b for r in rels if r.lag == lag and r.klass in classes for b in range(r.target[0], r.target[1] + 1)}
    return set(range(target[0], target[1] + 1)) <= bars


def test_identical_repeats_of_a_section(lazy):
    _n, _bf, _sim, rels = lazy
    assert _covered(rels, (51, 57), 48, {"IDENTICAL"})        # A 2절 (050 은 첫 마디라 다름)
    assert _covered(rels, (98, 104), 96, {"IDENTICAL"})       # 아웃트로 = A


def test_chorus_repeat_same_backing(lazy):
    _n, _bf, _sim, rels = lazy
    assert _covered(rels, (74, 88), 40, {"IDENTICAL", "SAME_BACKING"})


def test_verse_repeat_only_melody_similar(lazy):
    _n, _bf, _sim, rels = lazy
    assert _covered(rels, (58, 73), 48, {"SIMILAR"})          # X'·B' 는 반주가 달라 '비슷함'


def test_proposal_matches_users_copy_rules(lazy):
    _n, _bf, _sim, rels = lazy
    rules = {(r["bars"], r["copy_of"], r["transform"]) for r in proposal_yaml(rels)}
    assert ("74-81", "34-41", "mirror") in rules
    assert ("82-85", "42-45", "same") in rules
    assert ("86-88", "46-48", "mirror") in rules
    assert ("98-104", "2-8", "mirror") in rules


# ---------------- sync ----------------

BASE = HEADER.format(n=0) + "#000:13:0000;\n#000:14:0000;\n"


def _chart(lines: dict[int, tuple[str, str]]):
    rows = []
    for b in sorted(lines):
        a, c = lines[b]
        if b % 2:
            rows += [f"#{b:03d}:01:{a};", f"#{b:03d}:02:{c};"]
        else:
            rows += [f"#{b:03d}:13:{a};", f"#{b:03d}:14:{c};"]
    return chart_from_text(BASE + "\n".join(rows))[0]


def _cfg(**kw):
    return SongConfig(id=9999, sections=[{"bars": "3", "copy_of": "1", "transform": "mirror"}], **kw)


def test_rules_parse_and_validate():
    cfg = SongConfig(id=1, sections=[{"bars": "5-6", "copy_of": "1-2", "transform": "same", "drop": [{"lane": 2, "pos": "14/16"}]},
                                     {"bars": "9", "copy_of": "1", "transform": "manual"}])
    rules = rules_from_config(cfg)
    assert len(rules) == 1 and rules[0].targets == [5, 6] and rules[0].drop == [(2, Fraction(7, 8))]


def test_first_sync_overwrites_untouched_normal_bar():
    normal = _chart({1: ("1000", "0010"), 2: ("0000", "0000"), 3: ("1000", "0000")})
    hard = _chart({1: ("1010", "0101"), 2: ("0000", "0000"), 3: ("1000", "0000")})   # 3마디는 노멀 그대로
    new, items = plan_sync(_cfg(), hard, normal, {"bars": {}})
    assert items[0].status == "write"
    assert new.bars[3].cells[2] == hard.bars[1].cells[1]          # 반전 복사: 레인1 → 레인2


def test_user_edited_target_is_not_overwritten_then_source_change_propagates():
    normal = _chart({1: ("1000", "0010"), 2: ("0000", "0000"), 3: ("1000", "0000")})
    hard = _chart({1: ("1010", "0101"), 2: ("0000", "0000"), 3: ("0110", "0000")})   # 3마디를 사용자가 직접 찍음
    _new, items = plan_sync(_cfg(), hard, normal, {"bars": {}})
    assert items[0].status == "user_edited"
    _new, items = plan_sync(_cfg(locked_bars={3}), hard, normal, {"bars": {}})
    assert items[0].status == "locked"
    # sync 가 지난번에 쓴 내용 그대로면, 원본이 바뀌었을 때 다시 복사한다
    synced, items = plan_sync(_cfg(), hard, normal, {"bars": {}}, force=True)
    state = updated_state(items, {"bars": {}})
    synced.bars[1].cells[1][Fraction(3, 4)] = "1"                  # 원본 수정
    again, items = plan_sync(_cfg(), synced, normal, state)
    assert items[0].status == "write" and Fraction(3, 4) in again.bars[3].cells[2]


def test_bar_hash_ignores_subdivision():
    a = _chart({1: ("1000", "0000")}).bars[1]
    b = _chart({1: ("10000000", "00000000")}).bars[1]
    assert bar_hash(a) == bar_hash(b)
