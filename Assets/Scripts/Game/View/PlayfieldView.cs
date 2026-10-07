using System.Collections.Generic;
using SCOdyssey.Config;
using SCOdyssey.Rhythm;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 플레이필드 (게임 화면의 노트·판정선·카운트다운·이펙트) ─────────────────────
    //
    //  게임 시작 시 GameManager가 Init()을 1회 호출한다.
    //    PrepareBar(0) -> StartBar()   (0번 마디는 비어 있고, 그동안 1번 마디가 준비된다)
    //
    //  이후 매 프레임 GameManager.OnTimingAdvance가 판정 세션을 진행시킨 뒤 Tick(songTime)을 호출한다.
    //    1) 준비해 둔 마디의 시작 시각(NoteLifecycle.ActiveAt)에 닿았으면  StartBar()
    //    2) 카운트다운 갱신
    //  마디 진행과 카운트다운은 곡 시각(songTime)으로 한다. 판정 싱크가 들어간 판정 시각(judgeTime)은 판정 세션만 본다.
    //
    //  마디 전환(StartBar): 판정선 승격·유턴 -> 띄워 둔 노트 Active -> 다음 마디 PrepareBar()
    //    PrepareBar()가 다음 마디 판정선을 준비하고 노트를 Ghost/Hidden으로 미리 띄운다.
    //    현재 마디를 스크롤하는 동안 다음 마디는 항상 한 발 앞서 준비돼 있다. 빈 마디도 똑같이 넘어간다.
    //
    //  판정은 RhythmSession이 하고 버스(NoteJudged)로 알려 온다. 여기는 그 알림에 깨어나
    //  판정 엔진 상태(IJudgeStateReader)를 읽고 노트 뷰/이펙트에 반영한다(miss 포함). 이펙트는 실제 등급을 그대로 띄운다.
    //  타격음(HitSoundPlayer)과 판정 오차 기록(JudgementTimingRecorder)은 같은 버스를 따로 구독하는 자기 클래스가 맡는다.
    //
    //  일은 협력자가 나눠 맡는다: NoteLifecycle(언제 띄우고 올리고 빼는지), JudgeLineDirector(판정선),
    //  NoteFieldSpawner(노트), CountdownView(3/2/1 스프라이트), JudgeEffectSpawner(판정 텍스트), NoteGeometry(좌표), GameObjectPool(풀).
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
        public GameObject holdBarPrefab;    // 루트(RectMask2D 뷰포트) > Fill(아트) 2단 구조 프리팹 (Inspector 할당)

        [Header("이펙트 풀링")]
        public GameObject effectPrefab;

        [Header("판정선")]
        public GameObject timelinePrefab; // 판정선 프리팹
        public RectTransform[] timelineTransforms = new RectTransform[LANE_GROUP_COUNT];   // 판정선의 상하 위치 좌표

        [Header("레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)

        [Header("카운트다운")]
        public Image[] countdownImages = new Image[COUNTDOWN_SLOT_COUNT];   // 슬롯(그룹 × 진행방향) 순서. CountdownSlot과 1:1

        // GameUI_Countdown_1~3. 인덱스 = 표시 숫자 - 1
        [SerializeField] private Sprite[] countdownSprites = new Sprite[3];

        private IJudgementBus _judgementBus;
        private IJudgeStateReader _judge;

        private JudgeLineDirector _lines;
        private NoteFieldSpawner _field;
        private CountdownView _countdown;
        private JudgeEffectSpawner _effects;

        // 마디 진행
        private BarClock _bars;
        private NoteLifecycle _lifecycle;
        private BarStreamer _chart;
        private readonly List<LaneData> _nextBarLanes = new();   // 준비해 둔 마디의 레인들
        private int _nextBar;               // 준비해 둔(다음에 시작할) 마디 번호
        private bool _endOfChartLogged;

        // 마디 하나의 길이(초). GameManager가 음원을 이만큼 늦게 시작한다
        public double BarDuration => _bars.BarDuration;

        void Awake()
        {
            var geometry = new NoteGeometry(leftEndpoint, rightEndpoint);
            _lines = new JudgeLineDirector(new GameObjectPool(timelinePrefab, objectPoolParent), timelineParent, timelineTransforms, geometry);
            _field = new NoteFieldSpawner(new GameObjectPool(notePrefab, objectPoolParent), new GameObjectPool(holdBarPrefab, objectPoolParent),
                headLayer, holdLayer, laneTransforms, geometry);
            PlayfieldSettingsSO settings = PlayfieldSettingsSO.Shared;
            _countdown = new CountdownView(countdownImages, countdownSprites, settings.countdownBeats, settings.countdownEpsilonBeats);
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
            _lifecycle = new NoteLifecycle(_bars, judge);
            _endOfChartLogged = false;

            _field.Reset(judge.Count);
            _countdown.Reset();

            PrepareBar(0);
            StartBar();
        }

        /// <summary>
        /// 매 프레임 GameManager가 곡 시각을 주입하는 진입점.
        /// 준비해 둔 마디의 시작 시각에 닿으면 그 마디로 넘어가고 카운트다운을 갱신한다.
        /// </summary>
        public void Tick(double time)
        {
            if (time >= _lifecycle.ActiveAt(_nextBar))
            {
                StartBar();
            }

            _countdown.Tick(time, _bars.BeatDuration);
        }

        /// <summary>
        /// 마디를 "준비만" 한다(아직 현재 마디로 만들지 않음).
        /// 그 마디의 레인을 모두 꺼낸 뒤 판정선을 준비하고 노트를 Ghost/Hidden 상태로 미리 띄운다.
        /// </summary>
        private void PrepareBar(int bar)
        {
            _nextBar = bar;
            _nextBarLanes.Clear();
            _chart.TakeUpTo(bar, _nextBarLanes);

            if (_nextBarLanes.Count == 0)
            {
                if (_chart.IsDone && !_endOfChartLogged)
                {
                    Debug.Log("End of Chart Reached.");
                    _endOfChartLogged = true;
                }
                return;
            }

            double startTime = _lifecycle.ActiveAt(bar);
            foreach (var (group, isLTR) in _lines.Preload(_nextBarLanes, startTime, _bars.BarDuration))
            {
                // 카운트다운은 방향에 따라 좌/우 슬롯이 달라진다
                _countdown.Activate(LaneLayout.ToCountdownSlot(group, isLTR), startTime);
            }

            _field.SpawnBar(_nextBarLanes, _lines);
        }

        /// <summary>
        /// 준비돼 있던 마디를 "현재 마디"로 승격시킨다.
        /// 판정선 승격·유턴 → 띄워 둔 노트 Active → 그다음 마디 준비. 빈 마디여도 다음으로 넘어간다.
        /// </summary>
        private void StartBar()
        {
            _lines.Promote(_nextBarLanes, _lifecycle.ActiveAt(_nextBar), _bars.BarDuration);
            _field.ActivateGhosts(_lines, _lifecycle);

            PrepareBar(_nextBar + 1);
        }

        // 버스 알림에 깨어나 판정 엔진에서 그 노트의 결과를 읽어 노트 뷰/연출에 반영한다.
        private void OnNoteJudged(JudgeEvent judged)
        {
            int noteId = judged.NoteId;
            if (!_lifecycle.IsDecided(noteId)) return;

            JudgeType? grade = _judge.GradeOf(noteId);
            bool missed = grade == null;
            if (!_field.ApplyDecided(noteId, judged.Kind, judged.PairId, missed, out Vector3 at)) return;   // 스폰 전이거나 이미 반영한 노트

            if (!missed)
            {
                _effects.Spawn(grade.Value, at);
                return;
            }

            // 제 시각 전에 끊긴 꼬리는 앞쪽 꼬리 자리에 이펙트를 띄우지 않는다. 끊김은 흐려진 홀드바로 보인다
            bool brokenBeforeTail = judged.Kind == NoteKind.HoldTail && judged.Time < _judge.NoteAt(noteId).Time;
            if (!brokenBeforeTail) _effects.Spawn(JudgeType.Umm, at);
        }
    }
}
