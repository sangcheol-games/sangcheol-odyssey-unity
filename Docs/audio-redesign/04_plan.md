> **문서 안내**: 승인된 재설계 계획 전문(2026-09-26 승인). 내용은 대부분 `Assets/Scripts/Audio/`의 두 문서에 옮겨져 있다. 부록 C의 로컬 경로(`%TEMP%`, `~/.claude`)는 작성한 PC 기준이며, 그 자료는 이 폴더의 01~03이다.

---

# FMOD 오디오·타이밍 레이어 재설계 계획

## 1. 한눈에 보기
- **왜**: 지금 게임 오디오는 `FMODAudioManager` 하나가 FMOD for Unity의 `RuntimeManager`를 빌려 쓰는 임시 구현이다.
  - 곡 시계가 부정확하다. 동기점을 한 번만 잡고, DSP 클록이 계단식으로 오르며, 포커스를 잃거나 리드인 중에 일시정지하면 판정이 밀린다.
  - 출력 장치가 부팅 시 복원되지 않는다.
  - 곡 로드에 실패하면 무한 대기에 빠진다.
  - BGM 슬롯 하나를 로비 BGM, 프리뷰, 게임 곡이 공유한다.
  - miss 판정에는 판정 싱크가 적용되지 않는다.
  - ASIO를 지원하지 않는다.
- **무엇을**: 게임 쪽 오디오·타이밍 레이어를 처음부터 다시 만든다.
  - FMOD Core System을 직접 소유한다.
  - ASIO와 런타임 출력 전환을 지원한다.
  - 연속 곡 시계와 입력 시각 기반 판정 타이밍을 만든다.
  - 모듈은 GameManager/ChartManager와 분리한다.
- **어떻게**:
  1. 먼저 **버리는 최소 스파이크**로 핵심 전제(FMOD 수명주기, ASIO, 시계 품질)를 확인한다.
  2. 모듈을 만든다.
  3. 나머지 코드를 연결한다.
  4. 마지막에 GameManager/ChartManager를 좁은 통합 지점으로만 연결한다.
  5. 단계마다 게임을 플레이할 수 있는 상태를 유지한다.
- **역할 분담**:
  - Claude: 코드 작성, 컴파일 확인(`dotnet build`, 10장)
  - 사용자: 에디터 작업, 스파이크 실행, 플레이 확인. 단계마다 사용자 카드(7장)에 할 일과 보고할 내용을 적는다.
- **범위 밖**: ChartEditor, FMOD Studio, 곡별 타격음 EQ, Raw Input 구현, 캘리브레이션 씬, 음원 파일명 변경

## 2. 용어
| 용어 | 뜻 |
|---|---|
| DSP 클록 | FMOD 믹서가 지금까지 믹스한 샘플 수. 믹스 블록 단위로 계단식으로 오르며, 실제로 들리는 시각보다 조금 앞선다. |
| QPC | Windows 고정밀 타이머(`Stopwatch.GetTimestamp`). 입력 시각과 프레임 시각의 기준이다. |
| 곡 시계(SongClock) | DSP 클록과 QPC를 짝지어 "지금 곡의 몇 초인가"를 연속된 값으로 알려 주는 객체 |
| 앵커 / 리드인 | 곡 시작 기준점, 그리고 첫 마디 전의 빈 구간. 노트 싱크는 앵커에서만 적용한다. |
| 불연속(epoch) | 시계를 새로 맞춰야 하는 사건(시작, 일시정지, 재개, 장치 변경, 재초기화) |
| 클램프 | 곡 시계 값을 매 프레임 실제 DSP 읽기값 근처로 제한하는 안전장치. 믹서가 멈추면 곡 시계도 함께 멈춘다. |
| 세대(Generation) | FMOD System을 새로 초기화할 때마다 올라가는 번호. 이전 세대의 핸들은 무효가 된다. |
| WASAPI / ASIO | Windows 기본 오디오 출력 / 저지연 전문 오디오 드라이버 규격 |
| Follow-Default / Pinned | 장치 설정 방식. Windows 기본 장치를 따라가는 방식 / 특정 장치를 GUID로 고정하는 방식 |
| STA | COM 스레드 모델. ASIO는 STA 스레드에서 초기화해야 한다. |
| 스파이크 / 게이트 | 실측 실험 / 통과해야 다음 단계로 가는 스파이크 |
| 통합 지점 `[AUDIO-IP]` | GameManager/ChartManager 안에서 새 모듈과 연결하는 코드 위치. 줄 번호가 아니라 심볼로 가리킨다. |
| FrameBatched | 한 프레임에 모인 입력을 시각순으로 처리한 뒤 시간을 진행하는 방식. 지금 판정 규칙과 결과가 같다. |
| Synthetic 입력 | 실제 키 입력이 아닌 이벤트. 입력 맵 비활성화나 포커스 상실 때 Input System이 만드는 cancel이다. |
| asmdef | Unity 어셈블리 정의. 폴더 단위로 별도 DLL로 컴파일하며, 참조를 선언하지 않은 코드는 컴파일러가 막는다. |
| 과도기 어댑터 | 기존 `IAudioManager`를 새 모듈 위에서 흉내 내는 임시 구현. 모든 소비자를 옮긴 뒤 C 단계에서 삭제한다. |

## 3. 확정 결정
1. **범위**: 게임 쪽 오디오를 처음부터 다시 설계한다. 기준은 코드 품질, 확장성, 객체지향, 정확한 판정, 레이턴시다. ChartEditor는 건드리지 않는다. 자동 채보 1마디 오프셋(H4)은 의도된 동작이다.
2. **FMOD 소유**: Core System을 직접 소유하고 Studio는 쓰지 않는다.
   - RuntimeManager는 게임 경로에서 초기화되지 않게 한다. StudioListener를 제거하고 가드를 둔다.
   - FMOD 패키지와 빈 Master 뱅크는 유지한다.
   - 곡별 타격음 EQ는 넣지 않는다.
3. **ASIO**: 구현한다. 출력 장치, 출력 타입, 버퍼 변경은 로비와 설정 화면에서만 허용한다.
4. **입력**: 1단계만 한다. 입력 소스 추상화, 연속 곡 시계, 입력 시각 기반 판정 타이밍을 만든다. Raw Input은 스파이크 뒤 후속 작업이다.
5. **오프셋**: 두 설정의 UI와 단위를 유지한다.
   - 노트 싱크 `audioOffsetMs`는 곡 시계 앵커 한 곳에서만 적용한다.
   - 판정 싱크 `judgmentOffset`(판정 입력 윈도우 이동, 3ms 단위)은 JudgementTimeline 한 곳에서만 적용한다. miss 컷오프도 포함한다.
   - 버퍼 지연 자동 보정, 안내, 장치별 프로필, 결과 화면 자동 제안, 차트 오프셋은 넣지 않는다.
   - 캘리브레이션 씬은 후속 작업이다. 판정별 부호 있는 오차 기록만 남긴다.
   - 새 시계에서는 같은 노트 싱크 값의 체감이 약간 달라진다(256×4에서 약 +2.7ms). 저장값은 그대로 두고 실측값만 기록한다.
6. **백그라운드**: `playInBackground`를 살린다(`Application.runInBackground`와 연동).
   - 로비에서는 설정에 따라 계속 재생하거나 음소거한다.
   - **게임 중에는 설정과 관계없이 항상 자동 일시정지한다.**
   - mixerSuspend는 쓰지 않는다.
7. **GameManager/ChartManager 병행 리팩터 대응**
   - 모듈은 두 Manager를 모른다(asmdef로 강제).
   - 두 파일은 심볼로 표시한 통합 지점만 편집한다.
   - 기준 브랜치는 지금의 `refactor-FMOD`다. Illustar를 머지한 뒤에는 재적용 규칙 R1~R11로 다시 맞춘다.
8. **배치**: 인터페이스는 모듈이 소유한다(`Net/IApiClient` 선례).
   - 오디오 계약은 `Audio/`에, 판정·입력 타이밍은 `Game/Timing/`에 둔다.
   - `App/Interfaces`는 매니저 인터페이스 전용이므로 추가하지 않는다.
9. **스레드**: 우리가 만드는 스레드는 없다. FMOD 호출은 전부 메인 스레드에서 한다(9장).
10. **단순화**
    - 사용자 출력 변경은 항상 close → init 한 경로로 처리한다.
    - 폴백은 부팅 3단계, 적용 4단계로 둔다.
    - 시계와 견고성 장치는 최소한으로 시작하고, 스파이크에서 필요가 확인될 때만 추가한다.
    - A/B define은 두지 않는다. 롤백은 PR revert로 한다.
11. **asmdef**: Core, Audio, Game/Timing, 테스트에 asmdef를 둔다.
12. **음원 파일명은 그대로 둔다.** 빌드 검증기는 파일이 실제로 있는지만 확인한다.
13. **코드 작성 원칙**
    - 이해하고 유지보수하기 쉽게 쓴다.
    - 주석은 핵심 위치에만, 역할을 간결하게 적는다.
    - **삼항 연산자는 쓰지 않는다.**
    - 동시성이 없는 곳에 동시성 자료구조를 쓰지 않는다.
14. 소통은 한국어로 한다.

## 4. 구조

### 4-1. 계층과 어셈블리
```
SCOdyssey.Core (asmdef, Assets/Scripts/Core)                ← System·UnityEngine만. Qpc 추가
   ↑
SCOdyssey.Audio (asmdef, Assets/Scripts/Audio)              ← Core, FMODUnity, UniTask
   ↑   FMOD Core System 유일 소유자: 엔진, 출력, 믹서, 원샷, 음악, 곡 세션, 곡 시계
SCOdyssey.Game.Timing (asmdef, Assets/Scripts/Game/Timing)  ← Core, Audio, Unity.InputSystem
   ↑   입력 시각 변환, 판정 타이밍(판정 싱크), JudgementDriver
Assembly-CSharp (기존 코드 전부)
   · App/Managers: 모듈 설치와 등록(조립 루트)
   · App/AudioSettingsMapper, App/SettingsMigration
   · App/LegacyAudioManagerAdapter: 과도기 IAudioManager 구현, C 단계에서 삭제
   · GameManager/ChartManager: 통합 지점만
Assembly-CSharp-Editor: Assets/Scripts/Editor/{AudioBuildValidator.cs, Tests/**}(App 코드 테스트)
테스트 asmdef(Editor 전용): Audio/Tests/Editor, Game/Timing/Tests/Editor
```
- **asmdef로 얻는 것**: Audio나 Game/Timing이 GM/CM, Game, UI, App, Domain 타입을 쓰면 **컴파일 오류**가 난다. 그래서 커스텀 의존성 검사기와 SelfTests 우회 구조는 만들지 않는다.
- **grep 규칙**(PR마다 확인): `\bRuntimeManager\b`는 가드 파일과 `ChartEditor/**`에서만 허용한다. 삼항 연산자는 0건이어야 한다.
- **확인된 사실**
  - Core 11개 파일은 System과 UnityEngine만 쓰고 전부 public이다.
  - Plugins와 Editor 코드 중 Core를 참조하는 곳이 없다.
  - FMOD C# 래퍼는 `FMODUnity.asmdef`에 들어 있다.
  - Domain은 MusicSO의 Odin 직렬화에 타입 이름이 들어 있으므로 **asmdef로 옮기지 않는다.**
  - Core asmdef에는 `noEngineReferences`를 켜지 않는다.
- **어셈블리 경계 규칙**
  - Audio와 Timing은 설정 DTO, 입력 액션, 판정 상수를 모른다. 값은 App이 넘긴다.
    - 엔진 구성은 `AudioSettingsMapper`가 Install 인자로 넘긴다.
    - 노트 싱크는 `Start`의 인자로 넘긴다.
    - 판정 싱크는 `Func<int>`로 넘긴다.
    - playInBackground는 `Func<bool>`로 넘긴다.
  - 과도기 어댑터(App)는 Audio의 internal에 접근할 수 없다. 그래서 Audio에 **public 과도기 API `ILegacyTransport`**(단일 슬롯 Load/IsLoaded/PlayAt(dspSec)/Pause/Resume/Stop/IsPlaying/DspSeconds, 원샷 슬롯)를 둔다. 어댑터는 호출을 전달만 한다. C 단계에서 둘 다 삭제한다.
  - `InternalsVisibleTo`는 테스트 어셈블리에만 연다(Assembly-CSharp에는 열지 않는다).
  - 공개 계약에는 FMOD 타입을 노출하지 않는다. UniTask는 로드와 적용 API에만 쓴다.
  - 이름 충돌 피하기: `AudioOutputType`, `AudioOutputConfig`, `AudioBus`(C 단계 전까지 App에 남음)와 UnityEngine의 `Audio*` 이름을 쓰지 않는다. 새 출력 타입 enum은 `AudioOutputKind`로 한다.
  - `Boot.ExecutionOrder`는 참조할 수 없다. 그래서 Audio에 실행 순서 상수를 따로 둔다. Runner는 -1010(Boot의 Early -1000보다 먼저), JudgementDriver는 -900이다.
  - 로그는 메인 스레드에서 `Debug.Log*`로 남긴다. MainScene 경로에는 CoreLogger가 없다.
  - `.slnx` 추적 여부는 S0에서 정한다.

### 4-2. 주요 계약
**Audio(`SCOdyssey.Audio`)**

| 계약 | 역할 |
|---|---|
| `IAudioEngine` | 상태(Running/Degraded/Failed), 세대 번호, 상태 변경 이벤트 |
| `IAudioOutputService` | 장치 목록(`GetCachedDevices`, `GetDevicesAsync`), 출력 설정 적용 `ApplyAsync`(진행 중이면 `Busy`로 거절). 요청 구성으로 성공했을 때만 설정 저장 신호를 준다. |
| `IAudioMixer` / `IMixBus` | Master, Music, HitSound, Sfx 버스 볼륨과 음소거 |
| `IOneShotPlayer` | 타격음 등록(멱등)과 할당 없는 재생 |
| `IMusicPlayers` / `IMusicPlayer` | 로비 BGM과 프리뷰 재생기(분리, 마지막 요청만 유효, 프리뷰는 150ms 디바운스) |
| `ISongPlayer` / `ISongSession` | 게임 곡 로드(성공/실패/취소), 시작(`Start`는 노트 싱크만 래치), 일시정지, 재개, 정지, `IsAudioFinished` |
| `ISongClock` / `SongFrame` | 프레임 스냅샷(SongTime, QpcTicks, Epoch), `SongTimeAt(qpc)`, 불연속 이벤트. **판정 시각(JudgeTime)은 없다.** |
| `ILegacyTransport` | 과도기 전용(4-1). C 단계에서 삭제한다. |

**Game/Timing(`SCOdyssey.Game.Timing`)**
- 입력: `IInputTimestampSource`(1단계에서는 `Drain`만), `UnityInputSystemTimestampSource`(순수 클래스), `LaneInputEvent`
- `JudgementTimeline`: 판정 싱크를 적용하는 유일한 지점이다. `Func<int>`에서 한 번 래치한다. 래치 시점은 세션 Started 이벤트이고, 놓쳤으면 첫 진행 프레임, Started 뒤에 Attach했으면 Attach 즉시다. judgeTime만 계산하고, 판정 창과 등급은 ChartManager에 남긴다.
- `JudgementPump`: FrameBatched. NaN 시각은 거부한다.
- `IJudgementClient`: `Advance`, `OnLaneInput`, `OnFrame`(매 프레임 호출)
- `TimingSample` / `IJudgementTimingLog`
  - 자체 `TimingKind{Press, HoldBody, Release, Miss}`와 int 등급을 쓴다.
  - 부호 있는 오차 ms, 래치한 판정 싱크 단계, Epoch를 기록한다.
- `GameplayTimingBinding`: 얇은 어댑터
- `JudgementDriver`: Game/Timing의 **유일한 MonoBehaviour**다. 입력 버퍼, 타임라인, Pump, 판정 로그를 소유한다. 진단 오버레이(순수 클래스)도 이 드라이버의 OnGUI에서 그린다.

### 4-3. 핵심 동작
- **프레임 순서**
  1. AudioEngineRunner(-1010): 엔진 update → 플래그 처리 → 클록 샘플 → 세션 Tick → SongFrame. **입력 수집과 판정은 하지 않는다.**
  2. JudgementDriver(-900): 입력 버퍼 비우기 → 곡 시각 변환 → Pump
  3. 게임플레이(0)
- **엔진 부팅**
  - 초기화 순서: `System_Create` → setOutput → setDriver(GUID로 찾은 인덱스) → setSoftwareFormat → setDSPBufferSize → init → 실제 상태 검증
  - WASAPI 믹서 레이트는 `min(장치 레이트, 48000)`이다. ASIO는 드라이버 레이트를 따른다.
  - ASIO는 `RuntimeInformation.ProcessArchitecture == X64`일 때만 노출한다.
  - STA 여부(`CoGetApartmentType`)를 로그로 남긴다.
  - ASIO 버퍼 UI에는 "ASIO 제어판 값과 같게"라는 안내를 둔다.
  - 부팅 폴백: 요청 → WASAPI 기본 512×4 → NOSOUND
  - 명령행 인자 `-sco-audio-safe`가 있으면 곧바로 WASAPI 기본으로 부팅한다.
- **출력 변경(설정 화면)**
  - 볼륨은 즉시 적용한다. 나머지는 **항상 close → 재설정 → init**으로 처리한다.
  - 전제 검사와 변경 분류는 `UniTask.Yield` 뒤에 한다.
  - 적용 폴백: 요청 → 직전에 동작하던 구성 → WASAPI 기본 512×4 → NOSOUND
  - **설정은 요청 구성으로 성공했을 때만 저장한다.** 그래서 적용 중 크래시가 나도 다음 부팅이 같은 구성으로 크래시하지 않는다.
  - init 뒤에는 ChannelGroup, 원샷, 볼륨을 다시 만들고 로비 BGM과 프리뷰를 원래 상태대로 다시 연다.
  - SoundSettingUI에는 `_applying` 가드를 둔다.
- **장치 콜백과 핫플러그**(마스크는 항상 명시한다. 기본값 ALL은 자동 전환을 꺼 버린다)

  | 모드 | 마스크 | 처리 |
  |---|---|---|
  | Follow-Default | ERROR \| DEVICELOST \| DEVICEREINITIALIZE | FMOD 자동 전환에 맡긴다. DEVICEREINITIALIZE가 오면 곡 시계를 리셋하고, 게임 중이면 일시정지한다. |
  | Pinned | 위 마스크 + DEVICELISTCHANGED | 목록이 바뀌면 GUID가 있는지 확인한다. 없어졌으면 1초 디바운스 뒤 기본 장치로 close→init한다. 장치가 돌아와도 자동으로 복귀하지 않는다. |
  | 공통 | – | DEVICELOST는 재초기화 경로(적용 폴백)로 처리한다. Windows 장치 형식 변경, 독점 앱, 절전 복귀가 여기에 해당한다. 게임 중이면 즉시 일시정지하고, 재개는 사용자가 Pause UI에서 한다. |

  - NOSOUND로 떨어지면 상태 줄과 MainUI 경고 1회로 알린다. 설정을 적용할 때 다시 시도한다.
- **곡 시계(최소안)**
  - DSP 하한 포락선 오프셋(최근 구간 기준으로 갱신) + **매 프레임 원시 DSP 읽기값 클램프**(`c_read ≤ DspAt ≤ c_read + S_max`)
  - S_max 초기값은 `L·(N+1)` 샘플이다. 세대마다 관측한 최대 계단으로 갱신하고, SP3로 확정한다.
  - 불연속 때 리셋한다.
  - Starting이나 Playing 중에 포커스가 있는데 원시 DSP 값이 0.5초 넘게 그대로면, 세션을 Recovering으로 두고 일시정지 경로를 탄다. 재초기화는 하지 않는다.
  - 드리프트 보정, 렌더 평활 시계, 소프트 에폭은 SP3 데이터로 필요가 확인될 때만 넣는다. 평활 시계를 넣으면 세션 불연속에서 스냅한다. 평활 시계가 없으면 Timeline과 BGA는 `Frame.SongTime`을 쓴다. BGA는 100ms 드리프트 규칙만 쓴다.
  - 노트 싱크: `Z = leadIn + audioOffsetMs / 1000.0`(실수 나눗셈)
- **곡 재생**
  - 게임 곡: CREATESTREAM | NONBLOCKING | ACCURATETIME | IGNORETAGS
  - 로비·프리뷰: ACCURATETIME 없이 IGNORETAGS만
  - 원샷: CREATESAMPLE
  - 채널 우선순위: BGM 0, 타격음, SFX 순
  - 세션은 자신을 만든 씬이 언로드되면 자동 Dispose된다.
- **일시정지와 재개**
  - 재개는 항상 "정지 → 위치 설정 → 미래 DSP 시각에 재예약"으로 한다(리드인 중 일시정지 H2도 해결).
  - Ready·Starting 상태의 SongTime은 0이다. Starting 중 일시정지하면 재개 위치도 0이다.
  - Runner의 `OnApplicationFocus(false)`는 게임 세션을 직접 일시정지한다(백그라운드에서 Update가 멈춰도 동작).
- **음원 종료**: `IsAudioFinished`와 `AudioEnded`로 알린다. 종료 뒤에도 일시정지할 수 있다. 곡 끝에서 스트림이 같은 위치에서 두 번 끊기면 종료로 처리한다.
- **판정 타이밍**
  - 입력은 `min(qpc, Frame.QpcTicks)`로 클램프하고 보류하지 않는다.
  - Synthetic 입력(맵 비활성 구간, `!Application.isFocused`)에는 표시를 붙인다.
  - 클라이언트가 없어도 매 프레임 입력 버퍼를 비운다. Attach 이전 시각의 이벤트는 버린다. 세션이 Disposed되면 자동으로 detach한다.
  - Pump는 입력 버퍼의 복사본만 순회한다.
  - 타격음은 입력을 처리할 때 재생한다.
- **포커스**
  - binding은 `OnFrame`에서 매 프레임 세션 상태를 확인한다. `Paused/Recovering`이고 사용자 일시정지가 아니면 GameManager에 일시정지를 전달한다.
  - 곡 시작과 재개 커밋 때도 포커스를 확인한다.
  - 로비 음소거는 모듈이 `Func<bool>`을 매번 읽어 처리한다. **S4부터 동작한다.**
- **이벤트 발행**: 외부로 보내는 이벤트는 try/catch로 감싼다. 상태 전이 중에 생긴 이벤트는 모았다가 작업이 끝난 뒤 발행한다. 구독자는 OnDestroy에서 구독을 해제한다.
- **Install 실패**: Managers가 try/catch로 감싼다. 실패하면 no-op 모듈(QPC 시계로 진행하는 무음 세션)과 **어댑터를 함께 등록**한다. 어댑터의 `GetDSPTime`은 QPC로 진행하고 `IsLoaded`는 즉시 true를 돌려준다. `SetAudioDevice`는 범위 밖 인덱스면 no-op이다.
- **문자열**: 새 UI 문자열은 `AudioUiText` 한 곳에 모은다(이후 Localization으로 옮기기 쉽게).

## 5. GameManager/ChartManager 통합 지점
심볼로 가리키고 `// [AUDIO-IP:G#]`, `// [AUDIO-IP:C#]`로 표시한다. 구조를 바꾸지 않는다. I1/I2를 시작하기 전에 Illustar 머지 여부를 확인하고, 머지되었으면 그 코드 기준으로 다시 대조한다.

- **I1: GameManager.cs만 편집. 곡 시각만 넘긴다(판정 시각은 I2부터).**
  - G1: 세션과 binding을 얻는다.
  - G2: 레인 입력 구독을 삭제하고, OnDestroy에서 binding을 Dispose한다(SwitchToUI보다 먼저).
  - G3: `StartGame`에서 Attach한다(`() => IsGameRunning`). `globalStartTime = _audioManager.GetDSPTime();` 줄과 `SetTimeSyncPoint(...)` 호출을 삭제한다.
  - G4: `StartMusic`을 `_session.Start`로 바꾸고, `bgaController?.SchedulePlay` 호출을 삭제한다.
  - G5: `GetCurrentTime`을 `Frame.SongTime`으로 바꾼다.
  - G6: `Update`의 SyncTime 호출을 삭제한다.
  - G7: `Pause`를 `_session.Pause(User)`로 바꾸고, `bgaController?.Pause`를 삭제한다.
  - G8: `Resume`을 `_session.Resume()`으로 바꾸고, `bgaController?.Resume`을 삭제한다.
  - G9: binding 콜백 세 개(진행, 입력, 비사용자 일시정지)를 둔다. 곡 시각만 넘긴다.
  - G10: `IsAudioPlaying`을 `!IsAudioFinished`로 바꾼다.
  - G11: `OnGameFinished`에서 `Stop()`을 호출한다.
  - G12: `HandleNonUserPause() { if (IsGameRunning && !IsPaused) Pause(); }`를 추가한다.
- **I2: ChartManager.cs + G13·G14(한 PR)**
  - 노트 선택, 등급 창, 홀드·miss 규칙은 바꾸지 않는다. 의도된 변경은 두 가지다.
    - miss 컷오프에 판정 싱크를 적용한다.
    - ESC로 생긴 합성 release는 판정하지 않는다. HoldRelease 창 안에서 ESC를 누르면 miss가 된다.
  - C1: 모든 `_judgmentOffsetSec` 사용처를 JudgementTimeline 값으로 바꾼다.
  - C2: 원샷 재생기, 세션, 판정 로그를 얻는다.
  - C3: `Init` 끝의 `gameManager.StartMusic(barDuration)` 호출을 `_session.Start`로 바꾼다.
  - C4: `SyncTime(songTime, judgeTime)`으로 바꾼다.
  - C5: `CheckHoldingBody`가 judgeTime을 쓰게 한다.
  - C6: `TryJudgeRelease(..., bool applyJudgement = true)`로 바꾼다. 메서드 안 지역 변수 `JudgeType judge`와 이름이 겹치지 않게 한다.
  - C7: `CheckGameClear`에서 `IsAudioFinished`를 쓴다.
  - C8: `PlayHitSound`를 IOneShotPlayer로 바꾼다.
  - C9: 판정 기록 네 곳(Press / HoldBody / Release / Miss)을 넣는다.
  - G13: SyncTime 인자를 맞추고, 이제부터 판정 시각을 넘긴다.
  - G14: `applyJudgement: e.Judgeable`을 넘긴다.
- **재적용 규칙 R1~R11**을 이행 문서에 둔다. 곡 시작, 시간 진행, 입력, binding 수명, 일시정지 UI, 종료, 타격음, 판정 기록 계약, 그리고 C 단계에서 삭제될 심볼(IAudioManager 계열, ILegacyTransport, IInputManager 레거시 3종, BGAController.SchedulePlay/Pause/Resume)을 쓰지 말 것.

## 6. 파일
- **신규**
  - `Core/Qpc.cs`, `Core/SCOdyssey.Core.asmdef`
  - `Audio/*.cs`(계약), `Audio/{Clock, Engine, Output, Mixing, Playback, Hosting, Legacy, Diagnostics}/**`, `Audio/SCOdyssey.Audio.asmdef`, `Audio/AssemblyInfo.cs`(테스트 전용 InternalsVisibleTo)
  - `Game/Timing/{LaneInput, Judgement, Diagnostics}/**`, `Game/Timing/{GameplayTimingBinding, JudgementDriver}.cs`, `SCOdyssey.Game.Timing.asmdef`, `AssemblyInfo.cs`
  - `App/{AudioSettingsMapper, SettingsMigration, LegacyAudioManagerAdapter, AudioUiText}.cs`
  - 테스트
    - `Audio/Tests/Editor/**`, `Game/Timing/Tests/Editor/**`: 테스트 asmdef. `includePlatforms: [Editor]`, `overrideReferences: true`, `precompiledReferences: [nunit.framework.dll]`, `autoReferenced: false`, `defineConstraints: [UNITY_INCLUDE_TESTS]`, 참조 `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, Core, Audio(, Timing, UniTask)
    - `Assets/Scripts/Editor/Tests/**`: asmdef 없음. 마이그레이션과 매퍼 테스트
  - `Assets/Scripts/Editor/AudioBuildValidator.cs`: MusicSO, 로비 BGM, 타격음 파일이 실제로 있는지 검사
  - 스파이크: `Testing/AudioSpike/**`. 파일 전체를 `#if SCO_AUDIO_HARNESS`로 감싼다. S-0.5의 버리는 코드는 S1에서 모듈 하네스로 교체한다.
  - 문서: `Audio/Audio_architecture.md`(구조, 오래 유지), `Audio/Audio_migration.md`(단계 카드, 스파이크 결과, R1~R11. C 단계 뒤 삭제)
- **수정**
  - Managers.cs, SettingsManager.cs(S5a에서 Apply를 IAudioMixer 기반으로), SettingsData.cs(v2)
  - SoundSettingUI.cs와 프리팹, InputManager.cs(S4a), IInputManager.cs(C), MainUI.cs, AdventureUI.cs, GameDataLoader.cs
  - TimelineController.cs(시그니처 유지, `timeProvider`가 null일 때만 늦게 `TryGet`), BGAController.cs
  - GameManager.cs, ChartManager.cs(통합 지점만), GameSceneTester.cs(C 단계: IAudioManager 블록 삭제)
  - `Plugins/FMOD/src/Platform.cs`는 :871-872, :874-878만 되돌린다(:873 getter는 유지).
  - FMODStudioSettings.asset(playInEditor DSPBufferCount 4), MainScene(StudioListener 제거), GameScene(남은 AudioSource, AudioListener 제거)
- **삭제**
  - S4b: FMODAudioPreInit.cs, FMODAudioManager.cs
  - C: IAudioManager, 어댑터, ILegacyTransport, 레거시 멤버
- **변경 없음**: `ChartEditor/**`, ChartEditorScene, APITestScene, IGameManager, Constants, CharacterAnimator, GameSettingUI, PauseUI, ResultUI, UIManager

## 7. 단계와 사용자 카드
**진행 방식**
1. Claude가 코드를 쓴다.
2. 새 파일이나 asmdef가 생기면 사용자가 Unity에 포커스해 csproj를 다시 생성한다.
3. Claude가 새 파일이 csproj의 Compile 목록에 있는지 확인한 뒤 `dotnet build`로 컴파일을 확인한다.
4. 사용자가 에디터에서 카드대로 확인하고 결과를 붙여 넣는다.
5. Claude가 수정한다.

PR마다 저장소 규칙대로 이슈를 먼저 만든다. 커밋과 PR은 요청할 때만 만든다.

| 단계 | 내용 | 게이트와 확인 | 롤백 주의 |
|---|---|---|---|
| **S-1** | 설계 문서를 저장소로 옮겨 두 문서로 나누고, 부록 A 체크리스트 grep 0건. **15개 단계 카드 전부 작성**, 변경 요약 1쪽 | 문서 검토 | – |
| **S-0.5** | 버리는 최소 스파이크: System 생성·해제 반복, ASIO init과 장치 목록, 5분 DSP↔QPC CSV(프리셋별, 96k 포함), setDelay와 일시정지, 스트림 위치 설정 정확도, `ctx.time` 분포, 콜백 스레드 ID | SP1, SP2, SP3, SP4, SP11, SP15 | define과 빌드 프로필도 지운다 |
| S0 | asmdef(Core 이동 포함)와 테스트 asmdef, `Core/Qpc`, **Audio 계약 파일**, `Audio/Clock`(S-0.5 결과로 복잡도 확정), Game/Timing 순수 부분(입력 타입, 판정 타임라인, Pump), 테스트 | 전체 컴파일, Test Runner 통과, 콘솔 오류 0 | Core asmdef만 따로 되돌릴 수 있다 |
| S1 | Engine(부팅, 폴백, 콜백 마스크, 로그, 에디터 정리), Mixer, OneShot, Hosting, 오버레이, **모듈 하네스**(S-0.5 코드 교체) | SP1 재확인 | revert |
| S2a | 곡 재생: StreamLoader, MusicPlayers, SongSession | SP4(루프백 녹음) | revert |
| S2b | 출력: 장치 카탈로그, ApplyAsync(close→init, 롤백, 성공 시에만 저장), 핫플러그 | SP6, SP8, SP9, SP10 | revert |
| S3 | Game/Timing: Unity 입력 소스, JudgementDriver, binding, 판정 로그, 오버레이 | SP11, SP-IN(하네스) | revert |
| S4a | Managers 설치(try/catch, no-op 경로에서도 어댑터 등록), ILegacyTransport와 어댑터, AudioSettingsMapper v1, JudgementDriver·판정 로그 등록, **InputManager**(소스 소유, Push, Synthetic 구간), 로비 포커스 음소거. FMODAudioManager는 등록만 끊는다. **GM/CM 편집 없음** | 기존과 같은 동작(로비 BGM, 프리뷰, 곡, 타격음, 장치 선택, 포커스 음소거) | revert |
| S4b | PreInit·FMODAudioManager 삭제, Platform.cs 되돌림, 씬·에셋 정리, RuntimeManager 가드 | ChartEditorScene 동등, 릴리스 스모크 빌드 | 씬·에셋 YAML을 같은 PR에 둔다 |
| S5a | 설정 v2(v1 필드는 `[Obsolete]`로 남김), 마이그레이션(`.v1.bak`, 손상 값 검증, ResetToDefault), `SettingsManager.Apply` → IAudioMixer, runInBackground 연동 | 마이그레이션 테스트 통과, SP14(로비) | 착수 전 PlayerPrefs 키를 백업한다 |
| S5b | SoundSettingUI(출력 타입, GUID 장치 목록, 버퍼, 상태 줄, 적용 가드), v1 필드 삭제, MainUI 폴백 경고, 빌드 검증기 | SP13(버퍼 프리셋 확정) | revert |
| S6 | MainUI, AdventureUI를 IMusicPlayers로 전환(TryGet, 음원 없음 안내) | 프리뷰가 하나만 나옴 | revert |
| S7 | TimelineController, BGAController를 곡 시계 기준으로 전환. 세션이 없으면 레거시 경로를 쓴다. | ChartEditorScene 동등(새 경로 검증은 I1에서) | revert |
| I1 | GameManager 통합 G1~G12, GameDataLoader | SP4, SP10, SP14, SP-IN(게임), SP13(b), BGA·Timeline 새 경로 | I2보다 먼저 revert할 수 없다 |
| I2 | ChartManager 통합 C1~C9 + G13·G14 | SP11, 판정 싱크 ±20, miss·홀드가 I1과 같음 | revert |
| C | 어댑터, IAudioManager, ILegacyTransport, 레거시 경로 삭제, GameSceneTester의 IAudioManager 블록 삭제 | R11 grep 0건, 전체 회귀 | 병행 리팩터와 충돌하면 C-a/C-b로 나누거나 `[Obsolete]` shim을 둔다 |

**사용자 카드 공통**(단계별 전체 카드는 S-1에서 이행 문서에 작성)
- 처음 한 번 Preferences → External Tools → Regenerate project files를 실행한다.
- 각 단계는 다음 순서로 한다: 에디터 열기 → 콘솔 오류 0건 → 카드 절차 → 결과 붙여 넣기.
  - 결과는 스파이크 요약 파일 `audio_spike_summary.txt`로 보낸다. 형식은 SP#, PASS/FAIL, 기준값, 실측값, 출력 타입·장치·레이트·버퍼, CharacterAnimator 로그 활성 여부다.
  - CSV는 `persistentDataPath/audio_spike/`에 저장된다.
- **준비물**
  - 출력 장치 두 개
  - ASIO 개발용 FlexASIO(무료)
  - 루프백 녹음용 Audacity
  - 실제 ASIO 장비나 ASIO4ALL은 SP8에서만 쓴다. 없으면 FlexASIO로 최소 매트릭스를 진행한다.
- **사용자만 할 수 있는 에디터 작업**(프리팹과 씬 YAML은 텍스트로 안전하게 편집할 수 없음)

| 단계 | 작업 |
|---|---|
| S-0.5 | `AudioSpikeScene`을 만들고 하네스를 붙인다. Build Profile `AudioSpike-Dev`(Development, Script Debugging OFF)와 `AudioSpike-Release`를 만들고, 두 프로필 모두 씬 목록은 AudioSpikeScene 하나, Scripting Defines는 `SCO_AUDIO_HARNESS`로 둔다. 에디터에서는 해당 프로필을 활성화한다. FMOD Event Browser는 닫는다. |
| S4b | MainScene 카메라의 StudioListener를 제거한다. GameScene의 "Audio Source" GameObject와 AudioListener를 제거한다. FMOD Settings → Play In Editor → DSP Buffer Count를 4로 한다. |
| S5b | SoundSettingUI 프리팹에 `Btn_OutputTypePrev`, `Btn_OutputTypeNext`, `Text_OutputTypeValue`, `Text_AudioStatusValue`를 추가한다(`BaseUI.Bind` enum 규칙). Player Settings에서 Force Single Instance를 켠다. |
| C | GameSceneTester 파일을 지우는 경우 GameScene에서 해당 컴포넌트를 제거한다. |

- 에디터에서 ASIO를 쓰면 Play마다 장치를 잡는다. 이를 피하려고 EditorPrefs 토글 "에디터에서 ASIO 허용"(기본 OFF)을 둔다.

## 8. 게이트 실패 시 조치
| 스파이크 | 무엇을 보나 | 실패하면 | 사용자 결정 |
|---|---|---|---|
| SP1 수명주기 | init·release 20회, 재컴파일, RuntimeManager 미초기화 | 해제 경로를 보강한다. 계속 실패하면 A안(RuntimeManager 유지, 출력 변경은 재시작 후 적용)을 제안한다. | **예** |
| SP2 STA·ASIO | 메인 스레드 STA, ASIO init과 목록 | ASIO를 끈 채 출시하거나 별도 과제로 분리하는 안을 제안한다(스레드는 만들지 않음). | **예** |
| SP3 시계 품질 | 드리프트, 계단 폭, 96k | 드리프트 항, 평활 시계, 소프트 에폭을 추가한다. | 아니오 |
| SP4 예약·seek | 루프백 onset 차이, 굶주림 | 게임 곡만 CREATESAMPLE로 바꾼다. 굶주림이 재현되면 1초 위치 검사와 재예약을 넣는다. | 아니오 |
| SP6 close→init 반복 | 50회, 소요 시간 | release → create로 바꾼다. | 아니오 |
| SP8 ASIO 드라이버별 | 블록 불일치, 레이트, 점유 | 문제 드라이버를 안내한다. 크래시가 재현되면 크래시한 구성을 기억하는 기능을 추가한다. | 아니오 |
| SP9 장치 목록 | 타입별 임시 System 열거 | 목록은 재초기화 시점에만 만든다. | 아니오 |
| SP10 핫플러그 | USB, HDMI, 형식 변경, 절전, 블루투스, 전환 전후 DSP 단조성 | 감시를 보강한다(콜백 없는 정지는 0.5초 검사로 처리). | 아니오 |
| SP11 입력 양자화 | 60/144/무제한 fps의 `ctx.time` 분포 | 높은 FPS를 권장한다. Raw Input 도입 시점을 정한다. | **예** |
| SP-IN 입력 경계 | Synthetic 태깅, ESC와 같은 프레임 입력 | 태깅 조건을 보강한다. | 아니오 |
| SP13 버퍼·종단 지연 | 프리셋별 지연. (b) 타격음 시점 PreUpdate → -900을 레거시와 비교 | 기본 버퍼를 조정한다. v1의 64·128이 실제로 적용되고 있었다면 256으로 올릴 때의 체감 변화를 결정해야 한다. | **예**(체감 변화) |
| SP14 포커스 | 전체 화면, 창 모드, alt-tab. 게임 자동 일시정지, 복귀 뒤 판정 편향 변화 1ms 미만, ASIO 재획득 | 포커스 정책을 보강한다. | 아니오 |
| SP15 콜백 | 콜백 스레드, GC 영향 | Debug 로그를 FILE 모드로만 남긴다. | 아니오 |

게이트가 실패하면 해당 PR은 머지하지 않고, 결과를 이행 문서에 기록한다. **사용자 결정이 필요한 경우 수치와 선택지를 보고하고 멈춘다.**

## 9. 스레드 규칙
- **스레드가 문제되지 않는 이유**: Unity에서 메인 스레드에만 묶인 것은 Unity API뿐이다. FMOD는 원래 자체 스레드를 돌리고, FMOD for Unity 자신도 FMOD 스레드에서 들어오는 콜백을 쓴다(`RuntimeManager.cs:95-133`). Windows 백엔드는 Mono다.
- **스레드별 처리**
  - 메인: FMOD API 전부, 장치 콜백(update에서 호출되므로 `bool` 플래그), Input System 콜백, UniTask
  - FMOD 스레드: ERROR·Debug 콜백만 여기서 온다. 짧은 `lock` + 고정 배열에 복사만 하고, 로그는 메인 스레드가 남긴다.
  - 믹서 스레드 콜백(MIDMIX, UNDERRUN)은 등록하지 않는다.
- **반드시 지킬 것**
  - 콜백 델리게이트는 static 필드에 보관하고, 메서드는 `[AOT.MonoPInvokeCallback]` static으로 둔다.
  - 플레이 모드 종료와 도메인 리로드 **전에** 콜백 해제 → Debug 종료(에디터 FILE 모드, EditorUtils와 같게) → System release를 한다.
  - 에디터 정리 훅은 Install의 첫 줄에서 등록한다.
  - Shutdown은 초기화가 어느 단계에서 실패했든 만든 것만 해제하고, 에디터 이벤트 구독도 해제한다.
  - Runner는 도메인 리로드 뒤 자신이 소유자가 아니면 스스로 비활성화한다. 소유자일 때만 Shutdown한다.
  - 플래그는 지역 변수에 복사하고 지운 뒤 처리한다. 세대가 바뀌면 초기화한다.
  - 모듈 수준 CTS는 Shutdown에서 취소한다.
  - `SwitchToThreadPool`은 쓰지 않는다.
- **성능**: 핫패스는 인덱스 for 루프만 쓴다. 첫 곡 로드 뒤 JIT 워밍업을 한 번 한다. 측정할 때는 오버레이를 끈다.

## 10. 검증 방법
- **컴파일**
  - 새 파일을 추가하면 csproj를 다시 생성하고, Compile 목록에 들어갔는지 확인한 뒤 `dotnet build`를 돌린다. 하네스는 define을 켜고 빌드한다.
  - 이 방법이 안 되면 사용자가 에디터 콘솔 오류 0건을 첫 확인으로 한다.
- **자동 테스트**(Test Runner EditMode)
  - 곡 시계: 클램프 정지·점프, 앵커, 노트 싱크 부호·실수 나눗셈
  - 판정 타임라인: 판정 싱크를 한 번만 적용, miss 포함, 래치 폴백, NaN 거부
  - Pump: 순회 중 Push, Attach 이전 이벤트 폐기, 자동 detach
  - binding: level 방식 일시정지
  - 폴백 계획
  - App 테스트(Editor 폴더): 설정 마이그레이션(손상 GUID·타입·길이), 매퍼
- **스파이크 하네스**: 7장 카드와 8장 기준을 따른다. 최종 수치는 Release 프로필로 확정한다.
- **수동 시나리오**(전체 목록은 이행 문서)
  - 로비 BGM, 프리뷰, 곡 플레이, 타격음
  - 리드인 중 일시정지, ESC와 같은 프레임 입력, 카운트다운 중 alt-tab, 로딩 중 alt-tab
  - 곡 도중 USB 분리, 로비·프리뷰 재생 중 Pinned 장치 분리
  - 음원 종료 뒤 ESC, 무음 곡 일시정지, 설정 적용 뒤 복원, Enter 두 번
  - ChartEditorScene 동등
- **릴리스 스모크 빌드**
  - Development OFF, 로그에서 `fmodstudio.dll`이 선택됐는지 확인
  - Localization·Addressables 포함 여부
  - 공백과 한글이 들어간 경로에서 실행
  - StreamingAssets 파일 수, Player.log 오류 확인

## 11. 리스크
- **병렬 리팩터 충돌**: 심볼 기준 통합 지점, R1~R11, I2 판정 규칙 불변, I1은 곡 시각만 넘긴다.
- **asmdef 부작용**: S0에서 전체 컴파일로 확인한다. 문제가 생기면 Core asmdef만 되돌리고 Audio가 Core를 쓰지 않게 조정한다.
- **ChartEditor 회귀**: playInEditor count 4, Platform.cs :873 유지, TimelineController는 늦게 `TryGet`한다(Timeline.prefab을 ChartEditorScene과 공유).
- **키보드 양자화**: 기본 60fps에서 가장 큰 오차 요인이다. Raw Input 이음새를 둔다.

---

## 부록 A. S-1 설계 문서 정리 체크리스트
설계 문서 원본(`tasks/wegk9x4rm.output`의 `result.final.designDoc`)은 "개정 2" 시점이다. 그 뒤의 결정은 **이 계획이 우선**한다. 옮길 때 다음 항목이 **0건**이어야 완료로 본다.
- **배치**
  - 경로 `App/Interfaces`(새 계약 전체), `Assets/Scripts/Timing/`
  - `SCOdyssey.Timing`, `SCOdyssey.App.Interfaces`(새 계약), `SCOdyssey.Game.Integration`, `Audio/Compat`, `SCOdyssey.Audio.Compat`, `RegisterInto`
- **판정 경로**
  - Runner의 `inputHub`, `feed.Pump`, `JudgementFeed`, `JudgementTimingLogService`, `ILaneInputStream`
  - `AttachJudgement`, `LaneSources`, `SongTimingOffsets`, `TimingOffsets`, `JudgmentOffsetSteps`(Audio 쪽), `SongFrame.JudgeTime`, `BuildFrame`의 ToJudgeTime
  - `HandleSessionEvent`, `onSessionEvent`, G12 이벤트 방식 코드
  - 입력 "다음 프레임 보류", 세션 없음 SongTime = NaN
- **출력 단순화**
  - `SetDriver` 등급, 사용자 변경·핫플러그·ASIO A→B의 `setDriver` 경로, SP5
  - MaskOnly, 격상 플래그, `SavedWasapi`, `WasapiDefault`, `SafeBuffer`, `FallbackWasapiDeviceId`, `SafeModeBoot`, SafeModeFlag, "세이프 모드"(SP8 조건부 언급 제외)
  - OutputWatchdog, "워치독", Degraded 2초 재시도, `SetBackgroundPolicy`
- **asmdef 전환**
  - SelfTests 전체, `AudioSelfTestsNUnit`, SP-T, ModuleDependencyCheck, "asmdef 없음", "규칙 4", "허용 목록", 1-3 토큰 규칙(4-1 참조 표로 대체), Audio의 `Boot.ExecutionOrder`
  - A/B define `SCO_LEGACY_FMOD`
- **시계**
  - MarkSuspect, 소프트 에폭, 품질 3단계, RenderTime 기본값, BGA "에폭 재seek"(SP3에서 필요할 때만 추가)
- **동시성**
  - `Interlocked` 전부(ERROR·Debug는 lock), `SpscRing`, "원자적으로 교체"
- **판정 모드**: `Interleaved`(부록 B I3에서만 언급)
- **단계와 게이트**: 설계 문서 12-1, 12-2, 14장의 단계와 게이트는 이 계획 7·8장으로 대체한다. FMODAudioManager 삭제 시점은 S4b다.
- **줄 번호와 번호 체계**
  - GM, CM, CharacterAnimator, IGameManager, Constants의 줄 번호 앵커
  - 설계 문서의 "결정 N" 번호를 이 계획 3장 번호로 바꾼다.
- **하네스**: `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 하네스 가드를 `SCO_AUDIO_HARNESS`로 바꾼다. 빌드 프로필은 Dev와 Release 두 개로 한다.
- **공개 계약**: `ScheduleOneShot`, `CancelScheduled`를 공개 계약에서 뺀다(하네스 내부 API로 둔다).
- **코드 스타일**
  - 삼항 연산자 9곳
  - `AudioOffsetMs/1000` → `/1000.0`
  - `FMOD.System System` → `CoreSystem`, 구조체 필드 `Qpc` → `QpcTicks`
  - 설정 v2 필드는 `audioOutputType`(string), `deviceGuid`, `deviceName`, `systemRate`, `dspBufferLength`, `dspBufferCount`다(타입별 GUID 필드 없음).

## 부록 B. 후속 백로그 (이번 범위 밖)
- **GM/CM 리팩터 쪽**
  - I3: Interleaved 판정, 구간 교차 홀드, 다중 miss
  - CharacterAnimator 입력 로그 정리(Illustar 머지 뒤)
  - GameSceneTester 정리
- **오디오 후속**: Raw Input 소스, 캘리브레이션 씬(곡 시계 원샷 예약 공개), ASIO 출력 채널 선택(현재 1/2 고정), 곡 Sound 캐시
- **사용자 결정 후보**
  - 블루투스 지연이 ±200ms를 넘는 경우(SP13)
  - 기본 FPS 60 상향
  - 출시 전 companyName/productName 확정(PlayerPrefs 경로가 이 값에 묶여 있음)
- **출시 전**: FMOD 크레딧 표기(라이선스 요구), ASIO 상표 표기 확인
- **음원 규약**: mp3는 쓰지 않는다. 루프 wav의 `smpl` 청크를 확인한다.

## 부록 C. 참고 자료
- 설계 문서 원본(8.9만 자): `%TEMP%\claude\d--Dev-Unity-sangcheol-odyssey-unity\0fb5a160-...\tasks\wegk9x4rm.output`. S-1에서 저장소로 옮긴다(임시 폴더라 사라질 수 있음).
- 결정 브리프(FMOD·Unity 사실 F1~F37, 스파이크 SP1~SP15): `~\.claude\projects\d--Dev-Unity-sangcheol-odyssey-unity\0fb5a160-...\tool-results\b1wmwee4w.txt`
- 현황 조사 보고서(이슈 H/M/L): 같은 폴더의 `b2vkl5ugm.txt`
- 검수 이력: 1·2차 조사, 설계 심사, 3·4·5차 검수. 모두 이 계획에 반영했다.
