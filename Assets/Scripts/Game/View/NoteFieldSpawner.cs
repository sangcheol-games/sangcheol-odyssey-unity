using System;
using System.Collections.Generic;
using SCOdyssey.Rhythm;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 노트 오브젝트를 띄우고 판정 결과를 반영한다.
    //  - 다음 마디 노트를 미리 띄운다(Ghost, 현재 판정선과 겹치면 Hidden). 마디가 시작되면 Active로 올린다
    //  - noteId(= NoteData.id = 판정 트랙 인덱스) -> 뷰로 판정 결과를 되찾는다
    //  - 홀드 꼬리가 miss면 머리의 홀드바를 흐리게 한다
    public sealed class NoteFieldSpawner
    {
        private readonly GameObjectPool _notePool;
        private readonly GameObjectPool _holdBarPool;
        private readonly RectTransform _headLayer;
        private readonly RectTransform _holdLayer;
        private readonly RectTransform[] _laneAnchors;   // 레인별 노트 높이
        private readonly NoteGeometry _geometry;

        // 다음 마디용으로 띄워 둔 노트. 레인별
        private readonly Queue<NoteController>[] _ghostNotes = new Queue<NoteController>[LANE_COUNT];

        // noteId -> 뷰. 스폰 때 등록하고, 판정/miss로 꺼낼 때 해제한다
        private NoteController[] _views = Array.Empty<NoteController>();

        // noteId -> 스폰한 자리(월드). 판정 이펙트는 뷰가 풀로 돌아간 뒤에도 이 자리에 띄운다
        private Vector3[] _spawnWorld = Array.Empty<Vector3>();

        // 판정된 홀드 머리 뷰(홀드바가 남아 있는 동안). 꼬리 noteId -> (머리 noteId, 뷰)
        private (int headId, HoldStartNote view)[] _holdHeads = Array.Empty<(int, HoldStartNote)>();

        public NoteFieldSpawner(GameObjectPool notePool, GameObjectPool holdBarPool, RectTransform headLayer, RectTransform holdLayer,
            RectTransform[] laneAnchors, NoteGeometry geometry)
        {
            _notePool = notePool;
            _holdBarPool = holdBarPool;
            _headLayer = headLayer;
            _holdLayer = holdLayer;
            _laneAnchors = laneAnchors;
            _geometry = geometry;
            for (int i = 0; i < _ghostNotes.Length; i++) _ghostNotes[i] = new Queue<NoteController>();
        }

        public void Reset(int noteCount)
        {
            foreach (var queue in _ghostNotes) queue.Clear();
            _views = new NoteController[noteCount];
            _spawnWorld = new Vector3[noteCount];
            _holdHeads = new (int, HoldStartNote)[noteCount];
        }

        /// <summary>
        /// 다음 마디 노트를 풀에서 꺼내 배치하고 Ghost(또는 Hidden)로 띄운다. 본체(3)는 띄우지 않는다.
        /// 같은 그룹 판정선이 아직 이 마디를 지나는 중이면(고난이도) 겹쳐 보이지 않게 Hidden으로 숨기고,
        /// 그 판정선이 지나가면 노트가 스스로 Ghost로 바뀐다. 판정선 도착점의 노트는 지나갈 일이 없어 바로 Ghost.
        /// </summary>
        public void SpawnBar(IEnumerable<LaneData> lanes, JudgeLineDirector lines)
        {
            foreach (LaneData lane in lanes)
            {
                Lane laneId = LaneMap.FromChartLine(lane.line);
                float noteInterval = _geometry.Interval(lane.beat);
                float laneY = _laneAnchors[(int)laneId].anchoredPosition.y;

                lines.TryGetActive(LaneLayout.GroupOf(laneId), out TimelineController currentLine);

                foreach (NoteData noteData in lane.Notes)
                {
                    if (noteData.noteType == NoteType.Holding) continue;

                    GameObject note = _notePool.Get();
                    note.transform.SetParent(_headLayer, false);   // 헤드는 홀드바보다 위 레이어

                    // 프리팹 하나가 모든 타입 컴포넌트를 갖고 있고, 타입에 맞는 것만 켜서 쓴다
                    NoteController view = note.GetComponent<NoteAdapter>().ActivateAndGet(noteData.noteType);
                    _ghostNotes[(int)laneId].Enqueue(view);

                    bool tracked = noteData.id >= 0 && noteData.id < _views.Length;
                    if (tracked) _views[noteData.id] = view;

                    var spawnPos = new Vector2(_geometry.NoteX(lane, noteData.index), laneY);

                    if (noteData.noteType == NoteType.HoldStart)
                    {
                        float holdWidth = noteInterval * (noteData.holdBarBeats ?? 1);
                        GameObject holdBar = _holdBarPool.Get();
                        holdBar.transform.SetParent(_holdLayer, false);
                        ((HoldStartNote)view).SetHoldBar(holdBar);
                        view.Init(noteData, spawnPos, lane.isLTR, holdWidth, returned =>
                        {
                            _holdBarPool.Return(holdBar);
                            _notePool.Return(returned.gameObject);
                        });
                    }
                    else
                    {
                        view.Init(noteData, spawnPos, lane.isLTR, noteInterval, returned => _notePool.Return(returned.gameObject));
                    }

                    if (tracked) _spawnWorld[noteData.id] = view.transform.position;

                    if (currentLine != null && !_geometry.IsAtEnd(spawnPos.x, currentLine.isLTR))
                    {
                        view.TrackTimeline(currentLine);
                        view.SetState(NoteState.Hidden);
                    }
                    else
                    {
                        view.SetState(NoteState.Ghost);
                    }
                }
            }
        }

        /// <summary>
        /// 마디 시작 시 띄워 둔 노트를 Active로 올린다(표시만 바뀐다). 그 사이 판정돼 화면에서 빠진 노트는 건너뛴다.
        /// 홀드 머리는 홀드바가 판정선을 따라 줄어들도록 그 그룹 판정선을 따라가게 한다.
        /// </summary>
        public void ActivateGhosts(JudgeLineDirector lines, NoteLifecycle lifecycle)
        {
            for (int i = 0; i < _ghostNotes.Length; i++)
            {
                lines.TryGetActive(LaneLayout.GroupOf((Lane)i), out TimelineController line);

                Queue<NoteController> queue = _ghostNotes[i];
                while (queue.Count > 0)
                {
                    NoteController note = queue.Dequeue();
                    if (!note.gameObject.activeSelf || !lifecycle.ShouldActivate(note.noteData.id)) continue;

                    note.SetState(NoteState.Active);

                    if (note.noteData.noteType == NoteType.HoldStart && line != null)
                        note.TrackTimeline(line);
                }
            }
        }

        /// <summary>
        /// 판정이 끝난 노트 하나를 뷰에 반영한다. 화면에 있던 노트면 true와 스폰 자리(월드)를 돌려준다
        /// (스폰 전이거나 이미 반영했으면 false). 탭·꼬리 뷰는 히트 연출이 끝나면 풀로 돌아가고(miss는 바로),
        /// 홀드 머리는 홀드바가 다 지나갈 때까지 남는다.
        /// </summary>
        public bool ApplyDecided(int noteId, NoteKind kind, int pairId, bool missed, out Vector3 spawnWorld)
        {
            NoteController view = TakeView(noteId);
            spawnWorld = view != null ? _spawnWorld[noteId] : default;
            if (kind == NoteKind.HoldHead && pairId >= 0 && view is HoldStartNote head)
                _holdHeads[pairId] = (noteId, head);

            if (missed)
            {
                view?.OnMiss();
                if (kind == NoteKind.HoldTail) BreakHoldBar(noteId);
            }
            else
            {
                // 히트 연출은 뷰가 풀로 돌아가기 전에 재생된다. Ghost/Hidden 색이면 어둡거나 안 보이므로 원색으로 올린다.
                // 홀드 머리는 마디 시작에 판정선을 붙이며 Active가 되므로 여기서 올리지 않는다(Hidden 머리가 지금 판정선을 따라가면 안 된다)
                if (view != null && kind != NoteKind.HoldHead) view.SetState(NoteState.Active);
                view?.OnHit();
                if (kind == NoteKind.HoldTail) _holdHeads[noteId] = default;
            }

            return view != null;
        }

        // 뷰를 꺼내면서 등록 해제. 같은 노트를 두 번 건드리지 않게 한다
        private NoteController TakeView(int noteId)
        {
            if (noteId < 0 || noteId >= _views.Length) return null;

            NoteController view = _views[noteId];
            _views[noteId] = null;
            return view;
        }

        // 꼬리가 miss면(중간에 뗌, 머리 놓침, 너무 오래 누름) 머리의 홀드바를 흐리게 한다.
        // 머리 뷰가 이미 풀로 돌아가 다른 노트로 쓰이고 있으면 건드리지 않는다
        private void BreakHoldBar(int tailId)
        {
            (int headId, HoldStartNote view) = _holdHeads[tailId];
            _holdHeads[tailId] = default;
            if (view != null && view.gameObject.activeSelf && view.noteData != null && view.noteData.id == headId)
                view.OnHoldBroken();
        }
    }
}
