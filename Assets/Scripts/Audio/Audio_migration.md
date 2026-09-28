# 오디오 재설계 이행 문서

기존 `FMODAudioManager`에서 새 오디오·타이밍 레이어(`Audio_architecture.md`)로 옮기는 과정의 단계, 스파이크, 통합 절차를 적은 문서다. **C 단계가 끝나면 이 문서는 삭제한다.**

- **브랜치**(2026-09-28 결정). FMOD 작업은 develop과 Illustar 둘 다에 들어가야 하고, GameManager·ChartManager 리팩터는 develop에서 분기한 브랜치에서 진행 중이며 Illustar에는 들어가지 않는다.
  - `refactor-FMOD`: 공통 부분(S-1~S6, develop 3e859a0 기준). develop의 GameManager·ChartManager 리팩터가 끝나 develop에 들어가면 develop을 병합하고, 그 코드 기준으로 I1 → I2 → C를 진행한다.
  - `refactor-FMOD-Illustar`: `refactor-FMOD`(S6 + 브랜치 문서, fe0411a)에서 분기해 `Illustar`(d309db0)를 병합한 브랜치(95b5003, 충돌 없음). Illustar의 GameManager·ChartManager 기준으로 I1 → I2 → C를 진행한다.
  - 두 브랜치에서 같은 파일(BGAController, GameDataLoader, 모듈 쪽 수정 등)은 한쪽에서 커밋한 뒤 cherry-pick한다. 6장 통합 지점 표는 develop 3e859a0 기준이므로 각 브랜치에서 시작할 때 다시 대조한다.
- 각 단계가 끝나면 게임을 처음부터 끝까지 플레이할 수 있어야 한다.

---

## 0. 인수인계 (2026-09-27 기준. 다른 PC나 새 세션에서 이어서 할 때 먼저 읽는다)

**새 Claude 세션에 줄 첫 요청 예시**: "`Assets/Scripts/Audio/Audio_migration.md`의 0장(인수인계)을 읽고 S-0.5를 이어서 진행해."

**근거 자료**: `Docs/audio-redesign/`
- `01` 기존 FMOD 사용 현황과 이슈
- `02` 결정 브리프: 검증된 FMOD·Unity 사실과 출처
- `03` 설계 원본(개정 2)
- `04` 승인된 계획
- 구현 기준은 이 문서와 `Audio_architecture.md`가 우선한다.

**현재 단계**
- S-1(설계 문서 이관): 완료
- S-0.5(버리는 최소 스파이크): **완료**(2026-09-27). 게이트 SP1, SP2, SP3, SP4, SP11, SP15 모두 PASS
  - 하네스 코드: S1에서 `Assets/Scripts/Testing/AudioHarness/`(모듈 하네스)로 교체했다. 파일 전체가 `#if SCO_AUDIO_HARNESS`로 감싸져 있다.
  - 씬 `Assets/Scenes/AudioSpikeScene.unity`, Build Profile `Assets/Settings/Build Profiles/AudioSpike-{Dev,Release}.asset`. 두 프로필 모두 define `SCO_AUDIO_HARNESS`가 들어 있고, 자체 씬 목록에는 스파이크 씬만 있다. 전체 씬 목록(EditorBuildSettings)은 건드리지 않는다.
- S0(어셈블리와 순수 코어): **완료**(2026-09-27). 콘솔 오류 0, Test Runner EditMode 39개 통과, 로비·곡 한 판·ChartEditorScene·하네스 컴파일 정상
  - asmdef: `Core/SCOdyssey.Core`, `Audio/SCOdyssey.Audio`, `Game/Timing/SCOdyssey.Game.Timing`, 테스트 `Audio/Tests/Editor`, `Game/Timing/Tests/Editor`
  - `Core/Qpc.cs`, Audio 루트 계약 11개 파일, `Audio/Clock/{DspQpcModel, SongTimeline, SongClock, SongAnchor}`
  - Game/Timing 루트 계약(LaneInputEvent, IInputTimestampSource, JudgedInput, IJudgementClient, TimingSample, IJudgementTimingLog), `Judgement/{JudgementTimeline, JudgementPump}`
  - EditMode 테스트 39개. 임시 csproj로 5개 어셈블리 컴파일(오류·경고 0)과 참조 경계를 확인했고, 리플렉션 실행기로 38개 통과(`LogAssert`를 쓰는 1개는 Test Runner에서만 실행)
  - `.slnx`는 `.gitignore`에 넣고 추적을 끊었다(`.sln`, `.csproj`와 같은 생성 파일)
  - `Assets/Scripts/Editor/Tests/` 폴더는 빈 폴더라 git에 남지 않으므로 첫 App 테스트를 넣는 S5a에서 만든다
  - 곡 시계 창을 1초에서 10초로 바꾸고 하향 계단 규칙을 넣었다(7장 결정 아래 참고)
- S1(엔진): **완료**(2026-09-27). SP1 재확인 PASS(7장)
  - `Audio/Engine/{AudioEngine, EngineConfigurator, BootPlan, SystemCallbackHub, FmodDebugBridge, AsioPolicy, AudioThread}`, `Audio/Output/DriverLookup`, `Audio/Mixing/FmodMixer`, `Audio/Playback/OneShotBank`, `Audio/Hosting/{AudioModule, AudioModuleInstaller, AudioModuleOptions, AudioEngineRunner, FocusPolicy, RuntimeManagerGuard, EditorAudioLifecycle}`, `Audio/Diagnostics/AudioOverlay`
  - 모듈 하네스: `Testing/AudioSpike/`를 `Testing/AudioHarness/`로 옮기고 S-0.5 코드를 지웠다. 하네스 컴포넌트는 `.meta`를 함께 옮겨 GUID를 유지했으므로 씬에서 컴포넌트를 바꿀 필요가 없다. 결과는 `persistentDataPath/audio_harness/audio_harness_summary.txt`
  - 임시 csproj로 에디터·플레이어·define 없음 세 조건 컴파일(오류·경고 0), 테스트 42개 통과(BootPlan 4개 추가, `LogAssert` 1개는 Test Runner 전용)
- S2a(곡 재생): **완료**(2026-09-27). SP4 PASS(녹음 1샘플 이내), 재구성·프리뷰 PASS(7장)
  - `Audio/Clock/ClockSampler`, `Audio/Playback/{StreamLoader, FmodSongSession, SongPlayer, FmodMusicPlayer}`, `Audio/Diagnostics/SongMetronome`, 엔진 재구성(`AudioEngine.Reinitialize`, `AudioModule.Reinitialize`)
  - 하네스: `Testing/AudioHarness/HarnessSongChecks`(곡 재생, SP4, 상태별 강제 재구성, 프리뷰 대체)
  - 테스트 50개(세션 상태 머신 7개 추가). 임시 csproj로 세 조건 컴파일 오류·경고 0, 49개 통과(`LogAssert` 1개는 Test Runner 전용)
- S2b(출력 관리): **완료**(2026-09-27). SP6·SP9·SP10 PASS, SP8 부분(ASIO4ALL만)
  - `Audio/Output/{DeviceCatalog, AudioOutputService}`, 엔진 재구성 일반화(`AudioEngine.Reinitialize(attempts, reason)`, `BootPlan.BuildApply/BuildRecovery`), 모듈의 장치 사건 처리(DEVICELOST, DEVICEREINITIALIZE, Pinned 1초 디바운스)와 콜백 없는 정지 감지, 포커스 상실 시 게임 곡 일시정지(S2a에서 빠졌던 것), `AudioApplyOutcome.Rejected`
  - 하네스: `Testing/AudioHarness/HarnessOutputChecks`(출력 적용, SP6, SP9, SP10 사건 기록)
  - 테스트 53개(BootPlan 3개 추가). 임시 csproj로 세 조건 컴파일 오류 0, 52개 통과(`LogAssert` 1개는 Test Runner 전용)
- S3(판정 타이밍): **완료**(2026-09-27). SP11(60/144/무제한 + 매퍼 46분)·SP-IN(입력 맵 끄기, alt-tab runInBackground ON/OFF) PASS(7장)
  - `Game/Timing/LaneInput/{RealtimeQpcMapper, UnityInputSystemTimestampSource}`, `Game/Timing/{JudgementDriver, GameplayTimingBinding}`, `Game/Timing/Judgement/TimingLog`, `Game/Timing/Diagnostics/TimingOverlay`. JudgementPump에 진단 누계(자름, 전달, 배치) 추가
  - 하네스: `Testing/AudioHarness/HarnessTimingChecks`(게임 입력 맵 Game.Lane1~4 → 새 입력 소스 → JudgementDriver → binding). SP11 탭 테스트, SP11 매퍼 장기 변동, SP-IN 입력 맵 끄기, SP-IN alt-tab
  - 테스트 81개(매퍼 7, 입력 소스 7, 판정 기록 5, binding 9 추가). 임시 csproj로 세 조건 컴파일 오류·경고 0, 80개 통과(`LogAssert` 1개는 Test Runner 전용)
  - 하네스 UI 버그 수정: "종료" 버튼을 누른 같은 OnGUI에서 끝난 모듈을 읽어 NullReferenceException(S1부터 있었음, 동작 영향 없음)
- S4a(게임에 설치): **완료**(2026-09-27). 로비 BGM, 프리뷰, 곡 한 판, 설정에서 장치 선택(S/PDIF ↔ BenQ, 재구성 35~46ms, 같은 장치는 Unchanged), 로비 alt-tab 음소거, 버퍼 설정 변경 뒤 재시작(256x4 → 64x4) 모두 정상. 콘솔 오류 0
  - `Audio/Legacy/{ILegacyTransport, LegacyTransport}`(AudioModule.Legacy, 재구성 참여), `App/{AudioSettingsMapper, LegacyAudioManagerAdapter}`
  - `Managers.InstallAudio`: 모듈 설치(try/catch, 실패하면 소리 없는 어댑터), 어댑터 등록, JudgementDriver·`IJudgementTimingLog` 등록. FMODAudioManager는 더 붙이지 않는다(파일은 S4b에서 삭제)
  - `InputManager`: `LaneTimestampSource`를 소유하고 레인 콜백에서 Push, 게임 맵 끄기(SwitchToUI, Disable)를 Synthetic 구간으로 감쌈. 레거시 이벤트는 그대로
  - Installer가 설치 도중 예외가 나면 모듈을 해제하고 다시 던진다
  - 임시 csproj로 모듈·하네스 세 조건 컴파일 오류·경고 0, 테스트 80개 통과. Assembly-CSharp는 Unity가 만든 csproj에 새 파일을 더한 임시 래퍼로 컴파일 오류 0
  - 사운드 설정의 장치 목록에는 현재 출력 타입(WASAPI) 장치만 나온다. ASIO 선택은 출력 타입 설정(S5a)과 새 사운드 설정 화면(S5b)에서 붙는다
- S4b(옛 경로 제거): **완료**(2026-09-27). 게임 전체 흐름·ChartEditorScene·릴리스 스모크 빌드 정상, RuntimeManager 가드 오류 0(7장)
  - `App/FMODAudioManager.cs`, `App/FMODAudioPreInit.cs` 삭제. `Plugins/FMOD/src/Platform.cs`를 임포트 원본(522d8378)으로 되돌림(버퍼 setter 두 개와 주석 제거)
  - Managers에서 RuntimeManager 가드를 켬. 주석·CLAUDE.md의 옛 참조 정리
  - 씬 확인: FMOD Studio 컴포넌트는 MainScene의 StudioListener 하나뿐이고(EventEmitter, BankLoader 등 없음), GameScene에 "Audio Source" GameObject와 AudioListener가 있다
  - Assembly-CSharp(옛 파일 제외)와 FMODUnity 임시 컴파일 오류 0
  - 에디터 작업: MainScene `StudioListener` 제거, GameScene "Audio Source"·`AudioListener` 제거, FMOD Settings의 Play In Editor만 256×4(개수 추가). 옛 PreInit이 메모리에서 모든 플랫폼을 덮어써 둔 값(64×4)이 Save Project로 함께 저장됐기 때문에, 파일은 원본에서 Play In Editor 개수만 바꾼 상태로 되돌렸다
  - 과도기 음악 채널의 끝을 END 콜백으로 알아채게 고침(릴리스 빌드 Player.log에서 곡 종료 뒤 `ERR_INVALID_HANDLE` 1건 → 에디터 재확인 0건)
- S5a(설정 v2): **완료**(2026-09-27). Test Runner 102개 통과, v1 → v2 마이그레이션 1회(`.v1.bak` 백업), 버퍼·볼륨·노트 싱크·판정 싱크 유지, 버퍼 변경 뒤 재시작 반영, 로비 playInBackground ON/OFF alt-tab 정상(SP14 로비)
  - `SettingsData` v2 필드와 v1 필드 `[Obsolete]`, `App/SettingsMigration`(v1 판별·이동·검증, `.v1.bak`/`.v1.corrupt.bak`), `AudioSettingsMapper` v2(`ToBootRequest`가 v2 필드를 읽음, 버퍼 프리셋)
  - `SettingsManager`: Load에서 마이그레이션·백업·저장, Apply에서 볼륨은 `IAudioMixer`, `Application.runInBackground = playInBackground`
  - 옛 `SoundSettingUI`: 저장 때 `dspBufferLength`도 씀(파일 첫 줄의 CS0618 억제는 S5b에서 지움)
  - `Assets/Scripts/Editor/Tests/SettingsMigrationTests`(21개, Assembly-CSharp-Editor). Assembly-CSharp와 테스트 임시 컴파일 오류·경고 0(JsonUtility를 써서 Test Runner에서만 실행)
  - 사운드 설정에서 ASIO는 아직 고를 수 없다. v2에 출력 타입 필드만 생겼고, 선택 UI는 S5b
- S5b(사운드 설정 화면): **완료**(2026-09-27). 통합 장치 목록(WASAPI·ASIO), 장치·버퍼 적용과 저장, 재시작 뒤 복원, Save 두 번, Pinned 장치 분리 정상. Force Single Instance 켬
  - 사용자 요청으로 바꾼 것: 출력 타입 선택을 없애고 장치 목록 하나로 합침(이름만 표시), 상태 줄 없음(적용 결과는 로그), 버퍼 목록에서 480 제거(원래 목록 유지, 장치 타입이 바뀌어도 길이 유지), 버퍼 ms 표시 없음
  - `SoundSettingUI` 새로 씀(WASAPI·ASIO 통합 장치 목록 비동기(출력 타입을 따로 고르지 않음, 이름만 표시), 버퍼 프리셋(숫자만, ms 표시 없음), `_applying` 가드, 요청 구성 성공 때만 v2 저장, 적용 결과는 로그). 프리팹 변경 없음(상태 줄은 두지 않기로 함)
  - `IAudioEngine.IsRequestedConfig`(부팅·적용 요청과 현재 구성 비교), `App/AudioUiText`, MainUI 부팅 폴백 경고 1회(로그. 공용 알림 UI 없음)
  - v1 필드 삭제(마이그레이션은 원문에서 `audioBufferIndex`만 읽음). 버퍼 프리셋은 원래 목록 `64, 128, 256, 512, 1024` 하나(S5a에서 초안대로 넣었던 480은 뺐다. 추가 여부는 SP13). 장치 타입이 바뀌어도 길이는 유지하고 개수만 WASAPI 4·ASIO 2
  - `Editor/AudioBuildValidator`(음원 파일 확인, 없으면 빌드 중단), `Editor/AudioEditorMenu`(에디터에서 ASIO 허용 토글)
  - 임시 컴파일: 모듈·하네스 세 조건, Assembly-CSharp, 에디터 코드 오류 0. 테스트 104개(설정 23개)
- S6(로비와 곡 선택): **완료**(2026-09-27). 로비 BGM, 빠르게 넘겨도 프리뷰 하나, 로비 ↔ 곡 선택 ↔ 게임 전환, 설정 왕복, 장치 변경 뒤 BGM 복원 정상
  - MainUI 로비 BGM → `IMusicPlayers.Lobby`, AdventureUI 프리뷰 → `IMusicPlayers.Preview`(마지막 요청만 유효, 150ms 디바운스, 화면을 떠나면 요청 취소·정지). 옛 어댑터를 쓰지 않는다
  - 음원이 없거나 못 열면 경고 로그(공용 알림 UI가 없어 화면 안내는 후속)
- I1(GameManager 통합, S7 포함, `refactor-FMOD-Illustar`): **완료**(2026-09-29). 곡 한 판, 리드인·곡 도중 일시정지·재개, alt-tab(카운트다운·로딩·곡 도중), ESC 경계, 곡 끝 ESC·리트라이·로비, 장치 분리 자동 일시정지, ChartEditorScene 모두 정상
  - 곡 종료 때 `ERR_INVALID_HANDLE` 경고 1건(끝난 곡 채널에 isPlaying·stop) → 공용 `Audio/Playback/ChannelEndWatch`(END 콜백 기록)로 곡 세션·음악 재생기·과도기 전송을 함께 고침(공통 커밋, 재확인 대기)
  - ChartEditorScene의 `[CharacterAnimator] IGameManager를 찾지 못했습니다` 오류는 Illustar 코드(97ef839)가 ChartEditor에 GameManager가 없어 내는 것이다. 오디오 작업과 무관(Illustar 담당에게 전달)
  - 공통 커밋 ad4fb42(`refactor-FMOD`에도 cherry-pick할 것): `ISongPlayer.CreateSilent()`, `ISongSession.AudioStartSongTime`, `BGAController.Follow(session)`(곡 시각 - 음원 시작 곡 시각, 시계가 멈추면 영상 정지, 100ms 넘게 벌어질 때만 다시 맞춤)
  - 이 브랜치 전용: GameManager G1~G12(Illustar 코드로 대조, 그대로 적용됨), GameDataLoader(`ISongPlayer.LoadAsync`, 실패하면 로비로, 경로가 비면 무음 세션). ChartManager·IGameManager는 그대로(I2)
  - I1에서는 판정 싱크를 아직 ChartManager가 적용하므로 입력·진행에 `SongTime`만 넘긴다. 타임라인은 G5로 곡 시계를 따른다
- 지금까지의 결과는 7장 표에 있다. S-0.5·S0 결정은 7장 표 아래에 있고 `Audio_architecture.md`에 반영했다.
- S-0.5로 정한 것: B안(Core System 직접 소유) 유지, 메인 스레드에서 ASIO 처리, 곡 시계 모델(드리프트 항·평활 시계 없음, 단조 보장), `S_max = max(L·(N+1), 32ms)`, 게임 곡 스트리밍 유지, WASAPI 버퍼 프리셋 초안(480이 10ms 주기와 맞음). 버퍼 선택지는 S5b에서 사용자 결정으로 `64, 128, 256, 512, 1024`(기본 256)로 확정했다(480 미포함, SP13 측정 생략)
- 2026-09-27에 고친 하네스 버그와 추가한 측정
  - 1차(노트북): SP4 준비 판정(`PLAYING` 포함), SP4 채널 위치 오차 측정, SP3 2초 연속 읽기, fps 경고
  - 2차(데스크탑): SP4 seek 미적용 수정(비동기 되감기가 끝난 뒤 seek, `ERR_NOTREADY` 재시도, seek 반영 확인, 요약에 seek 실패 수 표시). SP3 판정을 "원시 잔차 폭 ≤ 1블록"에서 "곡 시계 모델 프레임 오차 p99 ≤ 1ms"로 바꾸고, 요약에 모델 결과(역행, 클램프, 최대 앞섬)를 추가
- 데스크탑 에디터는 60fps로 돌아서 SP3·SP11을 에디터에서 측정할 수 있다.

**확정 결정 요약**(자세한 내용은 `Audio_architecture.md`)
- FMOD Core System을 직접 소유한다(RuntimeManager는 게임 경로에서 쓰지 않음). Studio와 곡별 EQ는 쓰지 않는다.
- ASIO를 구현한다. 출력 장치, 타입, 버퍼 변경은 로비와 설정 화면에서만, 항상 close→init으로 한다.
- 입력은 1단계만 한다(추상화, 연속 곡 시계, 시각 기반 판정 타이밍). Raw Input은 후속이다.
- 오프셋: 노트 싱크(`audioOffsetMs`)와 판정 싱크(`judgmentOffset`, 판정 입력 윈도우 이동)를 유지하고, 각각 한 곳에서만 적용한다. 버퍼 지연 자동 보정, 안내, 장치별 프로필, 자동 제안은 없다. 캘리브레이션 씬은 후속이다.
- 백그라운드: `playInBackground`를 살린다. 게임 중에는 항상 자동 일시정지한다.
- GameManager/ChartManager는 팀이 병행 리팩터 중이다. 모듈은 두 Manager를 모르고, 심볼 기준 통합 지점만 편집한다. 브랜치 전략은 이 문서 맨 위를 따른다.
- asmdef를 둔다: Core, Audio, Game/Timing, 테스트. 설계는 단순화 방향이다.
- 음원 파일명은 그대로 둔다. 새 노트 싱크 체감 변화(데스크탑 WASAPI 256x4에서 약 +5.9ms)도 저장값을 바꾸지 않는다.

**작업 규칙**
- 소통은 한국어로 한다.
- 코드: 유지보수하기 쉽게 쓴다. 주석은 핵심 위치에만 간결하게 단다. **삼항 연산자는 쓰지 않는다.** `using FMOD;`는 쓰지 않는다.
- 커밋과 PR은 사용자가 요청할 때만 만든다. 커밋 메시지는 한국어이고 접두사를 붙인다(`docs:`, `test:`, `refactor:` 등).
- 게이트 스파이크가 사용자 결정 사항(4장 "예")으로 실패하면 수치와 선택지를 보고하고 멈춘다.
- **컴파일 확인**: 저장소에 csproj가 없으면 임시 폴더에 csproj(netstandard2.1, LangVersion 9)를 만들어 확인한다.
  - 참조: `C:/Program Files/Unity/Hub/Editor/6000.3.13f1/Editor/Data/Managed/UnityEngine/*.dll`, `Library/ScriptAssemblies/FMODUnity.dll`, `Library/ScriptAssemblies/Unity.InputSystem.dll`
  - `UnityEditor.dll`은 따로 넣지 않는다(`UnityEditor.CoreModule`과 형식이 겹침).
  - define은 에디터(`SCO_AUDIO_HARNESS;UNITY_EDITOR`), 플레이어(`SCO_AUDIO_HARNESS`), 없음의 세 조건으로 빌드한다.

**다음 할 일**
1. (사용자, `refactor-FMOD-Illustar`) 곡을 끝까지 한 판 해서 결과 화면으로 넘어갈 때 `ERR_INVALID_HANDLE` 경고가 없어졌는지 확인한다.
2. (Claude) 확인되면 공통 커밋 두 개(ad4fb42 I1 공통 준비, ChannelEndWatch 수정)를 `refactor-FMOD`에 cherry-pick하고, I2(ChartManager 통합)를 Illustar의 ChartManager 기준으로 대조한다.
- 남은 선택 확인: SP8(FlexASIO·실제 ASIO 장비), SP10(USB 분리·절전 복귀·블루투스).
- C 단계 정리 후보: 빌드 `StreamingAssets`에 FMOD Studio 뱅크(`Master.bank`, `Master.strings.bank`)가 들어간다. 게임은 Studio를 쓰지 않지만 ChartEditor가 FMOD for Unity를 쓰므로 C에서 함께 정리할지 정한다.
- 후속: 공용 알림 UI가 생기면 MainUI의 부팅 폴백 경고, AdventureUI의 음원 없음 안내, GameDataLoader의 음원 열기 실패 안내를 화면에 띄운다.
- 선택: 공통 부분(`refactor-FMOD`, S6까지)은 옛 경로(과도기 어댑터)로 게임이 정상 동작하므로 develop에 먼저 넣을 수 있다. 시점은 팀이 정한다.

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
- **에디터 작업**: 없음(하네스 파일의 GUID를 유지해 씬의 컴포넌트가 그대로 모듈 하네스를 가리킨다).
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
  - SoundSettingUI: WASAPI·ASIO 통합 GUID 장치 목록(출력 타입 선택 없음, 비동기, "검색 중" 표시), 버퍼(공통 프리셋, 숫자만), `_applying` 가드
  - 적용 결과 처리, v1 필드 삭제
  - MainUI에서 부팅 폴백 경고 1회
  - 빌드 검증기(`Assets/Scripts/Editor/AudioBuildValidator.cs`)
  - 새 문자열은 `App/AudioUiText`에 모은다.
- **에디터 작업**
  1. Player Settings → Resolution and Presentation → Force Single Instance를 켠다.
- **확인**
  - 장치(WASAPI·ASIO), 버퍼를 각각 바꾸고 Save를 누른다. 설정을 닫은 뒤 로비 BGM, 프리뷰, 곡 한 판, 타격음이 정상인지 본다.
  - 재시작 후 장치가 복원되는지 본다.
  - Save를 빠르게 두 번 누른다.
  - 로비·프리뷰 재생 중 Pinned 장치를 분리한다.
- **통과 기준**: 위 확인 정상. 버퍼 선택지는 사용자 결정으로 `64, 128, 256, 512, 1024`(기본 256) 확정(SP13 측정 생략)
- **보고**: 확인 결과
- **롤백**: revert(프리팹 변경은 같은 PR)

### S6. 로비와 곡 선택
- **내용**: MainUI, AdventureUI를 `IMusicPlayers`로 전환한다(TryGet, 요청 취소, 프리뷰 디바운스). 음원이 없으면 AdventureUI에 안내를 띄운다.
- **에디터 작업**: 없음
- **확인**: 곡 목록을 빠르게 넘겨도 프리뷰가 하나만 나오는지, 로비↔곡 선택↔게임 전환, 설정 화면 왕복
- **통과 기준**: 위 확인 정상
- **롤백**: revert

### S7. 타임라인과 BGA → I1에 합침(2026-09-28)
- TimelineController는 `IGameManager.GetCurrentTime()`으로 움직이므로, I1(G5)에서 GameManager가 곡 시각을 돌려주면 코드를 바꾸지 않아도 곡 시계를 따른다(ChartEditor는 `timeProvider`를 그대로 쓴다).
- BGAController는 GameManager가 `Init/SchedulePlay/Pause/Resume/Stop`으로 조종하고 옛 DSP 시간으로 맞추므로, 새 경로 전환은 GameManager 수정(G4, G7, G8)과 함께 해야 한다. 그래서 따로 두지 않고 I1에서 한다.

### I1. GameManager 통합
- **내용**: 6장의 G1~G12와 GameDataLoader(`ISongPlayer.LoadAsync`, 실패하면 로비로 돌아가 안내). 곡 시각만 넘긴다.
  - (S7에서 옮김) BGAController가 세션을 따라가게 한다(곡 시계 `Frame.SongTime`, 세션 이벤트로 재생·일시정지·재개·정지, 100ms 이상 벌어질 때만 다시 맞춤, OnDestroy에서 구독 해제). GameManager의 `bgaController?.SchedulePlay/Pause/Resume` 호출은 뺀다. TimelineController는 G5로 따라온다.
- **선행 확인**: 진행하는 브랜치의 GameManager·ChartManager 코드로 6장 통합 지점을 다시 대조한다(맨 위 브랜치 항목).
- **에디터 작업**: 없음
- **확인**(ChartEditorScene 동등도 확인한다. Timeline 프리팹을 같이 쓴다)
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
| SP3 시계 품질 | S-0.5 | 프리셋별 5분 CSV, ASIO, 96kHz | 계단 폭과 드리프트(ppm) 수치 확보. 곡 시계 모델(하한 포락선 + 클램프)의 프레임 오차 p99 ≤ 1ms. 기본 버퍼에서 언더런 0 |
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
| 2026-09-27 (데스크탑) | SP1 | Editor | WASAPI NVIDIA HDMI 48k 256x4 | PASS(부팅만) | 부팅 2회, init 41~69ms, RuntimeManager 미초기화. Play/Exit 반복은 미실시 |
| 2026-09-27 (데스크탑) | SP2 | Editor | ASIO4ALL 44.1k 256x2 | PASS | 아파트먼트 MAINSTA(STA). init 실패 0/10, close→init 실패 0/10. FlexASIO는 미설치(목록에 ASIO4ALL만 있음). 플레이어 빌드는 미실시 |
| 2026-09-27 (데스크탑) | SP3 | Editor | WASAPI S/PDIF 48k 256x4, 1분 | PASS(재판정) | 연속 읽기: 계단 256샘플, 갱신 간격 p50 9.7ms → WASAPI 엔진 주기(10ms)마다 2블록을 몰아 믹스한다. 그래서 원시 잔차 폭 11.7ms로 옛 기준(1블록)은 FAIL. 곡 시계 모델을 CSV에 적용하면 프레임 오차 p1~p99 0.000ms, 최대 0.6ms, 클램프 0회, 역행 3회(≤0.5ms), 원시 대비 최대 앞섬 12.5ms(평균 5.9ms). 드리프트 0.7ppm |
| 2026-09-27 (데스크탑) | SP3 | Editor | ASIO4ALL 44.1k 256x2, 1분 | PASS(재판정, S_max 조건) | 3블록씩 약 15~17ms 주기로 믹스. S_max 초기값 `L·(N+1)` = 17.4ms면 상한 클램프 170회(프레임 오차 p99 0.5ms, 최대 2.9ms). S_max를 20ms 이상으로 두면 클램프 0회, p99 0.000ms, 최대 1.7ms. 원시 대비 최대 앞섬 20.0ms. 드리프트 0.9ppm. 이번에는 클록 정지 없음(최대 간격 65.8ms는 메인 스레드 멈춤과 같음) |
| 2026-09-27 (데스크탑) | SP4 | Editor | WASAPI S/PDIF 48k 256x4 | 무효(재측정) | 하네스 버그: seek 19회가 모두 적용되지 않았다. 채널 위치 로그와 루프백 녹음이 모두 곡이 위치 0부터 재생됐음을 보여 준다. 원인 추정: NONBLOCKING 스트림은 playSound 뒤 비동기로 되감는데, 그동안 부른 setPosition의 ERR_NOTREADY를 무시함. 이전 노트북의 "약 100ms 어긋남"도 같은 버그다(클릭이 0.5초 주기라 어긋남이 접혀 보임). **seek 없는 재개 구간의 곡–메트로놈 시작 차는 녹음에서 0.000ms(클릭 5개)로, setDelay 예약은 샘플 단위로 정확하다.** 수정: 준비된 뒤 seek, 반환값 확인, 재생 전 위치로 반영 확인 |

| 2026-09-27 (데스크탑) | SP4 | Editor | WASAPI S/PDIF 48k 256x4 | PASS | seek 수정 후. 일시정지 20회, 늦은 예약 0, seek 실패 0(목표 위치와 반영 위치가 19회 모두 같음), seek 준비 평균 71ms 최대 100ms. 채널 위치 오차 -10.7~0ms(getPosition의 블록 단위 갱신). **루프백 녹음(모노 다운믹스)에서 클릭 119개가 모두 피크 0.80, 길이 4.97ms로 곡과 메트로놈이 녹음 1샘플(0.023ms) 이내로 겹침** → 시작 차 평균·표준편차 사실상 0. 게임 곡은 스트리밍 유지 |
| 2026-09-27 (데스크탑) | SP3 | Editor | WASAPI S/PDIF 48k 480x4 / 512x4 / 1024x4(GC 부하), 각 1분 | PASS | 곡 시계 모델 프레임 오차 p99 0.08 / 0.06 / 0.88ms, 최대 2.8 / 3.2 / 3.4ms, 클램프 0회, 역행 0 / 3 / 8회. 원시 대비 최대 앞섬 10.4 / 19.9 / 28.0ms. 갱신은 10ms 배수 주기(480은 10ms마다 1블록, 512는 10·20ms, 1024는 20·30ms). 드리프트 추정은 -20~+21ppm으로 추정 방식에 따라 흔들림 → 1분 기록으로는 ±20ppm을 가릴 수 없는 잡음 |
| 2026-09-27 (데스크탑) | SP11 | Editor | 60 / 144 / 무제한(404)fps | PASS | 매퍼 오프셋 변동 0.000ms. 처리 지연(처리 시각 - ctx.time) 평균 9.8 / 4.3 / 2.0ms, p95 15.7 / 6.5 / 2.7ms. 60fps에서 지연이 0~16ms에 고르게 퍼지고 ctx.time 해상도가 1ms 미만 → ctx.time은 프레임 처리 시점이 아니라 그보다 앞선 이벤트 시각(노트북 6fps 결과와 다름). 실제 누른 시각과의 차이는 미측정 |

| 2026-09-27 (데스크탑) | SP1 | Dev / Release 플레이어 | WASAPI NVIDIA HDMI 48k 256x4 | PASS | 실행·종료 Dev 6회, Release 5회. 매번 init OK(24~28ms), RuntimeManager 미초기화. Player.log에 오류 없음 |
| 2026-09-27 (데스크탑) | SP2 | Dev 플레이어 | ASIO4ALL 44.1k 256x2 | PASS | 아파트먼트 MAINSTA(STA). init 실패 0/10, close→init 실패 0/10. 에디터와 같음 → 스레드를 따로 만들지 않고 메인 스레드에서 ASIO를 다룬다 |
| 2026-09-27 (데스크탑) | SP3 | Dev 플레이어 | WASAPI NVIDIA HDMI 48k 256x4, 1분 | PASS | 곡 시계 모델 프레임 오차 p99 0.46ms, 최대 1.7ms, 역행 0, 클램프 0, 원시 대비 최대 앞섬 14.6ms(S_max 26.7ms 안). 갱신 간격 p50 9.95ms(10ms 주기에 2블록). 최대 샘플 간격 19.5ms(에디터보다 메인 스레드 멈춤이 짧음) |
| 2026-09-27 (데스크탑) | SP15 | Dev 플레이어 | WASAPI NVIDIA HDMI 48k 256x4 | PASS | ERROR 콜백 1건(ERR_FILE_NOTFOUND), 메인 스레드, 메인 외 스레드 0건. 크래시 없음 |
| 2026-09-27 (데스크탑) | SP1 재컴파일 | Editor | – | PASS | 1차: 리로드가 일어나지 않음(Script Changes While Playing 설정). 2차(11:23): 리로드 5회, 오류 0이지만 시스템 상태 미기록. 3차(11:29): `SP1-reload` 5회 모두 시스템 켜짐 True(1회는 SP3 기록 중), 예외·크래시 0. 콜백이 등록된 상태의 리로드는 S1에서 실제 엔진으로 재확인 |

| 2026-09-27 (데스크탑) | S0 확인 | Editor | – | PASS | 전체 컴파일(콘솔 오류 0), Test Runner EditMode 39개 통과, 로비·곡 한 판·ChartEditorScene 정상, `AudioSpike-Dev`에서 하네스 컴파일 정상. 참고: `AudioSpike-*` 프로필에서는 GameScene이 씬 목록에 없어 곡 진입이 안 되므로 게임 확인은 기본 Windows 프로필에서 한다 |

| 2026-09-27 (데스크탑) | SP1 (S1 모듈) | Editor / Dev / Release | WASAPI NVIDIA HDMI(BenQ) 48k 256x4, ASIO4ALL 44.1k 256x2 | PASS | 모듈 부팅 33회(Editor 22, Dev 6, Release 5) 모두 Running·원샷 등록·RM(Studio) 미초기화, init 20~30ms. 설치·원샷·종료 20회 × 2 실패 0, 원샷 멱등, FMOD 메모리 변화 0KB. Play 중 리로드 5회 모두 모듈 동작 중(ASIO 3회 포함), 크래시 0. 볼륨·포커스 음소거·ASIO 설치·게임(기본 프로필) 정상. 참고: 게임 판정이 약간 늦게 느껴진다는 보고가 있었다. S1은 게임 경로를 바꾸지 않았고, Windows 기본 출력 장치가 모니터(HDMI)라 출력 지연이 큰 것이 유력한 원인이다(아래 사용자 확인) |

| 2026-09-27 (데스크탑) | SP4 (S2a 모듈) | Editor | WASAPI NVIDIA HDMI(BenQ) 48k 256x4 | PASS | 곡 세션으로 리드인 중 1회 + 곡 도중 20회 일시정지·재개. 재개 지연 최대 0.2ms, 메트로놈 예약 119회 늦은 예약 0, 상태 이상 0. 루프백 녹음(좌우 섞임, 상관 1.000)에서 클릭 115개 모두 피크 0.80·길이 4.97ms → 곡과 메트로놈이 녹음 1샘플(0.023ms) 이내로 겹침. 시작 차 평균·표준편차 사실상 0 |
| 2026-09-27 (데스크탑) | 재구성 (S2a) | Editor | 같음 | 부분 | Ready→시작, Playing→장치 일시정지·Recovered·재개, Paused→Recovering·Recovered·재개 모두 OK. "Starting→진행" 실패는 하네스 시나리오 문제(스트림이 열려 있으면 Start가 즉시 커밋해 LeadIn이 되고, 재구성이 흐르는 곡에 걸려 설계대로 장치 일시정지됨). 시나리오를 고쳐 재확인 |
| 2026-09-27 (데스크탑) | 재구성 재확인 (S2a) | Editor | 같음 | PASS | 시나리오 수정 뒤 Ready→시작, Starting(재구성으로 스트림을 닫은 직후 Start, Starting 중 한 번 더 재구성)→진행, Playing·Paused 복구 모두 OK. 강제 재구성 5회(세대 1→6). 같은 로그에서 진단 메트로놈이 끝난 클릭 채널에 stop을 불러 ERR_CHANNEL_STOLEN·ERR_INVALID_HANDLE 오류 콜백이 일시정지마다 약 5건 났다 → 끝나지 않은 클릭만 멈추도록 고침(S2b 하네스 실행 때 확인) |
| 2026-09-27 (데스크탑) | 프리뷰 대체 (S2a) | Editor | 같음 | PASS | 한 프레임 5회 요청: Superseded 4, 마지막 Ok·재생 |

| 2026-09-27 (데스크탑) | SP6 (S2b) | Editor | WASAPI BenQ HDMI 48k 256↔512x4 | PASS | 로비 BGM 재생 중 설정 적용 50회(최대 66ms) + 같은 구성 close→init 50회(최대 50ms) 실패 0, 음악 복원 실패 0, 원샷 id 유효 |
| 2026-09-27 (데스크탑) | SP8 (S2b) | Editor | ASIO4ALL 44.1k 1024x2, WASAPI 고정 장치 | 부분 | ASIO4ALL 적용 2회, WASAPI 고정 장치 256/1024 적용이 모두 Applied(44~105ms). FlexASIO·실제 장비, 블록 불일치, 다른 앱 점유는 미확인 |
| 2026-09-27 (데스크탑) | SP9 (S2b) | Editor | WASAPI 4개, ASIO 1개 | PASS | WASAPI는 메인 System으로 읽어 0ms, ASIO는 임시 System 40~47ms. 열거 중 BGM 재생 유지. 끊김 녹음은 미실시 |
| 2026-09-27 (데스크탑) | SP10 (S2b) | Editor | – | 미실시 | 장치 콜백(DEVICELOST·DEVICELISTCHANGED·DEVICEREINITIALIZE)이 로그에 없음. 하네스 SP10 기록기가 SP6의 재구성까지 기록해 요약이 107줄 늘어난 문제를 고침(확인 버튼 실행 중에는 기록 안 함) |

| 2026-09-27 (데스크탑) | SP10 재확인 (S2b) | Editor | WASAPI BenQ HDMI ↔ S/PDIF | PASS | 처음에는 Follow-Default에서 기본 장치를 바꿔도 소리가 BenQ에 남았다(DEVICEREINITIALIZE 0회). 원인: Follow-Default에서도 setDriver(0)를 직접 불렀다(옛 RuntimeManager는 부르지 않음) → 부르지 않도록 고친 뒤 1) 기본 장치 변경 때마다 DEVICEREINITIALIZE와 자동 전환, 장치 이름 갱신 2) 곡 재생 중 변경 → Paused(DeviceChanged) 3) S/PDIF 고정 적용 뒤 '사용 안 함' → DEVICELISTCHANGED, 1초 디바운스 뒤 기본 장치(BenQ)로 재구성(29.9ms). USB 분리, 절전 복귀, 블루투스, HDMI 해상도 변경은 미실시. 장치 분리 중 FMOD가 같은 경고를 초당 15번 남겨 반복 경고를 묶도록 고침 |

| 2026-09-27 (데스크탑) | SP11 (S3) | Editor | 목표 60 / 144(실제 116) / 무제한(실제 146)fps, WASAPI S/PDIF 256x4 | PASS | 새 경로(입력 소스 → 매퍼 → JudgementDriver → binding). 매퍼 오프셋 변동(p1~p99) 0.000ms. 프레임 시각 - 입력 시각 평균 10.5 / 5.1 / 4.7ms, p95 14.9 / 8.2 / 7.8ms(S-0.5와 같은 경향). 프레임 시각으로 자름 0, 거부 0, 버림 0. 탭 오차(참고)는 표준편차 131~141ms로 균등분포 수준이고 탭 수(129~139)가 클릭 수(120)보다 많아 박자 맞춤 측정으로는 쓰지 않음 |
| 2026-09-27 (데스크탑) | SP11 매퍼 장기 (S3) | Editor | 무제한 fps | PASS | 46분 동안 매퍼 오프셋(중앙값) 변동 0.020ms |
| 2026-09-27 (데스크탑) | SP-IN (S3, 하네스) | Editor | – | PASS | 입력 맵 끄기: 누른 레인 1, release 1(Synthetic 구간 안에서 동기로 옴, 판정됨 0). alt-tab: runInBackground ON/OFF 모두 누른 레인 1, 포커스 상실 뒤 release 1(판정됨 0), 세션 Paused(FocusLost), binding 외부 일시정지 알림 FocusLost |

| 2026-09-27 (데스크탑) | S4a 게임 설치 | Editor | WASAPI S/PDIF·BenQ 256x4, 64x4 | PASS | 새 모듈 + 과도기 어댑터로 로비 BGM, 프리뷰, 곡 한 판(타격음, 일시정지·재개, 결과 화면) 정상. 설정 저장으로 장치 변경 Applied(init 34.8~46.3ms), 같은 장치는 Unchanged. 버퍼 인덱스 변경이 다음 부팅에 반영됨(`[Audio] 엔진 부팅 … 버퍼 64x4`). 콘솔 오류 0 |

| 2026-09-27 (데스크탑) | S4b 옛 경로 제거 | Editor / Release 플레이어 | WASAPI BenQ 64x4 | PASS | 게임 전체 흐름, ChartEditorScene(음원 로드·재생·자동 채보) 정상, RuntimeManager 가드 오류 0. 릴리스 스모크: `D:\게임 테스트\test - 복사본`에서 곡 한 판 풀콤보까지 정상, Player.log 예외 0, `fmodstudio.dll`만 포함(`fmodstudioL.dll` 제외), StreamingAssets Music 5·HitSound 5(원본과 같음), Addressables `aa` 포함. 곡 종료 뒤 `ERR_INVALID_HANDLE` 1건(과도기 채널 isPlaying) → END 콜백으로 고친 뒤 에디터에서 0건 |

| 2026-09-27 (데스크탑) | S5a 설정 v2 · SP14(로비) | Editor | WASAPI BenQ 64x4 | PASS | 기존 v1 설정으로 부팅해 v2로 1회 마이그레이션(`.v1.bak` 백업), 두 번째 Play부터 마이그레이션 없음. 버퍼 64x4·볼륨·노트 싱크·판정 싱크 유지, 버퍼 변경 뒤 재시작 반영. 로비 alt-tab: playInBackground OFF 음소거, ON 계속 재생. Test Runner EditMode 102개 통과 |

| 2026-09-27 (데스크탑) | S5b 사운드 설정 화면 | Editor | WASAPI BenQ·S/PDIF, ASIO4ALL | PASS(SP13 제외) | 통합 장치 목록(기본 장치 → WASAPI → ASIO)에서 장치·버퍼를 바꿔 Save → 로비 BGM·프리뷰·곡 한 판·타격음 정상, 재시작 뒤 장치·버퍼 복원, Save 빠르게 두 번, 재생 중 Pinned 장치 분리 → 기본 장치 전환 정상. Test Runner 104개 통과 |

| 2026-09-27 | SP13 (S5b) | – | – | 생략(사용자 결정) | 버퍼 선택지 `64, 128, 256, 512, 1024`, 기본 256으로 확정. 480은 넣지 않는다. 64·128도 그대로 둔다. 키 입력 → 타격음 종단 지연과 블루투스 지연은 재지 않았다(노트 싱크 범위 확장 판단 보류). SP13(b)는 I1에서 |

| 2026-09-27 (데스크탑) | S6 로비와 곡 선택 | Editor | WASAPI | PASS | 로비 BGM(`IMusicPlayers.Lobby`), 곡 선택 프리뷰(`IMusicPlayers.Preview`, 빠르게 넘겨도 하나만), 로비 ↔ 곡 선택 ↔ 게임 전환(게임 곡·타격음 정상), 설정 왕복, 사운드 설정 장치 변경 뒤 로비 BGM 복원 정상. 콘솔 오류 0 |

| 2026-09-29 (데스크탑) | I1 GameManager 통합 (Illustar) | Editor | WASAPI | PASS | 곡 세션·곡 시계·JudgementDriver 경로로 곡 한 판(시작 싱크·타격음·판정·결과), 리드인·곡 도중 일시정지·재개, alt-tab 세 경우, ESC 경계, 곡 끝 ESC·리트라이·로비, 장치 분리 자동 일시정지 → 재개, ChartEditorScene 정상. 곡 종료 때 ERR_INVALID_HANDLE 경고 1건 → ChannelEndWatch로 수정 |

**S-0.5 결정(SP3·SP4, `Audio_architecture.md`에 반영함)**
- 드리프트 항은 넣지 않는다. 오프셋을 1초 창에서 계속 다시 잡으므로 드리프트가 20ppm이어도 창 안 오차는 0.02ms다.
- 별도 평활 렌더 시계는 넣지 않는다. 하한 포락선 + 클램프 모델만으로 모든 프리셋에서 프레임 오차 p99 ≤ 0.9ms였다.
- 세그먼트 안에서 곡 시각의 단조 증가를 보장한다(이전 값보다 작으면 이전 값 유지). 포락선 창이 밀릴 때 최대 1.7ms 역행이 관측됐다.
- S_max = max(`L·(N+1)`, 32ms). 프레임 사이 계단으로 갱신하지 않는다. 메인 스레드가 멈춘 시간이 섞여 최대 75ms까지 부풀려지기 때문이다. 관측한 최대 앞섬은 모든 프리셋에서 이 값 안에 있었다.
- 게임 곡은 스트리밍(CREATESTREAM | NONBLOCKING)을 유지한다. 단, playSound 뒤 비동기 되감기가 끝난 다음 setPosition하고 반환값을 확인한다. getPosition은 싱크 판단에 쓰지 않는다.
- WASAPI 버퍼 프리셋 초안: OS 믹스 주기(10ms)와 맞는 480이 계단이 가장 고르다(10ms마다 1블록, 역행 0). → S5b에서 사용자 결정으로 선택지는 원래 목록 `64, 128, 256, 512, 1024`, 기본 256으로 확정했다(480 미포함, SP13 측정 생략).
- **S0에서 바꿈: 포락선 창 1초 → 10초 + 하향 계단 규칙.** S-0.5 판정은 프레임 사이 오차만 봤다. S0 테스트(프레임 주기와 믹스 주기가 맞물리는 경우)에서 드러나 SP3 CSV를 다시 분석했다. 1초 창은 프레임 읽기 위상이 몇 개로 묶여 추정이 몇 번의 점프로 0.7~4.5ms 오르내렸다(요약의 "프레임 오차 최대 2.8~3.4ms"가 이 점프). 10초 창은 0.25~2.2ms였다. 긴 창은 언더런처럼 DSP가 영구히 뒤처질 때 늦게 따라가므로, 1초보다 오래된 최댓값이 최근 1초 최댓값보다 8ms 넘게 높으면 버린다. 실측 6개 CSV에서 오판정 0회(정상 차이 최대 4.7ms)였고, 합성 20ms 언더런은 1초 안에 따라갔다.
- 새 곡 시계가 원시 계단보다 앞서는 평균은 "블록/2"(2.7ms)가 아니라 "OS 믹스 주기/2"다. 이 데스크탑의 WASAPI는 5.9ms, ASIO4ALL은 9.0ms였다. 노트 싱크 체감 변화가 문서 예상보다 크다.

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
