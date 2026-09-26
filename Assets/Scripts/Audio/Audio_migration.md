# 오디오 재설계 이행 문서

기존 `FMODAudioManager`에서 새 오디오·타이밍 레이어(`Audio_architecture.md`)로 옮기는 과정의 단계, 스파이크, 통합 절차를 적은 문서다. **C 단계가 끝나면 이 문서는 삭제한다.**

- 기준 브랜치: `refactor-FMOD`. Illustar 브랜치(GameManager, ChartManager, IGameManager, Constants, CharacterAnimator 변경)는 아직 머지되지 않았다. I1/I2를 시작하기 전에 다시 확인한다.
- 각 단계가 끝나면 게임을 처음부터 끝까지 플레이할 수 있어야 한다.

---

## 0. 인수인계 (2026-09-27 기준. 다른 PC나 새 세션에서 이어서 할 때 먼저 읽는다)

**새 Claude 세션에 줄 첫 요청 예시**: "`Assets/Scripts/Audio/Audio_migration.md`의 0장(인수인계)을 읽고 S-0.5를 이어서 진행해."

**현재 단계**
- S-1(설계 문서 이관): 완료
- S-0.5(버리는 최소 스파이크): 진행 중
  - 하네스 코드: `Assets/Scripts/Testing/AudioSpike/`(파일 전체가 `#if SCO_AUDIO_HARNESS`로 감싸져 있음)
  - 씬 `Assets/Scenes/AudioSpikeScene.unity`, Build Profile `Assets/Settings/Build Profiles/AudioSpike-{Dev,Release}.asset`. 두 프로필 모두 define `SCO_AUDIO_HARNESS`가 들어 있고, 자체 씬 목록에는 스파이크 씬만 있다. 전체 씬 목록(EditorBuildSettings)은 건드리지 않는다.
- 지금까지의 결과는 7장 표에 있다.
- 2026-09-27에 고친 하네스 버그와 추가한 측정: SP4 준비 판정(`PLAYING` 포함), SP4 채널 위치 오차 측정, SP3 2초 연속 읽기, fps 경고

**확정 결정 요약**(자세한 내용은 `Audio_architecture.md`)
- FMOD Core System을 직접 소유한다(RuntimeManager는 게임 경로에서 쓰지 않음). Studio와 곡별 EQ는 쓰지 않는다.
- ASIO를 구현한다. 출력 장치, 타입, 버퍼 변경은 로비와 설정 화면에서만, 항상 close→init으로 한다.
- 입력은 1단계만 한다(추상화, 연속 곡 시계, 시각 기반 판정 타이밍). Raw Input은 후속이다.
- 오프셋: 노트 싱크(`audioOffsetMs`)와 판정 싱크(`judgmentOffset`, 판정 입력 윈도우 이동)를 유지하고, 각각 한 곳에서만 적용한다. 버퍼 지연 자동 보정, 안내, 장치별 프로필, 자동 제안은 없다. 캘리브레이션 씬은 후속이다.
- 백그라운드: `playInBackground`를 살린다. 게임 중에는 항상 자동 일시정지한다.
- GameManager/ChartManager는 팀이 병행 리팩터 중이다. 모듈은 두 Manager를 모르고, 심볼 기준 통합 지점만 편집한다. 기준 브랜치는 `refactor-FMOD`이며, Illustar는 아직 머지되지 않았다.
- asmdef를 둔다: Core, Audio, Game/Timing, 테스트. 설계는 단순화 방향이다.
- 음원 파일명은 그대로 둔다. 새 노트 싱크 체감 변화(약 +2.7ms)도 저장값을 바꾸지 않는다.

**작업 규칙**
- 소통은 한국어로 한다.
- 코드: 유지보수하기 쉽게 쓴다. 주석은 핵심 위치에만 간결하게 단다. **삼항 연산자는 쓰지 않는다.** `using FMOD;`는 쓰지 않는다.
- 커밋과 PR은 사용자가 요청할 때만 만든다. 커밋 메시지는 한국어이고 접두사를 붙인다(`docs:`, `test:`, `refactor:` 등).
- 게이트 스파이크가 사용자 결정 사항(4장 "예")으로 실패하면 수치와 선택지를 보고하고 멈춘다.
- **컴파일 확인**: 저장소에 csproj가 없으면 임시 폴더에 csproj(netstandard2.1, LangVersion 9)를 만들어 확인한다.
  - 참조: `C:/Program Files/Unity/Hub/Editor/6000.3.13f1/Editor/Data/Managed/UnityEngine/*.dll`, `Library/ScriptAssemblies/FMODUnity.dll`, `Library/ScriptAssemblies/Unity.InputSystem.dll`
  - `UnityEditor.dll`은 따로 넣지 않는다(`UnityEditor.CoreModule`과 형식이 겹침).
  - define은 에디터(`SCO_AUDIO_HARNESS;UNITY_EDITOR`), 플레이어(`SCO_AUDIO_HARNESS`), 없음의 세 조건으로 빌드한다.

**다음 할 일(사용자)**
1. 데스크탑 준비
   - 저장소를 pull한다.
   - Unity를 열고 File → Build Profiles에서 `AudioSpike-Dev`로 Switch Profile 한다.
   - FlexASIO를 설치하고, Audacity를 준비한다(Host: Windows WASAPI, 녹음 장치: loopback, 스테레오).
   - Windows 오디오 향상, 공간 음향, 모노 오디오를 끈다.
   - 전원 모드를 최고 성능으로 둔다.
2. 하네스에서 실행
   - **SP2**: ASIO 선택 → SP2 버튼(지난번에 빠짐)
   - **SP4**: WASAPI 256x4 → 시스템 켜기 → SP4. 요약의 "위치 오차(seek 있음)"가 핵심이다. 녹음은 선택이다.
   - **SP3**: 256x4에서 1분. 앞부분 연속 읽기 결과가 중요하다. ASIO4ALL(또는 FlexASIO)로도 1분(지난번 ASIO4ALL에서 클록이 60초 멈춤).
   - **SP1**: Play/Exit 반복
3. 가능하면 `AudioSpike-Dev` 빌드 실행 파일에서도 SP1, SP2, SP3, SP4를 하고, fps가 60 이상일 때 SP11을 한다.
4. `persistentDataPath/audio_spike/audio_spike_summary.txt`의 새 줄을 붙여 넣는다.
   - 노트북에서는 경로가 `C:\Users\serdi\AppData\LocalLow\DefaultCompany\sangcheol-odyssey\audio_spike`였다.

**다음 할 일(Claude, 결과를 받은 뒤)**
- 7장 표에 기록한다.
- SP4 위치 오차에 따라 게임 곡을 스트리밍으로 유지할지, 메모리 로드(CREATESAMPLE)로 바꿀지 정한다. 이전 녹음에서 seek 뒤 약 100ms 어긋남이 의심됐다.
- SP3 연속 읽기로 클록 계단과 S_max를 정하고, 드리프트 항이 필요한지 판단한다(이전 측정 -1.3ppm으로 불필요할 가능성이 높음).
- SP1·SP2 플레이어 결과로 B안과 ASIO 범위를 확정한다.
- 게이트를 모두 통과하면 S0으로 넘어간다.

---

## 1. 진행 방식

1. Claude가 코드를 쓴다.
2. 새 파일이나 asmdef가 생기면 사용자가 Unity에 포커스해 csproj를 다시 생성한다(처음 한 번은 Preferences → External Tools → Regenerate project files).
3. Claude는 새 파일이 해당 csproj의 Compile 목록에 있는지 확인한 뒤 `dotnet build`로 컴파일을 확인한다. 목록에 없으면 빌드 결과를 믿지 않는다.
4. 사용자가 에디터에서 카드대로 확인하고 결과를 붙여 넣는다.
   - 콘솔 오류가 있으면 오류 텍스트를 붙인다.
   - 스파이크 결과는 `persistentDataPath/audio_spike/audio_spike_summary.txt`를 붙인다. 한 줄 형식은 `SP#, PASS/FAIL, 기준값, 실측값, 출력 타입, 장치, 레이트, 버퍼, 빌드(Editor/Dev/Release), CharacterAnimator 로그 활성 여부`다.
   - CSV 파일은 경로만 알려 주면 된다.
5. Claude가 수정한다. 게이트가 실패하면 해당 PR은 머지하지 않는다(4장).

- PR마다 저장소 규칙대로 이슈를 먼저 만든다. 브랜치 접두사는 `refactor/`, `feature/` 등을 쓴다. 커밋과 PR은 사용자가 요청할 때만 만든다.
- **grep 규칙**(PR마다 확인)
  - `\bRuntimeManager\b`는 가드 파일과 `ChartEditor/**`에서만 나와야 한다.
  - 삼항 연산자는 0건이어야 한다.

**준비물**
- 출력 장치 두 개(예: 스피커와 헤드폰)
- ASIO 개발용 FlexASIO(무료, WASAPI 위에서 동작하는 ASIO 드라이버)
- 루프백 녹음용 Audacity(SP4, SP13)
- 실제 ASIO 장비나 ASIO4ALL은 SP8에서만 쓴다. 없으면 FlexASIO만으로 최소 매트릭스를 진행한다.

---

## 2. 단계 카드

형식: **내용**(Claude) / **에디터 작업**(사용자) / **확인** / **통과 기준** / **보고** / **롤백**

### S-1. 설계 문서 이관
- **내용**: 설계 원본을 이 문서와 `Audio_architecture.md` 두 개로 나누어 옮긴다. 원본에 있던 폐기 항목(부록)이 두 문서에 없는지 grep으로 확인한다.
- **에디터 작업**: 없음. Unity가 `.md.meta`를 만들면 함께 커밋한다.
- **확인**: 두 문서를 읽고 계획과 맞는지 검토한다.
- **통과 기준**: 사용자 승인
- **롤백**: 파일 삭제

### S-0.5. 최소 스파이크(버리는 코드)
- **내용**: `Testing/AudioSpike/`에 스파이크 하네스를 만든다. 파일 전체를 `#if SCO_AUDIO_HARNESS`로 감싼다. FMOD System을 직접 만들어 다음을 측정한다.
  - SP1: System 생성, init, release 반복
  - SP2: 메인 스레드 COM 아파트먼트, ASIO init과 장치 목록
  - SP3: 5분간 (QPC, DSP 클록) CSV. 버퍼 프리셋 256/480/512/1024 ×4, ASIO, 가능하면 96kHz 장치
  - SP4: setDelay 예약과 일시정지·재개, 스트림 위치 설정 정확도
  - SP11: `ctx.time`과 프레임 시각 차이 분포(60/144/무제한 fps)
  - SP15: 콜백 스레드 ID
  - 결과는 요약 파일과 CSV로 남긴다.
- **에디터 작업**(하네스 코드는 `SCO_AUDIO_HARNESS`가 켜져야 컴파일되므로 1이 먼저다)
  1. File → Build Profiles에서 Windows 프로필 두 개를 만든다. 두 프로필 모두 Scripting Defines에 `SCO_AUDIO_HARNESS`를 넣는다.
     - `AudioSpike-Dev`: Development Build ON, Script Debugging OFF
     - `AudioSpike-Release`: Development Build OFF
     - Build Profile에서 define을 넣기 어려우면 Project Settings → Player → Scripting Define Symbols에 넣어도 된다(측정이 끝나면 지운다).
  2. `AudioSpike-Dev` 프로필을 활성화(Switch Profile)하고 컴파일이 끝날 때까지 기다린다.
  3. 빈 씬 `Assets/Scenes/AudioSpikeScene.unity`를 만들고, 빈 GameObject에 `AudioSpikeHarness` 컴포넌트를 붙인다. 두 프로필의 씬 목록을 이 씬 하나로 둔다.
  4. FMOD Event Browser 창은 닫는다(에디터 미리듣기 System과의 충돌 방지).
  5. FlexASIO를 설치한다. SP4용으로 Audacity를 준비한다(Audio Setup → Host: Windows WASAPI, 녹음 장치: 재생 장치의 loopback).
- **확인**
  - 에디터에서 Play를 누르면 SP1 부팅 확인이 자동으로 한 줄 기록된다. Play/Exit를 20회 반복한다(에디터 SP1).
  - 화면 버튼을 순서대로 누른다.
    - SP1, SP2: 버튼만 누른다.
    - SP3: 시스템 켜기 → 시계 기록. 버퍼 256/480/512/1024 ×4와 ASIO에서 각각 기록한다. 한 번은 GC 부하를 켜고 기록한다.
    - SP4: 루프백 녹음을 시작한 뒤 버튼을 누른다.
    - SP11: FPS 60/144/무제한 각각에서 60초 동안 스페이스를 일정하게 누른다.
    - SP15: 버튼만 누른다.
  - 스크립트를 한 번 수정해 저장하고, Play 중 재컴파일이 크래시 없이 되는지 본다(5회).
  - 두 프로필로 빌드한 실행 파일에서도 SP1(실행·종료 5회), SP2, SP3(기본 버퍼), SP15를 실행한다.
- **통과 기준**: 3장의 SP1, SP2, SP3, SP4, SP11, SP15
- **보고**: 요약 파일, CSV 경로, 사용한 장치 목록
- **롤백**: 하네스 폴더, define, Build Profile, 씬 삭제
- **이 단계의 결과로 정하는 것**
  - (B)안(Core System 직접 소유) 유지 여부
  - 곡 시계 모델의 복잡도(드리프트 항, 평활 시계 필요 여부)
  - S_max 값
  - WASAPI 버퍼 프리셋 초안

### S0. 어셈블리와 순수 코어
- **내용**
  - asmdef를 만든다: `SCOdyssey.Core`(Core 폴더), `SCOdyssey.Audio`, `SCOdyssey.Game.Timing`, 테스트 asmdef 두 개
  - `Core/Qpc.cs`, Audio 루트 계약 파일, `Audio/Clock`(S-0.5 결과로 확정한 모델)
  - Game/Timing 순수 부분: LaneInputEvent, JudgementTimeline, JudgementPump, IJudgementClient, TimingSample
  - EditMode 테스트, 그리고 `Assets/Scripts/Editor/Tests/`(asmdef 없음) 폴더 준비
  - `.slnx`를 저장소에 둘지 `.gitignore`에 넣을지 정한다.
- **에디터 작업**: 없음. 컴파일 뒤 Test Runner(EditMode)를 실행한다.
- **확인**: 콘솔 오류 0건, 기존 게임 플레이(로비, 곡 한 판), ChartEditorScene 열기
- **통과 기준**: 전체 컴파일, Test Runner에서 새 테스트가 보이고 통과
- **보고**: Test Runner 결과, 콘솔 오류
- **롤백**: revert. Core asmdef만 따로 되돌릴 수 있다.

### S1. 엔진
- **내용**
  - Engine: 부팅 시도와 폴백, 검증, 콜백 마스크, FMOD Debug 전달, 에디터 정리 훅, Shutdown
  - Mixer, OneShotBank
  - Hosting: Installer, Runner(-1010), FocusPolicy, RuntimeManager 가드
  - 오디오 오버레이
  - **모듈 하네스**: S-0.5 코드를 지우고, 새 모듈을 단독으로 설치해 시험하는 하네스로 바꾼다.
- **에디터 작업**: AudioSpikeScene의 하네스 컴포넌트를 모듈 하네스로 교체한다.
- **확인**: 하네스로 설치 → 원샷 재생 → 플레이 종료를 20회, 재컴파일 5회, 두 프로필 빌드 실행·종료 5회
- **통과 기준**: SP1 재확인
- **보고**: 요약 파일
- **롤백**: revert

### S2a. 곡 재생
- **내용**: StreamLoader, 로비·프리뷰 음악 재생기, SongPlayer와 곡 세션(상태 머신, 앵커 커밋, 일시정지·재개, 재구성 중 처리, 종료 감지). 하네스에 곡 재생, 메트로놈 비교, 강제 재구성 메뉴를 추가한다.
- **에디터 작업**: 없음
- **확인**
  - 하네스에서 클릭 트랙 곡과 메트로놈을 함께 재생하고, 일시정지·재개를 20회 한다(리드인 중 포함).
  - Audacity로 스테레오 믹스를 루프백 녹음해 두 소리의 시작 차를 잰다.
  - 상태별(Ready, Starting, Paused)로 강제 재구성을 실행한다.
- **통과 기준**: SP4
- **보고**: 요약 파일, 녹음 측정값
- **롤백**: revert

### S2b. 출력 관리
- **내용**: DeviceCatalog, ApplyAsync(close→init, 적용 폴백, 요청 구성 성공 시에만 저장 신호), 콜백 처리(Follow-Default, Pinned 1초 디바운스, DEVICELOST), 콜백 없는 정지 감지
- **에디터 작업**: 없음
- **확인**
  - 하네스에서 WASAPI ↔ ASIO(FlexASIO) 전환, 버퍼 변경, 음악을 재생하는 중에 적용 50회
  - USB 장치 분리·재연결, Windows 사운드 설정에서 기본 장치·형식 변경, 절전 복귀
- **통과 기준**: SP6, SP8, SP9, SP10
- **보고**: 요약 파일, 사용한 드라이버 목록
- **롤백**: revert

### S3. 판정 타이밍
- **내용**: UnityInputSystemTimestampSource, RealtimeQpcMapper, JudgementDriver(-900), GameplayTimingBinding, 판정 로그, 타이밍 오버레이. 하네스에 탭 테스트와 Synthetic 확인 메뉴를 추가한다.
- **에디터 작업**: 없음
- **확인**
  - 하네스 탭 테스트를 60/144/무제한 fps에서 각 1분 한다.
  - 레인을 누른 채 입력 맵을 끄거나 alt-tab 한다.
- **통과 기준**: SP11, SP-IN(하네스)
- **보고**: 요약 파일
- **롤백**: revert

### S4a. 게임에 설치 (GM/CM 편집 없음)
- **내용**
  - `Managers.InitServices`에 설치를 넣는다. try/catch로 감싸고, 실패하면 no-op 모듈과 어댑터를 등록한다.
  - `ILegacyTransport`와 `App/LegacyAudioManagerAdapter`(IAudioManager 전 멤버)
    - 엔진이 Failed면 `IsLoaded`는 즉시 true, `GetDSPTime`은 QPC로 진행한다.
    - 장치 캐시가 비었으면 동기로 열거한다.
    - 범위 밖 `SetAudioDevice`는 무시한다.
  - `AudioSettingsMapper` v1: `audioBufferIndex`를 버퍼 길이로 바꾼다. 장치는 Follow-Default로 둔다.
  - JudgementDriver와 판정 로그를 등록한다.
  - **InputManager**: 입력 소스를 소유하고, 레인 콜백에서 Push한다. `SwitchToUI`/`Disable`에 Synthetic 구간을 둔다. 기존 이벤트는 C까지 유지한다.
  - 로비 포커스 음소거(`Func<bool>` playInBackground)
  - FMODAudioManager는 등록만 끊고 파일은 남긴다.
- **에디터 작업**: 없음
- **확인**: 로비 BGM, 프리뷰, 곡 한 판(타격음, 일시정지·재개), 설정에서 장치 선택, 로비에서 alt-tab 음소거, 재시작 후 버퍼 로그가 설정 인덱스와 맞는지
- **통과 기준**: 기존과 같은 동작
- **보고**: 콘솔 로그(엔진 부팅 줄)
- **롤백**: revert

### S4b. 옛 경로 제거
- **내용**: `FMODAudioPreInit.cs`와 `FMODAudioManager.cs`를 삭제한다. `Plugins/FMOD/src/Platform.cs`는 :871-872와 :874-878만 되돌린다(:873 getter는 원본이므로 유지). RuntimeManager 가드를 켠다.
- **에디터 작업**(같은 PR에 포함)
  1. MainScene 카메라에서 `StudioListener` 컴포넌트를 제거한다.
  2. GameScene에서 "Audio Source" GameObject와 카메라의 `AudioListener`를 제거한다.
  3. FMOD Settings → Play In Editor → DSP Buffer Count를 4로 둔다(ChartEditor가 256×4를 유지하게).
- **확인**: 게임 전체 흐름, **ChartEditorScene 동등**(음원 로드, 재생, 자동 채보), 릴리스 스모크 빌드(5장)
- **통과 기준**: 위 확인 모두 정상, 콘솔에 RuntimeManager 초기화 경고 없음
- **보고**: 스모크 빌드 결과, 콘솔 로그
- **롤백**: revert(씬과 에셋 YAML이 같은 PR에 있어야 원상 복구된다)

### S5a. 설정 v2
- **내용**
  - SettingsData v2 필드를 추가한다. v1 필드(`audioDeviceIndex`, `audioBufferIndex`)는 `[Obsolete]`로 남긴다.
  - `SettingsMigration`: `.v1.bak` 백업, 손상 값 검증, ResetToDefault 처리
  - `SettingsManager.Apply` → `IAudioMixer` 볼륨, `Application.runInBackground = playInBackground`
  - 부팅 시 v2 값으로 출력 구성
  - `Assets/Scripts/Editor/Tests/`에 마이그레이션·매퍼 테스트를 추가한다.
- **에디터 작업**: 착수 전에 PlayerPrefs를 백업한다(레지스트리 `HKCU\Software\DefaultCompany\sangcheol-odyssey-unity` 내보내기. 에디터는 `Unity\UnityEditor\DefaultCompany\…`).
- **확인**: 기존 v1 설정으로 부팅해 볼륨, 버퍼, 노트 싱크, 판정 싱크가 유지되는지 확인한다. 로비에서 playInBackground ON/OFF로 alt-tab 한다.
- **통과 기준**: 마이그레이션 테스트 통과, SP14(로비)
- **보고**: Test Runner 결과, 확인 결과
- **롤백**: revert 후 백업한 PlayerPrefs를 복원한다(또는 `SCOdyssey.Settings.v1.bak` 값을 원래 키에 넣는다).

### S5b. 사운드 설정 화면
- **내용**
  - SoundSettingUI: 출력 타입 선택, GUID 장치 목록(비동기, "검색 중" 표시), 버퍼(WASAPI/ASIO별 프리셋과 ms 표시, ASIO 안내), 실제 적용값 상태 줄, `_applying` 가드
  - 적용 결과 처리, v1 필드 삭제
  - MainUI에서 부팅 폴백 경고 1회
  - 빌드 검증기(`Assets/Scripts/Editor/AudioBuildValidator.cs`)
  - 새 문자열은 `App/AudioUiText`에 모은다.
- **에디터 작업**
  1. SoundSettingUI 프리팹에 `Btn_OutputTypePrev`, `Btn_OutputTypeNext`, `Text_OutputTypeValue`, `Text_AudioStatusValue`를 추가한다(`BaseUI.Bind`의 enum 이름과 같게, 기존 장치 선택 줄과 같은 형태로).
  2. Player Settings → Resolution and Presentation → Force Single Instance를 켠다.
- **확인**
  - 출력 타입, 장치, 버퍼를 각각 바꾸고 Save를 누른다. 설정을 닫은 뒤 로비 BGM, 프리뷰, 곡 한 판, 타격음이 정상인지 본다.
  - 재시작 후 장치가 복원되는지 본다.
  - Save를 빠르게 두 번 누른다.
  - 로비·프리뷰 재생 중 Pinned 장치를 분리한다.
- **통과 기준**: 위 확인 정상, SP13(버퍼 프리셋 확정)
- **보고**: 확인 결과, SP13 측정값
- **롤백**: revert(프리팹 변경은 같은 PR)

### S6. 로비와 곡 선택
- **내용**: MainUI, AdventureUI를 `IMusicPlayers`로 전환한다(TryGet, 요청 취소, 프리뷰 디바운스). 음원이 없으면 AdventureUI에 안내를 띄운다.
- **에디터 작업**: 없음
- **확인**: 곡 목록을 빠르게 넘겨도 프리뷰가 하나만 나오는지, 로비↔곡 선택↔게임 전환, 설정 화면 왕복
- **통과 기준**: 위 확인 정상
- **롤백**: revert

### S7. 타임라인과 BGA
- **내용**: TimelineController와 BGAController가 곡 시계를 쓰게 한다.
  - TimelineController는 `timeProvider`가 null일 때만 늦게 `TryGet`한다(Timeline.prefab을 ChartEditorScene과 공유).
  - 세션이 없으면 레거시 경로를 쓴다(I1 전까지).
  - BGAController는 OnDestroy에서 구독을 해제한다.
- **에디터 작업**: 없음
- **확인**: 게임 동작이 이전과 같은지(아직 레거시 경로), **ChartEditorScene 동등**
- **통과 기준**: 위 확인 정상. 새 경로 검증은 I1에서 한다.
- **롤백**: revert

### I1. GameManager 통합
- **내용**: 6장의 G1~G12와 GameDataLoader(`ISongPlayer.LoadAsync`, 실패하면 로비로 돌아가 안내). 곡 시각만 넘긴다.
- **선행 확인**: Illustar 머지 여부. 머지되었으면 그 코드 기준으로 통합 지점을 다시 대조한다.
- **에디터 작업**: 없음
- **확인**
  - 곡 한 판: 시작 싱크, 리드인 중 일시정지·재개, 카운트다운 중 alt-tab, 로딩 중 alt-tab, ESC와 같은 프레임 입력
  - 곡 도중 USB 분리 → 자동 일시정지 → Pause UI에서 재개
  - 음원이 끝난 뒤 ESC, 무음 곡 일시정지
  - BGA와 판정선이 곡 시계를 따르는지(늦은 Prepare 포함)
- **통과 기준**: SP4, SP10, SP14(게임), SP-IN(게임), SP13(b)
- **보고**: 요약 파일, 노트 싱크 체감 차이 측정값(SP3/SP4)
- **롤백**: revert(I2가 들어간 뒤에는 I2를 먼저 revert)

### I2. ChartManager 통합
- **내용**: 6장의 C1~C9와 G13·G14. 판정 규칙은 바꾸지 않는다. 의도된 변경은 두 가지다: miss 컷오프에 판정 싱크 적용, ESC로 생긴 합성 release 미판정.
- **에디터 작업**: 없음
- **확인**
  - 판정 싱크를 -20, 0, +20으로 바꿔 가며 곡 한 판씩 한다(miss 포함 창이 같이 움직이는지).
  - 홀드, 릴리즈, miss 동작이 I1과 같은지
  - HoldRelease 창 안에서 ESC를 누르면 miss가 되는지(의도된 변경)
  - 타격음 등급별 재생
- **통과 기준**: SP11, 위 확인 정상
- **롤백**: revert

### C. 정리
- **내용**
  - `IAudioManager`, 어댑터, `ILegacyTransport`, `AudioOutputType`/`AudioOutputConfig`/`AudioBus`를 삭제한다.
  - `IInputManager.OnLanePressed/OnLaneReleased/SetTimeSyncPoint`, `BGAController.SchedulePlay/Pause/Resume`, 레거시 경로를 삭제한다.
  - GameSceneTester의 IAudioManager 블록을 삭제한다.
  - 이 문서를 삭제한다.
- **에디터 작업**: GameSceneTester 파일 자체를 지우면 GameScene에서 해당 컴포넌트를 제거한다.
- **확인**: 전체 회귀(5장 수동 시나리오), 개발·릴리스 빌드
- **통과 기준**: R11 grep 0건(GameManager, ChartManager와 그 후속 클래스), 전체 회귀 정상
- **롤백**: 병행 리팩터와 충돌하면 C-a(어댑터만)와 C-b(나머지)로 나누거나 `[Obsolete]` shim을 남긴다.

---

## 3. 스파이크 정의

| 스파이크 | 게이트 | 측정 | 통과 기준(초안, 결과에 따라 조정) |
|---|---|---|---|
| SP1 수명주기 | S-0.5, S1 | 에디터 Play/Exit 20회(WASAPI, ASIO), 재컴파일 5회, 플레이어 실행·종료 5회 | 매번 init OK. `ERR_OUTPUT_ALLOCATED` 0회. 리로드 뒤 콜백 크래시 0회. RuntimeManager 미초기화. 생성·해제 20회 뒤에도 생성 성공. 메모리 추세 안정 |
| SP2 STA·ASIO | S-0.5 | 에디터와 플레이어에서 아파트먼트 기록, ASIO init·목록·close→init 10회 | STA이거나, STA가 아니어도 ASIO 동작 이상 없음 |
| SP3 시계 품질 | S-0.5 | 프리셋별 5분 CSV, ASIO, 96kHz | 계단 폭과 드리프트(ppm) 수치 확보. 클램프만으로 잔차 p99 ≤ 1블록. 기본 버퍼에서 언더런 0 |
| SP4 예약·seek | S-0.5, S2a, I1 | 루프백 녹음으로 곡 클릭과 메트로놈 시작 차 측정, 일시정지·재개 20회(리드인 포함), seek 준비 시간 | 평균 ≤ 1ms, 표준편차 ≤ 0.5ms. seek 준비 < 1초 |
| SP6 재구성 반복 | S2b | close→init 50회, 음악 재생 중 적용 50회 | 크래시 0. 1회 < 1초. 원샷·음악·어댑터 슬롯 복원 |
| SP8 ASIO 드라이버 | S2b | FlexASIO(필수), ASIO4ALL·실제 장비(가능하면). 블록 불일치, 44.1k/48k, 다른 앱 점유, 포커스 복귀 | 실패가 모두 명확한 결과 코드로 드러나고, 폴백이 들리는 WASAPI로 끝남 |
| SP9 장치 목록 | S2b | 타입별 임시 System 열거, 메인 출력 글리치(루프백), 소요 시간 | 메인 출력에 영향 없음. 실패한 드라이버는 격리 |
| SP10 핫플러그 | S2b, I1 | USB 분리·재연결, HDMI 모니터 스피커(해상도 변경, 전체 화면 전환), Windows 형식 변경, 절전 복귀, 블루투스 | 두 모드 모두 사용자 개입 없이 들리는 상태로 복귀. 게임 중이면 자동 일시정지. 전환 전후 DSP 클록 단조 증가 |
| SP11 입력 양자화 | S-0.5, S3, I2 | 60/144/무제한 fps에서 `ctx.time − 프레임 시각` 분포, 매퍼 오프셋 30분 변동 | 분포 기록. 매퍼 변동 < 0.1ms |
| SP-IN 입력 경계 | S3(하네스), I1(게임) | 입력 맵 비활성화의 cancel, 홀드 중 alt-tab(runInBackground ON/OFF) | Synthetic으로 표시되고 오판정 0 |
| SP13 버퍼·종단 지연 | S5b, I1(b) | 프리셋별 키 입력 → 타격음 루프백 지연. (b) 타격음 재생 시점(레거시 PreUpdate 콜백 대 JudgementDriver -900) 비교 | 수치 기록, WASAPI 프리셋 확정. 블루투스 지연 기록 |
| SP14 포커스 | S5a(로비), I1(게임) | 전체 화면, 테두리 없는 창, 창 모드에서 alt-tab | 게임은 자동 일시정지, 로비는 설정대로 음소거 또는 유지. 복귀 뒤 판정 편향 변화 < 1ms. ASIO 재획득 |
| SP15 콜백 | S-0.5 | 콜백 스레드 ID, GC 부하 중 콜백 켬/끔 비교 | 믹서 스레드 콜백 없이 오디오 끊김 0 |

## 4. 게이트 실패 시 조치

| 스파이크 | 실패하면 | 사용자 결정 |
|---|---|---|
| SP1 | 해제 경로를 보강하고 재측정한다. 계속 실패하면 A안(RuntimeManager 유지, 출력 변경은 재시작 후 적용)을 제안한다. | **예** |
| SP2 | ASIO를 끈 채 출시하거나 ASIO를 별도 과제로 분리하는 안을 제안한다(스레드는 만들지 않는다). | **예** |
| SP3 | 드리프트 항, 평활 렌더 시계를 추가한다. | 아니오 |
| SP4 | 게임 곡만 CREATESAMPLE로 바꾼다. 스트림 굶주림이 재현되면 1초 위치 검사와 재예약을 넣는다. | 아니오 |
| SP6 | 같은 System close→init 대신 release→새 System으로 바꾼다. | 아니오 |
| SP8 | 문제 드라이버를 안내한다. 크래시가 재현되면 크래시한 구성을 기억하는 기능을 추가한다. | 아니오 |
| SP9 | 목록은 재구성 시점에만 만든다. | 아니오 |
| SP10 | 감시를 보강한다(콜백 없는 정지는 0.5초 검사로 처리). | 아니오 |
| SP11 | 높은 FPS를 권장한다. Raw Input 도입 시점을 정한다. | **예** |
| SP-IN | 태깅 조건을 보강한다. | 아니오 |
| SP13 | 기본 버퍼를 조정한다. v1의 64·128이 실제로 적용되고 있었다면 256으로 올릴 때의 체감 변화를 결정해야 한다. 블루투스 지연이 ±200ms를 넘으면 노트 싱크 범위 확장을 결정해야 한다. | **예** |
| SP14 | 포커스 정책을 보강한다. | 아니오 |
| SP15 | FMOD Debug 로그를 FILE 모드로만 남긴다. | 아니오 |

**사용자 결정이 필요한 경우 수치와 선택지를 보고하고 멈춘다.** 결과는 7장에 기록한다.

## 5. 수동 시나리오와 릴리스 스모크

**수동 시나리오**(단계 카드에서 지정한 것을 수행하고, I2 이후에는 전부)
1. 로비 BGM 재생, 설정 화면 왕복 뒤 재생
2. 곡 목록을 빠르게 넘길 때 프리뷰가 하나만 나오는지
3. 곡 한 판: 시작 싱크, 타격음, 판정, 결과 화면
4. 리드인 중 일시정지·재개
5. ESC와 같은 프레임 레인 입력
6. 카운트다운 중 alt-tab
7. 로딩 중 alt-tab
8. 곡 도중 USB 장치 분리 → 자동 일시정지 → 재개
9. 로비·프리뷰 재생 중 Pinned 장치 분리
10. 음원이 끝난 뒤 ESC
11. 음원 없는 곡(무음 세션) 일시정지
12. 출력 타입, 장치, 버퍼 변경 뒤 설정 닫기, 로비 BGM 복원
13. Save 빠르게 두 번 누르기
14. 재시작 후 설정 복원
15. HoldRelease 창 안에서 ESC(miss가 되어야 함)
16. 판정 싱크 -20/0/+20
17. PauseUI 나가기, 리트라이, 결과 화면 나가기
18. ChartEditorScene 동등(음원 로드, 재생, 자동 채보)
19. `-sco-audio-safe` 인자로 실행

**릴리스 스모크 빌드**
1. Development OFF로 빌드한다.
2. Editor.log에서 `fmodstudio.dll`이 선택되고 `fmodstudioL.dll`은 제외됐는지 확인한다.
3. Localization·Addressables가 빌드에 포함됐는지 확인한다.
4. 공백과 한글이 들어간 경로(예: `C:\게임 테스트\`)에 복사해 실행한다.
5. `_Data/StreamingAssets/Music`과 `HitSound`의 파일 수를 확인한다.
6. Player.log에 오류가 없는지 확인한다.

---

## 6. GameManager/ChartManager 통합 지점

심볼로 가리키고 코드에 `// [AUDIO-IP:G#]`, `// [AUDIO-IP:C#]`를 단다. 구조를 바꾸거나 새 public API를 만들지 않는다.

**I1: GameManager.cs만 편집. 곡 시각만 넘긴다.**

| # | 위치(심볼) | 변경 |
|---|---|---|
| G1 | 필드, `Awake` | `ISongPlayer`, 세션, `GameplayTimingBinding`을 얻는다. |
| G2 | 레인 입력 구독, `OnDestroy` | 구독을 삭제한다. OnDestroy에서 binding을 Dispose한다(SwitchToUI보다 먼저). |
| G3 | `StartGame` | Attach(`() => IsGameRunning`). `globalStartTime = _audioManager.GetDSPTime();`와 `SetTimeSyncPoint(...)` 호출을 삭제한다. |
| G4 | `StartMusic` | 본문을 `_session.Start(leadIn, audioOffsetMs)`로 바꾸고 `bgaController?.SchedulePlay`를 삭제한다. |
| G5 | `GetCurrentTime` | `_session.Clock.Frame.SongTime`을 돌려준다(게임 진행 중이 아니면 0). |
| G6 | `Update` | SyncTime 호출을 삭제한다(JudgementDriver가 Advance로 대신한다). |
| G7 | `Pause` | `_session.Pause(PauseReason.User)`를 SwitchToUI보다 먼저 부르고, `bgaController?.Pause`를 삭제한다. |
| G8 | `Resume` | 카운트다운 뒤 `_session.Resume()`을 부르고, `bgaController?.Resume`을 삭제한다. |
| G9 | 새 private 메서드 | binding 콜백 세 개: 진행(`chartManager.SyncTime(songTime)`), 입력(`TryJudgeInput/TryJudgeRelease`에 곡 시각 전달), 비사용자 일시정지 |
| G10 | `IsAudioPlaying` | `!_session.IsAudioFinished` |
| G11 | `OnGameFinished` | `_session.Stop()` |
| G12 | 새 private 메서드 | `HandleNonUserPause() { if (IsGameRunning && !IsPaused) Pause(); }` |

**I2: ChartManager.cs + G13·G14(한 PR)**

| # | 위치(심볼) | 변경 |
|---|---|---|
| C1 | 모든 `_judgmentOffsetSec` 사용처 | 필드를 삭제하고, 전달받은 판정 시각(judgeTime)을 쓴다. |
| C2 | 필드, `Init` | `IOneShotPlayer`, 세션, `IJudgementTimingLog`를 TryGet한다. 타격음 등록은 `Register`로 한다. |
| C3 | `Init` 끝의 `gameManager.StartMusic(barDuration)` | `_session.Start(barDuration, audioOffsetMs)` 흐름으로 바꾼다(노트 싱크는 App이 넘긴 값). |
| C4 | `SyncTime` | `SyncTime(double songTime, double judgeTime)`: 마디 진행은 songTime, miss와 홀드는 judgeTime으로 비교한다(규칙은 그대로). |
| C5 | `CheckHoldingBody` | 현재 시각을 다시 읽지 않고 judgeTime을 인자로 받는다. |
| C6 | `TryJudgeRelease` | `bool applyJudgement = true` 매개변수를 추가한다. false면 홀드 상태만 해제한다. 메서드 안 지역 변수 `JudgeType judge`와 이름이 겹치지 않게 한다. |
| C7 | `CheckGameClear` | 채보 소진 && `IsAudioFinished` |
| C8 | `PlayHitSound` | `IOneShotPlayer.Play` |
| C9 | 판정 확정 지점 네 곳 | `Record(TimingKind.Press/HoldBody/Release/Miss, (int)grade, errorMs)` |
| G13 | G9의 진행 콜백 | `SyncTime(songTime, judgeTime)` |
| G14 | G9의 입력 콜백 | 판정 시각을 넘기고, `applyJudgement: e.Judgeable`을 넘긴다. |

**재적용 규칙**(GM/CM 리팩터가 끝난 뒤 새 클래스에 같은 계약을 다시 적용한다)

| 규칙 | 책임 | 계약 |
|---|---|---|
| R1 | 곡 시작 | `ISongSession.Start(leadIn, audioOffsetMs)`를 한 번만 부른다. 오프셋을 직접 계산하지 않는다. |
| R2 | 시간 진행 | Advance의 songTime으로 마디를, judgeTime으로 miss와 홀드를 처리한다. |
| R3 | 입력 판정 | 입력의 judgeTime만 비교에 쓴다. `Judgeable = false`인 release는 상태만 해제한다. |
| R4 | binding 수명 | 채보 초기화 전에 `Attach(session, isRunning: 게임 진행 중)`, 끝나면 Dispose. 일시정지 여부로 거르지 않는다. |
| R5 | 일시정지 UI | `Pause(User)`를 SwitchToUI보다 먼저 부른다. 카운트다운 뒤 `Resume()`. 비사용자 일시정지 알림을 받으면 Pause UI를 띄운다. |
| R6 | 비주얼 | 판정선과 BGA는 `Frame.SongTime`을 쓴다. |
| R7 | 종료 | 채보 소진 && `IsAudioFinished`이면 종료하고 `Stop()`을 부른다. 음원이 끝난 뒤에도 일시정지할 수 있다. |
| R8 | 타격음 | 로드 때 `Register`, 입력 때 `Play` |
| R9 | 판정 기록 | 모든 판정 확정 지점에서 `Record`(Miss 포함) |
| R10 | 금지 | 오프셋 직접 읽기, FMOD·DSP 직접 사용, `Time.*`로 곡 시각 계산 |
| R11 | C 단계에서 삭제되는 심볼 | `IAudioManager`, `ILegacyTransport`, `AudioOutputType`, `AudioOutputConfig`, `AudioBus`, `IInputManager.OnLanePressed/OnLaneReleased/SetTimeSyncPoint`, `BGAController.SchedulePlay/Pause/Resume`를 쓰지 않는다. |

---

## 7. 스파이크 결과

| 날짜 | 스파이크 | 빌드 | 장치·버퍼 | 결과 | 비고·결정 |
|---|---|---|---|---|---|
| 2026-09-27 | SP1 | Editor | WASAPI Realtek 48k 256x4 | PASS | Play/Exit 23회, 생성·해제 60회 실패 0, FMOD 메모리 변화 32KB, RuntimeManager 미초기화. init 45~106ms. 플레이어 빌드는 미실시 |
| 2026-09-27 | SP2 | – | – | 미실시 | 요약에 기록 없음. SP3 ASIO 기록에서 ASIO4ALL(44.1k, 256x2) init은 성공 |
| 2026-09-27 | SP3 | Editor | WASAPI 256x4 / ASIO4ALL 256x2 | 무효(재측정) | 에디터 fps 6~7이라 계단 폭 대신 프레임 간격을 잼. WASAPI 5분 드리프트 -1.3ppm(3분 곡에서 약 0.2ms, 드리프트 보정 불필요 가능성 높음). ASIO4ALL에서 클록이 60초 멈춘 구간 있음(원인 미확인). 하네스에 fps와 무관한 연속 읽기를 추가함 |
| 2026-09-27 | SP4 | Editor | WASAPI 256x4 | 무효(재측정) | 하네스 버그: 스트림 상태 PLAYING을 준비 완료로 보지 않아 매번 3초 대기(수정함). 루프백 녹음은 Windows 오디오 향상 때문에 좌우 채널이 섞임. 그래도 곡 도중 재개(seek 있음) 뒤 곡과 메트로놈이 약 100ms 어긋나는 패턴이 보임 → 채널 위치 비교 측정을 추가해 재확인 |
| 2026-09-27 | SP11 | Editor | – | 부분 | fps 6~7이라 fps 비교는 무효. `ctx.time`은 fps와 관계없이 콜백 약 8ms 전에 찍힘 → 누른 시각이 아니라 Unity 처리 시점(프레임 양자화 확인). 매퍼 오프셋 변동 0.001ms(단순 오프셋 변환 채택 가능) |
| 2026-09-27 | SP15 | Editor | WASAPI 256x4 | PASS | 동기 오류 콜백 1건, 메인 스레드. 비동기 오류 콜백은 오지 않음. Debug 콜백 0건. 크래시 없음 |

## 8. 후속 백로그 (이번 범위 밖)

- **GM/CM 리팩터 쪽**
  - 판정 방식 개선(입력과 시간 진행을 완전히 교차해 처리, 구간 교차 홀드, 한 프레임 다중 miss)
  - CharacterAnimator 입력별 로그 정리(Illustar 머지 뒤)
  - GameSceneTester 정리
- **오디오 후속**: Raw Input 입력 소스, 캘리브레이션 씬(곡 시계 기준 원샷 예약을 공개 API로), ASIO 출력 채널 선택, 곡 Sound 캐시(재시작 빠르게)
- **사용자 결정 후보**
  - 블루투스 지연이 ±200ms를 넘을 때 노트 싱크 범위 확장
  - 기본 FPS 60 상향
  - 출시 전 companyName/productName 확정(PlayerPrefs 경로가 이 값에 묶여 있음)
- **출시 전**: FMOD 크레딧 표기(라이선스 요구), ASIO 상표 표기 확인
- **음원 규약**: mp3는 쓰지 않는다(인코더 지연). 루프 wav의 `smpl` 청크를 확인한다.
