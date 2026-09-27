# 오디오·타이밍 레이어 구조

게임 쪽 오디오(FMOD Core)와 곡 시계, 입력 시각, 판정 타이밍의 구조를 정리한 문서다. 재설계 작업의 단계, 스파이크, 통합 절차는 `Audio_migration.md`에 있다.

## 범위

| 포함 | 제외 |
|---|---|
| FMOD Core System 소유와 수명주기, 출력 장치·타입·버퍼 관리(WASAPI, ASIO), 믹서, 원샷, 로비·프리뷰 음악, 게임 곡 세션, 곡 시계, 입력 시각 변환, 판정 타이밍 | ChartEditor(`EditorFMODAudio`가 RuntimeManager를 계속 씀), FMOD Studio, 곡별 효과(EQ 등), Raw Input, 캘리브레이션 씬 |

## 용어

| 용어 | 뜻 |
|---|---|
| DSP 클록 | FMOD 믹서가 믹스한 샘플 수. 믹스 블록 단위로 계단식으로 오르며, 들리는 시각보다 조금 앞선다. |
| QPC | `Stopwatch.GetTimestamp` 기반 고정밀 타이머. 입력과 프레임 시각의 기준 |
| 곡 시계 | DSP 클록과 QPC를 짝지어 "지금 곡의 몇 초인가"를 연속된 값으로 알려 주는 객체(`ISongClock`) |
| 앵커 / 리드인 | 곡 시작 기준점 / 첫 마디 전의 빈 구간 |
| 불연속(epoch) | 곡 시계를 새로 맞추는 사건. 시작, 일시정지, 재개, 장치 변경, 재초기화가 해당한다. |
| 세대(Generation) | FMOD System을 새로 초기화할 때마다 올라가는 번호. 이전 세대의 핸들은 무효가 된다. |
| Follow-Default / Pinned | Windows 기본 장치를 따라가는 방식 / 특정 장치를 GUID로 고정하는 방식 |
| FrameBatched | 한 프레임에 모인 입력을 시각순으로 전달한 뒤 시간을 진행하는 판정 방식 |
| Synthetic 입력 | 실제 키 입력이 아닌 이벤트. 입력 맵 비활성화나 포커스 상실 때 Input System이 만드는 cancel이다. |

---

## 1. 계층과 어셈블리

```
SCOdyssey.Core           (asmdef, Assets/Scripts/Core)         System·UnityEngine만 참조. Qpc
   ↑
SCOdyssey.Audio          (asmdef, Assets/Scripts/Audio)        Core, FMODUnity, UniTask 참조
   ↑   FMOD Core System 유일 소유자
SCOdyssey.Game.Timing    (asmdef, Assets/Scripts/Game/Timing)  Core, Audio, Unity.InputSystem 참조
   ↑   입력 시각 변환, 판정 타이밍, JudgementDriver
Assembly-CSharp (기존 코드)
   App/Managers(조립 루트) · App/AudioSettingsMapper · App/SettingsMigration
   App/LegacyAudioManagerAdapter(과도기) · GameManager/ChartManager(통합 지점만)
```

**경계 규칙**
- 의존은 위 방향으로만 흐른다. Audio와 Game/Timing이 App, Game, UI, Domain 타입을 쓰면 컴파일 오류가 난다.
- Audio와 Timing은 설정 DTO, 입력 액션, 판정 상수를 모른다. 필요한 값은 App이 넘긴다.
  - 엔진 구성: `AudioSettingsMapper`가 Install 인자로 넘긴다.
  - 노트 싱크: `ISongSession.Start` 인자로 넘긴다.
  - 판정 싱크: `Func<int>`(판정 싱크 단계)로 넘긴다.
  - 백그라운드 재생 설정: `Func<bool>`로 넘긴다.
- 공개 계약에는 FMOD 타입을 노출하지 않는다. UniTask는 로드와 설정 적용 API에만 쓴다.
- `InternalsVisibleTo`는 테스트 어셈블리에만 연다.
- 새 이름은 `SCOdyssey.App`의 과도기 타입(`AudioOutputType`, `AudioOutputConfig`, `AudioBus`)과 UnityEngine의 `Audio*` 이름을 피한다. 출력 타입 enum은 `AudioOutputKind`다.
- `FMODUnity.RuntimeManager`는 가드(초기화 여부 읽기 전용) 한 곳에서만 참조한다. ChartEditor는 예외다.
- 실행 순서 상수는 Audio에 따로 둔다(`Boot.ExecutionOrder`는 참조할 수 없음). AudioEngineRunner는 -1010, JudgementDriver는 -900이다.
- 로그는 메인 스레드에서 `Debug.Log*`로 남긴다(MainScene 경로에는 CoreLogger가 없다).

## 2. 컴포넌트

**Audio(`SCOdyssey.Audio`)**

| 폴더 | 컴포넌트 | 역할 |
|---|---|---|
| 루트 | 계약(3장) | 게임플레이가 쓰는 공개 인터페이스와 값 타입 |
| Engine | AudioEngine, EngineConfigurator, BootPlan, SystemCallbackHub, FmodDebugBridge, AsioPolicy, AudioThread | System 생성·설정·init·검증, 폴백 순서, 상태, 세대, 콜백 마스크, FMOD 로그 전달, ASIO 허용 여부, 메인 스레드 검사 |
| Output | DriverLookup, DeviceCatalog, AudioOutputService | 장치 번호 찾기(GUID → 이름 → 기본), 출력 타입별 장치 목록(GUID), 출력 설정 적용(close→init) |
| Mixing | FmodMixer, FmodMixBus | ChannelGroup 트리와 버스 볼륨 |
| Playback | OneShotBank, FmodMusicPlayer(+MusicPlayers), SongPlayer, FmodSongSession, StreamLoader | 원샷, 로비·프리뷰 음악, 게임 곡 세션, NONBLOCKING 로드 판정 |
| Clock | ClockSampler, DspQpcModel, SongTimeline, SongClock, SongAnchor | DSP 원천(SCO.Song 클록 또는 가상 QPC 클록), DSP↔QPC 대응, 세그먼트, 프레임 스냅샷, 앵커 커밋 계산 |
| Hosting | AudioModuleInstaller, AudioModuleOptions, AudioModule, AudioEngineRunner, FocusPolicy, RuntimeManagerGuard, EditorAudioLifecycle | 설치와 설치 인자, 모듈 한 벌과 종료, 프레임 구동, 포커스 정책, RuntimeManager 가드, 에디터 정리 |
| Legacy | LegacyTransport(`ILegacyTransport`) | 과도기 어댑터가 쓰는 공개 API(단일 음악 슬롯, DSP 초 예약, int 원샷 슬롯, 장치 이름·선택). 모든 소비자를 옮긴 뒤 삭제한다. |
| Diagnostics | AudioOverlay, SongMetronome | 엔진 상태·세대·출력·버퍼(개발 빌드), 곡 시각 기준 클릭 예약(SP4 루프백 비교, 캘리브레이션 바탕) |

**Game/Timing(`SCOdyssey.Game.Timing`)**

| 폴더 | 컴포넌트 | 역할 |
|---|---|---|
| 루트 | 계약(LaneInputEvent, IInputTimestampSource, JudgedInput, IJudgementClient, TimingSample, IJudgementTimingLog), JudgementDriver, GameplayTimingBinding | 공개 계약, 프레임 구동(유일한 MonoBehaviour), 게임플레이 쪽 얇은 어댑터 |
| LaneInput | UnityInputSystemTimestampSource, RealtimeQpcMapper | 레인 입력 수집, `ctx.time` → QPC 변환, Synthetic 표시 |
| Judgement | JudgementTimeline, JudgementPump, TimingLog | 판정 싱크 적용, 입력·시간 진행 전달, 판정 오차 기록 |
| Diagnostics | 타이밍 오버레이 | 입력 배치, 매퍼 오프셋, 판정 오차 분포(JudgementDriver의 OnGUI에서 그림) |

## 3. 계약

아래 시그니처는 방향을 보여 주기 위한 요약이다. 구현할 때 이름과 세부는 바뀔 수 있다.

```csharp
namespace SCOdyssey.Audio
{
    public enum AudioOutputKind { Wasapi, Asio, NoSound }
    public enum EngineStatus { Uninitialized, Starting, Running, Degraded, Failed, Disposed }

    public interface IAudioEngine
    {
        EngineStatus Status { get; }
        int Generation { get; }
        AudioOutputInfo CurrentOutput { get; }   // 타입, 장치 GUID·이름, 레이트, 버퍼 길이·개수
        bool IsRequestedConfig { get; }          // 마지막으로 요청한 구성(부팅 설정, 설정 화면 적용)으로 동작 중인지. false면 폴백
        event Action<EngineStatus> StatusChanged;
    }

    public interface IAudioOutputService
    {
        IReadOnlyList<AudioDeviceInfo> GetCachedDevices(AudioOutputKind kind);
        UniTask<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(AudioOutputKind kind, bool refresh, CancellationToken ct);
        bool IsSupported(AudioOutputKind kind);
        // 진행 중이면 Busy. Applied일 때만 호출자가 설정을 저장한다.
        UniTask<AudioApplyResult> ApplyAsync(AudioOutputRequest request, CancellationToken ct);
    }

    public interface IAudioMixer { IMixBus Master { get; } IMixBus Music { get; } IMixBus HitSound { get; } IMixBus Sfx { get; } }
    public interface IMixBus { float Volume { get; set; } }   // 선형 0~1

    public interface IOneShotPlayer
    {
        OneShotId Register(string fileName);   // 멱등. 실패하면 OneShotId.None
        void Play(OneShotId id);               // 할당 없음. 잘못된 id는 무시
    }

    public interface IMusicPlayers { IMusicPlayer Lobby { get; } IMusicPlayer Preview { get; } }
    public interface IMusicPlayer
    {
        UniTask<AudioLoadResult> PlayAsync(string fileName, bool loop, CancellationToken ct);  // 마지막 요청만 유효
        void Stop();
        bool IsPlaying { get; }
    }

    public interface ISongPlayer
    {
        UniTask<SongLoadResult> LoadAsync(string fileName, CancellationToken ct);  // 성공, 실패(사유), 취소
        ISongSession Current { get; }
    }

    public interface ISongSession
    {
        SongSessionState State { get; }
        PauseReason PauseReason { get; }
        bool IsAudioFinished { get; }
        ISongClock Clock { get; }
        void Start(double leadInSeconds, int audioOffsetMs);   // 노트 싱크는 여기서만 래치
        void Pause(PauseReason reason);
        void Resume();
        void Stop();
        event Action<SongSessionEvent> Changed;   // enum: Started, Paused, Resumed, AudioStarted, AudioEnded, Recovered, Stopped, Disposed
    }

    public interface ISongClock
    {
        SongFrame Frame { get; }                  // 이번 프레임 스냅샷
        bool TrySongTimeAt(long qpcTicks, out SongTimePoint point);   // 보관한 세그먼트(최근 8개)보다 오래됐으면 false
        event Action<ClockDiscontinuity> Discontinuity;
    }

    public readonly struct SongFrame
    {
        public readonly double SongTime;   // Ready·Starting에서는 0
        public readonly long QpcTicks;     // 스냅샷을 만든 시각
        public readonly int Epoch;
        public readonly bool IsRunning;
    }

    public readonly struct SongTimePoint   // 임의 시각의 곡 시각(입력 판정용, 결정적)
    {
        public readonly double SongTime;
        public readonly int Epoch;
        public readonly bool IsRunning;    // 그 시각에 곡 시계가 흐르고 있었는지
    }
}
```

```csharp
namespace SCOdyssey.Game.Timing
{
    public readonly struct LaneInputEvent
    {
        public readonly int Lane;            // 1~4
        public readonly bool IsDown;
        public readonly long QpcTicks;
        public readonly bool IsSynthetic;
    }

    public interface IInputTimestampSource { int Drain(List<LaneInputEvent> into); }

    public interface IJudgementClient
    {
        void OnLaneInput(in JudgedInput input);        // lane, isDown, qpc, songTime, judgeTime, judgeable, epoch
        void Advance(double songTime, double judgeTime);
        void OnFrame(ISongSession session);            // 매 프레임 호출. 상태 확인용
    }

    public enum TimingKind { Press, HoldBody, Release, Miss }

    public interface IJudgementTimingLog
    {
        void Record(TimingKind kind, int grade, double errorMs);   // 판정 싱크 단계와 Epoch는 로그가 채운다
    }
}
```

## 4. 엔진 수명주기

**설치**(`Managers.InitServices`에서, 설정을 읽은 뒤 `AudioModuleInstaller.Install(host, AudioModuleOptions)`)
1. 에디터 정리 훅을 가장 먼저 등록한다.
2. 메인 스레드와 COM 아파트먼트(STA 여부)를 기록한다.
3. 부팅 시도를 순서대로 실행한다(아래). 성공하면 믹서, 원샷, 음악 재생기를 만든다.
4. 계약을 ServiceLocator에 등록한다. JudgementDriver와 판정 로그는 Managers가 같은 GameObject에 붙이고 등록한다.
5. Install이 예외를 던지면 Installer가 만든 것을 모두 해제하고 다시 던지고, Managers가 잡는다. 이때는 소리 없는 과도기 어댑터(IsLoaded 즉시 true, GetDSPTime은 QPC)를 등록하고 부팅을 계속한다. 새 계약(ISongPlayer 등)은 등록되지 않는다(게임이 새 계약을 쓰기 시작하는 I1 전에 no-op 모듈로 바꾼다).
6. 판정 타이밍: Managers가 `JudgementDriver.Install(gameObject, inputManager.LaneTimestampSource, 판정 싱크 Func)`로 붙이고, JudgementDriver와 `IJudgementTimingLog`를 등록한다.

**시도 한 번의 순서**(메인 스레드)
```
System_Create
1. setOutput(kind)
2. Pinned: setDriver(ResolveIndex(guid, name))   // GUID → 이름 → 0(기본 장치)
   Follow-Default: setDriver를 부르지 않는다(부르면 기본 장치 자동 전환이 되지 않았다, SP10). close→init으로 이전 선택이 남아 있을 때만 setDriver(0)
3. getDriverInfo(…, out systemRate)
   WASAPI: mixerRate = min(systemRate, 48000). 값이 0이면 48000
   ASIO:   mixerRate = systemRate
   setSoftwareFormat(mixerRate, STEREO, 0)
4. setDSPBufferSize(length, count)      // 항상 명시. WASAPI ×4, ASIO ×2 요청
5. setSoftwareChannels(64)
6. setCallback(콜백, 모드별 마스크)       // 5장 표
7. init(256, NORMAL, IntPtr.Zero)
8. 검증: getOutput, 현재 장치 GUID, getDSPBufferSize, getSoftwareFormat
   결과가 OK여도 실제 상태가 요청과 다르면 실패로 본다.
성공 → Generation++ → 믹서 재구성, 볼륨 재적용 → 상태 발행
실패 → 이번 System은 release하고 다음 시도는 새 System_Create
```

**부팅 폴백**: 요청 구성 → WASAPI 기본 장치 512×4 → NOSOUND. 명령행 인자 `-sco-audio-safe`가 있으면 요청 구성을 건너뛴다. 부팅 때의 폴백은 저장값을 바꾸지 않는다.

**상태**
```
Uninitialized → Starting → Running
                         → Degraded (NOSOUND만 성공)
                         → Failed   (System_Create 실패)
Running → (설정 적용·장치 손실) → 재구성 → Running | Degraded
모든 상태 → Shutdown → Disposed
```
- Degraded나 Failed이면 재생기, 원샷, 세션은 FMOD를 호출하지 않고 no-op으로 동작한다. 세션은 QPC 시계로 진행한다.

**종료와 에디터 정리**
- Shutdown은 멱등이며, 초기화가 어느 단계에서 멈췄든 만든 것만 해제한다.
- 순서: 모듈 CTS 취소 → 콜백 해제(`setCallback(null, 0)`) → 채널·Sound·ChannelGroup 해제 → System release → FMOD Debug 종료(에디터는 FILE 모드로 되돌림, EditorUtils와 같게) → 에디터 이벤트 구독 해제
- 호출 시점: 플레이 모드 종료, 도메인 리로드 직전, 앱 종료, Runner가 소유자일 때의 OnDestroy
- 도메인 리로드 뒤 Runner는 자신이 엔진 소유자가 아니면 스스로 비활성화한다.
- 부팅과 씬 로드 때 RuntimeManager가 초기화되지 않았는지 확인한다. 초기화되어 있으면 오류 로그를 남긴다(`AudioModuleOptions.EnforceRuntimeManagerGuard`, 게임과 하네스는 켠다). ChartEditorScene은 Managers 없이 따로 열고 RuntimeManager를 쓰므로 가드 대상이 아니다.
- 모듈 하네스(`Testing/AudioHarness`, `SCO_AUDIO_HARNESS`)는 게임 없이 모듈만 설치해 시험한다.

## 5. 출력 변경과 장치

**설정 적용(`ApplyAsync`)**: 로비와 설정 화면에서만 쓴다.
1. 이미 진행 중이면 `Busy`로 거절한다. 곡이 진행 중(세션이 Ready·Stopped·Disposed가 아님)이거나 지원하지 않는 타입이면 `Rejected`다.
2. `UniTask.Yield` 뒤에 전제를 검사하고 변경을 분류한다. 볼륨만 바뀌었으면 즉시 적용하고 끝낸다.
3. 재생기와 세션에 "재구성 시작"을 알린다. 각자 되살릴 상태를 기억한다(로비 BGM 파일·루프·재생 여부 등).
4. close → 재설정 → init(4장 순서). 적용 폴백: 요청 구성 → 직전에 동작하던 구성 → WASAPI 기본 512×4 → NOSOUND
5. ChannelGroup, 원샷, 볼륨을 다시 만들고 재생기를 되살린다. 곡 시계는 리셋한다(Generation 변경).
6. 결과를 돌려준다. **요청 구성으로 성공했을 때만** 호출자가 설정을 저장한다. 그래서 적용 중 크래시가 나도 다음 부팅이 같은 구성으로 크래시하지 않는다.

**장치 목록**(DeviceCatalog)
- 현재 타입은 메인 System으로 열거하고, 다른 타입은 초기화하지 않은 임시 System으로 열거한 뒤 곧바로 release한다. 메인이 ASIO이면 임시 ASIO System을 만들지 않는다(ASIO는 프로세스당 하나).
- "NoSound Driver"(`Guid.Empty`)는 목록에서 뺀다. 저장과 비교는 GUID로 하고, 이름은 보조 키로 쓴다.
- `GetDevicesAsync`는 한 프레임 양보한 뒤 메인 스레드에서 동기로 열거한다(FMOD를 메인 스레드에 고정하기 위해서다).
- 캐시가 비어 있는데 동기 목록이 필요하면(과도기 어댑터) 현재 타입을 곧바로 열거한다.

**콜백 마스크와 처리**(마스크는 항상 명시한다. 기본값 ALL은 FMOD 자동 장치 전환을 꺼 버린다)

| 모드 | 마스크 | 처리 |
|---|---|---|
| Follow-Default | ERROR, DEVICELOST, DEVICEREINITIALIZE | FMOD 자동 전환에 맡긴다. DEVICEREINITIALIZE가 오면 곡 시계를 리셋하고, 게임 중이면 일시정지한다. |
| Pinned | 위 마스크 + DEVICELISTCHANGED | 목록이 바뀌면 GUID가 있는지 확인한다. 없어졌으면 1초 디바운스 뒤 기본 장치로 close→init한다. 장치가 돌아와도 자동으로 복귀하지 않는다(다음 부팅이나 재적용 때 복원). |
| 공통 | – | DEVICELOST는 재구성 경로(적용 폴백)로 처리한다. 게임 중이면 즉시 일시정지하고, 재개는 사용자가 Pause UI에서 한다. NOSOUND로 떨어지면 로비에 처음 들어갈 때 경고 로그를 1회 남긴다. |

- 장치 콜백은 `system.update()` 안(메인 스레드)에서 오므로 bool 플래그로 받는다. 플래그는 지역 변수로 복사하고 지운 뒤 처리한다. 세대가 바뀌면 플래그를 초기화한다.

**ASIO**
- `RuntimeInformation.ProcessArchitecture == X64`일 때만 노출한다(ARM64 미지원).
- 버퍼 선택 UI에는 "ASIO 제어판의 버퍼 크기와 같은 값을 고르세요"라는 안내를 둔다. init 뒤 실제 `getDSPBufferSize`를 표시한다.
- 출력 채널은 1/2로 고정한다.
- 에디터에서는 EditorPrefs 토글 "에디터에서 ASIO 허용"(기본 OFF)이 켜져 있을 때만 ASIO로 부팅한다.

## 6. 믹서와 원샷

```
System Master      ← 곡 시계 기준 클록. pause·mute·pitch·volume을 절대 바꾸지 않는다
└─ SCO.Master      (Master 볼륨, 로비 포커스 음소거)
   ├─ SCO.Music    (BGM 볼륨)
   │   ├─ SCO.Song (게임 곡 전용, pause·pitch 금지)
   │   └─ 로비·프리뷰·과도기 재생기 채널
   ├─ SCO.HitSound (타격음 볼륨)
   └─ SCO.Sfx      (효과음 볼륨)
```
- 모든 그룹은 DSP 클록을 전파하도록 연결한다. 어떤 그룹도 pause하지 않는다.
- 채널 우선순위: 곡과 음악 0, 타격음 64, 효과음 128. `setSoftwareChannels(64)`, `init(256)`으로 에디터와 빌드를 같게 둔다.
- 원샷: `CREATESAMPLE | _2D | LOOP_OFF | IGNORETAGS | LOWMEM`로 동기 로드하고, 파일명으로 멱등 등록한다. 재생은 버스 그룹에 `playSound(paused: false)` 한 번이며, 할당, 로그, 채널별 볼륨 설정이 없다. 재구성 뒤에는 같은 id로 다시 로드한다.

## 7. 곡 재생과 곡 시계

**기호**: q는 QPC(초), R은 믹서 레이트, L은 블록 길이, N은 블록 개수, Fs는 음원 레이트, Z는 음원 시작 곡 시각이다.

**로드**
- 게임 곡: `CREATESTREAM | NONBLOCKING | ACCURATETIME | IGNORETAGS | LOOP_OFF`. 로드 결과는 성공, 실패(파일 없음, 형식 오류, 시간 초과 10초), 취소다.
- 로비·프리뷰: `CREATESTREAM | NONBLOCKING | IGNORETAGS`. 프리뷰는 150ms 디바운스 뒤에 연다.
- 세션은 자신을 만든 씬이 언로드되거나 다음 로드가 오면 Dispose된다.

**앵커 커밋**(시작과 재개가 같은 루틴을 쓴다. DSP는 한 번만 읽는다)
```
Z = leadIn + audioOffsetMs / 1000.0          // 노트 싱크를 적용하는 유일한 지점(실수 나눗셈)
pos = τ0 - Z                                  // τ0: 커밋할 곡 시각(시작 0, 재개 T_p)
채널 준비: playSound(sound, SCO.Song, paused: true) → 준비 완료 대기(NONBLOCKING 스트림은 playSound 뒤 비동기로 되감는다)
          → pos > 0이면 setPosition(round(pos·Fs), PCM). ERR_NOTREADY면 다음 프레임에 다시 시도하고, 그 밖의 실패는 로드 실패로 처리
준비 완료 확인: 상태가 READY 또는 PLAYING(열기·seek·버퍼링 중 아님)이고 굶주리지 않음
p    = 곡 그룹 DSP 클록(부모 클록)
lead = max(3·L, 0.015·R)
Cf   = p + lead                               // 곡 시계가 움직이기 시작하는 DSP 시각
if (pos < 0)  S = Cf + round(-pos·R)          // 리드인이 남았으면 그만큼 뒤에 소리 시작
else          S = Cf
setDelay(S), setPaused(false)
세그먼트 기록: 시작 DSP Cf, 소리 DSP S, τ0
```
- 음원이 없거나 끝난 뒤의 재개, 엔진 Failed일 때는 채널 없이 같은 세그먼트만 기록한다(무음 커밋).

**곡 시계 계산(최소안)**
- 매 프레임 Update와 LateUpdate에서 (QPC 앞, DSP 클록, QPC 뒤)를 읽는다. 두 QPC 차이가 50µs를 넘으면 버린다. DSP는 `SCO.Song` 그룹 클록이다(곡 채널 `setDelay`와 같은 도메인). 엔진을 쓸 수 없으면(Degraded·Failed) QPC로 흐르는 가상 클록(48kHz)을 넣어 무음 세션도 같은 경로로 진행한다.
- 모델은 모듈이 하나 두고 세대마다 리셋한다. 세션마다 곡 시계(세그먼트)를 따로 가진다.
- DSP↔QPC 관계는 최근 10초 창에서 `c - q·R`의 최댓값(하한 포락선)으로 오프셋을 잡는다. 믹서는 블록마다가 아니라 OS 믹스 주기(WASAPI 약 10ms)마다 여러 블록을 몰아 믹스하므로 원시 DSP는 그 주기만큼 계단진다.
  - 창이 1초면 프레임 읽기 위상이 몇 개로 묶여(60fps에서 약 3.3ms 간격) 추정이 0.7~4.5ms 오르내렸다(SP3 CSV). 10초 창은 0.25~2.2ms였다.
  - **하향 계단**: 1초보다 오래된 최댓값이 최근 1초 최댓값보다 8ms 넘게 높으면 버린다. 언더런 등으로 DSP가 영구히 뒤처져도 1초 안에 따라간다. 정상 측정에서 두 값의 차이는 최대 4.7ms라 오판정은 없었다.
- **클램프**: `c_read ≤ DspAt(q) ≤ c_read + S_max`(c_read는 이번 프레임의 원시 DSP 값). 믹서가 멈추면 곡 시계도 곧바로 멈춘다. `S_max = max(L·(N+1) 샘플, 32ms)`로 고정한다. 프레임 사이 계단으로 갱신하지 않는다(메인 스레드 멈춤이 섞여 부풀려진다).
- **단조 보장**: 세그먼트 안에서 DspAt이 직전 값보다 작으면 직전 값을 쓴다(포락선 창이 밀릴 때 최대 약 2ms 역행이 관측됨).
- 곡 시각: 세그먼트 시작 전이면 τ0, 시작 뒤면 `τ0 + (DspAt(q) - Cf) / R`
- 불연속(시작, 일시정지, 재개, Generation 변경, DEVICEREINITIALIZE)에서 리셋한다.
- 드리프트 항과 평활 렌더 시계는 두지 않는다(SP3). 오프셋을 창에서 계속 다시 잡으므로 드리프트가 20ppm이어도 창 안 오차는 0.2ms이고, 이 모델만으로 프레임 사이 오차 p99가 1ms 이하였다.
- 곡 시계는 세션이 커밋·일시정지 때 넣는 세그먼트(멈춤 또는 흐름)를 최근 8개 보관한다. 프레임보다 조금 이른 입력이 이전 세그먼트(예: 일시정지 직전)에 속할 수 있기 때문이다. 모델이 리셋되기 전에 만든 흐르는 세그먼트는 DSP 도메인이 달라 계산하지 않는다. Timeline과 BGA는 `Frame.SongTime`을 쓰고, BGA는 100ms 이상 벌어질 때만 영상을 다시 맞춘다.
- `Channel.getPosition`은 믹스 블록 단위로만 맞으므로(±2블록) 싱크 판단에 쓰지 않는다.
- 콜백 없는 정지 감지: Starting, LeadIn, Playing 중에 포커스가 있는데 원시 DSP 값이 0.5초 넘게 그대로면 장치 사유(`DeviceChanged`)로 일시정지한다(재초기화는 하지 않음). 다시 열 음원이 없으므로 Recovering을 거치지 않고, 클록이 다시 흐르면 사용자가 재개한다.

**일시정지와 재개**
- 일시정지(Starting, LeadIn, Playing, Resuming에서만, 멱등)
  - T_p = 지금 곡 시각. Starting이면 0
  - 채널을 멈추고 곡 시계를 T_p에 고정한다.
  - 음원이 남아 있으면 새 채널을 paused로 만들어 T_p 위치로 미리 seek한다.
- 재개: 앵커 커밋을 τ0 = T_p로 다시 한다. 리드인 중이었어도 남은 대기만 다시 예약된다.
- 매번 샘플 단위로 재예약하므로 오차가 누적되지 않는다. stop이 반영되기 전에 이미 믹스된 최대 1블록만 겹친다.

**재구성(Generation 변경) 중 세션 처리**

| 상태 | 재구성 전 | 재구성 뒤 |
|---|---|---|
| 로드 중 | 로딩 Sound를 최대 200ms 기다린 뒤 release | 새 세대에서 다시 연다(시간 제한 유지) |
| Ready | Sound release | 다시 연다 |
| Starting | 채널과 Sound release | 다시 열고 τ0로 seek한 뒤 커밋. 3초를 넘기면 무음 세션으로 전환(채보는 진행) |
| LeadIn, Playing, Resuming | 먼저 일시정지(장치 사유) | – |
| Paused, Recovering | Recovering으로 두고 채널 release | 음원이 남았으면 다시 열고 T_p로 seek. 준비되면 Recovered 발행 |
| Stopped | 채널과 Sound 버림 | 아무것도 안 함 |

**세션 상태**

| 현재 | 이벤트 | 다음 |
|---|---|---|
| (로드) | 완료 / 실패·시간 초과·취소 | Ready / 결과만 반환 |
| Ready | Start | Starting → 커밋되면 LeadIn 또는 Playing |
| LeadIn | 곡 시각 ≥ Z | Playing(AudioStarted) |
| Playing | 음원 종료 | Playing 유지(IsAudioFinished, AudioEnded) |
| Starting, LeadIn, Playing, Resuming | 일시정지(사용자, 포커스, 장치, 스트림) | Paused |
| Paused | Resume / 재구성 | Resuming / Recovering |
| Recovering | 재로드·seek 완료 | Paused 또는 Resuming |
| Resuming | 커밋 | LeadIn 또는 Playing |
| 모든 상태 | Stop / Dispose | Stopped / Disposed |

- Ended 상태는 없다. 음원이 끝나도 Playing으로 남아 일시정지할 수 있다.
- 이벤트는 상태 전이가 끝난 뒤 순서대로 발행한다(구독자 안에서 다시 호출해도 순서가 지켜진다). Resumed는 재개 커밋 때, Recovered는 재구성 뒤 다시 열고 seek가 끝났을 때 발행한다.

**음원 종료 감지**(Playing이고 아직 끝나지 않았을 때만)
- 채널이 멈췄고 곡 시각이 끝 무렵이면 종료로 처리한다.
- 채널이 멈췄는데 끝까지 0.5초 넘게 남았으면 스트림 끊김으로 보고 일시정지한다. 같은 위치에서 두 번 끊기면 종료로 처리한다.
- 곡 시각이 끝보다 0.25초 이상 지나면 채널 상태와 관계없이 종료로 처리한다.

**과도기 경로(S4a~C)**
- 옛 `IAudioManager` 소비자(MainUI, AdventureUI, GameDataLoader, GameManager, ChartManager, SoundSettingUI)는 App의 `LegacyAudioManagerAdapter`가 `AudioModule.Legacy`(`ILegacyTransport`)와 `IAudioMixer`로 전달한다.
- 음악 슬롯 하나를 SCO.Music 아래에 둔다. 로드 플래그와 예약 순서는 옛 FMODAudioManager와 같다(`CREATESTREAM | NONBLOCKING`, playSound(paused) → setDelay → setLoopCount → setPriority(0) → unpause). 로드 실패는 옛 코드처럼 IsLoaded가 오지 않는 것으로만 드러난다.
- `DspSeconds = 기준 초 + (SCO.Music 클록 − 기준 클록) / R`. 재구성 직전에 기준 초를, 직후에 기준 클록을 잡아 세대를 넘어도 단조 증가한다. 엔진을 쓸 수 없으면 QPC로 진행한다.
- 재구성 때 슬롯 상태(파일, 반복, ms 위치, 일시정지, 아직 시작 전인 예약)를 기억해 다시 열고 이어서 재생한다. ms 단위라 샘플 단위로 맞지는 않는다.
- 채널 END 콜백(System.update 안, 메인 스레드)으로 끝을 알아채 핸들을 버린다. 옛 코드는 곡이 끝난 뒤에도 매 프레임 IsPlaying을 읽는데, 끝난 채널에 `isPlaying`을 부르면 ERR_INVALID_HANDLE 오류 콜백이 난다(S4b 릴리스 빌드에서 곡마다 1건 확인).
- 옛 FMODAudioManager·FMODAudioPreInit과 FMOD 플러그인 수정(Platform.cs의 버퍼 setter)은 S4b에서 지웠다. 게임 씬에는 FMOD Studio 컴포넌트(StudioListener 등)가 없다.

## 8. 입력과 판정 타이밍

**입력 수집**
- InputManager가 레인 콜백에서 `UnityInputSystemTimestampSource.Push(lane, isDown, ctx.time)`를 호출한다(`IsInputActive`일 때만).
- Synthetic 표시: `SwitchToUI()`와 `Disable()` 안의 입력 맵 비활성화 구간, 그리고 `!Application.isFocused`일 때 들어온 이벤트
- `ctx.time` → QPC 변환: 프레임마다 (QPC 앞, `InputState.currentTime`, QPC 뒤)를 읽어 오프셋을 구하고, 최근 32개의 중앙값을 쓴다.
- 1단계 버퍼는 256칸을 미리 할당한 링이다. 넘치면 오래된 것부터 버리고 개수를 센다. 생산자와 소비자가 모두 메인 스레드라 동시성 자료구조는 쓰지 않는다.
- 매퍼 표본은 JudgementDriver가 매 프레임 `Drain`을 부를 때 하나씩 넣는다. 첫 Push 때 표본이 없으면 그 자리에서 하나 읽는다. Synthetic 구간은 `BeginSynthetic/EndSynthetic`(중첩 가능)이다.

**JudgementDriver 프레임 처리**(Update -900, AudioEngineRunner 다음)
1. 입력 버퍼를 비워 복사본에 담는다(Pump 도중 Push가 와도 안전).
2. 클라이언트가 없으면 버리고 끝낸다. Attach 이전 시각의 이벤트도 버린다.
3. 각 입력: `qpc = min(qpc, Frame.QpcTicks)`로 클램프한 뒤 곡 시각으로 바꾼다. 보류하지 않는다.
4. `judgeTime = songTime - 판정 싱크 단계 × 0.003`(JudgementTimeline). `Judgeable = 입력 시각에 곡 시계가 흐르고 있었음(SongTimePoint.IsRunning) && !Synthetic`. 그래서 ESC보다 먼저 눌린 같은 프레임 입력은 판정된다. NaN 시각과 세그먼트를 찾지 못한 시각은 거부한다.
5. qpc 순서로 `OnLaneInput`을 부른 뒤 `Advance(Frame.SongTime, 판정 시각)`을 부른다(FrameBatched).
6. `OnFrame(session)`을 부른다.
7. 세션이 Disposed되면 클라이언트를 자동으로 뗀다.

**판정 싱크 래치**: JudgementTimeline이 App이 넘긴 `Func<int>`를 한 번만 읽는다. 시점은 세션 Started 이벤트이고, 놓쳤으면 첫 진행 프레임, Started 뒤에 Attach했으면 Attach 즉시다. 판정 창과 등급 규칙은 게임플레이(ChartManager)가 가진다.

**게임플레이 쪽 연결**(`GameplayTimingBinding`)
- GameManager가 `GameplayTimingBinding.Attach(driver, session, onAdvance(songTime, judgeTime), onLaneInput(in JudgedInput), isRunning, onExternalPause(PauseReason))`로 붙이고, 끝나면 Dispose한다(멱등).
- 판정할 수 없는 입력은 release만 넘긴다(눌림 상태 해제용). 판정할 수 있는 입력과 Advance는 `isRunning`일 때만 넘긴다. `isRunning`에는 일시정지 여부를 넣지 않는다(일시정지가 곡 시계를 멈추므로).
- `OnFrame`에서 매 프레임 세션 상태를 확인한다. `Paused/Recovering`이고 사용자 일시정지가 아니면 GameManager에 일시정지를 알린다(일시정지 한 번에 한 번).
- 곡 시작(Started)과 재개(Resumed) 때도 포커스를 확인해, 없으면 포커스 사유로 일시정지한다. AudioEngineRunner의 `OnApplicationFocus(false)`는 게임 세션을 직접 일시정지한다.
- CharacterAnimator의 "같은 프레임 = 동시 입력" 전제는 유지된다. 한 프레임의 입력이 한 번의 처리 안에서 전달되기 때문이다.

**판정 기록**: `TimingSample`에 종류(Press, HoldBody, Release, Miss), 등급(int), 부호 있는 오차 ms, 래치한 판정 싱크 단계, Epoch를 남긴다. `TimingLog`(JudgementDriver 소유)가 최근 4096건을 링에 보관하고, 종류별 최근 N건 평균·표준편차(`Summarize`)를 할당 없이 낸다. 후속 캘리브레이션과 개발 오버레이가 쓴다.

**타이밍 오버레이**(에디터·개발 빌드, `JudgementDriver.OverlayVisible`): 클라이언트 연결, 판정 싱크 래치 값, 이번 배치·전달·거부·프레임 시각으로 자른 입력 수, 소스의 Push·Synthetic·버림 수와 매퍼 오프셋, Press·Release 오차 분포. 오디오 오버레이 아래에 그린다.

## 9. 설정

**SettingsData v2(오디오 부분)**
```csharp
public int    settingsVersion = 2;
public string audioOutputType = "WASAPI";   // "WASAPI" | "ASIO"
public string deviceGuid      = "";         // 비어 있으면 Follow-Default
public string deviceName      = "";         // 보조 키, 표시용
public int    systemRate      = 0;          // 마지막으로 확인한 장치 레이트
public int    dspBufferLength = 256;
public int    dspBufferCount  = 4;          // UI에 노출하지 않음. ASIO는 2 요청
// 유지: 볼륨 4종, playInBackground, audioOffsetMs, judgmentOffset
```

**v1 → v2 마이그레이션**(`App/SettingsMigration`, 키 `SCOdyssey.Settings.v1` 유지)
- v1 판별: JSON에 `settingsVersion`이 없으면 v1이다(JsonUtility는 없는 필드를 기본값으로 채우므로 필드 값으로는 알 수 없다).
- 파싱에 실패하면 원문을 `…v1.corrupt.bak`에 남기고 기본값으로 시작한다.
- v1이면 원문을 `…v1.bak`에 한 번 저장한다.
- 출력은 WASAPI, 장치는 Follow-Default로 둔다(v1 인덱스는 믿을 수 없음).
- 버퍼 길이는 `{64, 128, 256, 512, 1024}[audioBufferIndex]`로 구한다. 범위 밖이면 256이다. 64·128을 256으로 올릴지는 SP13 결과로 정한다.
- 손상된 값을 검증한다(매 부팅): GUID 파싱 실패는 빈 값(장치 이름도 비움), 모르는 타입 문자열은 WASAPI, 프리셋(`64, 128, 256, 512, 1024`, WASAPI·ASIO 공통, 480 등 추가는 SP13)에 없는 길이는 가까운 프리셋(같은 거리면 작은 쪽), 개수가 2~8 밖이면 타입 기본값(WASAPI 4, ASIO 2), 볼륨은 0~1로 자른다.
- v1 필드(`audioDeviceIndex`, `audioBufferIndex`)는 S5b에서 지웠다. 마이그레이션은 원문 JSON에서 `audioBufferIndex`만 따로 읽는다.
- `ResetToDefault`는 새 SettingsData로 바꾸므로 출력 필드도 기본값(WASAPI, 기본 장치, 256×4)이 되고, 다음 부팅부터 적용한다.

**적용과 저장**
- `SettingsManager.Apply`: 볼륨을 `IAudioMixer`에 넣고, `Application.runInBackground = playInBackground`로 둔다. 출력 설정은 적용하지 않는다(부팅은 Installer, 설정 화면은 ApplyAsync가 맡는다).
- 로비 음소거는 FocusPolicy가 `Func<bool>`(playInBackground)을 포커스 변경 때마다 읽어 처리한다.
- 출력 설정은 ApplyAsync가 요청 구성으로 성공했을 때(Applied, Unchanged)만 저장한다. 폴백, Busy, Rejected, Failed는 저장하지 않고 경고 로그를 남긴다.

**사운드 설정 화면**(`UI/Settings/SoundSettingUI`)
- 출력 타입을 따로 고르지 않는다. 장치 목록 하나(기본 장치 → WASAPI 장치 → ASIO 드라이버, ASIO는 지원할 때만)에서 고르면 `audioOutputType`, `deviceGuid`, `deviceName`이 함께 정해진다. 이름은 구분 표시 없이 그대로 보여 준다. 목록은 비동기로 읽고 "검색 중"을 표시하며, 목록에 없는 저장 장치는 "(연결 안 됨)"으로 보인다.
- 버퍼는 프리셋 하나(`64, 128, 256, 512, 1024`)의 샘플 수만 보여 준다(블록 ms는 실제 출력 지연으로 오해하기 쉬워 표시하지 않는다). 장치를 골라 타입이 바뀌어도 길이는 그대로 두고, UI에 없는 개수만 타입 기본값(WASAPI 4, ASIO 2)으로 둔다. ASIO는 실제 버퍼를 드라이버 제어판 값으로 정한다.
- Save: 볼륨·백그라운드 재생은 바로 저장하고, 출력은 ApplyAsync 결과를 본 뒤 저장한다. 적용 중에는 Save를 막는다(`_applying`).
- 부팅 폴백은 MainUI가 처음 표시될 때 한 번 경고 로그로 남긴다(공용 알림 UI가 생기면 화면에 띄운다). 새 문자열은 `App/AudioUiText`에 모은다.
- 빌드 검증기(`Editor/AudioBuildValidator`): MusicSO 곡·프리뷰, MainUI 로비 BGM, ChartManager 타격음 파일이 StreamingAssets에 있는지 확인하고 없으면 빌드를 멈춘다. Force Single Instance가 꺼져 있으면 경고한다.
- 에디터에서 ASIO를 시험하려면 메뉴 SCOdyssey → Audio → 에디터에서 ASIO 허용을 켠다(`AsioPolicy`, EditorPrefs).

## 10. 스레드 규칙

- 우리가 만드는 스레드는 없다. FMOD API는 전부 메인 스레드에서 부른다. 에디터와 개발 빌드에서는 메인 스레드인지 검사한다.
- FMOD 스레드에서 오는 것은 ERROR 콜백과 FMOD Debug 콜백뿐이다. 짧은 `lock` 안에서 고정 크기 배열에 복사만 하고, 로그는 메인 스레드가 남긴다. 콜백 안에서는 Unity API와 FMOD API를 부르지 않는다.
- 믹서 스레드 콜백(MIDMIX, OUTPUTUNDERRUN)은 등록하지 않는다. 관리 코드가 GC로 멈추면 오디오가 끊길 수 있기 때문이다.
- 콜백 델리게이트는 static 필드에 보관하고, 메서드는 `[AOT.MonoPInvokeCallback]` static으로 둔다(`FMODUnity.RuntimeManager`와 같은 방식).
- UniTask continuation은 메인 PlayerLoop에서 돈다. `SwitchToThreadPool`은 쓰지 않는다(FMOD와 ASIO를 메인 스레드에 고정).
- 모듈 수준 CTS를 두고 Shutdown에서 취소한다. 모든 await 뒤에는 Shutdown 여부와 Generation을 다시 확인한다.
- 모듈이 외부로 보내는 이벤트는 try/catch로 감싼다. 상태 전이 중에 생긴 이벤트는 모았다가 작업이 끝난 뒤 발행한다. 구독자는 OnDestroy에서 구독을 해제한다.
- 핫패스(Pump, 원샷 재생, 프레임 스냅샷)는 할당 없이 인덱스 for 루프로 쓴다.

## 11. 코드 작성 원칙

- 이해하고 유지보수하기 쉽게 쓴다. 영리한 기법보다 읽기 쉬운 구조를 고른다.
- 주석은 핵심 위치(클래스 역할, 시계 수식, 폴백, 콜백 규칙)에만, 역할을 간결하게 적는다.
- 삼항 연산자는 쓰지 않는다. if/else로 풀어 쓴다.
- 동시성이 없는 곳에 동시성 자료구조를 쓰지 않는다.
- `using FMOD;`는 쓰지 않는다(`FMOD.System`이 `System`과 충돌). 속성 이름은 `CoreSystem`, 구조체 필드는 `QpcTicks`처럼 네임스페이스·타입 이름을 가리지 않게 짓는다.

## 12. 알려진 한계와 후속

- 키보드 입력은 Unity가 프레임마다 처리한다(60fps에서 처리까지 0~16.7ms). SP11(데스크탑 에디터)에서 `ctx.time`은 처리 시점보다 앞선 1ms 미만 해상도의 시각이었지만, 실제 누른 시각과의 차이는 재지 않았다. Raw Input 소스는 `IInputTimestampSource`를 구현해 후속 작업으로 붙인다.
- 새 곡 시계는 기존 계단식 읽기보다 평균 "OS 믹스 주기/2"만큼 앞선다. 같은 노트 싱크 값의 체감이 달라진다(SP3 데스크탑 WASAPI 256×4에서 약 +5.9ms, ASIO4ALL 256×2에서 약 +9ms). 저장값은 바꾸지 않는다.
- 캘리브레이션 씬, ASIO 출력 채널 선택, 곡 Sound 캐시, 판정 방식 개선(구간 교차 홀드, 다중 miss)은 후속 작업이다.
