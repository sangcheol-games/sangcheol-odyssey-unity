> **문서 안내**: **참고용 원본.** 구조 문서의 바탕이 된 설계 원본(개정 2)이다. 이후 결정으로 폐기하거나 바꾼 내용이 섞여 있다. 계층 배치(계약은 Audio 루트, 판정 타이밍은 Game/Timing), asmdef 도입, 단순화(close→init 한 경로, 폴백 4단계, 세이프 모드·워치독·소프트 에폭 제외), 스레드 규칙, 삼항 연산자 제거가 그 예다. **구현은 `Assets/Scripts/Audio/Audio_architecture.md`와 `Audio_migration.md`를 따른다.**

---

# 게임 오디오·타이밍 레이어 최종 설계: Core System 자체 소유, ASIO, 연속 SongClock, 타임스탬프 순서 판정 (개정 2)

- 기준: 브랜치 `refactor-FMOD`, FMOD for Unity 2.02.33, Unity 6000.3.13f1, Input System 1.19.0, UniTask 2.5.11(설치만 되어 있고 아직 사용처 없음)
- 경로는 저장소 루트 `D:\Dev\Unity\sangcheol-odyssey-unity` 기준입니다. F#·SP#는 결정 브리프, H/M/L#는 현황 조사 보고서의 번호입니다.
- 표기: **(추정)** 계산·추론, **(미확인)** 스파이크로 확인해야 함.
- 이번 개정에서는 2차 심사 지적 21건(주요 5, 경미 16)을 코드와 하나씩 대조해 반영했습니다. 21건 모두 사실로 확인했습니다. 일부는 범위를 조정해 반영했고, 그 이유는 끝의 "검토 메모"에 적었습니다. 결정 1~11의 범위를 벗어나는 기능은 넣지 않았습니다.

### 저장소 재대조 결과 (설계에 직접 영향)

| 항목 | 확인 내용 |
|---|---|
| 인터페이스 네임스페이스 | `App/Interfaces`의 기존 파일은 대부분 `SCOdyssey.App`이고, `ICharacterManager`와 `ISkinManager`만 `SCOdyssey.App.Interfaces`입니다. 새 계약은 폴더를 따라 `SCOdyssey.App.Interfaces`로 둡니다. |
| ChartManager | 파일은 `App/ChartManager.cs`, 네임스페이스는 `SCOdyssey.Game`입니다. `TryJudgeRelease`(:804) 안에 지역 변수 `JudgeType judge`(:826)가 있습니다. 클리어 검사는 마디 경계(:195-198)에서만 합니다. ChartEditor는 ChartManager를 참조하지 않으므로(grep) `SyncTime` 시그니처를 바꿔도 에디터에 영향이 없습니다. |
| GameManager | 일시정지 중에는 `GetCurrentTime`이 고정됩니다(:171). ESC는 Input System 콜백(`OnPause`, PreUpdate)에서 `Pause()`로 들어옵니다(:103, :234). |
| 실행 순서 상수 | `SCOdyssey.Boot.ExecutionOrder.Early = -1000` |
| Platform.cs 패치 | :870 `DSPBufferLength` getter(원본), :871-872 `[SCOdyssey 추가]` 주석과 `SetDSPBufferLength`, **:873 `DSPBufferCount` getter(원본, `RuntimeManager.cs:292`가 읽음)**, :874-878 주석과 `SetDSPBufferCount` |
| RuntimeManager | `setCallback(errorCallback, ERROR)`은 init 전(:356)에 호출합니다. `IsInitialized`(:1470)는 읽기만 하며 초기화를 일으키지 않습니다. |
| ChartEditor 버퍼 | playInEditor는 DSPBufferLength 256(HasValue 1), DSPBufferCount 0(HasValue 0, :420-422)입니다. `RuntimeManager.cs:344`의 가드 때문에 PreInit이 사라지면 setDSPBufferSize가 건너뛰어집니다. |
| ServiceLocator | `Get<T>`는 없으면 `KeyNotFoundException`을 던집니다(:56). `AdventureUI.OnDisable`(:74)은 `Get<IAudioManager>()`를 씁니다. |
| 설정 화면 진입 | `MainUI.cs:112` `ShowUI<GameSettingUI>()`는 기본 Push(`UIManager.cs:106`)라 MainUI가 숨겨지고, 그 OnDisable이 BGM을 멈춥니다(:55-58). SoundSettingUI는 설정 탭 간 Replace로만 열립니다. 설정은 로비에서만 열 수 있으므로 ApplyAsync는 게임 중에 일어나지 않습니다. |
| Input System 포커스 | 패키지 `InputManager.OnFocusChanged`(:2960-3050)는 runInBackground가 참이면 포커스를 잃을 때 비백그라운드 장치를 비활성화(소프트 리셋)합니다. 그래서 눌린 레인에 `canceled`가 발생합니다. 에디터에서는 runInBackground가 사실상 항상 참입니다. 기본값은 `ResetAndDisableNonBackgroundDevices`(InputSettings.cs:744)입니다. |
| 레거시 버퍼 매핑 | `FMODAudioPreInit`: `{64,128,256,512,1024}[audioBufferIndex]`×4이고, 범위 밖이면 기본 인덱스 2를 씁니다(:13, :32-33). |
| Testing 네임스페이스 | `SCOdyssey.Testing.{Boot, Checks, Config, Net, UI}`로 폴더를 따릅니다. |
| 빌드 프로필 | 저장소에 Build Profile 에셋이 없습니다. 씬 목록은 APITest/Main/Game입니다. |
| 기타 | `CONSTANTS.MAX_SYSTEMS = 8`, `setCallback` 기본 마스크 `ALL`(fmod.cs:1125), `NoteData.time`은 double, 판정 창 상수는 float, 도메인 리로드 켜짐(EditorSettings.asset:27-28), CharacterAnimator의 입력별 `Debug.Log` 7곳 |

### 2차 심사 반영 요약

| # | 지적 | 반영 위치 |
|---|---|---|
| 1 | C6 `bool judge`가 CS0136을 일으키고 C9에 캐스트가 없음 | 10-2 C6(`applyJudgement`), G14, C9(`TimingKind.*`, `(byte)`), I2 확인 항목 |
| 2 | 레거시 어댑터가 재초기화 뒤 무효 핸들을 씀 | 3-9 신설, 4-3의 참여자, 12-2 S5 확인 항목 |
| 3 | Ended 상태에서 일시정지가 무시됨 | 5-5, 5-6, 5-8, 5-9(Ended 상태 삭제, `IsAudioFinished` 플래그), 13-2 #17·#18 |
| 4 | 플레이어 빌드 스파이크를 할 경로가 없음 | 11-1 빌드 프로필, 12-1, 12-2 S1~S5 |
| 5 | 의존성 검사가 S0에서 녹색일 수 없음 | 1-3, 3-1, 13-3 허용 목록, `RegisterInto` |
| 6 | Platform.cs 삭제 범위에 :873이 포함됨 | 9, 11-2 |
| 7 | 하드 리셋과 MarkSuspect가 서로 모순 | 5-3, 5-6, 12-1 SP14, 13-3 |
| 8 | 같은 타입 장치 변경을 FullReinit으로 올림 | 4-2, 4-4, 12-1 SP5·SP10 |
| 9 | binding의 `!IsPaused` 게이트가 ESC 직전 입력을 버림 | 2-5, G3 |
| 10 | 종료 때 Remove하면 AdventureUI에서 예외 | 3-5 9단계, 9(S6 TryGet) |
| 11 | S4에서 audioBufferIndex가 적용되지 않음 | 3-1, 9 AudioSettingsMapper, 12-2 S4 |
| 12 | Ready·Starting 세션이 재초기화 중에 멈춤 | 5-7 상태별 표 |
| 13 | GetDevices가 동기 계약임 | 2-3, 4-1, 4-6 |
| 14 | 적용 트랜잭션에 롤백이 없음 | 3-3, 2-3 `AudioFallbackStep`, 8-3 |
| 15 | Miss 오차가 NaN | 2-2, 6-4, C9 |
| 16 | 포커스 손실 시 Input System 리셋의 cancel | 6-1, 12-1 SP-IN |
| 17 | C4·C5가 판정 규칙을 바꿈 | 10-2(I2는 규칙 유지), 선택 단계 I3, R2 |
| 18 | C 단계가 병렬 리팩터의 컴파일을 깰 수 있음 | 10-3 R11, 12-2 C 게이트 |
| 19 | SelfTests 네임스페이스 | 11-1 |
| 20 | S5 "적용 중 BGM 복원" 확인은 불가능 | 12-2 S2·S5, 13-2 #3 |
| 21 | Mono에서 할당 0 테스트가 공허하게 통과 | 13-3 |

---

## 1. 아키텍처 개요

### 1-1. 구성도

```
┌──────────── 게임플레이 (병렬 리팩터링 중 — 모듈은 이 영역을 전혀 모른다) ────────────┐
│ GameManager [AUDIO-IP:G*] ─델리게이트→ GameplayTimingBinding (Game/Integration)      │
│ ChartManager [AUDIO-IP:C*]                (계약만 참조, GM/CM 타입 모름)              │
│ GameDataLoader · TimelineController · BGAController · CharacterAnimator             │
│ MainUI · AdventureUI · SoundSettingUI · SettingsManager · InputManager · Managers    │
└────┬──────────────────────────┬────────────────────────────▲──────────────────────┘
     │ 호출(단방향)               │ 생성·배선(Managers)          │ IJudgementClient 역호출
┌────▼──────────────────────────▼────────────────────────────┴──────────────────────┐
│ 계약  App/Interfaces (SCOdyssey.App.Interfaces)                                       │
│   ISongPlayer · ISongSession · IOneShotPlayer · IAudioMixer/IMixBus · IMusicPlayers   │
│   IAudioEngine · IAudioOutputService · IJudgementTimingLog · ILaneInputStream         │
│ 계약  Timing (SCOdyssey.Timing.*, 순수 C#)                                             │
│   ISongClock · SongFrame · ClockDiscontinuity · IInputTimestampSource · LaneInputEvent│
│   IJudgementClient · JudgementAdvance · JudgementPumpMode · TimingWindow · TimingSample│
└────▲────────────────────────────────────────────────────────▲──────────────────────┘
     │ 구현                                                      │ 사용
┌────┴──────── SCOdyssey.Audio.* (FMOD Core System 유일 소유자) ┐  ┌─ SCOdyssey.Timing.* 구현 ─┐
│ Hosting : AudioEngineRunner · AudioModuleInstaller · AudioModule│  │ Clock: DspQpcModel ·     │
│           EditorAudioLifecycle · FocusPolicy · RuntimeManagerGuard│ │   SongTimeline · RenderClock│
│ Engine  : AudioEngine · EngineConfigurator · OutputFallbackPlanner│ │   SongClock               │
│           SafeModeFlag · SystemCallbackHub · FmodDebugBridge ·    │  │ LaneInput: LaneInputHub · │
│           FmodCheck · AudioLog · OutputWatchdog · IEngineResource │  │   RealtimeQpcMapper · Spsc │
│ Output  : DeviceCatalog · OutputChangePlanner · BufferPresets ·   │  │ Judgement: JudgementTimeline│
│           AudioOutputService                                      │  │   · JudgementPump · TimingLog│
│ Mixing  : FmodMixer · ClockDomainProbe                            │  │ Sources: UnityInputSystem │
│ Playback: StreamLoader · OneShotBank · FmodMusicPlayer · MusicPlayers│ │   TimestampSource (Unity) │
│           SongPlayer/FmodSongSession · MasterClockSampler ·        │  └───────────────────────────┘
│           JudgementFeed · JudgementTimingLogService                │
│ Compat  : LegacyAudioManagerAdapter : IAudioManager, IEngineResource│ (S4~C, RegisterInto로만 등록)
│ Diagnostics: AudioDiagnosticsOverlay                               │
└────────────────────────────────────────────────────────────────────┘
 Testing/Audio: AudioSpikeHarness 등 (SCOdyssey.Testing.Audio), SelfTests (…Audio.SelfTests), Editor/*
```

### 1-2. 소유권과 수명

| 대상 | 소유자 | 수명 |
|---|---|---|
| `FMOD.System`, ChannelGroup 트리, 콜백 | `AudioEngine`(Managers GameObject의 `AudioEngineRunner`가 구동) | MainScene 부팅부터 앱 종료까지(DDOL)입니다. 에디터에서는 플레이 종료나 도메인 리로드 때 해제합니다. `RuntimeInitializeOnLoadMethod`로 만들지 않으므로 ChartEditorScene을 직접 Play하면 RuntimeManager만 생깁니다(F34). |
| 원샷 Sound | `OneShotBank` | 앱 수명입니다. 재초기화하면 같은 슬롯에 다시 로드합니다. |
| 로비·프리뷰 Sound와 Channel | `FmodMusicPlayer`(슬롯마다 하나) | 앱 수명입니다. 재초기화 때 스냅샷을 뜨고 복원합니다. |
| 과도기 BGM 슬롯 | `LegacyAudioManagerAdapter`(내부 `FmodMusicPlayer` 하나) | S4~C. 재초기화에 참여합니다(3-9). |
| 게임 곡 Sound·Channel·SongTimeline | `FmodSongSession` | `OwnerSceneHandle` 씬이 언로드되거나 다음 `LoadAsync`가 오면 Dispose됩니다(M6). |
| DSP↔QPC 모델 | `MasterClockSampler` + `DspQpcModel` | 엔진 세대 단위입니다. 로비에서 이미 수렴한 상태가 곡으로 이어집니다. |
| Unity 입력 타임스탬프 소스 | `InputManager` | 앱 수명입니다. `LaneInputHub`는 드레인만 합니다. |
| 판정 클라이언트 연결 | 게임플레이 | `AttachJudgement`가 돌려준 `IDisposable`의 수명입니다. |

### 1-3. 의존 방향 (컴파일 규칙, `ModuleDependencyCheck`가 강제, 허용 목록은 13-3)

- 의존은 **게임플레이 → 계약 ← 모듈** 방향으로만 흐릅니다. `Audio/**`와 `Timing/**`에는 `GameManager`, `IGameManager`, `ChartManager`, `ChartData`, `LaneData`, `NoteController`, `JudgeType`, `NotePosition`, `SettingsData`, `ISettingsManager`, `SCOdyssey.Game` 토큰이 나오면 안 됩니다. 판정 창과 등급은 게임플레이가 소유하고, 모듈은 레인을 `int`, 등급을 불투명한 `byte`로만 다룹니다.
- 설정 DTO와 계약 값 사이의 변환은 App 쪽 `AudioSettingsMapper`가 맡습니다(M9 해소).
- `Timing/**`는 `System.*`만 씁니다. 예외는 `Timing/Sources/UnityInputSystemTimestampSource.cs` 하나입니다.
- `Audio/**`가 쓸 수 있는 것: `System.*`, `UnityEngine`, `FMOD.*`(항상 완전 수식, `using FMOD;` 금지), `Cysharp.Threading.Tasks`, `SCOdyssey.Core`, `SCOdyssey.Boot.ExecutionOrder`, `SCOdyssey.App.Interfaces`, `SCOdyssey.Timing.*`. **`SCOdyssey.App.IAudioManager`는 `Audio/Compat/**`만** 씁니다. 등록도 Compat의 `LegacyAudioManagerAdapter.RegisterInto(AudioModule)`가 하므로 Hosting은 IAudioManager를 이름으로 참조하지 않습니다.
- `FMODUnity.RuntimeManager` 토큰은 `Audio/Hosting/RuntimeManagerGuard.cs`와 `ChartEditor/**`에만 허용합니다. `FMOD.` 토큰은 `Audio/**`, `ChartEditor/**`, FMOD 플러그인에만 허용합니다. 과도기 파일은 13-3의 날짜 있는 허용 목록으로 관리합니다.
- **IGameManager는 바꾸지 않습니다.**

### 1-4. 스레드 모델

| 스레드 | 우리 코드가 하는 일 | 규칙 |
|---|---|---|
| Unity 메인 | 모든 FMOD System·Channel API, 클록 샘플, 세션 제어, 입력 수집·정렬·변환, 판정 Pump, UniTask 완료, UI | 에디터·개발 빌드에서 `MainThreadGuard.Assert()`. 모듈 안에서 `SwitchToThreadPool` 금지(STA, F18). |
| Input System 업데이트(메인, PreUpdate) | `UnityInputSystemTimestampSource.Push`로 사전 할당 링에 기록 | 할당·로그 없음 |
| FMOD 믹서 | 관리 콜백을 **등록하지 않습니다**(OUTPUTUNDERRUN과 MIDMIX는 마스크에서 제외, SP15 게이트). | – |
| 임의 FMOD 스레드 | `ERROR` System 콜백, `FMOD.Debug` 콜백 | static 트램펄린에서 Interlocked와 사전 할당 복사만 합니다. FMOD·Unity API, 로그, 할당은 금지합니다. |
| FMOD 스트림 로더 | 없음 | 메인 스레드에서 `getOpenState`를 폴링합니다. |
| (2단계) Raw Input | `IInputTimestampSource` 뒤 SPSC | 이음새만 둡니다. |

### 1-5. 프레임 순서

`PreUpdate: InputSystem → InputManager 레인 콜백 → source.Push(qpc)` → `Update(-1000) Runner: update → 플래그 → 클록 샘플 → 세션 Tick → 입력 수집 → SongFrame → 판정 Pump → 완료 발행` → `Update(0) 게임플레이` → `LateUpdate: 클록 샘플 2차, 로그 드레인`(3-4).

---

## 2. 컴포넌트와 인터페이스

### 2-1. 컴포넌트 책임

| 컴포넌트 | 네임스페이스 | 책임 |
|---|---|---|
| `AudioModuleInstaller`, `AudioModule` | SCOdyssey.Audio.Hosting | 조립과 ServiceLocator 등록을 맡습니다. `Install`은 `AudioModule` 핸들(public sealed, 내부 서비스는 internal)을 돌려줍니다. 입력 소스 연결, `InstallStandalone`(하네스용)도 제공합니다. |
| `AudioEngineRunner` | …Hosting | `[DefaultExecutionOrder(ExecutionOrder.Early)]` MonoBehaviour입니다. Update, LateUpdate, 포커스, 종료, 씬 언로드를 모듈로 전달합니다. |
| `EditorAudioLifecycle` | …Hosting (`#if UNITY_EDITOR`) | ExitingPlayMode, beforeAssemblyReload, pauseStateChanged 훅 |
| `FocusPolicy` | …Hosting | 로비는 설정에 따라 계속 재생하거나 음소거하고, 게임 중에는 항상 자동 일시정지합니다. 복귀하면 `MarkSuspect`를 호출합니다. |
| `RuntimeManagerGuard` | …Hosting | `RuntimeManager.IsInitialized`만 읽습니다. |
| `AudioEngine` | SCOdyssey.Audio.Engine | `FMOD.System`의 유일한 소유자입니다. 상태 머신, `Generation`, IEngineResource 레지스트리, 부팅과 복구를 맡습니다. |
| `EngineConfigurator` | …Engine | 시도 한 번(설정 → init → 실제 상태 검증) |
| `OutputFallbackPlanner`(순수) | …Engine | 시도 목록 생성과 중복 제거(부팅과 적용 모드를 구분) |
| `SafeModeFlag` | …Engine | PlayerPrefs로 init 중 크래시를 감지합니다. |
| `SystemCallbackHub` | …Engine | static 트램펄린, 명시적 마스크, 비트 플래그, ERROR 링, 살아 있는 System IntPtr 비교, **init 후 마스크 교체**(`SetMask`) |
| `FmodDebugBridge`, `FmodCheck`, `AudioLog`, `MainThreadGuard`, `ComApartmentProbe`, `OutputWatchdog` | …Engine | 로그 링, RESULT 검사, CoreLogger TryGet(없으면 Debug.Log), 스레드 가드, 아파트먼트 로그, 1Hz 감시 |
| `DeviceCatalog` | SCOdyssey.Audio.Output | 타입별 **비동기** 열거와 캐시, GUID↔index 해석 |
| `OutputChangePlanner`, `BufferPresets`(순수) | …Output | 변경 등급 분류와 격상 플래그, 타입별 버퍼 프리셋 |
| `AudioOutputService` | …Output | `IAudioOutputService` 구현. setDriver 경로, 재초기화 트랜잭션, 롤백 |
| `FmodMixer`, `FmodMixBus`, `ClockDomainProbe` | SCOdyssey.Audio.Mixing | `IAudioMixer` 구현, 그룹 트리, A-B-A 괄호 읽기 |
| `StreamLoader`, `AudioPaths` | SCOdyssey.Audio.Playback | NONBLOCKING 열기·폴링·취소, 지연 release(M7), 경로 규약(L10) |
| `OneShotBank` | …Playback | `IOneShotPlayer` 구현. CREATESAMPLE 멱등 등록, 할당 없는 재생 |
| `FmodMusicPlayer`, `MusicPlayers` | …Playback | 로비와 프리뷰. 마지막 요청만 유효 |
| `SongPlayer`, `FmodSongSession` | …Playback | 게임 곡 세션. 단일 앵커, 선 seek, 예약 재시작, 상태별 재초기화 처리, 종료 감지 |
| `MasterClockSampler` | …Playback | 메인 스레드 괄호 샘플. Failed면 QPC 가상 클록 |
| `JudgementFeed`, `JudgementTimingLogService` | …Playback | Pump 구동과 예외 격리, 오차 링(4096) |
| `LegacyAudioManagerAdapter` | SCOdyssey.Audio.Compat | 과도기(S4~C)에 옛 `IAudioManager` 의미를 새 엔진 위에서 재현합니다. **IEngineResource이며 FMOD 핸들을 캐시하지 않습니다**(3-9). |
| `AudioDiagnosticsOverlay` | SCOdyssey.Audio.Diagnostics | 에디터·개발 빌드 IMGUI |
| `DspQpcModel`, `SongTimeline`, `RenderClock`, `SongClock` | SCOdyssey.Timing.Clock | 회귀, 세그먼트, 렌더 평가기, `ISongClock` |
| `LaneInputHub`, `RealtimeQpcMapper`, `SpscRing<T>` | SCOdyssey.Timing.LaneInput | 병합, 안정 정렬, 시각 변환, horizon |
| `JudgementTimeline`, `JudgementPump`, `TimingWindow`, `TimingLog` | SCOdyssey.Timing.Judgement | 판정 싱크의 유일한 적용 함수, 시각순 병합, 구간 기하, 오차 기록 |
| `UnityInputSystemTimestampSource` | SCOdyssey.Timing.Sources | `ctx.time`→QPC, Synthetic 태깅(구간과 포커스) |
| `GameplayTimingBinding` | SCOdyssey.Game.Integration | 게임플레이 쪽 얇은 어댑터. 델리게이트만 받음 |

### 2-2. 순수 계약: `Assets/Scripts/Timing/**`

```csharp
// ── SCOdyssey.Timing ─────────────────────────────────────────────
public static class Qpc
{
    public static readonly long Frequency = System.Diagnostics.Stopwatch.Frequency;
    public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
    public static double ToSeconds(long ticks) => (double)ticks / Frequency;
    public static long FromSeconds(double s) => (long)System.Math.Round(s * Frequency);
}
public readonly struct SongTimingOffsets                  // 세션 Start 시 1회 래치
{
    public const double JudgmentStepSeconds = 0.003;      // 판정 싱크 1단계 = 3ms
    public readonly int AudioOffsetMs;                    // 노트 싱크 ±200, + = 음악이 늦게 시작
    public readonly int JudgmentOffsetSteps;              // 판정 싱크 ±20
    public SongTimingOffsets(int audioOffsetMs, int judgmentOffsetSteps) { AudioOffsetMs = audioOffsetMs; JudgmentOffsetSteps = judgmentOffsetSteps; }
}

// ── SCOdyssey.Timing.Clock ───────────────────────────────────────
public enum SongClockPhase : byte { NotStarted, Frozen, Running, Stopped }
public enum ClockQuality  : byte { None, WarmingUp, Locked, Suspect }
public readonly struct SongFrame              // Runner Early에서 프레임당 1회. 모든 소비자가 같은 값을 읽는다
{
    public readonly long Index, Qpc;          // Index = LaneInputEvent.BatchId, Qpc = q* = min(Qpc.Now, 소스 horizon)
    public readonly SongClockPhase Phase;
    public readonly int Epoch;                // Start/Pause/Resume/클록 리셋/Stop 마다 +1
    public readonly double SongTime;          // 판정 평가기(스무딩 없음), 판정 싱크 미적용
    public readonly double JudgeTime;         // SongTime − 판정 싱크 (JudgementTimeline만 계산)
    public readonly double RenderTime;        // 판정선·노트·BGA 전용
    public readonly double AudioZeroSongTime; // Z (노트 싱크 반영값)
    public readonly ClockQuality Quality;
    public bool IsAdvancing => Phase == SongClockPhase.Running;
}
public readonly struct SongTimePoint { public readonly double SongTime; public readonly int Epoch; public readonly bool IsRunning; }
public enum DiscontinuityReason : byte { Start, Pause, Resume, ClockReset, ClockStep, OutputChanged, Stop }
public readonly struct ClockDiscontinuity { public readonly int PreviousEpoch, Epoch; public readonly DiscontinuityReason Reason; public readonly double SongTime; }
public interface ISongClock
{
    SongFrame Frame { get; }
    int Epoch { get; }
    bool TryGetSongTime(long qpc, out SongTimePoint point);  // 결정적, 최근 8개 세그먼트까지
    double SongTimeAt(long qpc);                             // 실패 시 Frame.SongTime
    SongClockDiagnostics Diagnostics { get; }                // Quality, DriftPpm, ResidualP50/P99Ms, RenderMinusSongMs, ClockEpoch, SoftResets
    event Action<ClockDiscontinuity> Discontinuity;
}

// ── SCOdyssey.Timing.LaneInput ───────────────────────────────────
[System.Flags] public enum LaneInputFlags : byte { None = 0, Synthetic = 1 }   // 맵 Disable·포커스 리셋 등으로 생긴 가짜 release
public enum InputSourceKind : byte { UnityInputSystem = 1, WindowsRawInput = 2 /* 2단계 예약, 미구현 */, Harness = 9 }
public struct RawLaneInput { public int Lane; public bool IsDown; public long Qpc; public LaneInputFlags Flags; }
public interface IInputTimestampSource                     // 2단계 Raw Input 이음새
{
    InputSourceKind Kind { get; }
    bool FrameQuantized { get; }
    long HorizonQpc { get; }
    void OnFrame(long frameQpc);
    int Drain(RawLaneInput[] buffer, int start);
}
public readonly struct LaneInputEvent
{
    public readonly int Lane; public readonly bool IsDown; public readonly long Qpc;
    public readonly InputSourceKind Source; public readonly int Sequence; public readonly long BatchId; public readonly int Epoch;
    public readonly double SongTime;        // 판정 싱크 미적용
    public readonly double JudgeTime;       // 판정 싱크 적용. 노트 시각과 비교하는 유일한 값
    public readonly bool Judgeable;         // Running 세그먼트 && !Synthetic
    public readonly LaneInputFlags Flags;
}
public delegate void LaneInputHandler(in LaneInputEvent e);

// ── SCOdyssey.Timing.Judgement ───────────────────────────────────
public readonly struct JudgementAdvance          // 판정 시간 축 반열린 구간 (From, To]
{
    public readonly double FromJudgeTime, ToJudgeTime;
    public readonly double ToSongTime;           // 마디 전환·카운트다운용
    public readonly int Epoch;
    public bool IsEmpty => !(ToJudgeTime > FromJudgeTime);
}
public delegate void JudgementAdvanceHandler(in JudgementAdvance advance);
public interface IJudgementClient
{
    void Advance(in JudgementAdvance advance);
    void OnLaneInput(in LaneInputEvent e);
}
public enum JudgementPumpMode : byte { FrameBatched, Interleaved }
public static class TimingWindow                 // 시간 기하만. 창 크기는 호출자가 준다
{
    public static bool Overlaps(double from, double to, double center, double half) => to > from && to >= center - half && from < center + half;
    public static double ClosestPoint(double from, double to, double center) => center <= from ? from : (center >= to ? to : center);
    public static bool IsPastLateEdge(double judgeNow, double noteTime, double lateWindow) => judgeNow > noteTime + lateWindow;
    public static double SignedErrorMs(double judgeTime, double noteTime) => (judgeTime - noteTime) * 1000.0;   // + = 늦음
}
public enum TimingKind : byte { Press, Release, HoldBody, Miss }
public readonly struct TimingSample              // 캘리브레이션 훅: 모든 판정 결과에 부호 있는 오차
{
    public readonly int Lane; public readonly TimingKind Kind; public readonly byte Grade;
    public readonly double NoteTime, JudgeTime; public readonly int Epoch, SessionId;
    // Miss 포함 모든 Kind에서 정의. Miss는 확정 시각 기준이라 항상 ≥ UMM(늦음) — 소비자는 Kind로 거른다
    public double SignedErrorMs => (JudgeTime - NoteTime) * 1000.0;
}
```

### 2-3. 매니저 계약: `Assets/Scripts/App/Interfaces/` (`SCOdyssey.App.Interfaces`)

UnityEngine 타입을 노출하지 않습니다. 씬은 `int` 핸들로 넘기고, UniTask에만 의존합니다.

```csharp
// ISongPlayer.cs
public enum SongLoadStatus { Ok, NotFound, DecodeError, Timeout, Cancelled, EngineUnavailable }
public readonly struct SongLoadRequest { public readonly string MusicFileName; public readonly int OwnerSceneHandle; public SongLoadRequest(string f, int h) { MusicFileName = f; OwnerSceneHandle = h; } }
public readonly struct SongLoadResult { public readonly SongLoadStatus Status; public readonly ISongSession Session; public readonly string Detail; public bool Ok => Status == SongLoadStatus.Ok; }
public readonly struct SongStartOptions { public readonly double LeadInSeconds; public SongStartOptions(double leadIn) { LeadInSeconds = leadIn; } }
// Ended 상태는 없다: 음원 종료는 IsAudioFinished 플래그 + AudioEnded 이벤트이며 상태는 Playing으로 남아 일시정지가 가능하다
public enum SongSessionState { Ready, Starting, LeadIn, Playing, Paused, Resuming, Recovering, Stopped, Disposed }
public enum SongPauseReason { User, FocusLost, EditorPaused, DeviceChanged, StreamStalled }
public enum SongSessionEventKind { Started, AudioStarted, AudioEnded, Paused, Resumed, Recovered, Stopped, Disposed }
public readonly struct SongSessionEvent { public readonly SongSessionEventKind Kind; public readonly SongPauseReason Reason; public readonly int Epoch; }
public readonly struct SongSessionTiming { public readonly double LeadInSeconds, AudioZeroSongTime, AudioLengthSeconds; public double AudioEndSongTime => AudioZeroSongTime + AudioLengthSeconds; }
public readonly struct ScheduledVoice { internal readonly int Id; internal readonly int Generation; public bool IsValid => Id != 0; }
public interface ISongPlayer
{
    ISongSession Current { get; }
    UniTask<SongLoadResult> LoadAsync(SongLoadRequest request, CancellationToken ct);  // 예외 대신 상태. 이전 세션 Dispose
    SongLoadResult CreateSilent(SongLoadRequest request);
    event Action<ISongSession> CurrentChanged;
}
public interface ISongSession : IDisposable
{
    int Id { get; }  SongSessionState State { get; }  SongSessionTiming Timing { get; }  ISongClock Clock { get; }
    bool IsAudioFinished { get; }            // 무음 세션은 Start 후 true (상태는 LeadIn/Playing)
    bool CanResume { get; }
    SongPauseReason PauseReason { get; }
    void Start(in SongStartOptions options); // 1회. 두 오프셋을 이 시점에 래치
    void Pause(SongPauseReason reason = SongPauseReason.User);   // Starting|LeadIn|Playing|Resuming에서 유효, 멱등
    void Resume();
    void Stop();
    IDisposable AttachJudgement(IJudgementClient client, JudgementPumpMode mode);
    ScheduledVoice ScheduleOneShot(OneShotId id, double songTime);   // 미래 메트로놈 훅
    void CancelScheduled(ScheduledVoice voice);
    event Action<SongSessionEvent> Changed;
}

// IOneShotPlayer.cs
public enum MixBusId { Master, Music, HitSound, Sfx }
public readonly struct OneShotId { internal readonly int Slot1; internal OneShotId(int slot1) { Slot1 = slot1; } public bool IsValid => Slot1 > 0; public static readonly OneShotId None = default; }
public interface IOneShotPlayer { OneShotId Register(string hitSoundFileName, MixBusId bus = MixBusId.HitSound); void Play(OneShotId id); }

// IAudioMixer.cs
public interface IMixBus { MixBusId Id { get; } float Volume { get; set; } }   // 선형 0~1
public interface IAudioMixer { IMixBus this[MixBusId id] { get; } }

// IMusicPlayers.cs
public enum MusicSlot { Lobby, Preview }
public enum MusicPlayStatus { Ok, NotFound, DecodeError, Cancelled, Superseded, EngineUnavailable }
public interface IMusicPlayer { bool IsPlaying { get; } string CurrentFile { get; } UniTask<MusicPlayStatus> PlayAsync(string musicFileName, bool loop, CancellationToken ct); void Stop(); }
public interface IMusicPlayers { IMusicPlayer Get(MusicSlot slot); }

// IAudioEngine.cs
public enum AudioEngineState { Uninitialized, Starting, Running, Degraded, Reconfiguring, Recovering, Failed, Disposed }
public enum AudioOutputKind { Wasapi, Asio, NoSound }
public enum AudioFailure { None, SessionActive, UnsupportedKind, DeviceNotFound, DeviceBusy, InitFailed, VerifyMismatch, SafeModeBoot }
public enum AudioFallbackStep { None, Rollback, SavedWasapi, WasapiDefault, SafeBuffer, NoSound }
public readonly struct AudioOutputStatus
{
    public readonly AudioEngineState State;
    public readonly AudioOutputKind RequestedKind, ActualKind;
    public readonly Guid DeviceId; public readonly string DeviceName; public readonly bool FollowsSystemDefault;
    public readonly int SampleRate, BufferLength, BufferCount;          // 실제값
    public readonly AudioFallbackStep FallbackStep; public readonly AudioFailure FallbackCause;
    public bool IsFallback => FallbackStep != AudioFallbackStep.None;
    public readonly int Generation, ClockEpoch;
}
public interface IAudioEngine
{
    AudioEngineState State { get; }  AudioOutputStatus Status { get; }
    void SetBackgroundPolicy(bool playInBackground);
    AudioEngineDiagnostics Diagnostics { get; }
    event Action<AudioOutputStatus> StatusChanged;
}

// IAudioOutputService.cs — 로비·설정 화면 전용
public readonly struct AudioDeviceInfo { public readonly Guid Id; public readonly string Name; public readonly int SystemRate, Channels; }
public readonly struct AudioOutputRequest : IEquatable<AudioOutputRequest>
{
    public readonly AudioOutputKind Kind; public readonly Guid DeviceId; public readonly string DeviceName;   // Empty = 기본 따라가기 / 첫 ASIO 드라이버
    public readonly int BufferLength, BufferCount; public readonly Guid FallbackWasapiDeviceId;
}
public enum AudioApplyOutcome { Unchanged, Applied, AppliedWithFallback, Rejected, Failed }
public readonly struct AudioApplyResult { public readonly AudioApplyOutcome Outcome; public readonly AudioFallbackStep FallbackStep; public readonly AudioOutputStatus Status; public readonly AudioFailure Failure; public readonly string Message; }
public interface IAudioOutputService
{
    bool IsSupported(AudioOutputKind kind);
    IReadOnlyList<AudioDeviceInfo> GetCachedDevices(AudioOutputKind kind);         // 표시용. 열거하지 않음(없으면 빈 목록)
    UniTask<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(AudioOutputKind kind, bool refresh, CancellationToken ct);
    IReadOnlyList<int> GetBufferPresets(AudioOutputKind kind);
    bool CanReconfigure(out AudioFailure reason);                                // 세션이 Stopped/Disposed가 아니면 false
    UniTask<AudioApplyResult> ApplyAsync(AudioOutputRequest request, CancellationToken ct);
    event Action DevicesChanged;
}

// IJudgementTimingLog.cs
public interface IJudgementTimingLog
{
    void Record(int lane, TimingKind kind, byte grade, double noteTime, double judgeTime);
    int CopyRecent(TimingSample[] buffer);
    event Action<TimingSample> Recorded;
}

// ILaneInputStream.cs — 하네스·진단용
public interface ILaneInputStream { IReadOnlyList<LaneInputEvent> FrameEvents { get; } long HorizonQpc { get; } void AddSource(IInputTimestampSource s); void RemoveSource(IInputTimestampSource s); }
```

### 2-4. 모듈 내부 계약

```csharp
namespace SCOdyssey.Audio.Engine
{
    internal interface IFmodEngineContext
    {
        FMOD.System System { get; }  bool IsUsable { get; }  int Generation { get; }
        int SampleRate { get; }  uint BlockLength { get; }  int BlockCount { get; }
        FMOD.ChannelGroup ReferenceClockGroup { get; }   // 시스템 마스터: pause·mute·pitch 금지
        FMOD.ChannelGroup SongGroup { get; }
        FMOD.ChannelGroup Bus(MixBusId id);
        AudioLog Log { get; }
    }
    internal interface IEngineResource { void OnEngineClosing(); void OnEngineOpened(IFmodEngineContext ctx); }
    internal interface IClockSampleSource { bool TrySample(out ClockSample sample); }
}
```

규칙: 모든 IEngineResource는 FMOD 핸들을 **세대 번호와 함께** 보관합니다. `ctx.Generation`과 다르면 사용하지 않습니다.

### 2-5. 게임플레이 쪽 얇은 어댑터 (`Game/Integration/GameplayTimingBinding.cs`)

```csharp
namespace SCOdyssey.Game.Integration
{
    public sealed class GameplayTimingBinding : IJudgementClient, IDisposable
    {
        public static GameplayTimingBinding Attach(ISongSession session,
            JudgementAdvanceHandler onAdvance, LaneInputHandler onLaneInput,
            Func<bool> isRunning,                     // "게임 진행 중" (일시정지 여부는 넣지 않는다)
            Action<SongSessionEvent> onSessionEvent, JudgementPumpMode mode);
        public ISongSession Session { get; }

        void IJudgementClient.Advance(in JudgementAdvance a) { if (_isRunning()) _onAdvance(a); }
        void IJudgementClient.OnLaneInput(in LaneInputEvent e)
        {
            if (!e.Judgeable) { if (!e.IsDown) _onLaneInput(e); return; }   // 상태 해제용 release만 통과
            if (_isRunning()) _onLaneInput(e);
        }
        public void Dispose();   // 멱등
    }
}
```

- **일시정지 게이트는 모듈이 맡습니다.** Pause가 세그먼트를 Frozen으로 만들기 때문에, 이후 입력은 Judgeable=false가 되고 진행 구간도 빈 구간이 됩니다.
- ESC는 PreUpdate(Input System 콜백)에서 `GameManager.Pause()`를 먼저 실행하고, 그 뒤 Runner가 그 프레임을 Pump합니다. binding이 `IsPaused`로 거르지 않으므로 같은 배치에서 ESC보다 먼저 들어온 입력(qpc < Freeze)과 T_p까지의 진행 구간은 현행처럼 판정됩니다.
- 이 구간 안에서 마디 경계와 클리어 조건이 겹치면 PauseUI 위에서 종료 연출이 시작될 수 있습니다. 확률은 약 프레임/마디입니다. I1에서 확인하는 항목(13-2 #5)에 넣었습니다.

---

## 3. 엔진 수명주기

### 3-1. 부트 순서 (`Managers.InitServices`)

```
1 SettingsManager.Load()          (S5부터 try/catch + 백업 + v1→v2)
2 InputManager 생성·Enable         (S4부터 UnityInputSystemTimestampSource 소유)
3 UIManager / MusicManager / CharacterManager 등록
4 var audio = AudioModuleInstaller.Install(gameObject, new AudioModuleOptions {
      BootRequest      = AudioSettingsMapper.ToBootRequest(settings.Current),   // S4: v1 매핑, S5: v2
      PlayInBackground = settings.Current.playInBackground,
      TimingOffsets    = () => AudioSettingsMapper.ToTimingOffsets(settings.Current),
      LaneSources      = { inputManager.LaneTimestampSource },
      AsioEnabled      = true });
  LegacyAudioManagerAdapter.RegisterInto(audio);   // [과도기] C에서 삭제 — Compat만 IAudioManager를 안다
5 settingsManager.Apply()         (볼륨, runInBackground, 백그라운드 정책)
6 uiManager.Init()
```

- **S4의 `ToBootRequest`(v1)**: `WASAPI`, `Guid.Empty`(Follow-Default)를 씁니다. 버퍼 길이는 `{64,128,256,512,1024}[audioBufferIndex]`(범위 밖이면 인덱스 2), count는 4입니다. SnapUp을 하지 않으므로 PreInit과 동작이 같습니다. 옛 코드도 부팅 때 장치를 복원하지 않았으므로(H3) Follow-Default가 동등한 동작입니다. S5에서 v2 필드로 바꿉니다.
- static 상태(콜백 플래그, 링, System 핸들, 설치 여부)는 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`에서 리셋합니다.

### 3-2. 시도 한 번의 순서 (`EngineConfigurator`, 메인 스레드)

```
[프로세스 1회] MainThreadGuard.Capture → ComApartmentProbe.Log → RuntimeUtils.EnforceLibraryOrder → FmodDebugBridge.Install
[System 준비] 부팅·폴백 시도: System_Create + getVersion 확인 / 재초기화 1차 시도: setCallback(null,0); close()  (같은 System)
 1 setOutput(kind)                       // init 전(또는 close 후)에만. init 후 setOutput은 어떤 경로에서도 쓰지 않음
 2 setDriver(DeviceCatalog.ResolveIndex(sys, guid, name))   // GUID → 이름 → 0(IsFallback)
 3 getDriverInfo(idx, …, out systemRate, …); setSoftwareFormat(systemRate>0 ? systemRate : 48000, STEREO, 0)
 4 setDSPBufferSize(len, count)          // 항상 명시 (WASAPI ×4, ASIO ×2)
 5 setSoftwareChannels(64)
 6 setCallback(s_systemCb, MaskFor(mode))
 7 init(256, NORMAL, IntPtr.Zero)
 8 검증: getOutput, getDriver→GUID(Pinned), getDSPBufferSize, getSoftwareFormat. OK여도 실제 상태가 다르면 실패
[성공] setCallback 재등록 → Generation++ → FmodMixer 재구성(볼륨 재적용) → OnEngineOpened(정방향) → 모델 하드 리셋 → StatusChanged
[실패] 이번 System은 release, 다음 시도는 새 System_Create
```

### 3-3. 폴백 체인 (`OutputFallbackPlanner`, 순수, 중복 제거)

| # | 시도 | 부팅 | 설정 화면 ApplyAsync |
|---|---|---|---|
| 1 | 요청 그대로 | 세이프 모드면 생략 | ✓ |
| 2 | **직전에 적용되어 있던 구성(롤백)** → `FallbackStep = Rollback` | – | ✓ (1과 다를 때) |
| 3 | WASAPI + 저장된 WASAPI GUID + `SnapUp(Wasapi, len)`×4 → `SavedWasapi` | ✓ | ✓ |
| 4 | WASAPI driver 0 + 같은 버퍼 → `WasapiDefault` | ✓(세이프 모드는 여기서 시작) | ✓ |
| 5 | WASAPI 기본 + 512×4 → `SafeBuffer` | ✓ | ✓ |
| 6 | NOSOUND → `Degraded`, UI 경고. 기준 클록은 흐릅니다(추론, SP1). | ✓ | ✓ |

- `ERR_OUTPUT_ALLOCATED`, `ERR_OUTPUT_INIT`, `ERR_OUTPUT_DRIVERCALL`은 `DeviceBusy`로 분류해 안내합니다(F16, F17). 모든 단계가 실패하면 `Failed`이고, QPC 가상 클록으로 무음 진행만 합니다.
- **세이프 모드**: PlayerPrefs `SCOdyssey.Audio.InitInProgress`를 기록하고 `Save()`한 뒤 init합니다. 완료하면 지웁니다. 부팅 때 키가 남아 있으면 4번부터 시작하고 `FallbackCause = SafeModeBoot`로 표시합니다.
- 부팅 때 폴백해도 저장값은 덮어쓰지 않습니다. 설정 화면의 적용 결과 처리는 8-3에 있습니다.

### 3-4. 프레임 드라이버 (`AudioEngineRunner`, Early)

```
Update: 1 q=Qpc.Now  2 engine.Tick(update → 플래그 소비 → ERROR 링 → 지연 release → 워치독 1Hz)
        3 sampler.Sample  4 songs.Tick(q)  5 inputHub.Collect(q)  6 clock.BuildFrame(q*)  7 feed.Pump  8 completions.Flush
LateUpdate: 9 sampler.Sample  10 debugBridge.Drain
OnApplicationFocus/Pause → FocusPolicy (5-6)   OnApplicationQuit/OnDestroy → engine.Shutdown (멱등)
```

### 3-5. 종료와 에디터 정리 (`Shutdown()`은 멱등)

| 트리거 | 위치 |
|---|---|
| ExitingPlayMode, beforeAssemblyReload | `EditorAudioLifecycle` |
| pauseStateChanged | 누르면 세션 `Pause(EditorPaused)`와 Master 음소거, 풀면 음소거 해제와 `MarkSuspect()`. **그룹은 pause하지 않습니다.** |
| `Application.quitting`, `OnApplicationQuit`, `OnDestroy` | Runner |
| `AppDomain.DomainUnload` | 최후 수단 |

순서
1. `_accepting = false`
2. 모든 Channel stop
3. Sound release(로딩 중이면 상태 전이를 최대 200ms 기다림)
4. 원샷 release
5. 자식 그룹부터 release
6. `setCallback(null, 0)`
7. `sys.release()`
8. Debug 콜백 복원: 에디터는 `FMOD.Debug.Initialize(LOG, FILE, null, "fmod_editor.log")`, 플레이어는 `(NONE, TTY, null, null)`. 릴리스 DLL의 `ERR_UNSUPPORTED`는 무시합니다.
9. **ServiceLocator 등록은 제거하지 않습니다.** 폐기된 서비스(어댑터 포함)는 모든 호출을 no-op으로 처리합니다. 등록은 다음 SubsystemRegistration의 `ServiceLocator.Reset`이 비웁니다. `OnApplicationQuit`과 ExitingPlayMode는 다른 객체의 OnDisable보다 먼저 오기 때문에, 여기서 Remove하면 `AdventureUI.OnDisable`(:74 `Get`)이 예외를 냅니다. `InstallStandalone`(하네스)만 하네스 OnDestroy에서 같은 인스턴스일 때 Remove합니다.

### 3-6. FMOD 로그와 오류 라우팅

- **Debug 콜백**(에디터·개발 빌드): `FMOD.Debug.Initialize(level, CALLBACK, s_debugCb, null)`. 기본 level은 `ERROR|WARNING`, 진단 모드에서는 `LOG`(ASIO "low latency mode" 확인용)입니다. `[AOT.MonoPInvokeCallback]` static 메서드이고 델리게이트는 static readonly로 붙잡습니다. 본문은 Interlocked로 64개 중 한 슬롯을 잡고 사전 할당한 `byte[64][512]`에 `Marshal.ReadByte` 루프로 복사합니다(할당 없음). LateUpdate에서 UTF8로 디코드해 `AudioLog`로 보냅니다.
- **System ERROR 콜백**(모든 빌드): `(result, instancetype)`만 64칸 원자 링에 기록합니다. 채널의 `ERR_INVALID_HANDLE`과 `ERR_CHANNEL_STOLEN`은 걸러냅니다.
- `FmodCheck.Ok(result, ctx)`는 수명주기 경로에서만 씁니다(핫패스 제외).
- `AudioLog`는 CoreLogger를 TryGet하고, 없으면 `Debug.Log*`를 씁니다.
- SP15에서 문제가 드러나면 Debug는 FILE 모드로 돌리고 메인 스레드에서 tail하며, ERROR는 개발 빌드 전용으로 줄입니다(R3).

### 3-7. RuntimeManager 가드

- MainScene 카메라의 `StudioListener`(:252-265)를 제거합니다(S4).
- `RuntimeManagerGuard`는 Install 때와 MainScene·GameScene 로드 때 `IsInitialized`를 읽습니다. true면 LogError를 남기고 오버레이에 표시합니다.
- `FMODStudioSettings.asset` playInEditor의 `DSPBufferCount`를 4로 바꿉니다(:420-422 `Value: 4 / HasValue: 1`). ChartEditorScene의 256×4를 유지하기 위함입니다. 나머지 설정과 빈 Master 뱅크는 유지합니다.

### 3-8. 엔진 상태 머신

```
Uninitialized ─Install→ Starting ─1~5 성공→ Running ─ApplyAsync(FullReinit)→ Reconfiguring → Running | Running(Fallback) | Degraded | Failed
                          │                 ├─ setDriver / DEVICEREINITIALIZE → Running (ClockEpoch++)
                          │                 └─ 장치 손실·NOSOUND 전락·클록 정지 → Recovering → Running | Degraded
                          ├─ 6(NOSOUND)만 성공 → Degraded ─장치 복귀(2초 주기, 세션 비활성)→ Reconfiguring
                          └─ System_Create 실패 → Failed
Any ─Shutdown→ Disposed
```

`IsUsable`이 false이면 재생기, 원샷, 세션, 어댑터는 FMOD를 호출하지 않고 no-op으로 처리합니다.

### 3-9. 과도기 호환 어댑터 (`LegacyAudioManagerAdapter`, S4~C)

S6까지는 MainUI와 AdventureUI가, I1까지는 GameDataLoader와 GameManager가, I2까지는 ChartManager가 이 어댑터를 씁니다. S4부터는 복구 재초기화가, S5부터는 ApplyAsync가 일어날 수 있으므로 어댑터가 재초기화를 견뎌야 합니다.

| 멤버 | 구현 (FMOD 핸들을 필드에 캐시하지 않고, 필요할 때 `IFmodEngineContext`에서 읽음) |
|---|---|
| 등록 | `public static void RegisterInto(AudioModule m)`: 생성 → `IEngineResource`로 등록 → `TryRegister<IAudioManager>` |
| `LoadAudio`, `IsLoaded`, `IsPlaying`, `Stop`, `Pause`, `Resume` | 내부 `FmodMusicPlayer` 하나(SCO.Music 직속, 레거시 단일 슬롯 의미 유지) 위에 구현합니다. `IsLoaded`는 READY입니다. 실패 상태가 없다는 레거시 한계(M1)는 S6·I1에서 없어집니다. |
| `GetDSPTime` | `genBase + (ref.getDSPClock − genClock0) / ctx.SampleRate`. OnEngineClosing에서 `genBase = 마지막 값`, OnEngineOpened에서 `genClock0 = 새 기준 클록`으로 둡니다. 세대가 바뀌어도 단조 증가하므로 레거시 GameManager의 `globalStartTime` 산술이 깨지지 않습니다. |
| `PlayScheduled(t, loop)` | `c_ref* = genClock0 + (t − genBase)·R`, `ClockDomainProbe`로 SCO.Music 오프셋을 더해 `playSound(paused) → setDelay → setLoopCount → setPriority(0) → unpause`. 과거 시각이면 즉시 재생합니다(현행 UI 사용법과 같음). |
| `RegisterOneShot`, `PlayOneShot` | `OneShotBank.Register(file)` → `Slot1 − 1`(실패하면 −1). `Play(new OneShotId(slot + 1))`입니다. 뱅크가 재초기화 뒤 같은 슬롯에 다시 로드하므로 ChartManager가 들고 있는 int 슬롯이 계속 유효합니다. `bus`와 `volume` 인자는 무시합니다(사용처 0). |
| 볼륨 4종 | `IAudioMixer[...]` |
| `GetAvailableDevices`, `SetAudioDevice(i)` | 캐시된 WASAPI 목록 이름을 돌려줍니다. i → GUID → `AudioOutputService`의 setDriver 경로로 가고, 같으면 Unchanged입니다. |
| `ConfigureOutput` | no-op(죽은 API) |
| `OnEngineClosing` / `OnEngineOpened` | 슬롯 스냅샷(파일, loop, 위치 ms, 재생 여부)을 뜨고 release합니다. 열린 뒤 NONBLOCKING으로 다시 열어 READY가 되면 `setPosition(ms)` 후 재생합니다. 게임 곡 도중 복구하면 스냅샷 위치에서 이어집니다. 샘플 단위로 정확하지는 않으며 과도기 한계입니다(R17, I1에서 해소). |

---

## 4. 출력·장치 관리

### 4-1. DeviceCatalog

- 현재 타입은 메인 System으로 열거하고, 다른 타입은 초기화하지 않은 임시 System으로 열거한 뒤 곧바로 release합니다(F9). 메인이 ASIO이면 임시 ASIO System을 만들지 않습니다(F16, SP9).
- `GetDevicesAsync(kind, refresh, ct)`는 `await UniTask.Yield(PlayerLoopTiming.Update, ct)`로 호출자가 "검색 중…"을 한 프레임 그리게 한 뒤, 메인 스레드에서 열거합니다(STA). 결과는 타입별 캐시에 넣습니다. 드라이버별 실패는 격리해 로그로 남깁니다. 열거가 끝날 때까지 그 프레임은 블록되며, 이는 SP9에서 측정합니다.
- `GetCachedDevices(kind)`는 열거하지 않고 캐시만 돌려줍니다. 캐시는 DEVICELISTCHANGED, `refresh:true`, 설정 화면 진입 때 무효화합니다.
- `Guid.Empty`인 "NoSound Driver"는 목록에서 뺍니다(F10). `ResolveIndex`는 setDriver나 init 직전에 매번 호출합니다. 현재 장치는 앱이 추적한 GUID로 표시합니다.

### 4-2. 변경 등급 (`OutputChangePlanner`, 순수)

| 등급 | 조건 | 방식 | 허용 시점 |
|---|---|---|---|
| 즉시 | 버스 볼륨 | `IMixBus.Volume` | 언제나 |
| Unchanged | 요청이 실제 적용값과 같음 | 아무것도 하지 않음(M8 글리치 제거) | – |
| MaskOnly | Follow-Default↔Pinned 전환인데 해석한 인덱스가 현재 드라이버와 같음 | `SystemCallbackHub.SetMask(MaskFor(새 모드))`만 호출 | 곡 세션 비활성 |
| **SetDriver** | 같은 출력 타입의 장치 변경: WASAPI Pinned↔Pinned, **Follow-Default↔Pinned**, **ASIO 드라이버 A→B**. 새 장치 systemrate가 믹서 레이트와 같아야 함 | `setCallback(null,0)` → `setDriver(ResolveIndex)` → 검증 → `setCallback(hub, MaskFor(새 모드))`(init 후 마스크 교체) → `getSoftwareFormat` 재조회 → 모델 하드 리셋. 음악 채널이 죽었으면 스냅샷에서 복원 | 같음 |
| FullReinit | 출력 타입 변경, 버퍼 길이·개수 변경, **레이트 불일치**(결정 4: 레이트는 재초기화), 격상 플래그 | 4-3 트랜잭션 | 같음. 예외는 곡 도중 장치 손실 복구(세션을 먼저 Paused로 둠) |

- 격상 플래그(`OutputChangePlanner` 옵션, 스파이크가 실패했을 때만 켬): `EscalateAllDeviceChanges`(SP5), `EscalateModeSwitch`(SP10에서 마스크 교체 뒤 자동 전환이 재개되지 않을 때), `EscalateAsioDriverChange`(SP8에서 ASIO setDriver가 실패할 때)
- **init 후 `setOutput`은 어떤 경로에서도 쓰지 않습니다.** SP7은 폐기합니다.

### 4-3. 재초기화 트랜잭션 (`ApplyAsync`, 기본은 close → 재설정 → init)

1. 전제: 메인 스레드이고 `CanReconfigure`가 참이어야 합니다. 곡 세션이 없거나 Stopped·Disposed여야 하며, 예외는 복구 모드입니다. 아니면 `Rejected(SessionActive)`입니다. 설정은 로비에서만 열리므로 게임 세션은 이미 Disposed 상태입니다.
2. `await UniTask.Yield()` 한 번 뒤로는 동기로 진행합니다.
3. `SafeModeFlag.Set(hash)`. `_lastApplied`(현재 실제 구성)를 롤백 후보로 기억합니다.
4. `OnEngineClosing()`을 등록 역순으로 호출합니다. 참여자는 **LegacyAudioManagerAdapter(S4~C)**, MusicPlayers(스냅샷), OneShotBank(등록 목록 유지), SongPlayer·StreamLoader(5-7 표), FmodMixer입니다.
5. `setCallback(null, 0); close()`
6. 같은 System에서 3-2의 1~8을 수행합니다. 실패하면 release하고 3-3 체인의 2번(롤백)부터 새 System으로 시도합니다.
7. 성공 처리(3-2 [성공]): 원샷은 같은 슬롯에 동기로 다시 로드하고, 음악은 NONBLOCKING으로 다시 열어 READY 뒤 `setPosition` 후 재생합니다.
8. `SafeModeFlag.Clear()` → `StatusChanged` → `AudioApplyResult{Applied | AppliedWithFallback(FallbackStep) | Failed}`

SP6에서 불안정하면 `ReinitStrategy = ReleaseAndCreate`로 5~6단계를 바꿉니다. 계약은 그대로입니다.

### 4-4. 핫플러그 콜백 (마스크 항상 명시, F11)

| 모드 | 마스크 | 메인 스레드 처리 |
|---|---|---|
| WASAPI Follow-Default | `DEVICEREINITIALIZE \| DEVICELOST \| ERROR` | FMOD 자동 전환을 유지합니다. DEVICEREINITIALIZE를 받으면 driver 0 정보와 레이트를 다시 읽고, 모델을 하드 리셋하고, StatusChanged를 발행합니다. 활성 세션은 먼저 `Pause(DeviceChanged)`합니다. |
| WASAPI Pinned, ASIO | 위 마스크 + `DEVICELISTCHANGED` | 카탈로그를 갱신하고 `DevicesChanged`를 발행합니다. 고정 GUID가 사라지면 세션을 일시정지한 뒤 driver 0으로 setDriver합니다(설정 GUID는 유지, `IsFallback`). GUID가 돌아오면 세션이 비활성일 때 되돌립니다. NOSOUND이면 Degraded로 두고 워치독으로 복구합니다. 콜백 안에서 setOutput으로 복구하지 않습니다(F13). ASIO의 DEVICELOST는 복구 재초기화로 처리합니다. |
| 모드 전환 | SetDriver·MaskOnly 등급에서 `SetMask`로 교체 | SP10에서 교체 뒤 자동 전환이 재개되는지 확인합니다. 실패하면 `EscalateModeSwitch`를 켭니다. |
| 공통 | `OUTPUTUNDERRUN`, `MIDMIX`는 등록하지 않습니다. 언더런은 모델 불연속(5-3)으로 잡습니다. | – |

- 트램펄린: `[AOT.MonoPInvokeCallback(typeof(FMOD.SYSTEM_CALLBACK))] static RESULT OnSystem(...)`. 살아 있는 System의 IntPtr과 비교한 뒤 CAS 루프로 비트를 세웁니다(`Interlocked.Or` 없음). d1과 d2는 `Interlocked.Exchange`로 저장합니다. FMOD API, Unity API, 로그는 호출하지 않습니다.
- **OutputWatchdog**(1Hz, 포커스 상태이고 에디터 일시정지가 아닐 때): NOSOUND로 떨어졌거나 **기준 클록**이 500ms 넘게 멈추면 Recovering으로 갑니다. 복구는 5초 간격으로 최대 3회 시도하고, 그래도 안 되면 Degraded입니다. 활성 세션은 먼저 `Pause(DeviceChanged)`합니다.

### 4-5. ASIO 제약

- **노출**: `UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN`이고 X64이며 `AsioEnabled`일 때만(F15)
- **STA**: 아파트먼트를 로그로 남기고, 모든 호출을 메인 스레드로 가드합니다(SP2).
- **블록**: 프리셋 64/128/256/512/1024, count 2를 요청합니다. "ASIO 제어판의 버퍼 크기와 같은 값을 선택하세요"라고 안내합니다. init 뒤 `getDSPBufferSize`를 표시하고, count가 2이고 길이가 요청값과 같으면 "저지연 모드(추정)"로 표시합니다. COM 추천 기능은 범위 밖입니다.
- **단일 인스턴스**: 재초기화는 같은 System의 close→init이고, 폴백은 release 뒤 새 System입니다. 드라이버 A→B는 같은 System의 setDriver입니다(SP8). 메인이 ASIO일 때 임시 ASIO 열거를 하지 않습니다.
- **레이트**: `systemrate`로 `setSoftwareFormat`합니다. 거부되면 폴백합니다(SP8).
- **포커스 복귀**: 기준 클록이 250ms 안에 진행하지 않으면 Recovering(재초기화)으로 갑니다(SP14).
- **개발 가이드**: FMOD Studio 툴, DAW, 에디터 미리듣기 System과 충돌할 수 있습니다(F17). 에디터에서는 WASAPI를 권장하고 ASIO는 `AudioSpike-Dev` 플레이어 빌드에서 검증합니다(12-1).

### 4-6. 설정 UI 흐름

```
SoundSettingUI.OnEnable → kinds = [WASAPI] + (IsSupported(Asio) ? [ASIO] : [])
   → 목록 = GetCachedDevices(kind) (비었으면 "검색 중…") → GetDevicesAsync(kind, refresh:true, destroyToken) 완료 시 다시 그림
   → DevicesChanged 구독
타입 변경 → 같은 방식으로 그 타입 목록, 저장 GUID 선택, 버퍼 슬라이더 = GetBufferPresets(kind)
Save → 볼륨·playInBackground: settings.Apply()
     → req = AudioSettingsMapper.ToRequest(_pending); 실제 적용값과 같으면 Unchanged
     → CanReconfigure 실패면 사유 표시 / 아니면 버튼 비활성 + "적용 중…" → await ApplyAsync(req, destroyToken)
     → 결과 표시(실제 타입·장치·len×count·레이트, 폴백 단계와 사유) → 8-3 규칙으로 저장 → settings.Save()
```

---

## 5. 곡 재생과 SongClock

### 5-1. 기호와 클록 도메인

- q: QPC(초), R: 믹서 레이트(세대별 캐시, C8), L: 블록 길이, Fs: 음원 기본 레이트
- **기준 클록 c_ref**: 시스템 마스터 ChannelGroup의 `getDSPClock`입니다. pause, mute, pitch를 절대 걸지 않습니다. 회귀와 세그먼트는 모두 이 도메인에서 계산합니다.
- **곡 도메인 c_song**: 곡 채널의 parentclock(`SCO.Song`)입니다. 샘플 단위 예약은 이 값으로만 합니다(F25). 그룹은 `addGroup(child, propagatedspclock: true)`로 연결하고 어떤 그룹도 pause하지 않습니다.
- **도메인 연결**(`ClockDomainProbe`, A-B-A): `m1 = ref`, `ch.getDSPClock(out _, out p)`, `m2 = ref`를 읽고 `m1 == m2`일 때 `offset = p − m1`을 채택합니다(최대 4회 재시도). 앵커를 커밋할 때마다 다시 재고, 재생 중에는 1초마다 검증합니다. 값이 바뀌면 `Cref_ref`를 옮기고 소프트 에폭을 올립니다. 절대값이 같다고 가정하지 않습니다(SP4).

### 5-2. 앵커 커밋 `ScheduleFrom(τ0)`: 시작, 재개, 재예약 공통 (DSP 읽기 한 번)

```
[Start 1회] offsets = TimingOffsets(); Z = leadIn + AudioOffsetMs/1000     ★ audioOffsetMs 유일 적용 지점
            judgement = new JudgementTimeline(offsets.JudgmentOffsetSteps)  ★ 판정 싱크 래치
[채널 준비] pos = τ0 − Z; k = max(0, round(pos·Fs))
            ch = playSound(sound, SongGroup, paused:true); setPriority(0); setLoopCount(0); setPosition(k, PCM)
            준비 완료 = openstate ∉ {SEEKING, SETPOSITION, LOADING, BUFFERING} && !starving
[커밋]      ClockDomainProbe → p, offset;  lead = max(3·L, ⌈0.015·R⌉)
            Cf = p + lead;  S = Cf + (pos < 0 ? round(−pos·R) : 0)
            ch.setDelay(S, 0, false); ch.setPaused(false)
            검증: pNow + L ≥ Cf 이면 stop → 한 번 다시(lead×2)
            세그먼트 = Running{ Cf_ref = Cf − offset, Cref_ref = S − offset, τ0, τref = Z + k/Fs }
            τ(c_ref) = c_ref < Cf_ref ? τ0 : τref + (c_ref − Cref_ref)/R
[무음 커밋] 채널 없이 Cf_ref = c_ref(now) + lead, τ(c) = τ0 + max(0, c − Cf_ref)/R
            (무음 세션, 음원이 끝난 뒤의 재개, 엔진 Failed(c_ref = q·R)에 공통)
```

- 스트림 옵션은 `CREATESTREAM | NONBLOCKING | ACCURATETIME | LOOP_OFF`입니다(결정 8). seek가 부정확하면 R5로 갑니다.
- Start는 Ready에서만 받습니다. 즉시 커밋할 수 없으면 Starting 상태로 Tick에서 커밋합니다. 모델이 WarmingUp이면 최대 1초 기다립니다.
- 채널 pause와 그룹 pause는 쓰지 않습니다. 대기 delay가 걸린 채널을 pause했을 때의 동작이 문서에 없고(F26), 그룹을 pause하면 도메인 클록이 멈추기 때문입니다.

### 5-3. 연속 회귀 `DspQpcModel` (순수 C#, 하한 포락선)

- **샘플**: Update와 LateUpdate에서 `(q0, c, q1)`을 읽습니다. `q1 − q0 > 50µs`이면 버립니다.
- **관측**: `x = mid(q0,q1)/f − q_e`, `r = x − (c − c_e)/R`. 관측이 항상 클록이 오른 뒤에 일어나므로 r은 단측 잡음이고, 하한 포락선이 참 관계입니다.
- **추정**: 250ms 버킷 최솟값을 구하고, 최근 120개 버킷(30초)에 최소제곱 `r = a + d·x`를 맞춥니다. 위쪽 이상치(`> max(L/R, 2ms)`)를 기각하고 한 번 다시 맞춥니다. 버킷이 4개 미만이면 `a = min r`, `d = 0`, 4~19개면 d = 0, 20개 이상이고 폭이 5초 이상이면 d를 쓰되 ±500ppm으로 클램프합니다.
- **평가**: `DspAt(q) = c_e + R·((1 − d)·x − a)`, `QpcAt`는 그 역함수입니다. 파라미터 이력 4개를 보관합니다.
- **소프트 에폭**(불연속): 같은 부호로 연속 3개 버킷의 잔차가 3L/R을 넘으면 절편만 리셋하고 d는 유지합니다. `ClockEpoch++`(ClockStep). 언더런 검출도 이 방법으로 합니다.
- **하드 리셋**(버킷 전부 삭제, d = 0, `ClockEpoch++`): **재초기화(Generation 변경), setDriver, DEVICEREINITIALIZE에서만** 합니다.
- **MarkSuspect**(포커스 복귀, 에디터 일시정지 해제): 버킷 이력과 d는 유지하고 Quality를 Suspect로 내립니다. 복귀 뒤 첫 2개 버킷 안에서 `|r_b − fit| > max(L/R, 2ms)`이면 즉시 소프트 에폭(절편 리셋, d 유지, `ClockEpoch++`)을 냅니다. 그렇지 않으면 새 버킷 8개(2초)가 쌓인 뒤 Locked로 복귀합니다. mixerSuspend를 쓰지 않으므로 기준 클록은 백그라운드 동안에도 흐르고, 보통은 에폭 없이 복귀합니다(추론, SP14에서 확인).
- **품질**: 버킷 4개 미만은 WarmingUp, 8개 이상이고 잔차 p99 ≤ L/R이면 Locked, 기각률이 50%를 넘으면 Suspect입니다.
- **정밀도(추정)**: 버킷 최솟값 편향은 약 0.3ms이고 같은 fps에서 일정해 노트 싱크에 흡수됩니다. 절편 잡음은 0.1ms 미만입니다. 파라미터는 SP3로 확정합니다.
- 기준 클록은 엔진 세대 동안 멈추지 않으므로, 로비에서 수렴한 모델이 첫 노트까지 이어집니다.

### 5-4. 판정 평가기, 렌더 평가기, 스냅샷

- **판정 평가기** `TryGetSongTime(qpc)`: `FromQpc ≤ qpc`인 가장 최근 세그먼트를 씁니다. Frozen이면 τ_frozen, Running이면 `τ(DspAt(qpc))`입니다. 결정적이고 스무딩이 없습니다.
- **프레임 SongTime**: 같은 에폭 안에서 `max(prev, TryGetSongTime(q*))`입니다. `JudgeTime = judgement.ToJudgeTime(SongTime)`입니다.
- **렌더 평가기**: 하드 에폭에서는 스냅합니다. 평소에는 `pred = R_prev + Δx·(1 − d)`로 예측하고, 오차가 +30ms를 넘으면 스냅, −30ms보다 작으면 유지, 그 사이는 60ms 시상수로 수렴합니다. 단조 증가를 보장하고 Frozen이면 고정합니다. 표시 시각 예측이나 지연 보정은 하지 않습니다(결정 6).
- 판정선, 노트, 카운트다운, BGA, 게임플레이가 모두 `ISongClock.Frame` 하나를 읽습니다.

### 5-5. 일시정지와 재개

```
Pause(reason):  Starting | LeadIn | Playing(음원 종료 후 포함) | Resuming 에서만 (멱등)
  T_p = reason == DeviceChanged ? Frame.SongTime : TryGetSongTime(Qpc.Now)   // Resuming이면 τ0 그대로
  채널이 있으면 ch.stop(); 예약 원샷 일괄 취소; timeline.Freeze(T_p, Qpc.Now); Epoch++
  선 seek: !IsAudioFinished && T_p < E 이면 ch' = playSound(paused) + setPosition(max(0, round((T_p − Z)·Fs)), PCM)
           그 외(음원 종료 후·무음)는 선 seek 없음 → 재개는 무음 커밋
  State = Paused; Changed(Paused, reason)
Resume():  Paused → Resuming. CanResume이면 즉시, 아니면 Tick에서
  ScheduleFrom(T_p) with ch' (또는 무음 커밋) → State = T_p < Z ? LeadIn : Playing; Changed(Resumed)
```

- **리드인 중 일시정지**: `S = Cf + (Z − T_p)·R`로 남은 대기만 다시 예약합니다(H2 해소).
- **음원이 끝난 뒤의 일시정지**: 클록만 멈춥니다. 차트, 판정선, 판정이 모두 T_p에서 멈추고, 재개하면 무음 커밋으로 T_p부터 이어집니다. 현행 동작(`GameManager.cs:171`, 일시정지 중 차트 고정)과 같습니다.
- **들리는 소리**: stop이 반영되기 전에 이미 믹스된 최대 약 1블록만 겹치고, 건너뛰는 구간은 없습니다.
- **가드 구간**(lead ≈ 15~21ms): 곡 시각은 T_p로 고정되고, 이 구간의 입력은 Judgeable입니다.
- 매번 샘플 단위로 재예약하므로 오차가 누적되지 않습니다(L1 해소).

### 5-6. 포커스 손실 (결정 7)

| 상황 | 동작 |
|---|---|
| `SettingsManager.Apply` | `Application.runInBackground = playInBackground`, `SetBackgroundPolicy(playInBackground)` |
| 포커스 손실(`OnApplicationFocus(false)`와 `OnApplicationPause(true)`를 같게 처리) | 세션이 **Starting, LeadIn, Playing(음원 종료 후 포함), Resuming** 중 하나면 설정과 관계없이 `Pause(FocusLost)`합니다. 로비에서 playInBackground가 false면 `SCO.Master`를 음소거하고, true면 계속 재생합니다. mixerSuspend는 쓰지 않습니다. |
| 카운트다운 중 포커스 손실 | 세션이 포커스 대기를 기억합니다. 포커스가 없는 상태에서 `Resume()`이 오면 커밋하지 않고 `Changed(Paused, FocusLost)`를 다시 발행합니다(G12). |
| 복귀 | 음소거를 해제하고 **`MarkSuspect()`**(하드 리셋 아님, 5-3)를 호출합니다. ASIO면 출력이 살아 있는지 확인합니다. 세션은 사용자가 재개할 때까지 Paused이고, 재개는 예약 재시작입니다. |

runInBackground가 false면 백그라운드에서 `system.update()`가 멈추지만 믹서 스레드는 계속 돕니다. 장치 이벤트는 복귀 즉시 처리합니다(R12).

### 5-7. 장치 손실과 재초기화 중 세션 처리 (상태별)

감지하면(콜백 플래그나 워치독) 엔진은 먼저 일시정지할 수 있는 세션에 `Pause(DeviceChanged)`를 걸고 복구에 들어갑니다(Follow-Default는 자동 전환 뒤 DEVICEREINITIALIZE, Pinned는 driver 0으로 setDriver, NOSOUND나 DEVICELOST는 복구 재초기화). Generation이 바뀌면 모든 상태가 아래처럼 처리됩니다.

| 상태 | OnEngineClosing | OnEngineOpened |
|---|---|---|
| (LoadAsync 진행 중, 세션 없음) | 로딩 중 Sound를 최대 200ms 기다린 뒤 release | 새 세대에서 다시 엽니다. LoadAsync의 10초 시간 제한은 계속 적용됩니다. |
| Ready | Sound를 release하고 `NeedsReopen` 표시 | NONBLOCKING으로 다시 엽니다. Start가 먼저 오면 Starting에서 열기와 seek 완료를 기다립니다. |
| Starting | 준비한 채널과 Sound를 release | 다시 열고 τ0로 다시 seek한 뒤 커밋합니다. **Starting이 3초를 넘기면** 무음 세션으로 전환합니다(경고 로그, `IsAudioFinished = true`, `Changed(Started)`). 채보는 진행하므로 M1 같은 멈춤이 생기지 않습니다. |
| LeadIn, Playing, Resuming | (이미 `Pause(DeviceChanged)`로 Paused) | – |
| Paused / Recovering | 상태를 `Recovering`으로 바꾸고 ch'를 release | 음원이 남아 있으면 다시 열고 T_p로 선 seek합니다. 준비되면 `CanResume = true`, `Changed(Recovered)`를 발행하고 Paused(또는 대기 중이던 Resuming)로 돌아갑니다. 음원이 끝났으면 재오픈 없이 곧바로 CanResume입니다. |
| Stopped | 채널과 Sound를 버림 | 아무것도 하지 않음(클록은 Stopped 유지) |
| Disposed | 레지스트리에서 이미 빠짐 | – |

- 복구 중 Resume이 오면 Resuming으로 기다립니다. 차트는 멈춰 있고 입력은 Judgeable=false입니다. 복구에 실패하면(Degraded) 무음으로 재개할 수 있습니다.
- 세그먼트가 기준 도메인에 있고 모델을 하드 리셋하므로, 재초기화 뒤 DSP 클록이 0부터 다시 시작해도 괜찮습니다(SP6).

### 5-8. 곡 음원 종료 감지

- `E = Z + getLength(PCM)/Fs`. 감지는 **Playing이면서 `!IsAudioFinished`일 때만** 합니다(Paused, Recovering, Resuming 중에는 하지 않음).
- `!ch.isPlaying`이고 τ ≥ E − 2L/R이면 `IsAudioFinished = true`와 `Changed(AudioEnded)`를 발행합니다. **상태는 Playing으로 남고, Stop 전까지 클록이 흐르며, 일시정지할 수 있습니다.**
- `!isPlaying`인데 τ < E − 0.5초이면 스트림 중단으로 보고 `Pause(StreamStalled)`합니다. τ ≥ E + 0.25초이면 채널 상태와 관계없이 종료로 처리합니다(안전망).
- 무음 세션은 Start 직후 `IsAudioFinished = true`이고 상태는 LeadIn/Playing입니다. 일시정지와 재개가 곡 전체에서 동작합니다.

### 5-9. 세션 상태 머신

| 현재 | 이벤트 | 다음 |
|---|---|---|
| (LoadAsync) | READY / ERROR, 시간 초과, 취소 | Ready / 결과만 반환 |
| Ready | `Start` | Starting → 커밋되면 LeadIn 또는 Playing |
| Starting | 3초 초과 | Playing(무음 전환) |
| LeadIn | τ ≥ Z | Playing (`AudioStarted`) |
| Playing | 음원 종료 | Playing (`IsAudioFinished`, `AudioEnded`) |
| Starting, LeadIn, Playing, Resuming | Pause, 포커스, 장치, 스트림 | Paused |
| Paused | `Resume` / 엔진 재구성 | Resuming / Recovering |
| Recovering | 재로드·선 seek 완료 | Paused(또는 Resuming) |
| Resuming | 커밋 | LeadIn 또는 Playing |
| 모든 상태 | `Stop` / `Dispose` | Stopped / Disposed |

Disposed 이후의 모든 호출은 no-op입니다.

---

## 6. 입력과 판정 타이밍

### 6-1. 이벤트 모델과 변환

- **소스**(`UnityInputSystemTimestampSource`, `FrameQuantized = true`)
  - InputManager 레인 콜백이 `IsInputActive` 게이트 뒤에서 `Push(lane, isDown, ctx.time)`을 호출합니다. 256칸 사전 할당 링을 쓰고, 넘치면 오래된 것부터 버리고 카운터를 올립니다.
  - **Synthetic 태깅** (a): `SwitchToUI()`와 `Disable()`의 `Game.Disable()`을 `BeginSynthetic/EndSynthetic`으로 감쌉니다.
  - **Synthetic 태깅** (b): `Push` 시점에 `!Application.isFocused`이면 Synthetic입니다. runInBackground가 참(설정 ON, 에디터는 항상)이면 Input System이 포커스를 잃을 때 장치를 소프트 리셋해 `canceled`를 만듭니다(패키지 InputManager.cs:2960-3050). 이 이벤트의 qpc가 FocusLost Freeze보다 앞설 수 있기 때문입니다.
  - 1차 방어는 qpc입니다. Pause(G7)가 `SwitchToUI`보다 먼저 Freeze하므로 그 canceled는 Frozen 구간에 들어갑니다.
  - `backgroundBehavior`는 기본값을 유지합니다. IgnoreFocus로 바꾸면 백그라운드에서 뗀 키가 눌린 채 남을 위험이 있습니다.
- **도메인 변환** `RealtimeQpcMapper`: `OnFrame`마다 `q1; t = InputState.currentTime; q2`를 읽고, `q2 − q1 < 50µs`인 표본에서 `o = mid/f − t`를 구합니다. 32개 표본의 중앙값을 쓰고, `qpc = FromSeconds(ctx.time + o)`로 변환합니다. SP11에서 변동이 0.1ms 이상이면 회귀로 바꿉니다.
- **수집** `LaneInputHub.Collect(q)`: 모든 소스를 드레인합니다. `qpc > q*`이면 다음 프레임으로 보류합니다. qpc 오름차순 안정 삽입 정렬(F30)을 합니다. `TryGetSongTime`으로 SongTime, Epoch, IsRunning을 구하고, `JudgeTime = judgement.ToJudgeTime(SongTime)`, `Judgeable = IsRunning && !Synthetic`, `BatchId = Frame.Index`로 둡니다. 세션이 없으면 SongTime = NaN, Judgeable = false입니다. 할당은 0입니다.
- **Horizon**: 1단계는 같은 스레드라 드레인 시각이 horizon이고, 2단계는 생산자 워터마크를 씁니다. 불변식은 J2입니다.

### 6-2. judgmentOffset은 한 곳에서만 적용

- `JudgementTimeline.ToJudgeTime(τ) = τ − steps × 0.003`. steps는 Start에서 래치합니다.
- 이 함수를 부르는 곳은 `SongClock.BuildFrame`과 `LaneInputHub` 두 곳입니다. `JudgementAdvance`는 이 두 값으로만 만듭니다.
- 게임플레이는 오프셋을 모릅니다. hit, release, hold body, miss 컷오프가 모두 판정 시각으로 비교하므로 M15가 해결됩니다(I2부터). 창 중심 부호는 현행(`ChartManager.cs:709`)과 같습니다.
- 마디 전환, 카운트다운, 렌더는 SongTime과 RenderTime을 쓰므로 판정 싱크로 움직이지 않습니다. audioOffsetMs는 Z에서만 적용합니다. 두 설정의 UI와 단위는 바꾸지 않습니다.

### 6-3. 판정 Pump (`JudgementPump`, 순수)

```
Run(f, events, client, mode):   // from/songFrom: Attach 때 f.JudgeTime/f.SongTime로 초기화, 에폭을 넘어 연속
  if Interleaved:
     foreach e (qpc 오름차순):
        if e.Judgeable && e.JudgeTime > from: client.Advance((from, e.JudgeTime], max(e.SongTime, songFrom)); from = e.JudgeTime …
        client.OnLaneInput(e)
  else foreach e: client.OnLaneInput(e)
  if f.JudgeTime > from: client.Advance((from, f.JudgeTime], max(f.SongTime, songFrom)); from = f.JudgeTime …
  // JudgeNow가 뒤로 가면 from 유지
```

- Runner Early에서 돌기 때문에 타격음이 스크립트 Update 중 가장 이른 시점에 납니다.
- Frozen에서는 진행 구간이 비어 있습니다. 한 프레임에 Advance가 여러 번 불려도 ChartManager의 단조 시간 처리는 멱등입니다. 문제가 생기면 `FrameBatched` 킬 스위치로 되돌립니다.
- `JudgementFeed`는 클라이언트 호출을 try/catch로 감싸고 로그를 1초에 한 번으로 제한합니다.

### 6-4. 홀드·miss 규칙의 위치와 부호 있는 오차

- **I2(필수)는 ChartManager의 규칙을 바꾸지 않습니다.** 홀드 몸통은 지금처럼 점 샘플 `|judgeTo − note| < JUDGE_PERFECT`이고, miss는 Advance마다 `CheckMissedNotes(judgeTo)`를 한 번 호출합니다. 순서도 현행(miss 먼저, 그다음 홀드)을 유지합니다. 바뀌는 것은 비교하는 시각(판정 시각)과 샘플 지점뿐입니다. Interleaved에서는 입력마다 Advance가 하나씩 더 생기므로 샘플이 늘어나는데, 이는 현행보다 조밀해지는 쪽입니다.
- **I3(선택)과 R2 안내**: 규칙 개선은 게임플레이의 결정입니다(11c). `TimingWindow.Overlaps(from, to, note, PERFECT)`로 하는 구간 교차 홀드(J4)와 한 Advance 안의 다중 miss 루프(가드 64, 홀드를 먼저)는 순수 도우미로 제공합니다. 적용은 I3 PR이나 리팩터된 클래스가 맡습니다.
- **오차 기록**: 판정이 확정되는 네 곳(Press, Release, HoldBody, Miss)에서 `IJudgementTimingLog.Record`를 **반드시** 호출합니다. 판정 시각은 Press와 Release는 입력의 JudgeTime, HoldBody는 샘플 시각(I2는 judgeTo, I3는 `ClosestPoint`), Miss는 확정 시각 judgeTo입니다. `SignedErrorMs = (judgeTime − note)·1000`이며 Miss도 정의됩니다(항상 ≥ UMM). 결과 화면 자동 제안은 하지 않습니다.

### 6-5. CharacterAnimator의 "같은 프레임 = 동시 입력"

한 프레임의 입력은 같은 Pump 호출 안에서 동기로 전달되고, `TryJudgeInput → OnLaneInput → CharacterAnimator` 순서도 그대로입니다. 따라서 `Time.frameCount` 비교(:117)는 바꾸지 않습니다. `BatchId`는 2단계 대비 훅입니다.

### 6-6. 게임플레이가 할 일 (현재 코드든 리팩터 후든)

1. `LoadAsync(new SongLoadRequest(file, gameObject.scene.handle), ct)`를 호출합니다. 실패하면 로비로 돌아가고, 경로가 비었거나 엔진이 없으면 `CreateSilent`를 씁니다.
2. 차트 초기화 전에 `AttachJudgement`(또는 binding)로 연결하고, 끝나면 Dispose합니다.
3. 리드인을 아는 쪽이 `Start(new SongStartOptions(barDuration))`를 한 번 호출합니다.
4. `Advance`에서 `ToSongTime`으로 마디와 카운트다운을, `ToJudgeTime`(필요하면 From도)으로 miss와 홀드를 처리합니다.
5. `OnLaneInput`에서 `Judgeable`이면 `JudgeTime`으로 판정하고 타격음을 냅니다. 판정 불가 release는 상태만 해제합니다.
6. 비주얼은 `RenderTime`, 그 밖의 시각 조회는 `SongTime`을 씁니다.
7. `Pause/Resume`을 쓰고, 사용자가 아닌 사유의 `Changed(Paused)`를 받으면 Pause UI를 띄웁니다.
8. 종료 조건은 채보 소진 && `IsAudioFinished`이고, 종료할 때 `Stop()`합니다.
9. 판정마다 `Record`를 호출합니다.

**금지**: 두 오프셋 직접 읽기, FMOD·DSP 직접 사용, `Time.*`나 `AudioSettings.dspTime`으로 곡 시각 계산.

### 6-7. 오차 예산 (추정)

| 오차원 | 현재 | 재설계 후 |
|---|---|---|
| 키보드 프레임 양자화(F30) | 0~1프레임 | 같음(최대 오차원, 2단계 대상, 고 FPS 유지) |
| 동기점 1개와 DSP 계단 | 판마다 무작위 0~1블록 | 0.5ms 미만, 판마다 일정 |
| 드리프트 | 100ppm이면 3분에 18ms | 30초 창으로 추적 |
| 이중 DSP 읽기(F36) | 0~1블록 | 0 |
| 일시정지 사이클(L1, H2) | 랜덤워크, 리드인 선행 | 0 |
| 포커스와 mixerSuspend(H1) | 백그라운드 시간 전체 | 0 |
| 홀드 몸통 | 프레임 샘플, 히치 42ms 초과 시 누락 | I2: 같은 규칙, 샘플 증가 / I3: 구간 교차 |
| miss 컷오프(M15) | 판정 싱크 미적용 | 적용(I2) |
| 출력 지연 | 사용자 노트 싱크 | 같음(자동 보정 없음) |

---

## 7. 원샷과 믹서

### 7-1. ChannelGroup 트리

```
System Master  ← 기준 클록. 절대 pause/mute/pitch/volume 변경 안 함
└─ SCO.Master (masterVolume, 포커스 음소거)
   ├─ SCO.Music (bgmVolume)
   │   ├─ SCO.Song   ← 게임 곡 전용. pause/pitch 금지
   │   └─ (Lobby/Preview/Legacy 재생기 채널은 SCO.Music 직속)
   ├─ SCO.HitSound (hitSoundVolume)
   └─ SCO.Sfx      (sfxVolume)
```

- 모든 그룹은 `addGroup(child, propagatedspclock: true)`로 붙입니다. `IMixBus`는 FMOD 핸들을 노출하지 않고, 볼륨은 선형 0~1입니다. `AudioBus` enum을 인덱스로 쓰던 결합은 없앱니다.
- 사용자용 Mute API는 두지 않습니다. 하이브리드(C)로 가면 `FmodMixer`와 `Bus()` 구현만 바꿉니다(이번에는 구현하지 않음).

### 7-2. 채널 수와 우선순위

`setSoftwareChannels(64)`, `init(256)`으로 에디터와 빌드를 같게 둡니다(L8). 곡과 음악 재생기는 우선순위 0, 타격음 64, Sfx 128이며 등록 때 `setDefaults`로 설정합니다.

### 7-3. 원샷 (`OneShotBank`)

- `CREATESAMPLE | _2D | LOOP_OFF | IGNORETAGS | LOWMEM` 동기 로드, 파일명 멱등, 슬롯 상한 없음, 등록 때만 슬롯 배열을 원자적으로 교체합니다.
- `Play`: `(uint)(Slot1 − 1) < count && _accepting`을 확인한 뒤 `playSound(sound, busGroup, false, out _)`. 채널별 setVolume은 없습니다(L7). 할당, 로그, RESULT 분기가 없습니다.
- 재초기화하면 같은 슬롯에 다시 로드합니다(`OneShotId`와 레거시 int 슬롯 모두 유효). 타격음은 입력 시에만 재생합니다(결정 8).

### 7-4. 곡 시계 예약 훅 `ScheduleOneShot(id, songTime)`

LeadIn이나 Playing에서만 받습니다. `c_ref* = Cref_ref + (songTime − τref)·R`에 버스 그룹의 괄호 오프셋을 더해 `playSound(paused) → setDelay → unpause`합니다. parentclock + 1블록보다 과거면 무효를 돌려줍니다. 고정 표에 보관하고, Pause, Stop, Dispose, 에폭 변경 때 일괄 stop합니다. 이번에는 하네스 메트로놈 검증에만 씁니다.

---

## 8. 설정, 마이그레이션, UI

### 8-1. `SettingsData` (Sound, S5)

```csharp
public int    settingsVersion  = 2;
public string audioOutputType  = "WASAPI";  // "WASAPI" | "ASIO"
public string wasapiDeviceGuid = "";        // "" = Follow-Default
public string wasapiDeviceName = "";
public string asioDriverGuid   = "";        // "" = 첫 ASIO 드라이버
public string asioDriverName   = "";
public int    dspBufferLength  = 256;
public int    dspBufferCount   = 4;         // UI 비노출. WASAPI 4, ASIO 2 요청
// 유지: 볼륨 4종, playInBackground, audioOffsetMs, judgmentOffset, inputPollingRateHz
// 삭제: audioDeviceIndex, audioBufferIndex (마이그레이션 프로브로만 읽음)
```

오케스트레이터 기본값에서 벗어나는 점은 하나입니다. 장치 GUID와 이름을 타입별로 저장합니다. 폴백 3단계(저장된 WASAPI GUID)를 ASIO 사용 중에도 지키기 위함입니다. 버퍼는 한 필드이고, 타입을 바꾸면 SnapUp합니다. 장치별 오프셋, visualOffset, hitSoundMode, 지연 보정 필드는 추가하지 않습니다.

### 8-2. 마이그레이션 (`App/SettingsMigration.cs` 순수, 파싱은 `SettingsManager.Load`)

```
json = PlayerPrefs.GetString("SCOdyssey.Settings.v1")      // 키 유지
비어 있음 → new SettingsData()
try { data = FromJson<SettingsData>; ver = FromJson<SettingsVersionProbe>.settingsVersion /*없으면 0*/ }
catch → "…v1.corrupt.bak" = json; 기본값; 경고 (L6)
ver < 2: "…v1.bak" = json (최초 1회); v1 = FromJson<SettingsV1SoundProbe>
   FromV1: WASAPI, wasapiDeviceGuid="" (인덱스는 신뢰 불가 → 기본 장치)
           dspBufferLength = SnapUp(Wasapi, {64,128,256,512,1024}[idx 범위 밖이면 2]) → 256,256,256,512,1024
           dspBufferCount = 4; settingsVersion = 2 → Save()
```

WASAPI 프리셋 초기안은 {256, 480, 512, 1024}입니다(F23 하한). ASIO는 {64, 128, 256, 512, 1024}입니다. 최종값은 SP3과 SP13으로 정합니다.

### 8-3. SettingsManager.Apply와 저장 규칙

- 그래픽과 입력 적용은 그대로 둡니다. Sound는 `IAudioMixer[...].Volume`, `Application.runInBackground = playInBackground`, `SetBackgroundPolicy`입니다.
- 출력 설정은 Apply에서 적용하지 않습니다. 부팅은 Installer, 설정 화면은 ApplyAsync가 맡습니다(L14).
- 설정 화면의 적용 결과 처리
  - `Applied`: pending을 저장합니다.
  - `AppliedWithFallback(Rollback)`: 직전 구성으로 복귀한 것입니다. pending과 Current를 실제값(= 이전 값)으로 되돌리고 "적용 실패(사유) — 이전 설정으로 되돌렸습니다"를 표시합니다.
  - 그 밖의 폴백과 `Failed`: `AudioSettingsMapper.WriteApplied`로 실제값을 저장합니다.
- 부팅 때의 폴백은 저장값을 덮어쓰지 않습니다.

### 8-4. SoundSettingUI 변경 (프리팹 포함, S5)

| 요소 | 변경 |
|---|---|
| enum | `Btn_OutputTypePrev/Next`, `Text_OutputTypeValue`, `Text_AudioStatusValue`를 추가합니다. `BaseUI.Bind`는 요소가 없으면 null로 두므로(`BaseUI.cs:83-86`) 코드는 null을 허용합니다. |
| 출력 타입 | WASAPI는 항상, ASIO는 `IsSupported(Asio)`일 때만 |
| 장치 목록 | 캐시로 먼저 그리고 `GetDevicesAsync`가 끝나면 다시 그립니다. WASAPI는 "시스템 기본 장치 (현재: …)"와 GUID 목록, ASIO는 드라이버 목록입니다. 없는 GUID는 "(연결 안 됨) {이름}"으로 표시합니다. ASIO 드라이버가 없으면 적용을 막습니다. |
| 버퍼 | `GetBufferPresets(kind)` 인덱스. ASIO일 때 제어판 안내. 중복 정의된 `BufferSizes`(:48)는 삭제 |
| 상태 줄 | 실제값(예: `WASAPI · 스피커 · 48000Hz · 256×4`), 저지연 모드, 폴백 단계와 세이프 모드 사유. 싱크 재확인 문구는 넣지 않습니다. |
| Save | 4-6 흐름. `:202-205` `SetAudioDevice`와 `:210` 주석을 삭제합니다. |
| GameSettingUI | 변경 없음 |

---

## 9. 소비자 마이그레이션 표 (ChartEditor 제외)

| 파일 | 바꿀 내용 | 단계 |
|---|---|---|
| `App/Managers.cs` | `:50-52`를 `Install(...)`과 `LegacyAudioManagerAdapter.RegisterInto(audio)`로 바꿉니다(3-1). S4에서는 `#if SCO_LEGACY_FMOD` A/B 스위치를 둡니다. 주석 `:30`, `:40`을 갱신하고, C에서 RegisterInto 줄을 삭제합니다. | S4, S5, C |
| `App/SettingsManager.cs` | Load에 try/catch, 백업, 마이그레이션. Apply의 `:71-78`을 8-3으로 바꾸고 IAudioManager 참조를 제거합니다. | S5 |
| `App/SettingsMigration.cs`(신규) | v1→v2 순수 변환 | S5 |
| `App/AudioSettingsMapper.cs`(신규) | **S4**: `ToBootRequest(v1)` = WASAPI, Follow-Default, `{64..1024}[audioBufferIndex]`×4(범위 밖이면 2, SnapUp 없음), `ToTimingOffsets`. **S5**: v2 `ToBootRequest`, `ToRequest`, `WriteApplied` | S4, S5 |
| `Domain/DTO/SettingsData.cs` | 8-1 | S5 |
| `UI/Settings/SoundSettingUI.cs` + 프리팹 | 8-4 | S5 |
| `UI/Settings/GameSettingUI.cs` | 변경 없음 | – |
| `App/InputManager.cs` | S4: `UnityInputSystemTimestampSource`를 만들어 `LaneTimestampSource`로 노출합니다. `:49-50`에서 `Push`를 호출합니다(레거시 이벤트 병행). `:55-59`, `:73-77`에 Synthetic 구간을 둡니다(포커스 태깅은 소스 내부). C: `:20-22`, `:81-92`와 레거시 이벤트를 삭제합니다. | S4, C |
| `App/Interfaces/IInputManager.cs` | C: `OnLanePressed/OnLaneReleased/SetTimeSyncPoint` 삭제(R11 게이트) | C |
| `App/Interfaces/IAudioManager.cs` | C: 삭제(죽은 타입 포함, R11 게이트) | C |
| `App/FMODAudioPreInit.cs` | 삭제(Platform.cs 되돌림과 같은 커밋) | S4 |
| `App/FMODAudioManager.cs` | S4: `#if SCO_LEGACY_FMOD` 안으로 옮김, S5: 삭제 | S4, S5 |
| `UI/MainUI.cs` | `TryGet<IMusicPlayers>` → `Get(Lobby).PlayAsync(bgm, true, cts.Token).Forget()`. OnDisable에서 `cts.Cancel()`과 로비 `Stop()`만 호출하고 전역 Stop은 없앱니다. 코루틴(`:61-85`)을 삭제합니다. | S6 |
| `UI/AdventureUI.cs` | 곡이 바뀔 때 이전 CTS를 취소하고 `Get(Preview).PlayAsync`를 호출합니다. OnDisable(`:74`)은 **`TryGet`**으로 바꾸고 프리뷰만 Stop합니다. `:151-172` 코루틴을 삭제합니다. | S6 |
| `App/UIManager.cs`, `UI/PauseUI.cs`, `UI/ResultUI.cs` | 변경 없음(씬 언로드 때 세션 자동 Dispose, M6) | – |
| `Game/GameDataLoader.cs` | `:38-57`: `LoadAsync(...).ToCoroutine(r => result = r)`로 기다립니다. 빈 경로나 EngineUnavailable이면 CreateSilent, Cancelled면 종료, 그 밖의 실패는 로비로 돌아갑니다(M1). | I1 |
| `Game/TimelineController.cs` | `:90-92`: timeProvider가 null이면 세션의 `Frame.RenderTime`, 세션이 없으면 레거시 `GetCurrentTime()`(C에서 0으로 교체). **`Init(..., Func<double> timeProvider = null)` 시그니처는 유지합니다.** | S7, C |
| `Game/BGAController.cs` | S7: 세션 추종(`videoT = RenderTime − AudioZeroSongTime`, 늦은 준비 따라잡기, Frozen이면 Pause, 에폭 재seek, 100ms 초과 드리프트면 2초에 1회 재동기화). `:165` 폴백을 삭제합니다. C: 레거시 경로와 `SchedulePlay/Pause/Resume`을 삭제합니다(R11). `Stop`은 유지합니다. | S7, C |
| `Game/CharacterAnimator.cs` | 핫패스 `Debug.Log` 7곳 삭제 | S7 |
| NoteController, LaneData, IGameManager, Constants | 변경 없음 | – |
| `Game/Test/GameSceneTester.cs` | `:24-28`을 LoadAsync로 바꾸고 `:11` 주석을 고칩니다. | C |
| **`App/GameManager.cs`** | 통합 지점 G1~G14(10-1) | I1, I2 |
| **`App/ChartManager.cs`** | 통합 지점 C1~C9(10-2), 선택 C10·C11(I3) | I2, (I3) |
| `Assets/Plugins/FMOD/src/Platform.cs` | **:871-872와 :874-878만 삭제합니다**(`[SCOdyssey 추가]` 주석과 두 Set 메서드). **:873 `DSPBufferCount` getter는 원본이며 `RuntimeManager.cs:292`가 쓰므로 남깁니다.** 또는 2.02.33 원본 파일로 복원합니다. PreInit 삭제와 같은 커밋이어야 합니다. | S4 |
| `FMODStudioSettings.asset` | playInEditor `DSPBufferCount = 4` | S4 |
| `Scenes/MainScene.unity` | StudioListener 제거 | S4 |
| `Scenes/GameScene.unity` | 고아 Audio Source와 AudioListener 제거 | S4 |
| ChartEditorScene, APITestScene, `ChartEditor/**` | 변경 없음 | – |

---

## 10. GameManager/ChartManager 통합 지점

**원칙**
- 구조는 바꾸지 않습니다. 모든 편집에 `// [AUDIO-IP:G#]`, `// [AUDIO-IP:C#]` 표식을 붙입니다.
- 새 ChartManager public API는 없습니다(`SyncTime` 인자 확장과 `TryJudgeRelease` 선택 인자 하나).
- I1(GameManager)과 I2(ChartManager + G13·G14)는 별도 PR입니다. 각 단계에서 판정 싱크 적용 지점은 하나입니다(I1은 ChartManager의 기존 뺄셈, I2는 `JudgementTimeline`).
- 판정 규칙은 I2에서 바꾸지 않습니다. 규칙 개선은 선택 단계 I3입니다.

### 10-1. GameManager.cs

| IP | 단계 | 위치 | 변경 | 계약 |
|---|---|---|---|---|
| G1 | I1 | `:39`, `:58`, `:61`, `:83-84` | `IAudioManager _audioManager`를 `ISongPlayer _songs; ISongSession _session; GameplayTimingBinding _timing;`으로 바꿉니다. globalStartTime과 `_pauseDspTime`은 삭제합니다. `TryGet<ISongPlayer>`로 조회합니다. | 서비스 조회 |
| G2 | I1 | `:100-101`, `:121-122` | 레인 구독 네 줄을 삭제합니다. OnDestroy에서 `SwitchToUI()` 앞에 `_timing?.Dispose(); _timing = null;`을 둡니다. | binding 수명 |
| G3 | I1 | `:131`, `:137` 앞, `:140`, `:144` | null 검사 대상을 `_songs`로 바꿉니다. Init 앞에 `_session = _songs.Current; _timing?.Dispose(); _timing = GameplayTimingBinding.Attach(_session, HandleJudgementAdvance, HandleTimedLaneInput, () => IsGameRunning, HandleSessionEvent, JudgementPumpMode.FrameBatched);`를 추가합니다(**isRunning은 IsGameRunning만**, 2-5). `:140`과 `:144`는 삭제합니다. | `AttachJudgement` |
| G4 | I1 | `:154-164` | 본문을 `_session?.Start(new SongStartOptions(delayTime));`로 바꿉니다(IGameManager 호환, I2 이후 호출자 없음). | `Start` |
| G5 | I1 | `:166-173` | `IsGameRunning && _session != null ? _session.Clock.Frame.SongTime : 0` | `Frame` |
| G6 | I1 | `:180` | `SyncTime` 줄을 삭제합니다. | `JudgementAdvance` |
| G7 | I1 | `:193-195` | `_session?.Pause(SongPauseReason.User);`. `:196 SwitchToUI()`보다 **앞에** 둡니다. | `Pause` |
| G8 | I1 | `:225-228` | `_session?.Resume();` | `Resume` |
| G9 | I1 | `:243-254` | binding 콜백 두 개로 교체(아래) | 핸들러 |
| G10 | I1 | `:62` | `=> _session != null && !_session.IsAudioFinished;` | `IsAudioFinished` |
| G11 | I1 | `:346` | `_session?.Stop();` | `Stop` |
| G12 | I1 | 신규 | `HandleSessionEvent`(아래) | `Changed` |
| G13 | I2 | `HandleJudgementAdvance` | 인자 3개짜리 SyncTime | `JudgementAdvance` |
| G14 | I2 | `HandleTimedLaneInput`, G3 모드 | JudgeTime과 `applyJudgement: e.Judgeable`, 모드를 `Interleaved`로 | `LaneInputEvent` |

`OnApplicationFocus`(`:183-187`)는 그대로 둡니다(멱등).

```csharp
// ── I1 (FrameBatched, 곡 시간 모드: ChartManager가 아직 자기 판정 오프셋을 뺀다) ──
// [AUDIO-IP:G9]
private void HandleJudgementAdvance(in JudgementAdvance a) => chartManager.SyncTime(a.ToSongTime);
private void HandleTimedLaneInput(in LaneInputEvent e)
{
    if (!IsGameRunning) return;                                     // 현행 HandleLane* 게이트와 동일
    if (e.IsDown) chartManager.TryJudgeInput(e.Lane, e.SongTime);
    else          chartManager.TryJudgeRelease(e.Lane, e.SongTime); // I1: 가짜 release도 현행처럼 판정(C6에서 해소)
}
// [AUDIO-IP:G12]
private void HandleSessionEvent(SongSessionEvent ev)
{
    if (ev.Kind == SongSessionEventKind.Paused && ev.Reason != SongPauseReason.User && IsGameRunning && !IsPaused)
        Pause();
}

// ── I2 (Interleaved, 판정 시간 모드) — C4·C6와 같은 PR에서만 컴파일된다 ──
// [AUDIO-IP:G13]
private void HandleJudgementAdvance(in JudgementAdvance a) => chartManager.SyncTime(a.ToSongTime, a.FromJudgeTime, a.ToJudgeTime);
// [AUDIO-IP:G14]
private void HandleTimedLaneInput(in LaneInputEvent e)
{
    if (!IsGameRunning) return;
    if (e.IsDown) chartManager.TryJudgeInput(e.Lane, e.JudgeTime);                       // 판정 불가 press는 binding이 이미 버림
    else          chartManager.TryJudgeRelease(e.Lane, e.JudgeTime, applyJudgement: e.Judgeable);
}
```

`using` 추가(`SCOdyssey.App.Interfaces`, `SCOdyssey.Timing.Judgement`, `SCOdyssey.Timing.LaneInput`, `SCOdyssey.Game.Integration`)도 통합 지점에 포함합니다.

### 10-2. ChartManager.cs (I2, 현재 줄 번호)

| IP | 위치 | 변경 | 계약 |
|---|---|---|---|
| C1 | `:106`, `:148-151`, `:709`, `:743`, `:818` | `_judgmentOffsetSec` 필드와 설정 읽기, 세 곳의 `- _judgmentOffsetSec`을 삭제합니다(`:789`는 C5). | 판정 싱크 단일 적용 |
| C2 | `:121-122`, `:155-159` | `IOneShotPlayer _oneShots; readonly OneShotId[] _hitSounds; ISongSession _session; IJudgementTimingLog _timingLog;`. Init에서 TryGet으로 등록하고, `_session = TryGet<ISongPlayer>(out var s) ? s.Current : null;`, `TryGet(out _timingLog)` | 원샷, 세션 |
| C3 | `:183` | `_session?.Start(new SongStartOptions(barDuration));` (GameManager 역호출 제거) | `Start` |
| C4 | `:190-209` | 시그니처 `SyncTime(double songTime, double judgeFrom, double judgeTo)`. `:192`는 `currentTime = songTime`. **`:201-205` 루프의 규칙과 순서는 그대로 두고 인자만 바꿉니다**(아래). | `JudgementAdvance` |
| C5 | `:777`, `:789` | `CheckHoldingBody(int listIndex, double judgeTime)`. `:789`를 `double timeDiff = Math.Abs(judgeTime - targetNote.noteData.time);`로 바꿉니다. `< JUDGE_PERFECT` 점 샘플 규칙은 유지하고 `gameManager.GetCurrentTime()` 의존을 없앱니다. | 판정 시각 |
| C6 | `:804`, `:810` 뒤 | `public void TryJudgeRelease(int laneIndex, double inputGameTime, bool applyJudgement = true)`. `:810 OnHoldRelease` 뒤에 `if (!applyJudgement) return;`. 매개변수 이름은 `:826`의 지역 변수 `judge`와 겹치지 않게 합니다(CS0136 회피). | `Judgeable` |
| C7 | `:230` | `if (_session != null && !_session.IsAudioFinished) return;` | `IsAudioFinished` |
| C8 | `:750-754` | `_oneShots?.Play(_hitSounds[(int)type]);` | `IOneShotPlayer` |
| C9 | `:717`, `:794` 앞, `:832` 앞, `:893` 뒤 | 아래 네 줄. 필수입니다. | 오차 훅 |

```csharp
// [AUDIO-IP:C4] songTime = 마디 전환·카운트다운(판정 싱크 미적용), judgeTo = miss·홀드 판정 시각(적용 완료)
// judgeFrom은 I2에서 쓰지 않는다 — I3(구간 교차 홀드)가 GameManager를 다시 건드리지 않도록 시그니처만 먼저 고정
public void SyncTime(double songTime, double judgeFrom, double judgeTo)
{
    this.currentTime = songTime;
    if (remainingChart.Count >= 0 && currentTime >= currentBarEndTime) { StartCurrentBar(); CheckGameClear(); }
    for (int i = 0; i < LANE_COUNT; i++)
    {
        CheckMissedNotes(i, judgeTo);                              // 규칙·순서 현행 유지, 비교 시각만 판정 시각
        if (_lanes[i].isHolding) CheckHoldingBody(i, judgeTo);
    }
    UpdateCountdowns();
}

// [AUDIO-IP:C9] 네 지점 (TimingKind는 SCOdyssey.Timing.Judgement, 등급은 byte로 명시 캐스트)
// :717  (TryJudgeInput)
var grade = GetJudgeType(timeDiff);
_timingLog?.Record(laneIndex, TimingKind.Press, (byte)grade, targetNote.noteData.time, inputGameTime);
ApplyJudgment(targetNote, listIndex, grade);
// :794 앞 (CheckHoldingBody)
_timingLog?.Record(listIndex + 1, TimingKind.HoldBody, (byte)JudgeType.Perfect, targetNote.noteData.time, judgeTime);
// :832 앞 (TryJudgeRelease, 지역 변수 judge = GetJudgeType(timeDiff))
_timingLog?.Record(laneIndex, TimingKind.Release, (byte)judge, targetNote.noteData.time, inputGameTime);
// :893 뒤 (CheckMissedNotes, 매개변수 currentTime = I2에서 judgeTo)
_timingLog?.Record(listIndex + 1, TimingKind.Miss, (byte)JudgeType.Umm, targetNote.noteData.time, currentTime);
```

`using SCOdyssey.App.Interfaces; using SCOdyssey.Timing.Judgement;`를 추가합니다. 노트 선택, 등급 창, 홀드 규칙, `FlushBufferedInput`, `PlayInputSound`, `GetJudgeType`은 바꾸지 않습니다. **I2 PR은 C4·C6와 G13·G14를 함께 넣어야 컴파일됩니다.**

### 10-3. 선택 단계 I3 (ChartManager 전용, GameManager 무편집)

| IP | 변경 |
|---|---|
| C10 | `CheckHoldingBody(listIndex, judgeFrom, judgeTo)`: `TimingWindow.Overlaps(judgeFrom, judgeTo, note, JUDGE_PERFECT)`로 바꾸고, 오차 기록 시각을 `ClosestPoint`로 둡니다(J4). |
| C11 | 레인 루프를 "홀드 먼저 → miss, 한 Advance 안에서 여러 노트가 지나가면 반복(가드 64)"으로 바꿉니다. |

리팩터된 클래스가 먼저 들어오면 R2 안내에 따라 거기에 적용합니다. I3을 하지 않아도 결정 6(오프셋 한 번 적용)은 I2로 충족됩니다.

### 10-4. 리팩터 후 재적용 규칙

| 규칙 | 책임 | 계약 | 원래 지점 |
|---|---|---|---|
| R1 | 곡 시작 | `Start(new SongStartOptions(leadIn))` 1회, 오프셋을 읽지 않음 | C3, G4 |
| R2 | 시간 진행 | `ToSongTime`으로 마디, `ToJudgeTime`으로 miss와 홀드(현행 규칙). 개선하려면 `Overlaps`와 다중 miss 루프(I3) | C4, C5, G13, (C10, C11) |
| R3 | 입력 판정 | `e.JudgeTime`만 비교에 쓰고, `Judgeable=false` release는 상태만 해제 | C6, G14, C1 |
| R4 | 오케스트레이션 | 차트 초기화 전에 `Attach(..., isRunning: 게임 진행 중, Interleaved)`, 끝나면 Dispose. 일시정지로 거르지 않음 | G2, G3 |
| R5 | 일시정지 UI | `Pause(User)`를 SwitchToUI 전에, 카운트다운 뒤 `Resume()`, 사용자 외 사유의 Paused를 받으면 Pause UI | G7, G8, G12 |
| R6 | 비주얼 | `RenderTime` / `SongTime` | G5, Timeline |
| R7 | 종료 | 채보 소진 && `IsAudioFinished`, 종료 시 `Stop()`. 음원이 끝난 뒤에도 일시정지 가능 | C7, G10, G11 |
| R8 | 타격음 | 로드 때 `Register`, 입력 때 `Play` | C2, C8 |
| R9 | 판정 결과 | 모든 확정 지점에서 `Record`(Miss 포함) | C9 |
| R10 | 금지 | 오프셋 직접 읽기, FMOD·DSP, `Time.*` 곡 시각 | 전체 |
| **R11** | **C 단계에서 삭제되는 심볼** | 리팩터 코드는 다음을 쓰면 안 됩니다: `IAudioManager`, `AudioOutputType`, `AudioOutputConfig`, `AudioBus`, `IInputManager.OnLanePressed/OnLaneReleased/SetTimeSyncPoint`, `BGAController.SchedulePlay/Pause/Resume`. C 게이트는 이 심볼들이 GameManager, ChartManager와 그 후속 클래스에서 grep 0건이어야 통과합니다(12-2 C). | 전체 |

---

## 11. 파일 배치

### 11-1. 신규 파일

| 경로 | 네임스페이스 | 비고 |
|---|---|---|
| `Assets/Scripts/Timing/{Qpc, SongTimingOffsets}.cs` | SCOdyssey.Timing | 순수 |
| `Assets/Scripts/Timing/Clock/{ClockSample, DspQpcModel, SongTimeline, SongAnchorMath, RenderClock, SongClock, ISongClock, SongFrame, ClockDiscontinuity, SongClockDiagnostics}.cs` | SCOdyssey.Timing.Clock | 순수 |
| `Assets/Scripts/Timing/LaneInput/{RawLaneInput, LaneInputEvent, IInputTimestampSource, LaneInputHub, RealtimeQpcMapper, SpscRing}.cs` | SCOdyssey.Timing.LaneInput | 순수 |
| `Assets/Scripts/Timing/Judgement/{JudgementTimeline, JudgementAdvance, IJudgementClient, JudgementPump, TimingWindow, TimingSample, TimingLog}.cs` | SCOdyssey.Timing.Judgement | 순수 |
| `Assets/Scripts/Timing/Sources/UnityInputSystemTimestampSource.cs` | SCOdyssey.Timing.Sources | 유일한 Unity 의존 |
| `Assets/Scripts/App/Interfaces/{ISongPlayer, IOneShotPlayer, IAudioMixer, IMusicPlayers, IAudioEngine, IAudioOutputService, IJudgementTimingLog, ILaneInputStream}.cs` | SCOdyssey.App.Interfaces | 계약 |
| `Assets/Scripts/Audio/Engine/{AudioEngine, AudioBootConfig, EngineConfigurator, OutputFallbackPlanner, SafeModeFlag, SystemCallbackHub, FmodDebugBridge, FmodCheck, AudioLog, ComApartmentProbe, MainThreadGuard, IFmodEngineContext, IEngineResource, OutputWatchdog}.cs` | SCOdyssey.Audio.Engine | |
| `Assets/Scripts/Audio/Output/{DeviceCatalog, OutputChangePlanner, BufferPresets, AudioOutputService}.cs` | SCOdyssey.Audio.Output | |
| `Assets/Scripts/Audio/Mixing/{FmodMixer, FmodMixBus, ClockDomainProbe}.cs` | SCOdyssey.Audio.Mixing | |
| `Assets/Scripts/Audio/Playback/{AudioPaths, StreamLoader, OneShotBank, FmodMusicPlayer, MusicPlayers, SongPlayer, FmodSongSession, MasterClockSampler, IClockSampleSource, JudgementFeed, JudgementTimingLogService}.cs` | SCOdyssey.Audio.Playback | |
| `Assets/Scripts/Audio/Hosting/{AudioModuleInstaller, AudioModule, AudioModuleOptions, AudioEngineRunner, FocusPolicy, RuntimeManagerGuard, EditorAudioLifecycle}.cs` | SCOdyssey.Audio.Hosting | Lifecycle은 `#if UNITY_EDITOR` |
| `Assets/Scripts/Audio/Compat/LegacyAudioManagerAdapter.cs` | SCOdyssey.Audio.Compat | S4 추가, C 삭제 |
| `Assets/Scripts/Audio/Diagnostics/AudioDiagnosticsOverlay.cs` | SCOdyssey.Audio.Diagnostics | 에디터·개발 빌드 |
| `Assets/Scripts/App/{AudioSettingsMapper, SettingsMigration}.cs` | SCOdyssey.App | |
| `Assets/Scripts/Game/Integration/GameplayTimingBinding.cs` | SCOdyssey.Game.Integration | |
| `Assets/Scripts/Testing/Audio/{AudioSpikeHarness, ClickTrackWriter, LoopbackAnalyzer, SpikeCsvWriter, AllocationProbe}.cs` | SCOdyssey.Testing.Audio | 동작은 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| `Assets/Scripts/Testing/Audio/SelfTests/{ClockSelfTests, JudgementSelfTests, LaneInputSelfTests, OutputPlannerSelfTests, SettingsMigrationSelfTests, BindingSelfTests}.cs` | **SCOdyssey.Testing.Audio.SelfTests** | 파일 전체 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| `Assets/Scripts/Testing/Audio/Editor/{AudioSelfTestsNUnit, AudioSelfTestMenu, ModuleDependencyCheck}.cs` | SCOdyssey.Testing.Audio | 유일한 예외입니다. `Editor` 특수 폴더 세그먼트는 네임스페이스에서 빼서 `UnityEditor.Editor`를 가리지 않게 합니다. |
| `Assets/Scenes/AudioSpikeScene.unity` | – | EditorBuildSettings에 넣지 않습니다. |
| `Assets/Settings/Build Profiles/AudioSpike-Dev.asset` | – | Unity 6 Build Profile입니다. Windows x64, Development Build, 씬 목록 재정의 = AudioSpikeScene만. 전역 씬 목록은 건드리지 않습니다. 6000.3 UI에서의 옵션 이름은 미확인입니다. |

네임스페이스 세그먼트에 `Input`, `Editor`, `Unity`는 쓰지 않습니다(`UnityEngine.Input`, `UnityEditor.Editor`, `Unity.*`를 가리기 때문). 위 예외만 문서화합니다.

### 11-2. 삭제, 벤더 패치 되돌림, 씬과 에셋

- 삭제: S4에 `FMODAudioPreInit.cs`, S5에 `FMODAudioManager.cs`와 define 분기, C에 `IAudioManager.cs`, Compat 어댑터, 입력·Timeline·BGA 레거시 멤버(R11 게이트)
- 벤더 되돌림(S4): `Platform.cs`의 **:871-872, :874-878**. :873은 남깁니다. 나머지 FMOD 파일은 원본 그대로이고, 패키지(DLL, 래퍼, 빌드 처리)는 유지합니다.
- 씬(S4): MainScene의 StudioListener, GameScene의 Audio Source와 AudioListener를 제거합니다. ChartEditorScene과 APITestScene은 건드리지 않습니다.
- 에셋(S4): playInEditor `DSPBufferCount = 4`. 빈 Master 뱅크는 유지합니다.
- ChartEditor 영향: PreInit이 PlayerPrefs 버퍼를 에디터에도 주입하던 경로가 없어지고, 항상 256×4가 됩니다(기본 사용자와 같은 값).

---

## 12. 단계별 구현 계획

- 모든 단계가 끝난 뒤에도 게임을 플레이할 수 있어야 합니다.
- S0~S3은 모듈 구축 단계입니다. 게임 파일을 건드리지 않고, 에디터의 AudioSpikeScene과 `AudioSpike-Dev` 플레이어 빌드로 검증합니다.
- S4~S7은 GM/CM 외 통합, I1·I2는 GM/CM 통합(분리 PR), I3은 선택, C는 정리입니다.

### 12-1. 게이트 스파이크와 통과 기준

| 스파이크 | 게이트 | 통과 기준(제안) |
|---|---|---|
| SP-T 테스트 발견 | S0 | Test Runner에 Assembly-CSharp-Editor의 `[Test]`가 보이는지 확인합니다(실패해도 메뉴 러너로 진행). |
| **SP1** 수명주기 | S1 → **S4 필수** | 에디터: WASAPI와 ASIO 각각 Play/Exit 20회, 재컴파일 5회. **플레이어(`AudioSpike-Dev`)**: 실행과 종료 5회. 매번 init OK, `ERR_OUTPUT_ALLOCATED` 0회, 리로드 후 Debug 콜백 크래시 0회, `RuntimeManager.IsInitialized == false`, 생성·해제 20회 뒤 9번째 `System_Create` 성공, 메모리 추세 안정, NOSOUND에서 기준 클록 진행 |
| **SP2** STA/ASIO | S1 → S5 ASIO 노출 | **에디터와 플레이어(`AudioSpike-Dev`)**에서 아파트먼트를 기록합니다. ASIO init, 열거, close→init 10회. 실패하면 `AsioEnabled = false`와 R2 |
| SP3 클록·버퍼 | S1 → 모델 파라미터, S5 프리셋 | 256/480/512/1024 ×4와 ASIO에서 각 5분. 잔차 p99 ≤ 1블록, 60/144fps 절편 차 ≤ 0.5ms, drift 확보, 기본 버퍼 클록 스텝 0 |
| SP15 관리 콜백 | S1 → 마스크와 Debug 모드 | 50MB/s GC에서 콜백 켬/끔의 클록 스텝 차 0, 콜백 스레드 ID 기록 |
| SP4 도메인·재예약·seek | S2 → **I1 필수** | 괄호 오프셋 일정. 곡 클릭과 메트로놈 onset 차 평균 ≤ 1ms, σ ≤ 0.5ms(일시정지·재개 20회 뒤, 리드인 포함). seek 준비 < 1초. ACCURATETIME 로드 시간 기록 |
| SP5 setDriver | S2 → S5 | 채널 생존, 클록 단조, 공백 ms. **WASAPI Pinned↔Pinned, Follow↔Pinned(마스크 교체 포함)**. 실패하면 `EscalateAllDeviceChanges` |
| SP6 재초기화 | S2 → S5 전략 | close→init 50회, 크래시 0, 1회 < 1초, 원샷·음악·**레거시 어댑터 슬롯** 복원. **음악을 재생하는 중에 ApplyAsync ×50**으로 스냅샷·복원 검증 |
| SP8 ASIO 매트릭스 | S2 → S5 ASIO UI | ASIO4ALL, Realtek, 팀 장비로 블록 불일치, 44.1k/48k, 점유 앱, **드라이버 A→B setDriver**, 포커스 복귀. **플레이어 빌드 검증**. 실패는 RESULT로 드러나고 들리는 WASAPI로 끝나야 함. setDriver가 실패하면 `EscalateAsioDriverChange` |
| SP9 교차 열거 | S2 → S5 | 메인 출력 글리치 없음, 실패 격리, 열거 1회 소요 시간(블록 프레임) 기록 |
| SP10 핫플러그 | S2(하네스), I1(게임) | 두 모드에서 분리·재연결 뒤 개입 없이 복귀, 게임 중이면 자동 일시정지 → 복구 → 재개. **Pinned→Follow 마스크 교체 뒤 기본 장치 자동 전환이 재개되는지**. 실패하면 `EscalateModeSwitch` |
| SP11 입력 도메인 | S3 → I2 | 60/144/무제한 fps의 `ctx.time − 프레임 시각` 분포, 매퍼 오프셋 30분 변동 < 0.1ms |
| SP-IN Synthetic | S4 | (a) `Game.Disable()`의 canceled가 동기로 오고 Synthetic으로 태깅되며 Frozen 구간에 속함. (b) **포커스 손실: runInBackground ON/OFF 각각에서** 홀드 중 alt-tab 시 리셋 canceled가 Synthetic이거나 Frozen 구간에 속하고, 오판정 0 |
| SP13 종단 지연 | 정보용 | 버퍼별, 출력별 수치(자동 보정 없음) |
| SP14 포커스 | S5(로비), I1(게임) | **Exclusive 전체 화면(`AudioSpike-Dev`와 게임 빌드)**, 테두리 없는 창, 창 모드에서 alt-tab. 게임 자동 일시정지, 로비 음소거 또는 유지, 복귀 뒤 `MarkSuspect` 경로로 **2초 안에 Locked**(소프트 에폭이 나면 그 뒤 2초), 복귀 전후 판정 편향 변화 < 1ms, ASIO 재획득 |

SP7은 폐기하고, SP12는 다음 단계 작업입니다.

### 12-2. 단계

| 단계 | 종류 | 내용 | 게이트 | 확인 | 롤백 |
|---|---|---|---|---|---|
| **S0** | 모듈 | Timing 순수 라이브러리, SelfTests, NUnit 래퍼, 메뉴 러너, `ModuleDependencyCheck`(허용 목록 포함, 13-3) | SP-T | 메뉴 러너 녹색, **허용 목록 적용 상태에서** 의존성 검사 녹색 | 파일 삭제 |
| **S1** | 모듈 | Engine, Mixer, OneShotBank, 샘플러와 모델, Hosting(`InstallStandalone`), 오버레이, 하네스, **`AudioSpike-Dev` 빌드 프로필** | SP1, SP2, SP3, SP15 | 에디터 하네스와 **플레이어 빌드**에서 init, 원샷, 반복, 재컴파일, 아파트먼트, drift | 파일 삭제 |
| **S2** | 모듈 | StreamLoader, MusicPlayers, 세션(상태별 재초기화 포함), ScheduleOneShot, DeviceCatalog(비동기), Planner(격상 플래그), AudioOutputService(롤백 포함), 세이프 모드, 콜백 정책(마스크 교체), 워치독, 클릭 트랙, 루프백 | SP4, 5, 6, 8, 9, 10, 13 | 메트로놈 정렬, 일시정지·재개 20회, 종료 뒤 일시정지, Ready·Starting 중 강제 재초기화(3초 안에 커밋 또는 무음 전환), USB 분리, **음악 재생 중 ApplyAsync ×50(스냅샷·복원)**, 롤백 강제(가짜 GUID) | 파일 삭제 |
| **S3** | 모듈 | 입력 소스(포커스 태깅 포함), 허브, 매퍼, Pump, 타이밍 로그, 탭 테스트 | SP11 | 탭 오차 분포, 정렬 로그, 매퍼 추세 | 파일 삭제 |
| **S4** | 통합(엔진 교체) | Managers → Install + `RegisterInto`, 어댑터(3-9), `AudioSettingsMapper` v1(버퍼 인덱스 매핑), InputManager Push와 Synthetic, PreInit 삭제 + Platform.cs(:871-872, :874-878), playInEditor count 4, 씬 정리, 가드, `SCO_LEGACY_FMOD` A/B. **GM/CM 편집 없음** | SP1(실경로), SP-IN | 로비 BGM, 프리뷰, 곡, 타격음, ESC, 볼륨, 장치 선택이 이전과 같은지. 로그의 실제 버퍼가 저장 인덱스와 일치하는지(0~4 각각). `IsInitialized = false`. **ChartEditorScene 동등**, Play/Exit 10회. **릴리스(비 L) 스모크 빌드**: 부팅, 로비, 한 곡, 타격음, 종료 ×3, `Debug.Initialize` ERR_UNSUPPORTED 무시, ERROR 링 동작 | define A/B 또는 revert |
| **S5** | 통합(설정·출력) | SettingsData v2, 마이그레이션, Load/Apply, 매퍼 v2, SoundSettingUI와 프리팹(비동기 목록, 상태 줄, 롤백 문구), ApplyAsync, define과 FMODAudioManager 삭제 | SP5/6/8/9 반영, SP14(로비) | v2 변환과 `.v1.bak`. **적용 유형마다(setDriver, MaskOnly, 버퍼 재초기화, WASAPI↔ASIO, 롤백) 설정을 닫은 뒤 로비 BGM이 다시 시작되고, 프리뷰, 한 곡 전체, 타격음이 정상인지**(어댑터 경유). 재시작 후 GUID와 타입 복원(H3). playInBackground ON/OFF alt-tab. **릴리스 스모크 빌드 반복** | revert(`.bak`) |
| **S6** | 통합(로비 음악) | MainUI, AdventureUI → IMusicPlayers(TryGet) | – | 프리뷰 하나만, 화면 전환, GameScene 진입 시 정지, 없는 파일은 로그만 | revert |
| **S7** | 통합(비 GM/CM) | Timeline, BGA 세션 인지형(레거시 폴백), CharacterAnimator 로그 삭제 | – | 동작 동일, 입력 로그 없음 | revert |
| **I1** | **GM 통합** | Binding, G1~G12, GameDataLoader. ChartManager 무편집 | SP4, SP10·SP14(게임) | 시작 싱크, 리드인 일시정지(H2), ESC 20회, **ESC와 같은 프레임의 입력 판정**, alt-tab(설정 ON/OFF), 카운트다운 중 alt-tab, USB 분리, R 재시작·리트라이·나가기(M6), **음원 종료 뒤 결과 전 ESC와 alt-tab**, **무음 곡 일시정지**, 종료 직전 ESC(2-5 경계), 음원 누락, 빈 경로, 노트 싱크 ±200, BGA | revert |
| **I2** | **CM 통합** | C1~C9 + G13·G14 **한 PR**(따로는 컴파일 불가). 판정 규칙 불변 | SP11 | 컴파일. 판정 싱크 ±20이 hit, release, hold, **miss**에 반영(M15). 홀드와 miss 동작이 I1과 같은지(규칙 불변). Middle 승격, 선입력 버퍼, 오차 히스토그램(Miss 포함 유한값), 분포 유지. 문제가 있으면 `FrameBatched` | 킬 스위치 또는 revert |
| I3(선택) | CM 규칙 개선 | C10·C11(ChartManager만) 또는 리팩터 클래스에 R2 적용 | – | 30fps와 100ms 히치에서 홀드 몸통이 같은지 | revert |
| **C** | 정리 | 어댑터, IAudioManager, 레거시 입력·Timeline·BGA, GameSceneTester, 규칙 4 오류 승격 | **R11 grep 0건**(GM/CM과 후속 클래스), 의존성 검사 | 전체 회귀, ChartEditor, 개발·릴리스 빌드(Addressables 포함) | revert |

C 게이트가 실패하면(병렬 리팩터가 옛 심볼을 쓰는 상태로 들어오는 경우) C를 둘로 나눕니다. **C-a**는 GM/CM과 무관한 정리를 하고, **C-b**는 심볼을 삭제합니다. C-b는 리팩터가 R11을 반영한 뒤에 합니다. 그 전까지는 삭제 대상 심볼을 `[Obsolete]` no-op shim으로 한 주기 유지합니다.

---

## 13. 검증 계획

### 13-1. 스파이크 하네스 (`AudioSpikeScene`, GameManager·ChartManager 없음)

- `InstallStandalone`로 모듈만 띄웁니다. IMGUI 버튼으로 조작하고 `persistentDataPath/audio_spike_*.csv`에 기록합니다. 에디터와 `AudioSpike-Dev` 플레이어 빌드에서 같은 씬으로 실행합니다.

| 영역 | 기능 | 스파이크 |
|---|---|---|
| 수명주기 | 타입, GUID, 버퍼를 골라 init. Shutdown/Init N회, 가짜 GUID 폴백, 누수 검사, 아파트먼트와 버전 | SP1, SP2 |
| 출력 | 비동기 열거(소요 시간), setDriver와 마스크 교체 순환, 음악 재생 중 ApplyAsync ×50, 롤백 강제, 핫플러그 로그 | SP5, 6, 8, 9, 10 |
| 클록 | 5분 CSV, GC 스트레스, 콜백 스레드 ID, MarkSuspect 재락 시간 | SP3, SP15, SP14 |
| 세션 | 클릭 WAV, 리드인·곡 중·**종료 뒤** 일시정지·재개 N회, 메트로놈, 종료 감지, 상태별 강제 재초기화 | SP4 |
| 루프백(선택) | `recordStart`와 onset 분석 | SP4, SP13 |
| 입력 | 탭 테스트, `ctx.time` 분포, 매퍼 추세, 포커스 태깅 | SP11, SP-IN |
| 할당 | `AllocationProbe` 결과 표시(13-3) | – |

- 진단 오버레이: 엔진 상태와 실제 출력, Generation, ClockEpoch, Quality, drift, 잔차, Render−Song, 스텝 수, 매퍼 오프셋, 입력 배치, 판정 오차 히스토그램, `IsInitialized`

### 13-2. 수동 에디터 시나리오 (I2 이후 전체 회귀)

1. 첫 실행과 v1 설정으로 부팅
2. 로비 → 프리뷰 빠른 전환, 설정 열고 닫기
3. 설정 적용(setDriver, Follow↔Pinned, 버퍼, ASIO, 롤백 문구): **설정을 닫은 뒤 로비 BGM이 다시 시작되는지** 확인합니다(설정 화면에서는 MainUI가 숨겨져 BGM이 꺼져 있음). 재부팅 뒤 복원, ASIO 점유 앱이 있는 상태로 부팅(세이프 모드)
4. 전곡 → 결과 → 로비
5. 리드인 중, 곡 중간, 종료 직전 ESC. 카운트다운 중 alt-tab
6. ESC 10회 뒤 판정 편향 변화 < 2ms
7. 게임 중 alt-tab(ON/OFF, 전체 화면·창) → 재개 뒤 판정 정상(H1). **홀드 중 alt-tab에서 오판정이 없는지**(SP-IN b)
8. USB 분리·재연결 → 자동 일시정지 → 복구 → 재개
9. R 재시작, 리트라이, 나가기 때 즉시 정지
10. 노트 싱크 ±200
11. 판정 싱크 ±20(+60ms에서도 늦은 쪽 창 126ms, 홀드도 이동)
12. 30/60/144/무제한 fps 홀드(I3을 했을 때는 히치 키까지)
13. 상하 동시 입력 Middle 승격
14. 음원 오타 → 로비, 빈 경로 → 무음 세션으로 종료
15. ChartEditorScene 단독 Play(로드, 재생·일시정지, 자동 채보), Play/Exit 20회
16. Play/Exit 반복, 재컴파일, 에디터 일시정지 버튼
17. **음원이 끝난 뒤 결과 화면 전 구간**(채보가 음원보다 긴 곡이나 마지막 마디)에서 ESC와 alt-tab: 차트와 판정선이 멈추고 재개 뒤 홀드, miss, 마디가 점프하지 않음
18. **무음 곡(빈 audioFilePath) 도중 ESC, alt-tab, 재개**
19. **로딩 직후(Ready)나 시작 직후(Starting)에 USB 분리**: 3초 안에 곡이 시작되거나 무음으로 진행(멈춤 없음)

### 13-3. 자동 테스트와 컴파일 경로 (asmdef 없음)

- **본문**: `Testing/Audio/SelfTests/*.cs`(Assembly-CSharp, `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, `SCOdyssey.Testing.Audio.SelfTests`). public static 메서드와 자체 assert를 쓰고 NUnit에 의존하지 않습니다.
- **NUnit 래퍼**: `Testing/Audio/Editor/AudioSelfTestsNUnit.cs`는 Assembly-CSharp-Editor로 컴파일되어 Assembly-CSharp를 자동 참조합니다. nunit은 `isExplicitlyReferenced: 0`, `defineConstraints: []`라 자동 참조되므로 `[Test] public void X() => ClockSelfTests.X();`가 컴파일됩니다. TestRunner 어셈블리는 `autoReferenced: false`라서 `[UnityTest]`는 쓰지 않습니다. 발견되지 않으면 `AudioSelfTestMenu`와 하네스 버튼으로 실행합니다.
- **할당 0 검증**(`AllocationProbe`): Unity Mono(Boehm)에서는 `GC.GetAllocatedBytesForCurrentThread`가 0을 돌려주는 스텁일 가능성이 높습니다.
  1. 1순위: `UnityEngine.Profiling.Recorder.Get("GC.Alloc")`에 `FilterToCurrentThread()`를 걸고, 대상 코드 앞뒤로 `enabled`를 켜고 끈 뒤 `sampleBlockCount == 0`인지 봅니다. 에디터와 개발 빌드에서 동작하며, Unity 테스트 프레임워크의 할당 제약과 같은 원리입니다.
  2. 2순위: `GC.GetAllocatedBytesForCurrentThread` 전후 차이
  3. 각 방법마다 **정상 동작 확인**을 먼저 합니다. `new object[16]`에서 0이 아닌 값이 나와야 그 방법을 씁니다. 둘 다 정상 동작하지 않으면 결과는 "측정 불가(Inconclusive)"이며 통과로 보지 않습니다.
  4. 대상: `LaneInputHub.Collect`, `JudgementPump.Run`(가짜 클라이언트), `DspQpcModel.Add`, 하네스의 `OneShotBank.Play`
- **케이스**

| 대상 | 검증 |
|---|---|
| `DspQpcModel` | 합성 premix(10ms, 버스트), 60/144fps와 지터, ±100ppm에서 30초 뒤 절편 ≤ 0.3ms, drift ≤ 10ppm. +30ms 정지 → 소프트 에폭(기울기 유지). **MarkSuspect 뒤 정상 데이터면 에폭 없이 8버킷 뒤 Locked, 30ms 점프면 첫 2버킷 안에 소프트 에폭 1회.** **하드 리셋은 Reset 호출에서만.** 이상치 기각 |
| `SongTimeline`, `SongAnchorMath` | `τ(Cf) ≈ τ0`, audioOffsetMs가 Z에 한 번 반영, Freeze/Resume, 과거 qpc 평가, 리드인 재개, **무음 커밋(음원 종료 뒤 재개)이 T_p에서 연속** |
| `RenderClock` | 단조, 스냅, 역행 유지, 수렴 |
| `JudgementTimeline` | 1단계 = 3ms, 래치, 단일 적용 |
| `JudgementPump` | Interleaved 순서, release 전 진행, Frozen 진행 없음, 판정 불가 통과, 안정 순서, FrameBatched, 역행 보류 |
| `TimingWindow`, `TimingSample` | 경계, ClosestPoint, IsPastLateEdge, **Miss 오차 유한값이고 ≥ UMM** |
| `LaneInputHub`, `RealtimeQpcMapper` | 병합 정렬, horizon, 에폭, Synthetic → 판정 불가, **할당 0(`AllocationProbe`)**, 중앙값 |
| `OutputFallbackPlanner`, `OutputChangePlanner`, `BufferPresets` | 부팅과 적용 체인(롤백 2단계, 세이프 모드 4단계 시작), 중복 제거. **ASIO A→B와 Follow↔Pinned → SetDriver, 같은 인덱스 → MaskOnly, 레이트 불일치 → FullReinit, 격상 플래그 3종**, SnapUp |
| `SettingsMigration`, `AudioSettingsMapper` | v1 0~4 → 256/256/256/512/1024, 장치 리셋, 손상 JSON. **S4 v1 부트 매핑 0~4 → 64/128/256/512/1024×4, 범위 밖 → 256** |
| `GameplayTimingBinding` | 판정 불가 press 버림, release 통과, isRunning=false면 버림, **isRunning=true면 일시정지와 무관하게 진행과 입력 전달**, Dispose 멱등 |
| 세션 상태(가짜 엔진 컨텍스트) | 음원 종료 뒤 Pause/Resume 동작, 무음 세션 Pause, Ready·Starting 재초기화 → 재오픈 또는 3초 무음 전환 |
| `ModuleDependencyCheck` | 1-3 규칙과 아래 허용 목록 |

- **`ModuleDependencyCheck` 허용 목록**(텍스트 스캔이라 `#if`를 평가하지 않으므로 파일 단위로 명시)

| 파일 | 면제 규칙 | 기한 |
|---|---|---|
| `App/FMODAudioPreInit.cs` | `FMOD.`, `FMODUnity`, `RuntimeManager` 토큰 | S4(삭제) |
| `App/FMODAudioManager.cs` | 같음 | S5(삭제) |
| `Audio/Compat/**` | `IAudioManager` 토큰 | C(삭제) |
| `App/GameManager.cs` | 규칙 4(audioOffsetMs 읽기) | I1(G4) |
| `App/ChartManager.cs` | 규칙 4(judgmentOffset 읽기) | I2(C1) |
| `App/AudioSettingsMapper.cs`, `App/SettingsMigration.cs` | 규칙 4 | 영구 |

기한이 지난 항목이 남아 있으면 검사가 "허용 목록 만료" 경고를 냅니다. C 이후에는 오류로 승격합니다. `UI/Settings/GameSettingUI.cs`는 규칙 4의 대상 폴더(App, Game) 밖이라 면제가 필요 없습니다.

---

## 14. 리스크와 대안

| # | 리스크 | 신호 | 대안 |
|---|---|---|---|
| R1 | SP1 실패(해제 누락, ASIO 점유) | 두 번째 Play `ERR_OUTPUT_ALLOCATED`, 9번째 Create 실패 | 정리 강화. `SessionState`에 IntPtr을 기록해 다음 Install에서 회수. 최후에는 (A) 재검토(계약 유지) |
| R2 | SP2 실패(STA) | init·열거 오류 | ASIO 전용 STA 스레드 마샬링 또는 `AsioEnabled = false` |
| R3 | 관리 콜백의 GC 언더런 | SP15 | UNDERRUN·MIDMIX 끔, Debug는 FILE, ERROR는 개발 빌드 전용 |
| R4 | 하한 포락선 정밀도 부족 | SP3 p99 > 1블록 | 파라미터 조정, `onBeforeRender` 샘플 추가, 폴러나 MIDMIX 샘플러를 `IClockSampleSource`로 |
| R5 | 스트림 seek·setDelay 부정확 | SP4 | 게임 곡만 CREATESAMPLE(조건부) |
| R6 | setDriver 불안정 | SP5, SP8 | 격상 플래그(전체, ASIO 드라이버) |
| R7 | close/init 불안정 | SP6 | `ReleaseAndCreate` |
| R8 | ASIO 드라이버 품질 | SP8, SP9 | 지원 목록 제한, 격리, 세이프 모드 |
| R9 | 입력 오프셋 변동, 프레임 양자화 | SP11 | 회귀 매퍼, 고 FPS 유지, 2단계 Raw Input 이음새 |
| R10 | 병렬 리팩터와 충돌 | 머지 충돌 | 표식, 델리게이트 binding, R1~R11, I1/I2 분리, I2 규칙 불변(재적용 면적 최소), IGameManager 불변 |
| R11 | Interleaved에서 SyncTime이 한 프레임에 여러 번 불림 | I2 회귀 | `FrameBatched` 킬 스위치 |
| R12 | runInBackground=false에서 백그라운드 Tick 정지 | – | 복귀 첫 프레임에 처리. 그동안은 음소거나 일시정지 |
| R13 | 에디터 ASIO 충돌(F17) | 에디터 | WASAPI 권장, 플레이어 빌드 검증 |
| R14 | 과도기 오프셋 이중 적용 | 3ms 단위 편향 | 단계별 단일 적용 지점, 규칙 4 |
| R15 | Pump가 게임플레이 예외를 전파 | 예외 | try/catch, 로그 제한 |
| R16 | ChartEditor 회귀 | 미리듣기 변화 | playInEditor count 4, Platform.cs :873 유지, 시그니처 불변 |
| R17 | 과도기 S4~I1의 레거시 의미 | – | 문서화. 곡 도중 복구 재초기화는 스냅샷 위치에서 이어짐(샘플 단위 아님). I1에서 해소. H1은 S4부터 사라짐 |
| R18 | 리팩터가 R11 심볼을 쓰는 상태로 들어옴 | C 게이트 grep | C-a/C-b 분할, `[Obsolete]` shim 한 주기 |
| R19 | 포커스 손실 리셋 canceled 오판정 | SP-IN(b) | `!Application.isFocused` 태깅. 그래도 새면 FocusPolicy 이전 정지를 위해 `Application.focusChanged` 구독을 Runner에서 앞당기는 방안 검토 |
| R20 | 일시정지 구간의 클리어 성립 | I1 #5 | 확률 약 프레임/마디. 발생하면 리팩터 쪽에서 종료 시 PauseUI 닫기(R5에 추가) |

---

## 검토 메모

- **#1 (CS0136)**: `ChartManager.cs:826`에서 확인했습니다. 매개변수 이름을 `applyJudgement`로 바꾸고 C9 캐스트를 명시했습니다.
- **#2 (어댑터)**: 사실입니다. 추가로 `GetDSPTime`이 세대를 넘어 단조 증가하도록 `genBase`를 두었습니다. 레거시 GameManager의 `globalStartTime` 산술이 S4~I1의 복구 재초기화 뒤에도 깨지지 않게 하기 위한 것이며, 심사 수정안에는 없던 보강입니다.
- **#3 (Ended)**: `GameManager.cs:171`과 `ChartManager.cs:195-198`로 확인했습니다. 두 대안 중 "Ended 상태 삭제, 플래그화"를 택했습니다. 상태가 하나 줄어 FocusPolicy, 복구 표, 리팩터 규칙이 모두 단순해지기 때문입니다.
- **#4**: Build Profile 에셋이 저장소에 없음을 확인했습니다. 6000.3 UI의 씬 목록 재정의 옵션 이름은 미확인입니다.
- **#5**: 허용 목록 방식을 채택했습니다. 등록 호출은 Hosting이 아닌 Managers에서 `LegacyAudioManagerAdapter.RegisterInto`로 합니다(Managers는 App이라 IAudioManager를 알아도 됨).
- **#6**: :873은 원본 getter이고 `RuntimeManager.cs:292`가 읽는 것을 확인했습니다.
- **#7**: 포커스 복귀와 에디터 일시정지 해제는 MarkSuspect로 통일했습니다. ASIO 복귀에서 클록이 멈춘 경우에는 Recovering → 재초기화 → 하드 리셋으로 가므로 모순이 없습니다.
- **#8**: 채택했습니다. 다만 **레이트가 다른 장치로의 변경은 FullReinit으로 남겼습니다.** 결정 4가 "샘플레이트는 전체 재초기화"로 정했기 때문입니다. `setCallback`을 init 후에 호출하는 것은 이미 3-2 [성공] 단계가 전제하고 있습니다. RuntimeManager는 init 전(:356)에만 호출하므로, init 후 마스크 교체의 효과는 SP5·SP10에서 확인합니다.
- **#9**: 두 대안 중 "IsGameRunning만으로 게이트"를 택했습니다. "`from` 유지" 대안은 구간 안의 입력이 miss 진행보다 먼저 전달되어 Interleaved 순서(J2)를 깨기 때문입니다. 남는 경계 사례(일시정지 구간 안의 클리어)는 R20으로 기록했습니다.
- **#10**: `AdventureUI.cs:74`와 `ServiceLocator.cs:56`으로 확인했습니다. 등록은 제거하지 않고, 폐기된 객체는 no-op로 두었습니다.
- **#11**: PreInit의 매핑과 범위 밖 처리(:13, :32-33)를 그대로 옮겼습니다.
- **#12**: Starting 시간 초과는 무음 전환으로 했습니다. 로비 복귀보다 사용자 흐름을 덜 깨기 때문입니다.
- **#13, #14, #15, #19, #21**: 그대로 채택했습니다. #21의 Boehm 스텁 여부는 미확인이지만, 정상 동작 확인 단계가 있어 어느 쪽이든 안전합니다.
- **#16**: 패키지 `InputManager.OnFocusChanged`로 확인했습니다. 에디터에서는 runInBackground가 사실상 참이라 현재도 발생할 수 있는 경로입니다. `backgroundBehavior`는 백그라운드에서 뗀 키가 눌린 채 남을 위험 때문에 바꾸지 않고, 포커스 태깅을 택했습니다.
- **#17**: 채택했습니다. `SyncTime`은 인자 3개를 유지했습니다(I2에서는 judgeFrom 미사용). 선택 단계 I3이 GameManager(G13)를 다시 편집하지 않게 하기 위함입니다.
- **#18**: R11과 C 게이트를 추가했습니다. `GameManager.StartMusic`은 IGameManager 멤버라 삭제 대상이 아닙니다.
- **#20**: `MainUI.cs:112`(Push)와 `UIManager.cs:106`, 설정이 MainUI에서만 열린다는 것을 확인했습니다. 따라서 ApplyAsync가 게임 중에 일어나지 않는다는 전제도 코드로 뒷받침됩니다.

### Critical Files for Implementation
- D:\Dev\Unity\sangcheol-odyssey-unity\Assets\Scripts\App\Managers.cs
- D:\Dev\Unity\sangcheol-odyssey-unity\Assets\Scripts\App\GameManager.cs
- D:\Dev\Unity\sangcheol-odyssey-unity\Assets\Scripts\App\ChartManager.cs
- D:\Dev\Unity\sangcheol-odyssey-unity\Assets\Scripts\App\InputManager.cs
- D:\Dev\Unity\sangcheol-odyssey-unity\Assets\Scripts\UI\Settings\SoundSettingUI.cs
