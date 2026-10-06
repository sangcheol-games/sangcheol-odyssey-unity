using System;
using System.Collections.Generic;
using SCOdyssey.Rhythm;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 그룹(위/아래)마다 판정선 하나를 굴린다.
    // 판정선은 두 단계를 거친다: preloaded(다음 마디용으로 만들어만 둠) → active(현재 마디에서 이동 중).
    // 같은 그룹이 다음 마디에 반대 방향으로 가면 새로 만들지 않고 이동 중인 판정선을 뒤집어 다시 쓴다(유턴).
    public sealed class JudgeLineDirector
    {
        private readonly GameObjectPool _pool;
        private readonly Transform _parent;
        private readonly RectTransform[] _anchors;    // 그룹별 판정선 높이
        private readonly NoteGeometry _geometry;

        private readonly Dictionary<LaneGroup, TimelineController> _active = new();
        private readonly Dictionary<LaneGroup, TimelineController> _preloaded = new();
        private readonly Dictionary<LaneGroup, bool> _groupDirs = new();   // 마디에 나오는 그룹별 진행 방향(isLTR)

        public JudgeLineDirector(GameObjectPool pool, Transform parent, RectTransform[] anchors, NoteGeometry geometry)
        {
            _pool = pool;
            _parent = parent;
            _anchors = anchors;
            _geometry = geometry;
        }

        public IReadOnlyDictionary<LaneGroup, TimelineController> Active => _active;

        public bool TryGetActive(LaneGroup group, out TimelineController timeline) => _active.TryGetValue(group, out timeline);

        /// <summary>
        /// 다음 마디에 필요한 판정선을 준비한다. 유턴으로 다시 쓸 판정선이 있는 그룹은 새로 만들지 않는다.
        /// 마디에 나오는 그룹별 방향을 돌려준다(다음 호출 전까지 유효).
        /// </summary>
        public IReadOnlyDictionary<LaneGroup, bool> Preload(IEnumerable<LaneData> lanes, double startTime, double duration)
        {
            CollectGroups(lanes);

            foreach (var (group, isLTR) in _groupDirs)
            {
                bool isReused = _active.TryGetValue(group, out var existing) && existing.isLTR != isLTR;
                if (isReused) continue;

                TimelineController timeline = _pool.Get().GetComponent<TimelineController>();
                timeline.transform.SetParent(_parent, false);
                timeline.transform.position = _anchors[(int)group].position;
                StartLine(timeline, group, isLTR, startTime, duration);

                _preloaded.Add(group, timeline);
            }

            return _groupDirs;
        }

        /// <summary>
        /// 준비해 둔 판정선을 현재 마디로 올린다. 반대 방향으로 이어지는 그룹은 이동 중인 판정선을 뒤집어 다시 시작하고,
        /// 이번 마디에 안 쓰거나 방향이 같은 판정선은 active에서 뺀다(화면 밖으로 나가면 스스로 풀에 돌아간다).
        /// </summary>
        public void Promote(IEnumerable<LaneData> lanes, double startTime, double duration)
        {
            CollectGroups(lanes);

            Span<bool> groupsToRemove = stackalloc bool[LANE_GROUP_COUNT];

            foreach (var (group, timeline) in _active)
            {
                if (_groupDirs.TryGetValue(group, out bool isLTR) && timeline.isLTR != isLTR)
                    StartLine(timeline, group, isLTR, startTime, duration);   // 유턴(캐릭터 중복 교차 방지)
                else
                    groupsToRemove[(int)group] = true;
            }

            for (int i = 0; i < groupsToRemove.Length; ++i)
            {
                if (groupsToRemove[i]) _active.Remove((LaneGroup)i);
            }

            foreach (var (group, timeline) in _preloaded)
            {
                if (!_active.TryAdd(group, timeline))
                {
                    Debug.LogWarning("Timeline 승격 중복 발생: 그룹 " + group);
                    _pool.Return(timeline.gameObject);
                }
            }

            _preloaded.Clear();
        }

        private void CollectGroups(IEnumerable<LaneData> lanes)
        {
            _groupDirs.Clear();
            foreach (LaneData lane in lanes)
                _groupDirs[LaneLayout.GroupOf(LaneMap.FromChartLine(lane.line))] = lane.isLTR;
        }

        private void StartLine(TimelineController timeline, LaneGroup group, bool isLTR, double startTime, double duration)
        {
            timeline.Init(
                startTime,
                duration,
                _geometry.StartX(isLTR),
                _geometry.EndX(isLTR),
                returned => _pool.Return(returned.gameObject),
                group: group
            );
        }
    }
}
