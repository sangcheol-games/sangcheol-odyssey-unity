using System.Collections.Generic;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Rhythm;
using TMPro;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 플레이필드 (게임 화면의 노트·판정선·카운트다운·이펙트) ─────────────────────
    //
    //  게임 시작 시 GameManager가 Init()을 1회 호출한다.
    //    PrepareNextBar() -> StartCurrentBar()
    //
    //  이후 매 프레임 GameManager.Update()가 판정 세션을 진행시킨 뒤 Tick(time)을 호출한다.
    //    1) 현재 시간이 마디 종료 시각을 넘었으면  StartCurrentBar()
    //    2) 카운트다운 갱신
    //
    //  마디 전환(StartCurrentBar): 판정선 승격·유턴 -> 띄워 둔 노트 Active -> 마디 번호 증가 -> PrepareNextBar()
    //    PrepareNextBar()가 다음 마디 판정선을 준비하고 노트를 Ghost/Hidden으로 미리 띄운다.
    //    현재 마디를 스크롤하는 동안 다음 마디는 항상 한 발 앞서 준비돼 있다.
    //
    //  판정은 RhythmSession이 하고 버스(NoteJudged)로 알려 온다. 여기는 OnNoteJudged()에서
    //  그 결과를 노트 뷰/이펙트로 옮기기만 한다(miss 포함).
    //
    //  일은 협력자가 나눠 맡는다: JudgeLineDirector(판정선), NoteFieldSpawner(노트),
    //  CountdownView(3/2/1), JudgeEffectSpawner(판정 텍스트), NoteGeometry(좌표), GameObjectPool(풀).
    // ──────────────────────────────────────────────────────────────────────────
    public class PlayfieldView : MonoBehaviour
    {
        public RectTransform timelineParent;
        public Transform objectPoolParent;

        [Header("노트 풀링")]
        public GameObject notePrefab;

        [Header("레이어 분리")]
        public RectTransform holdLayer;     // HoldBar용 Canvas (Inspector 할당)
        public RectTransform headLayer;     // NoteHead용 Canvas (Inspector 할당)
        public GameObject holdBarPrefab;    // holdImage만 있는 별도 프리팹 (Inspector 할당)

        [Header("이펙트 풀링")]
        public GameObject effectPrefab;

        [Header("판정선")]
        public GameObject timelinePrefab; // 판정선 프리팹
        public RectTransform[] timelineTransforms = new RectTransform[LANE_GROUP_COUNT];   // 판정선의 상하 위치 좌표

        [Header("레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)

        public TextMeshProUGUI[] countdownTexts = new TextMeshProUGUI[COUNTDOWN_SLOT_COUNT];

        private IJudgementBus _judgementBus;
        private IJudgeStateReader _judge;

        private JudgeLineDirector _lines;
        private NoteFieldSpawner _field;
        private CountdownView _countdown;
        private JudgeEffectSpawner _effects;

        // 마디 진행
        private BarClock _bars;
        private BarStreamer _chart;
        private readonly List<LaneData> _nextBarLanes = new();   // 다음 스크롤을 준비 중인 마디의 레인들
        private int currentBarNumber = 0;      // 현재 마디 인덱스(0부터). StartCurrentBar 승격 시 ++
        private double currentBarEndTime = 0f; // 현재 마디의 종료 시간. Tick에서 이 값을 넘으면 다음 마디로 전환
        private bool endOfChartLogged = false; // 채보 종료 로그 1회 제한 (StartCurrentBar가 매 프레임 재진입하므로)

        // 마디 하나의 길이(초). GameManager가 음원을 이만큼 늦게 시작한다
        public double BarDuration => _bars.BarDuration;

        void Awake()
        {
            var geometry = new NoteGeometry(leftEndpoint, rightEndpoint);
            _lines = new JudgeLineDirector(new GameObjectPool(timelinePrefab, objectPoolParent), timelineParent, timelineTransforms, geometry);
            _field = new NoteFieldSpawner(new GameObjectPool(notePrefab, objectPoolParent), new GameObjectPool(holdBarPrefab, objectPoolParent),
                headLayer, holdLayer, laneTransforms, geometry);
            _countdown = new CountdownView(countdownTexts);
            _effects = new JudgeEffectSpawner(new GameObjectPool(effectPrefab, objectPoolParent));
        }

        private void OnDestroy()
        {
            if (_judgementBus != null) _judgementBus.NoteJudged -= OnNoteJudged;
        }

        /// <summary>
        /// 게임 시작 시 GameManager가 1회 호출. 채보를 적재하고 판정 버스를 구독한 뒤 첫 마디를 준비/시작한다.
        /// </summary>
        public void Init(ChartData chartData, IJudgeStateReader judge, IJudgementBus judgementBus)
        {
            if (_judgementBus != null) _judgementBus.NoteJudged -= OnNoteJudged;
            _judge = judge;
            _judgementBus = judgementBus;
            _judgementBus.NoteJudged += OnNoteJudged;

            _chart = new BarStreamer(chartData.GetFullChartList());
            _bars = BarClock.FromBpm(chartData.bpm);
            currentBarNumber = 0;
            currentBarEndTime = _bars.BarDuration;
            endOfChartLogged = false;   // 재시작 시 로그 1회 제한 초기화

            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                _effects.ShowPerfect = settingsManager.Current.showPerfect;

            _field.Reset(judge.Count);
            _countdown.Reset();

            PrepareNextBar();   // 0번(빈) 마디를 준비
            StartCurrentBar();  // 준비된 마디를 현재 마디로 승격 + 다음 마디 선행 준비
        }

        /// <summary>
        /// 매 프레임 GameManager가 현재 게임 시간을 주입하는 진입점.
        /// 마디 종료 시각을 넘으면 다음 마디로 전환하고 카운트다운을 갱신한다.
        /// </summary>
        public void Tick(double time)
        {
            if (time >= currentBarEndTime)
            {
                StartCurrentBar();
            }

            _countdown.Tick(time, _bars.BeatDuration);
        }

        /// <summary>
        /// 다음 마디를 "준비만" 한다(아직 현재 마디로 만들지 않음).
        /// 다음 마디의 레인을 모두 꺼낸 뒤 판정선을 준비하고 노트를 Ghost/Hidden 상태로 미리 띄운다.
        /// </summary>
        private void PrepareNextBar()
        {
            _nextBarLanes.Clear();
            _chart.TakeUpTo(currentBarNumber, _nextBarLanes);

            if (_nextBarLanes.Count > 0)
            {
                double nextStartTime = _bars.BarStart(currentBarNumber);
                foreach (var (group, isLTR) in _lines.Preload(_nextBarLanes, nextStartTime, _bars.BarDuration))
                {
                    // 카운트다운은 방향에 따라 좌/우 슬롯이 달라진다
                    _countdown.Activate(LaneLayout.ToCountdownSlot(group, isLTR), nextStartTime);
                }

                _field.SpawnBar(_nextBarLanes, _lines);
            }
        }

        /// <summary>
        /// 준비돼 있던 다음 마디를 "현재 마디"로 승격시킨다.
        /// 판정선 승격·유턴 → 띄워 둔 노트 Active → 다음 마디 선행 준비.
        /// </summary>
        private void StartCurrentBar()
        {
            double startTime = _bars.BarStart(currentBarNumber);
            currentBarEndTime = startTime + _bars.BarDuration;

            if (_nextBarLanes.Count == 0)
            {
                if (!endOfChartLogged)
                {
                    Debug.Log("End of Chart Reached.");
                    endOfChartLogged = true;
                }
                return;
            }

            _lines.Promote(_nextBarLanes, startTime, _bars.BarDuration);
            _field.ActivateGhosts(_lines);

            // 마디 번호 증가 후, 그 다음 마디를 다시 선행 준비 (항상 한 마디 앞서 준비 유지)
            currentBarNumber++;
            PrepareNextBar();
        }

        // 버스로 온 판정 1건을 노트 뷰/연출에 반영한다.
        private void OnNoteJudged(JudgeEvent judged)
        {
            NoteController view = _field.ApplyJudged(judged);
            if (view == null) return;   // 스폰 전이거나 이미 회수된 노트

            if (!judged.IsMiss)
            {
                _effects.SpawnHit(judged.Judge, view);
                return;
            }

            // 제 시각 전에 끊긴 꼬리는 앞쪽 꼬리 자리에 이펙트를 띄우지 않는다. 끊김은 흐려진 홀드바로 보인다
            bool brokenBeforeTail = judged.Kind == NoteKind.HoldTail && judged.Time < _judge.NoteAt(judged.NoteId).Time;
            if (!brokenBeforeTail) _effects.Spawn(JudgeType.Umm, view);
        }
    }
}
