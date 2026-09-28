> **문서 안내**: 재설계 전 기존 FMOD 사용 방식을 조사한 보고서(2026-09-25, 1차 조사). 여기 나오는 이슈 번호(H1~H3, M1~M15, L1~L15)를 다른 문서가 참조한다. 조사 시점의 코드 기준이며, 줄 번호는 이후 바뀔 수 있다.

---

# FMOD 사운드 레이어 현황 조사 보고서

> 범위: `Assets/Scripts` 전체, FMOD 플러그인과 설정 에셋, 씬, `StreamingAssets`, ProjectSettings를 읽기 전용으로 조사했습니다.
> 여러 영역에서 중복 보고된 이슈는 하나로 합치고 원래 ID를 괄호에 적었습니다. "(부분 확인)"은 교차 검증에서 범위나 심각도가 조정된 항목입니다. "(런타임 확인 필요)"는 코드 경로는 확인했지만 실측이 필요한 항목입니다.

---

## 1. 한눈에 보기

- **Studio API는 쓰지 않고 Core(low-level) API만 씁니다.** FMOD for Unity 2.02.33이 `Assets/Plugins/FMOD`에 벤더링되어 있습니다(UPM 아님, `fmod.cs:22` `0x00020233`). 게임 코드에서 `EventInstance`, `StudioEventEmitter`, 뱅크 API를 호출하는 곳은 0건입니다.
- **FMOD System은 `FMODUnity.RuntimeManager`가 소유합니다.** 게임 코드는 System을 만들지 않고 `RuntimeManager.CoreSystem`을 빌려 씁니다. 처음 접근하는 순간 lazy init이 일어납니다(`RuntimeManager.cs:147-226`). 매 프레임 `studioSystem.update()`(`:569`)와 포커스 상실 시 `mixerSuspend`(`:813-828`)도 RuntimeManager가 처리합니다.
- **FMOD를 직접 다루는 코드는 세 곳이며 서로 독립적입니다.**
  1. `FMODAudioManager`: 게임과 로비용이며 `IAudioManager`의 유일한 구현입니다.
  2. `EditorFMODAudio`: 채보 에디터 전용이며 `IAudioManager`를 쓰지 않고 복제 코드로 되어 있습니다.
  3. `FMODAudioPreInit`: init 전에 DSP 버퍼 설정을 주입합니다.

  1과 2는 서로 다른 씬에만 있어서 실제로 동시에 존재하지 않습니다.
- **BGM 슬롯은 `_sound`/`_channel` 1개뿐입니다.** 로비 BGM, 곡 프리뷰, 게임 곡이 소유권 구분 없이 이 슬롯을 공유합니다. 로드는 `CREATESTREAM|NONBLOCKING`로 열고 `Update`에서 `getOpenState`를 폴링합니다. 재생은 `setDelay(절대 DSP 클록)`로 예약합니다.
- **타격음은 `CREATESAMPLE`로 동기 디코드한 최대 32개 슬롯입니다.** `paused:false`로 즉시 재생하고, 채널 핸들은 버립니다.
- **게임의 유일한 시계는 System Master ChannelGroup의 `getDSPClock`을 캐싱된 `_sampleRate`로 나눈 값입니다.** GameManager, BGAController, InputManager(동기점 1회), ChartManager, TimelineController가 모두 이 값을 씁니다. 이 값은 믹스 블록 단위로 계단식으로 오르고, 실제로 들리는 시각이 아니라 믹스 커서 위치입니다.
- **자체 ChannelGroup 계층이 있습니다.** `Master → BGM / HitSound / SFX` 구조이고 볼륨 4종은 선형 게인입니다. SFX 버스를 쓰는 곳은 없습니다.
- **설정 연동이 제각각입니다.**
  - 볼륨: `SettingsManager.Apply()`에서 적용합니다.
  - 버퍼: 재시작해야 적용되고, 벤더 파일을 패치해서 구현했습니다.
  - 출력 장치: Save할 때만 적용되고 부팅 시에는 복원되지 않습니다.
  - `playInBackground`: 사실상 동작하지 않습니다.
- **환경 조건:** Unity 내장 오디오가 꺼져 있고(`m_DisableAudio: 1`), `runInBackground: 0`입니다. 두 설정 모두 FMOD 클록 모델에 직접 영향을 줍니다.
- **가장 큰 리스크는 다섯 가지입니다.**
  - 포커스를 잃으면 `mixerSuspend`가 걸리고, 복귀 후 입력 판정이 백그라운드에 있던 시간만큼 밀립니다.
  - 리드인 중 일시정지하면 `setDelay` 예약이 보정되지 않아 BGM이 채보보다 먼저 나옵니다.
  - 로드 실패 상태가 인터페이스에 없어 대기 루프가 무한히 돕니다.
  - 저장한 출력 장치가 부팅 시 복원되지 않습니다.
  - 에디터 자동 채보가 음원보다 정확히 1마디 이르게 배치됩니다.

---

## 2. 구성 요소 맵

| 파일 | 역할 | 사용하는 FMOD / IAudioManager API |
|---|---|---|
| `Assets/Scripts/App/FMODAudioManager.cs` | `IAudioManager`의 유일한 구현(MonoBehaviour). `Managers` GameObject에 붙어 DDOL로 유지 | `CoreSystem` 캐싱(:67), `getMasterChannelGroup`(:70), `getSoftwareFormat`→`_sampleRate`(:73), `getDSPBufferSize`(:102), `createChannelGroup`×4 + `addGroup(…, false)`(:78-84), `setMute`(:112), `getOpenState` 폴링(:120), `createSound(CREATESTREAM\|NONBLOCKING\|LOOP_*)`(:190-194), `playSound(paused)`→`setDelay`→`setLoopCount`→`setPriority(0)`→`setPaused(false)`(:223-229), 원샷 `createSound(CREATESAMPLE\|_2D\|LOOP_OFF\|IGNORETAGS\|LOWMEM)`(:265), `playSound(bus, paused:false)`/`setVolume`(:289-291), `getDSPClock`(:311), `getNumDrivers`/`getDriverInfo`/`setDriver`(:324-333), `ChannelGroup.setVolume`×4(:336-339) |
| `Assets/Scripts/App/Interfaces/IAudioManager.cs` | 오디오 계약과 `AudioOutputType`/`AudioOutputConfig`/`AudioBus` | FMOD 타입은 노출하지 않음. 대신 절대 DSP 시각, 경로 규약, 드라이버 인덱스, NONBLOCKING 폴링이 계약에 전제로 깔려 있음(:27-61) |
| `Assets/Scripts/App/FMODAudioPreInit.cs` | `SubsystemRegistration`에서 DSP 버퍼 주입 | `FMODUnity.Settings.Instance`(:36), `Platform.SetDSPBufferLength/Count`(벤더 패치 메서드, :44-50), PlayerPrefs JSON 직접 파싱(:54-68) |
| `Assets/Plugins/FMOD/src/Platform.cs` | 벤더 파일 + **로컬 패치** | `SetDSPBufferLength`/`SetDSPBufferCount`(:871-878, "[SCOdyssey 추가]"). 쓰이지 않는 공식 훅 `PlatformCallbackHandler.PreInitialize`(:38-46) |
| `Assets/Plugins/FMOD/src/RuntimeManager.cs` | 벤더 파일. Core와 Studio System의 소유자 | lazy `Instance`(:147-226), `Initialize`(:286-390), `Update`→`studioSystem.update()`(:569), `OnApplicationPause`→`mixerSuspend/Resume`(:813-828), `OnDestroy`(:750-753) |
| `Assets/Scripts/App/Managers.cs` | MainScene 부트. 오디오 매니저를 생성하고 등록하는 **유일한 지점** | `AddComponent<FMODAudioManager>()`(:51), `TryRegister<IAudioManager>`(:52), `settingsManager.Apply()`(:55), `uiManager.Init()`(:58) |
| `Assets/Scripts/App/SettingsManager.cs` | 설정 로드, 저장, push | `SetMaster/Bgm/HitSound/SfxVolume`(:74-77). `SetAudioDevice`는 호출하지 않음 |
| `Assets/Scripts/Domain/DTO/SettingsData.cs` | 오디오 설정 DTO(FMOD는 주석에만 등장) | `audioOffsetMs`(:9), `judgmentOffset`(:10), 볼륨 4종(:22-25), `audioDeviceIndex`(:26), `audioBufferIndex=2`(:27), `playInBackground`(:28) |
| `Assets/Scripts/UI/Settings/SoundSettingUI.cs` | 사운드 설정 UI | `GetAvailableDevices`(:69), `SetAudioDevice`(:204-205), `BufferSizes` 중복 정의(:48) |
| `Assets/Scripts/App/GameManager.cs` | 게임 시계 소유자 | `GetDSPTime`(:140, 161, 172, 193, 226), `PlayScheduled`(:162), `Pause`/`Resume`(:194/227), `Stop`(:346), `IsPlaying`(:62), `SetTimeSyncPoint`(:144), `OnApplicationFocus`→`Pause`(:183-187) |
| `Assets/Scripts/App/ChartManager.cs` | 타격음 등록과 재생, 음악 예약 트리거, 곡 종료 판정 | `RegisterOneShot`×5(:155-159), `StartMusic(barDuration)`(:183), `IsAudioPlaying`(:230), `PlayOneShot`(:753) |
| `Assets/Scripts/App/InputManager.cs` | `ctx.time`(realtime)을 DSP 시각으로 선형 변환 | `SetTimeSyncPoint`(:81-86), `ConvertToDspTime`(:88-92), `AudioSettings.dspTime` 폴백(:90) |
| `Assets/Scripts/Game/BGAController.cs` | 영상 시작을 DSP 시각에 맞춤 | `GetDSPTime`, `AudioSettings.dspTime` 폴백(:165) |
| `Assets/Scripts/Game/TimelineController.cs` | 간접 소비자(판정선) | `timeProvider()` 또는 `IGameManager.GetCurrentTime()`(:90-92) |
| `Assets/Scripts/Game/GameDataLoader.cs` | 게임 곡 로드 | `LoadAudio`(:50), `while(!IsLoaded)`(:52) |
| `Assets/Scripts/UI/MainUI.cs` | 로비 BGM | `Stop`(:57, 77), `LoadAudio(loopHint:true)`(:79), `IsLoaded`(:81), `PlayScheduled(GetDSPTime(), loop)`(:83) |
| `Assets/Scripts/UI/AdventureUI.cs` | 곡 선택 프리뷰 | `Stop`(:75, 154), `LoadAudio`(:165), `IsLoaded`(:167), `GetDSPTime`/`PlayScheduled`(:169-170) |
| `Assets/Scripts/App/UIManager.cs` | 오디오 직접 호출 없음. `SetActive` 토글로 MainUI/AdventureUI의 오디오 호출을 사실상 발생시킴 | `OnSceneLoaded`(:43-50), 비활성화 루프(:163-168)가 활성화 루프(:171-178)보다 먼저 실행 |
| `Assets/Scripts/UI/PauseUI.cs`, `ResultUI.cs` | 씬 전환만 함(`Stop` 호출 없음) | `PauseUI.cs:90, 96`, `ResultUI.cs:122, 130` |
| `Assets/Scripts/Game/Test/GameSceneTester.cs` | 비활성 테스트 코드 | `LoadAudio`(:27) |
| `Assets/Scripts/ChartEditor/Preview/EditorFMODAudio.cs` | 에디터 전용 FMOD 플레이어(FMODAudioManager의 복제본) | `getMasterChannelGroup`(:23), `createSound(CREATESAMPLE\|NONBLOCKING)`(:91-95), System Master에 직접 `playSound`(:129), `setPosition(MS)`(:135), `setDelay`(:141), `getDSPClock`+`getSoftwareFormat`(:170-171), `Sound.lock/unlock`(:210-248) |
| `Assets/Scripts/ChartEditor/Preview/EditorTimeProvider.cs` | 에디터 시계. `Func<double>`을 주입받음(FMOD는 주석에만) | `dspTimeSource()` 호출(:27, 51, 73) |
| `Assets/Scripts/ChartEditor/Preview/EditorPreviewManager.cs` | 마디 시간을 음원 시간으로 매핑하고 프리뷰 제어 | `timeProvider.Start(…, fmodAudio.GetDSPTime)`(:145), `Play(audioTime)`(:153-154), `Pause`/`Resume`(:188-196) |
| `Assets/Scripts/ChartEditor/ChartEditorManager.cs` | 자동 채보(PCM → onset) | `GetMonoSamples`(:363), `DetectOnsets`(:370-371) |
| `Assets/Scripts/ChartEditor/UI/EditorFilePanel.cs`, `IO/ChartFileIO.cs` | 음원 파일 선택(절대 경로, 기본 폴더 `Assets/Audios`) | `fmodAudio?.LoadAudio(path)`(`EditorFilePanel.cs:130`), `ShowAudioOpenDialog`(`ChartFileIO.cs:109-122`) |
| `Assets/Scripts/ChartEditor/Analysis/AudioOnsetDetector.cs`, `AutoChartGenerator.cs` | PCM 분석과 노트 배치. FMOD 직접 의존은 없음 | 입력 계약 `DetectOnsets(float[], int, …)`(:32-33), `onset.time` 사용(`AutoChartGenerator.cs:40-47`) |
| `Assets/Scripts/ChartEditor/Data/EditorChartData.cs` | `audioFilePath`(주석에만 FMOD) | :19, :86 |

- **주석에서만 매치된 파일:** `SettingsData.cs`, `EditorChartData.cs`, `AudioOnsetDetector.cs`, `EditorTimeProvider.cs`. `EditorFilePanel.cs`와 `EditorPreviewManager.cs`도 FMOD 문자열은 주석에만 있지만, `EditorFMODAudio`를 실제로 호출합니다.
- **초기 21개 목록 밖에서 추가로 확인한 관련 파일:** `InputManager.cs`, `UIManager.cs`, `PauseUI.cs`, `ResultUI.cs`, `TimelineController.cs`, `AutoChartGenerator.cs`, `ChartFileIO.cs`, `MusicSO.cs`(경로 주석).
- **오디오를 전혀 쓰지 않는 코드:** Boot, Testing, Net, Core 폴더(Phased installer 부트 경로)에는 오디오 관련 코드가 없습니다.

---

## 3. 초기화 / 수명주기

### 3.1 앱 시작부터 MainScene까지

1. **[SubsystemRegistration]** `ServiceLocator.Reset`(`Core/ServiceLocator.cs:14-20`)
2. **[SubsystemRegistration]** `FMODAudioPreInit.ApplyBufferSize`(`FMODAudioPreInit.cs:24-51`)
   - PlayerPrefs `"SCOdyssey.Settings.v1"`를 직접 파싱해 `audioBufferIndex`를 얻습니다(기본 2, 즉 256).
   - 모든 Platform과 DefaultPlatform에 `SetDSPBufferLength(64~1024)`와 `SetDSPBufferCount(4)`를 씁니다.
3. MainScene이 로드되면 `Managers.Awake`(`Managers.cs:12-24`)가 DDOL로 전환한 뒤 `InitServices`를 실행합니다. 순서는 Settings(`Load`) → Input → UI(등록만) → Music → Character입니다.
4. `AddComponent<FMODAudioManager>()`(`Managers.cs:51`)가 호출되면 Awake가 **동기로** 실행됩니다.
   - `RuntimeManager.CoreSystem`에 처음 접근하는 순간(`FMODAudioManager.cs:67`) RuntimeManager가 숨김 DDOL 객체를 만들고 `Initialize`를 실행합니다(`RuntimeManager.cs:157-212`).
   - `Initialize` 순서: `FindCurrentPlatform`(:286, 에디터는 playInEditor, 빌드는 default) → `setOutput`(:335) → `setSoftwareChannels`(:338) → `setSoftwareFormat`(:341) → `setDSPBufferSize`(:344-348, Length>0 && Count>0일 때만) → `PlatformCallbackHandler.PreInitialize`(:372-379, 미사용) → `studioSystem.initialize`(:381, 실패하면 NOSOUND로 재시도 :382-389, 그 결과는 throw되지 않음) → 빈 Master/Master.strings 뱅크 로드.
5. `FMODAudioManager.Awake`의 나머지 작업
   - `_masterGroup`(:70), `_sampleRate` 캐싱(:73), 지연 로그(:75, 100-106)
   - ChannelGroup 생성과 재부모화(:78-84), `_busGroups = {HitSound, SFX}`(:87)
6. `TryRegister<IAudioManager>`(`Managers.cs:52`) 다음 `settingsManager.Apply()`(:55)가 볼륨 4종만 적용합니다(`SettingsManager.cs:72-78`). **장치 인덱스는 적용하지 않습니다.**
7. `uiManager.Init()`(`Managers.cs:58`) → `ShowUI<MainUI>` → `MainUI.OnEnable` → 로비 BGM 코루틴(`MainUI.cs:41`)
8. 매 프레임 실행 순서
   1. `FMODAudioManager.Update`(로드 폴링, 실행 순서 0)
   2. 코루틴 재개
   3. `RuntimeManager.Update`(실행 순서 100, `studioSystem.update()` :569). Core `System::update`도 이 안에서 처리되므로, **Studio를 없애면 `coreSystem.update()`를 직접 불러야 합니다.**

### 3.2 씬 전환

- FMODAudioManager와 RuntimeManager는 둘 다 DDOL이라 씬이 바뀌어도 살아 있습니다. MainScene에 다시 들어오면 새 `Managers`는 곧바로 Destroy됩니다(`Managers.cs:14-18`).
- `Managers`는 **MainScene에만** 있습니다. GameScene을 단독으로 Play하면 `IAudioManager`가 없어서 게임이 시작되지 않습니다(`GameManager.cs:83-84`, `GameDataLoader.cs:39-43`).
- **씬 전환 시 오디오 정지를 책임지는 코드가 없습니다.**
  - PauseUI의 나가기와 리트라이, R키 재시작(`GameManager.cs:256-260`), `GameManager.OnDestroy`(:115-127) 모두 `Stop`을 부르지 않습니다.
  - 대신 다음 소비자가 부르는 `LoadAudio`/`Stop`이 우연히 정리해 줍니다.
- `ServiceLocator.Remove<IAudioManager>()`를 호출하는 곳이 없습니다.

### 3.3 ChartEditorScene (에디터 전용, 빌드에서 제외)

- PreInit은 이 씬에서도 똑같이 실행되므로, 게임 PlayerPrefs의 버퍼 설정이 에디터에도 적용됩니다.
- `EditorFMODAudio.Awake`(:21-24)의 첫 `CoreSystem` 접근이 FMOD를 초기화합니다.
- 이 씬에는 `Managers`가 없어서 `IAudioManager`와 `ISettingsManager`도 없습니다.
- `ChartEditorScene`은 EditorBuildSettings에 없고, 이 씬을 `LoadScene`하는 코드도 없습니다. 따라서 FMODAudioManager와 동시에 존재하는 일이 없습니다.

### 3.4 종료

- `FMODAudioManager.OnDestroy`(:135-150): `Stop` → `_sound.release` → 원샷 release → ChannelGroup 4개 release 순서입니다. RESULT는 모두 무시합니다.
- `RuntimeManager.OnDestroy`(:750-753): `setCallback(null)` 다음 Studio System을 release하며, 이때 Core System도 함께 해제됩니다.
- 두 객체 모두 DDOL이라 **파괴 순서가 보장되지 않습니다**(7장 L2 참고).

### 3.5 ChannelGroup 계층

```
FMOD System Master ChannelGroup        ← _masterGroup: GetDSPTime 클록 소스 (FMODAudioManager.cs:70, 311)
│                                         EditorFMODAudio 채널은 여기에 직접 붙음 (EditorFMODAudio.cs:129)
└─ "Master"    _ourMasterGroup         ← SetMasterVolume(:336), 포커스 음소거 setMute(:112)
   ├─ "BGM"      _bgmGroup             ← BGM 슬롯 채널 1개(:223), SetBgmVolume
   ├─ "HitSound" _hitSoundGroup        ← AudioBus.HitSound = _busGroups[0], 타격음 원샷
   └─ "SFX"      _sfxGroup             ← AudioBus.Sfx = _busGroups[1], 사용처 없음
```

- 하위 그룹은 `addGroup(child, propagatedspclock:false, …)`로 연결합니다(:82-84).
- `setDelay`의 기준은 부모인 `_bgmGroup`의 클록인데, 예약 시각은 System Master 클록으로 계산합니다. 중간 그룹에 pause나 pitch가 걸리지 않는 동안에만 두 값이 같습니다(7장 L3).

---

## 4. 주요 흐름

### 4.1 게임 곡: 로드 → 예약 재생 → 일시정지/재개 → 종료

1. **진입:** `AdventureUI.HandleSubmit`이 `LoadScene("GameScene")`을 호출합니다(`AdventureUI.cs:210-214`). 이어서 sceneLoaded → `UIManager.RefreshVisibility` → `AdventureUI.OnDisable` → `Stop()` 순으로 실행됩니다. 이때 프리뷰 사운드는 해제되지 않고 `_isLoaded=true`가 그대로 남습니다.
2. **로드:** `GameDataLoader`가 `LoadAudio(music.audioFilePath)`를 호출합니다(loop off, `:50`).
   - `LoadAudio` 안에서 이전 사운드를 Stop하고 release한 뒤(`FMODAudioManager.cs:174-179`) `CREATESTREAM|NONBLOCKING`으로 엽니다.
   - 호출자는 `while(!IsLoaded)`로 기다립니다(`:52`). `IsLoaded`는 `Update` 폴링에서 READY를 보면 true가 됩니다. ERROR면 `_isLoading=false`만 바뀌고 `IsLoaded`는 계속 false입니다(`:116-133`).
3. **StartGame**(`GameManager.cs:129-147`)
   1. `chartManager.Init`에서 `RegisterOneShot`×5를 동기 디코드합니다. 파일명 기준으로 멱등입니다.
   2. `Init`의 **마지막 줄**에서 `StartMusic(barDuration)`을 호출합니다(`ChartManager.cs:183`).
      - `dspStartTime = GetDSPTime() + barDuration + audioOffsetMs/1000`을 계산합니다(`GameManager.cs:161`, DSP 1차 읽기).
      - `PlayScheduled`에서 `startDspClock = (ulong)(dspStartTime*_sampleRate)`를 구해 `playSound(paused, _bgmGroup)` → `setDelay` → `setLoopCount(0)` → `setPriority(0)` → `setPaused(false)` 순으로 호출합니다(`FMODAudioManager.cs:220-229`).
      - `bgaController.SchedulePlay(dspStartTime)`(`:163`)
   3. `Init`이 반환된 뒤 `globalStartTime = GetDSPTime()`을 **따로 한 번 더** 읽습니다(`:140`, DSP 2차 읽기). 이어서 `SetTimeSyncPoint(globalStartTime, realtime)`로 **곡 전체에서 단 한 번** 동기점을 기록하고(`:144`), `IsGameRunning=true`로 바꿉니다(`:146`). 이 시점부터 일시정지가 가능해집니다.
4. **프레임 진행:** `GetCurrentTime = GetDSPTime() - globalStartTime`이고, 일시정지 중에는 `_pauseDspTime`에 고정됩니다(`:166-173`).
5. **일시정지:** ESC 또는 `OnApplicationFocus(false)`로 들어옵니다. `_pauseDspTime = GetDSPTime()` → `_channel.setPaused(true)` → `bga.Pause()` 순입니다(`:189-202`, `FMODAudioManager.cs:300`).
6. **재개:** 3초 카운트다운 뒤 `globalStartTime += GetDSPTime() - _pauseDspTime` → `setPaused(false)` → `bga.Resume()` 순입니다(`:210-231`). **`setDelay` 예약값과 입력 동기점은 보정하지 않습니다.**
7. **종료:** 채보 큐가 비고 `IsAudioPlaying==false`가 되면(`ChartManager.cs:215-233`, `_channel.isPlaying`) `OnGameFinished`가 `Stop()`을 부릅니다(`GameManager.cs:346`).

```
DSP ─●──────────●─────────────────────────────●──────────▶
     t0         t1                            t1+barDuration(+offset)
     StartMusic globalStartTime (채보 t=0)     BGM이 들리기 시작(채보 1번 마디), BGA Play
     (:161)     SetTimeSyncPoint 1회 (:140, 144)
     setDelay(t0+lead-in) ← 부모 그룹 기준 절대 클록
```

### 4.2 타격음 (원샷)

- **등록:** `ChartManager.Init`이 `RegisterOneShot(hit_perfect|master|ideal|kind|umm.wav)`을 호출합니다(`:112-119, 155-159`). 슬롯 인덱스는 `(int)JudgeType`과 같습니다. 파일 경로는 `StreamingAssets/HitSound/`이며, 모드는 `CREATESAMPLE|LOWMEM` 동기입니다(`FMODAudioManager.cs:254-265`).
- **재생:** 입력이 들어오면 `TryJudgeInput`이 **판정보다 먼저** `PlayInputSound`를 부르고, 예측한 판정 등급으로 `PlayOneShot(slot)`을 호출합니다(`ChartManager.cs:676-753`). 버스는 항상 HitSound이고 volume은 1입니다.
- **즉시성:** 곧바로 `playSound(paused:false)`가 실행되므로, 소리는 다음 믹스 블록에서 나기 시작합니다(`FMODAudioManager.cs:281-292`).
- **소리가 나지 않는 경우:** 릴리즈 판정은 HoldRelease가 성공했을 때만 소리를 냅니다. 홀드 본체의 자동 Perfect와 miss는 무음입니다.
- **해제:** `OnDestroy`에서만 해제되고, 언로드 API는 없습니다.

### 4.3 DSP 시간과 입력 동기화

- **시계의 성질:** `GetDSPTime`은 믹스 블록 단위로 계단식으로 증가합니다. 256 샘플 @48kHz면 5.33ms, 1024면 21.3ms입니다. 들리는 시각과의 차이(출력 지연 ≈ 블록 × (count−1.5))는 로그로만 남기고 보정하지 않습니다(`FMODAudioManager.cs:100-106`).
- **입력 변환:** `ctx.time`을 `_dspAtSync + (ctxTime - _realtimeAtSync)`로 바꿉니다(`InputManager.cs:91`). 동기점은 한 번만 잡고, 드리프트나 믹서 정지는 재보정하지 않습니다.
- **한 프레임 안의 DSP 조회:** `GameManager.Update`(:180), TimelineController 인스턴스마다(`:90-92`), `ChartManager.CheckHoldingBody`(:789), `BGAController.Update`(:165)가 각자 조회합니다. 캐싱이나 보간은 없습니다.

### 4.4 설정 반영

| 설정 | 적용 시점 | 경로 |
|---|---|---|
| 볼륨 4종 | Save할 때와 부팅 시 `Apply` | `SettingsManager.cs:72-78` → `ChannelGroup.setVolume`(선형, `FMODAudioManager.cs:336-339`) |
| `audioDeviceIndex` | **Save할 때만** | `SoundSettingUI.cs:204-205` → `setDriver`(`FMODAudioManager.cs:333`). 부팅 시에는 적용하지 않음 |
| `audioBufferIndex` | 다음 실행 | `FMODAudioPreInit` → Settings 에셋 → `RuntimeManager.Initialize`의 `setDSPBufferSize`(:344-348) |
| `playInBackground` | 다음 포커스 이벤트 | `FMODAudioManager.OnApplicationFocus` → `setMute`(:108-113). `runInBackground=0`이라 사실상 무효 |
| `audioOffsetMs` | 곡 시작 시 | `GameManager.cs:157-161` |
| `judgmentOffset` | `ChartManager.Init` | `ChartManager.cs:150`(×3ms) |

### 4.5 로비 BGM과 곡 프리뷰

- **공통 패턴:** `Stop` → `LoadAudio(file, loopHint:true)` → `while(!IsLoaded)` → `PlayScheduled(GetDSPTime(), loopPlay:true)`. 예약 시각이 이미 지났으므로 즉시 재생됩니다. 이 패턴이 `MainUI.cs:77-83`과 `AdventureUI.cs:154-170`에 복붙되어 있습니다.
- **로비 BGM 수명:** MainUI가 보일 때마다(`OnEnable`) 처음부터 다시 로드합니다. `OnDisable`에서는 코루틴을 멈추고 전역 `Stop()`을 부릅니다(`MainUI.cs:36-59`). 그래서 설정 화면을 Push로 열면 로비 BGM이 멈춥니다.
- **AdventureUI를 처음 열 때:** `Awake → Init → OnSelectedMusicChanged`(:148)와 `OnEnable`(:66)이 각각 코루틴을 시작해 **두 번 실행**됩니다. 곡을 바꿀 때마다 코루틴이 쌓이고, 취소하지도 않습니다.
- **UI 전환 순서:** 새 UI의 `Awake`/`OnEnable`에서 `LoadAudio`가 먼저 실행되고, 그다음 `RefreshVisibility`가 MainUI를 끄면서 `Stop()`이 불립니다(`UIManager.cs:163-178`). 이때는 프리뷰가 아직 로딩 중이라 우연히 문제가 없습니다.

### 4.6 채보 에디터 재생과 PCM 분석

- **로드:** 파일 다이얼로그로 받은 절대 경로를 `CREATESAMPLE|NONBLOCKING`으로 열고 Update에서 폴링합니다(`EditorFMODAudio.cs:27-44, 73-105`).
- **재생:** `audioTime = startBar*barDuration - barDuration`으로 계산합니다(`EditorPreviewManager.cs:153`). 이 값이 0 이상이면 `setPosition(ms)`, 음수면 `setDelay(GetDSPTime()+(-audioTime))`로 시작합니다(`EditorFMODAudio.cs:132-142`). 채널은 System Master에 직접 붙습니다.
- **시계:** `EditorTimeProvider`가 GameManager의 일시정지 보정을 따로 다시 구현합니다(`:27, 36-53, 73`). 에디터 쪽 `setDelay`도 일시정지 중에 보정되지 않습니다.
- **PCM 추출:** `GetMonoSamples`가 `getDefaults`, `getFormat`, `getLength(PCMBYTES)`로 정보를 얻고 `lock(0, 전체)`로 데이터를 읽습니다. `PCMFLOAT`와 `PCM16`만 변환한 뒤 모노로 다운믹스합니다(`:183-267`). 결과는 `AudioOnsetDetector`(음원 시간 기준)를 거쳐 `AutoChartGenerator`로 넘어가는데, 여기서 음원 시간을 채보 시간으로 착각합니다(7장 H4).
- **지원하지 않는 기능:** 에디터에는 타격음과 메트로놈이 없고, `audioOffsetMs`와 볼륨 설정도 적용되지 않습니다.

---

## 5. IAudioManager 인터페이스 사용 현황

| 멤버 | 호출처 | 비고 |
|---|---|---|
| `LoadAudio(filePath, loopHint=false)` | `GameDataLoader.cs:50`, `MainUI.cs:79`, `AdventureUI.cs:165`, `GameSceneTester.cs:27`(비활성) | `StreamingAssets/Music/` 접두사를 내부에서 붙임. loop 플래그가 `PlayScheduled`와 둘로 나뉨 |
| `PlayScheduled(dspStartTime, loopPlay=false)` | `GameManager.cs:162`, `MainUI.cs:83`, `AdventureUI.cs:170` | 인자는 절대 DSP 초. UI는 "지금 즉시 재생" 용도로만 씀 |
| `Stop()` | `GameManager.cs:346`, `MainUI.cs:57, 77`, `AdventureUI.cs:75, 154`(`LoadAudio` 내부에서도 호출) | 전역 슬롯이라 호출자를 구분하지 않음 |
| `Pause()` / `Resume()` | `GameManager.cs:194` / `:227` | 채널만 `setPaused`. 예약된 `setDelay`는 보존하지 않음 |
| `GetDSPTime()` | `GameManager.cs:140, 161, 172, 193, 226`, `BGAController.cs:165`, `MainUI.cs:83`, `AdventureUI.cs:169` | 게임 전체의 시계 |
| `IsPlaying` | `GameManager.cs:62`(→ `ChartManager.cs:230` 곡 종료 판정), `MainUI.cs:77`, `AdventureUI.cs:154` | 곡 종료 감지를 겸함 |
| `IsLoaded` | `GameDataLoader.cs:52`, `MainUI.cs:81`, `AdventureUI.cs:167` | 실패 상태가 없음. 폴링 루프가 세 곳에 복제됨 |
| `ConfigureOutput(config)` | **없음** | 값을 저장만 함. 계약("FMOD 초기화 전에 호출")은 구조상 지킬 수 없음 |
| `GetAvailableDevices()` | `SoundSettingUI.cs:69` | UI Init에서 한 번만 호출. GUID는 버림 |
| `SetAudioDevice(driverIndex)` | `SoundSettingUI.cs:205` | Save 때마다 무조건 호출. 부팅 시에는 호출하는 곳이 없음 |
| `RegisterOneShot(fileName)` | `ChartManager.cs:158` | 멱등. 최대 32개. 언로드 API 없음 |
| `PlayOneShot(slot, bus, volume)` | `ChartManager.cs:753` | `bus`와 `volume`은 항상 기본값(**Sfx 버스와 volume 인자는 사용처 0**) |
| `SetMaster/Bgm/HitSound/SfxVolume` | `SettingsManager.cs:74-77` | 선형 게인. SFX는 실제로 들리는 소리가 없음 |
| `AudioOutputType` / `AudioOutputConfig` | **없음** | 죽은 타입 |
| `AudioBus.Sfx` | **없음** | 연결만 되어 있음 |

---

## 6. 설정 / 에셋

**FMOD 통합**
- FMOD for Unity 2.02.33을 `Assets/Plugins/FMOD`에 벤더링했습니다(`fmod.cs:22`).
- 벤더 파일을 로컬에서 패치했습니다(`Platform.cs:871-878`).
- `FMODUnity.asmdef`가 autoReferenced입니다.
- 쓰지 않는 ResonanceAudio 애드온과 전 플랫폼 네이티브 라이브러리가 포함되어 있습니다.

**`Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset` 핵심값**

| 항목 | 값 |
|---|---|
| `BankLoadType` | 0 = All(:535). `MasterBanks: Master`(:551-552), `ImportType` = StreamingAssets(:538) |
| `LoggingLevel` / `EnableErrorCallback` | 2 = WARNING(:542, 에디터와 개발 빌드만) / **0**(:562) |
| playInEditor 플랫폼(:380-419) | 48000Hz, Real 256, Virtual 1024, LiveUpdate와 Overlay 켜짐 |
| default 플랫폼(빌드, :594~) | SampleRate, Real, Virtual 미설정. 따라서 Platform 기본값 **Real 32 / Virtual 128**(:616-630) |
| DSPBufferLength / Count | 모든 플랫폼이 256 / HasValue 1로 커밋됨(8ea2d73에서 변조 흔적). Count는 미설정(:631-636). 실제 적용값은 매 실행 PreInit이 덮어씀 |

**뱅크**
- Studio 프로젝트(`Assets/FMOD/sangcheol-odyssey`)에는 이벤트가 0개입니다. 빈 Master와 Master.strings 뱅크만 빌드됩니다.
- 이렇게 둔 이유는 Addressables 빌드가 실패하던 문제를 피하기 위해서입니다(8ea2d73).
- 빌드 시 뱅크는 `StreamingAssets` 루트로 복사됩니다(`Editor/EventManager.cs:748-788`). `.bank` 파일은 gitignore 대상입니다(`.gitignore:113-114`).
- Studio 프로젝트의 `.cache`/`.unsaved`가 git에 추적되고 있습니다.

**StreamingAssets 구조**
```
StreamingAssets/
├─ Music/     Lobby BGM.wav (PCM16 48k, 14.2MB, 루프 스트림)
│             Una Alarm.ogg, サバ！サマー！サンバ！.ogg, …_preview.ogg (Vorbis 48k)
│             Una Alarm_preview.wav (PCM24 44.1k — 다른 파일과 포맷이 다름)
├─ HitSound/  hit_perfect|master|ideal.wav (PCM16 mono 48k, 100ms), hit_kind|umm.wav (80ms)
├─ BGA/       비어 있음 (MusicSO가 BGA_0001/0002.mp4를 참조하지만 BGAController가 File.Exists로 건너뜀)
└─ Desktop/   빈 폴더 (.meta만 추적됨)
```
- `MusicSO`는 파일명만 담습니다(`MusicSO_0002.asset:194`). 로비 BGM 파일명은 `MainUI.prefab:939`에 있습니다.

**Unity 내장 오디오 상태**
- `ProjectSettings/AudioManager.asset:18`이 `m_DisableAudio: 1`이라 `AudioSettings.dspTime`은 의미 없는 값을 돌려줍니다.
- `ProjectSettings/ProjectSettings.asset:86`이 `runInBackground: 0`입니다.
- 리스너가 섞여 있습니다. MainScene 카메라에는 StudioListener가 있고(`MainScene.unity:252-265`), GameScene, ChartEditorScene, APITestScene에는 Unity AudioListener가 있습니다.
- GameScene에 고아 `AudioSource`가 남아 있습니다(`GameScene.unity:3231-3260`).
- BGA VideoPlayer는 `m_AudioOutputMode: 0(None)`, `m_TimeUpdateMode: 2(UnscaledGameTime)`입니다(`GameScene.unity:706-708`).

**그 밖의 패키지:** UniTask(`Packages/manifest.json:3`, a16d4a8)와 DOTween(3e859a0)이 최근 도입되었습니다. 리팩터 시 비동기 로드 API나 볼륨 페이드에 쓸 수 있습니다.

---

## 7. 검증된 문제점

### 심각도: 높음

**H1. 포커스를 잃으면 `mixerSuspend`로 DSP 클록이 멈추는데, 입력 동기점은 갱신되지 않아 복귀 후 판정이 밀림** (CORE-01, GP-02, SUI-07, CFG-01) *(런타임 확인 필요)*
- **위치:** `Assets/Plugins/FMOD/src/RuntimeManager.cs:813-822`, `Assets/Scripts/App/InputManager.cs:91`, `Assets/Scripts/App/GameManager.cs:144, 226`, `ProjectSettings/ProjectSettings.asset:86`
- **설명:**
  - `runInBackground: 0`이라 포커스를 잃으면 `OnApplicationPause(true)`가 옵니다. 이때 RuntimeManager가 데스크톱과 에디터에서도(`#else` 분기) `coreSystem.mixerSuspend()`를 호출해 DSP 클록이 멈춥니다. 반면 realtime(`ctx.time`의 기준)은 계속 흐릅니다.
  - Resume은 `globalStartTime`만 DSP 경과량으로 보정합니다. 입력 변환 `_dspAtSync + (ctxTime - _realtimeAtSync)`의 동기점은 `StartGame`에서 한 번만 잡힙니다.
  - 그 결과 복귀 후 모든 입력이 백그라운드에 머문 시간만큼 늦게 계산됩니다. 그 시간이 126ms를 넘으면 곡 나머지 전체가 Umm/miss가 됩니다.
  - ESC 일시정지는 두 클록이 함께 흐르므로 문제가 없습니다. 오디오 장치가 끊겨 믹서가 멈추는 경우에도 같은 현상이 생깁니다.
- **리팩터링 시 고려사항:**
  - Resume할 때(와 `OnApplicationPause(false)`에서) 동기점을 다시 잡거나, 주기적으로 (DSP, realtime) 쌍을 재샘플링해야 합니다.
  - 포커스 처리(3곳, M9)와 함께 설계해야 합니다.
  - 빌드에서 alt-tab 전후 `GetDSPTime`과 realtime을 로그로 비교해 실측하세요.

**H2. 리드인(0번 빈 마디) 중 일시정지 후 재개하면 BGM이 채보보다 앞서 나오고, 곡 끝까지 보정되지 않음** (CORE-02, GP-01, CE-05, GP-06 (1), CFG-18 부분)
- **위치:** `Assets/Scripts/App/FMODAudioManager.cs:224, 300-301`, `Assets/Scripts/App/GameManager.cs:146, 161, 226-227`, `Assets/Scripts/ChartEditor/Preview/EditorFMODAudio.cs:139-155`, `Assets/Scripts/Game/BGAController.cs:161-172`
- **설명:**
  - `setDelay`의 시작 클록은 부모 그룹 기준의 절대값이고, 채널을 pause해도 이동하지 않습니다. 부모 BGM 그룹의 클록은 계속 흐르기 때문입니다.
  - 재개 시각을 R, 예약 시작을 S, 일시정지 시각을 P라고 하면: 보통 R > S(재개 카운트다운이 3초)이므로 unpause 즉시 0초부터 재생되어 S−P만큼 앞섭니다. BPM이 80 미만이라 리드인이 3초보다 길면 R < S가 되고, 이때는 R−P만큼 앞섭니다.
  - `IsGameRunning=true`가 StartGame 직후에 켜지므로, 로딩 직후 alt-tab만 해도 재현됩니다.
  - BGA도 `Update`가 `IsPaused`를 확인하지 않아 일시정지 화면에서 영상이 시작되고, `scheduledDspTime`도 밀리지 않습니다.
  - 채보 에디터의 PlayFull도 구조가 같습니다.
- **리팩터링 시 고려사항:**
  - 재개할 때 남은 대기 시간으로 `setDelay`를 다시 걸거나, 대기 구간이면 stop 후 다시 예약하는 방식이 필요합니다.
  - BGM ChannelGroup 자체를 pause하는 방식도 있지만, 이 경우 `getDSPClock`의 parentclock 기준으로 바꿔야 합니다(L3).
  - 게임과 에디터를 공용 레이어에서 한 번에 고치세요. FMOD 2.02.33에서 짧은 재현 테스트를 권장합니다.

**H3. 저장한 출력 장치(`audioDeviceIndex`)가 부팅 시 복원되지 않음** (SUI-01, CORE-06, CFG-12)
- **위치:** `Assets/Scripts/App/SettingsManager.cs:72-78`, `Assets/Scripts/UI/Settings/SoundSettingUI.cs:151, 204-205`
- **설명:** `setDriver`를 호출하는 곳은 Save 버튼 하나뿐이고, `Apply()`는 볼륨만 적용합니다. 그래서 재시작하면 출력은 드라이버 0(OS 기본)인데, UI는 저장된 장치를 현재 장치처럼 보여 줍니다.
- **리팩터링 시 고려사항:**
  - 부팅 시 FMOD init 직후(`Managers.cs:55` 부근)에 복원해야 합니다.
  - 식별자는 인덱스 대신 GUID로 바꾸는 편이 안전합니다(M8).

**H4. 에디터 자동 채보가 음원 시간을 채보 시간으로 취급해 노트가 1마디 일찍 배치됨** (CE-01) — 에디터 한정
- **위치:** `Assets/Scripts/ChartEditor/Analysis/AutoChartGenerator.cs:40-47`
- **설명:**
  - 게임과 프리뷰는 모두 "음원 t=0이 채보 t=barDuration(1번 마디)"이라는 매핑을 씁니다(`ChartManager.cs:183`, `EditorPreviewManager.cs:148-153`).
  - 그런데 `onset.time`(음원 기준)을 그대로 마디와 비트 계산에 넣어서, 생성된 노트가 모두 음악보다 정확히 1마디 이르게 나옵니다.
  - 음원 첫 마디 구간의 onset은 0번 마디로 오인되어 버려집니다.
- **리팩터링 시 고려사항:** 음원 시간과 채보 시간 사이 변환 규칙이 세 곳에 흩어져 있습니다. 공용 시계 레이어 한 곳으로 모으세요.

### 심각도: 중간

**M1. 로드 실패 상태가 인터페이스에 없어 대기 루프가 무한히 돎** (CORE-04, GP-07 부분, SUI-05, CFG-16)
- **위치:** `FMODAudioManager.cs:128-132, 196-200`, `IAudioManager.cs:34`, `GameDataLoader.cs:52`, `MainUI.cs:81`, `AdventureUI.cs:167`
- **설명:**
  - `createSound`가 실패하거나 `OPENSTATE.ERROR`가 나면 로그만 남고 `IsLoaded`는 영원히 false입니다.
  - GameScene에서는 `StartGame`이 호출되지 않고, `IsGameRunning` 가드 때문에 Pause/Restart도 막혀 소프트락이 됩니다.
  - 로비에서는 좀비 코루틴이 남고, 다음 로드가 성공하면 `PlayScheduled`가 중복 호출됩니다.
  - ERROR 상태의 `_sound`는 다음 `LoadAudio` 전까지 해제되지 않습니다.
  - 파일명 오타나 경로 규약 착오(L10의 `"Music/"` 이중 접두사)도 곧바로 무한 로딩이 됩니다.
- **리팩터링 시 고려사항:** Loading/Ready/Failed 상태를 노출하고, UniTask/콜백과 취소, 타임아웃을 지원하세요. 호출자 세 곳의 폴링 루프를 하나로 모으세요.

**M2. DSP 클록 블록 양자화, 이중 샘플링, 단일 동기점 때문에 판마다 판정 편향이 다르고 드리프트가 누적됨** (CORE-03, GP-03, GP-05 부분, CE-11, GP-12, CE-12)
- **위치:** `FMODAudioManager.cs:311-312`, `GameManager.cs:140-144, 161`, `EditorTimeProvider.cs:27` 및 `EditorFMODAudio.cs:140`, `TimelineController.cs:90-92`, `ChartManager.cs:789`, `EditorFMODAudio.cs:168-173`
- **설명:**
  1. 음악 예약 기준(`:161`)과 채보 원점(`:140`)을 따로 읽습니다. 그 사이 P/Invoke와 `Debug.Log`가 끼어 있어 블록 경계를 넘으면 한 블록 어긋납니다. 에디터에도 같은 패턴이 있습니다.
  2. 양자화된 값 하나로 동기점을 잡으므로 0~1블록의 상수 편향이 판마다 무작위로 생깁니다. 버퍼가 1024면 21.3ms로 Perfect 판정 창 전체와 같습니다.
  3. 오디오 크리스털과 OS 클록 사이의 드리프트를 재보정하지 않습니다.
  4. 한 프레임 안에서도 호출처마다 DSP를 따로 조회하므로, 판정선과 판정이 서로 다른 시각을 볼 수 있습니다. 고주사율 화면에서는 판정선이 저더를 보입니다. 에디터 쪽은 매 호출마다 `getSoftwareFormat`도 다시 조회합니다.
- **리팩터링 시 고려사항:** 원점을 한 번만 읽어 공유하세요. 프레임당 1회 캐싱하고 realtime으로 보간한 단조 증가 곡 시계(SongClock)를 두고, 동기점을 주기적으로 재추정하세요.

**M3. DSP 버퍼 선택(64~1024 × 4)에 따라 출력 지연이 최대 약 50ms 달라지지만 자동 보정이 없음** (CFG-09)
- **위치:** `FMODAudioPreInit.cs:13, 19`, `FMODAudioManager.cs:103-105`, `GameManager.cs:159-161`
- **설명:**
  - `GetDSPTime`은 믹스 커서 위치입니다. 실제로 들리는 시각은 블록 × (count−1.5)에 OS 지연을 더한 만큼 늦습니다.
  - 버퍼를 바꾸면 기존 `audioOffsetMs` 보정값이 틀어지고, UI에 재시작 안내도 없습니다(L13).
  - WASAPI 공유 모드에서 64 샘플은 언더런 위험이 큽니다.
- **리팩터링 시 고려사항:** 측정된 출력 지연을 `IAudioManager`로 노출해 보정에 반영할지 결정해야 합니다.

**M4. `FMOD.RESULT`를 거의 전부 무시하고, 에러 콜백도 꺼져 있으며, NOSOUND 폴백을 알리지 않음** (CORE-10 부분, CFG-06, CE-09)
- **위치:** `FMODAudioManager.cs:70, 73, 78-84, 223-229, 289, 311, 333`, `FMODStudioSettings.asset:562`, `RuntimeManager.cs:212, 382-388`, `EditorFMODAudio.cs:31, 129-142`
- **설명:**
  - RESULT를 확인하는 곳은 `createSound`와 에디터의 `lock`뿐입니다.
  - 릴리스 빌드에서는 실패가 완전히 묻힙니다. 에디터와 개발 빌드에서는 WARNING 레벨 로그로 일부만 보입니다.
  - init이 실패하면 NOSOUND로 조용히 계속 진행되고, 반쯤 초기화된 매니저가 `IAudioManager`로 등록됩니다.
  - 에디터는 `getOpenState` ERROR의 실제 오류 코드도 버리고 사용자 경고도 띄우지 않습니다.
- **리팩터링 시 고려사항:** 리팩터 초기에 `EnableErrorCallback`을 켜세요. 공용 `Check(result, ctx)` 헬퍼를 두고, init 실패를 상위로 전달하는 경로를 만드세요.

**M5. `FMODAudioPreInit`가 벤더 파일 패치에 의존하고, 에디터에서 공유 Settings 에셋을 런타임에 바꿈** (CORE-08, CFG-03, CFG-02 부분, SUI-10 일부)
- **위치:** `FMODAudioPreInit.cs:36-50`, `Platform.cs:871-878`, `FMODStudioSettings.asset:631-636`
- **설명:**
  - `SetDSPBufferLength/Count`는 벤더 소스에 직접 추가한 메서드라서 FMOD를 업그레이드하면 컴파일이 깨집니다.
  - 에디터에서는 `Settings.Instance`가 실제 ScriptableObject라서, 개발자 로컬 PlayerPrefs 값이 에셋에 새어 커밋될 수 있습니다. 8ea2d73에서 모든 플랫폼의 DSPBufferLength가 0에서 256으로 바뀐 것이 그 흔적입니다.
  - 런타임 동작에는 영향이 없습니다. PreInit이 매 실행마다 init 전에 값을 덮어쓰기 때문입니다.
  - 공식 훅 `PlatformCallbackHandler.PreInitialize`는 `setDSPBufferSize` 뒤, `studioSystem.initialize` 앞에서 호출됩니다(`RuntimeManager.cs:372-381`). 그런데 이 훅은 쓰지 않고 있습니다.
- **리팩터링 시 고려사항:** 버퍼와 출력 타입(ASIO)을 이 훅(또는 System 직접 소유)으로 옮기면 벤더 패치, SubsystemRegistration 우회, 에셋 변조가 모두 사라집니다.

**M6. 로비, 프리뷰, 게임 곡이 소유권 없는 단일 BGM 슬롯과 전역 `Stop()`을 공유하고, 정리 동작이 이벤트 순서에 암묵적으로 의존함** (CORE-11 (6), GP-08 부분, SUI-04, GP-13)
- **위치:** `FMODAudioManager.cs:13-14`, `MainUI.cs:55-58`, `AdventureUI.cs:74-75`, `PauseUI.cs:93-97`, `GameManager.cs:256-260`, `UIManager.cs:163-178`, `GameDataLoader.cs:48-57`
- **설명:**
  - 현재 올바르게 동작하는 이유는 두 가지 순서 덕분입니다. (a) 새 UI의 `Awake`/`OnEnable`이 `LoadAudio`를 먼저 호출하고, 그 뒤 MainUI의 `OnDisable`이 `Stop()`을 호출합니다. (b) sceneLoaded가 `GameDataLoader.Start`보다 먼저 옵니다.
  - 로드가 동기화되거나 캐시가 생겨 `IsLoaded`가 즉시 true가 되면, MainUI의 `Stop()`이 방금 시작한 프리뷰를 꺼 버립니다.
  - R키 재시작 때는 씬이 로드되는 동안 이전 곡이 계속 들립니다.
  - `audioFilePath`가 비어 있는 곡이면 해제되지 않은 프리뷰 사운드가 게임 곡으로 재생되고, 곡 종료도 그 길이로 판정됩니다(GP-13).
- **리팩터링 시 고려사항:** 용도별 재생기(Music/Preview/Song)나 핸들/토큰 기반 소유권을 두세요. 게임플레이가 BGM 수명을 명시적으로 소유하게 하세요.

**M7. AdventureUI 프리뷰 코루틴이 중복 실행되고, 로딩 중인 사운드를 블로킹 release함** (SUI-03 부분, CORE-05 부분)
- **위치:** `AdventureUI.cs:50→107→148, 66, 199-200`, `FMODAudioManager.cs:174-179, 223`
- **설명:**
  - 처음 열 때 코루틴이 2회 실행되고, 곡을 바꿀 때마다 쌓이며 취소되지 않습니다.
  - 두 번째 `LoadAudio`는 아직 로딩 중(NONBLOCKING)인 사운드를 OPENSTATE 확인 없이 release하므로, 메인 스레드가 멈출 수 있습니다.
  - 로드가 끝나면 대기 중이던 코루틴이 모두 깨어나 같은 스트림에 `playSound`를 여러 번 호출합니다. 다만 FMOD가 채널을 빼앗으므로 들리는 영향은 작습니다.
- **리팩터링 시 고려사항:** 코루틴 핸들을 저장해 `StopCoroutine`하세요. 로드 API에 요청 식별자와 취소를 넣으세요.

**M8. 장치를 위치 인덱스로만 식별하고, 목록을 한 번만 캐싱하며, `setDriver`를 무조건 호출함** (SUI-02, CORE-06 일부)
- **위치:** `SoundSettingUI.cs:69-70, 204-205`, `FMODAudioManager.cs:327, 333`, `SettingsData.cs:26`
- **설명:**
  - `getDriverInfo`가 돌려주는 GUID를 버립니다. USB 장치를 꽂거나 빼면 인덱스가 다른 장치를 가리키게 됩니다.
  - 목록은 UI를 처음 열 때만 열거하고, DEVICELISTCHANGED 콜백도 없습니다.
  - Save 때마다 값이 바뀌지 않아도 `setDriver`를 불러 출력 글리치가 생길 수 있습니다. 결과 RESULT도 무시합니다.
- **리팩터링 시 고려사항:** GUID와 이름으로 저장하고, 부팅 시 복원(H3)과 장치 변경 콜백을 함께 설계하세요.

**M9. `playInBackground`가 사실상 동작하지 않고, 포커스 처리가 세 곳에 분산되어 있으며, 오디오가 설정을 역참조함** (CORE-07 부분, SUI-06, CFG-07 부분)
- **위치:** `FMODAudioManager.cs:108-113`, `RuntimeManager.cs:813-828`, `GameManager.cs:183-187`, `ProjectSettings/ProjectSettings.asset:86`
- **설명:**
  - `runInBackground=0`과 `mixerSuspend` 때문에 ON으로 설정해도 백그라운드에서 소리가 나지 않습니다. OFF일 때의 `setMute`는 중복입니다.
  - 인게임은 이 설정과 무관하게 무조건 Pause하므로, 영향 범위는 로비와 프리뷰입니다.
  - `FMODAudioManager`가 `ServiceLocator.Get<ISettingsManager>()`로 설정을 거꾸로 읽습니다. `Application.runInBackground`를 설정하는 코드는 없습니다.
- **리팩터링 시 고려사항:** 옵션을 살리려면 `runInBackground`와 연동하고 `mixerSuspend` 처리를 정리해야 합니다. 이 경우 H1의 동기점 처리도 같이 바뀝니다.

**M10. BGA가 시작 시점만 DSP에 맞추고, 이후 드리프트와 늦은 준비를 보정하지 않음** (GP-06, CFG-18 부분)
- **위치:** `BGAController.cs:69, 139-172`, `GameScene.unity:706`
- **설명:**
  - Prepare가 예약보다 늦게 끝나면 늦은 만큼 건너뛰지 않고 time=0부터 시작해 계속 뒤처집니다.
  - `skipOnDrop=false`에 Freerun 클록이라 디코드가 밀리면 지연이 쌓이고, DSP 기준 재동기가 없습니다.
  - 시작 판정은 프레임 단위이고, 믹서 클록 기준이라 출력 지연을 반영하지 않습니다.
  - 일시정지 무시 문제는 H2에서 다뤘습니다.
- **리팩터링 시 고려사항:** 곡 재생 위치나 SongClock을 노출해 영상 재동기의 기준으로 삼으세요.

**M11. `IAudioManager`에 FMOD 전제가 새어 나와, 곡 시간 계산이 호출자 여러 곳에 흩어짐** (CORE-11, GP-11, CE-03 부분, SUI-15 부분)
- **위치:** `IAudioManager.cs:27-44`, `GameManager.cs:161, 172, 226`, `BGAController.cs:165`, `InputManager.cs:91`, `EditorTimeProvider.cs:36-53`
- **설명:**
  - 절대 DSP 시각 모델, `StreamingAssets/Music` 하드코딩, loop 플래그 이원화(`loopHint=false`이면 `setLoopCount(-1)`이 먹지 않는데 경고도 없음), FMOD 드라이버 인덱스, 지킬 수 없는 `ConfigureOutput` 계약이 인터페이스에 드러나 있습니다.
  - 탐색(seek), PCM 접근, 로드 상태가 없어서 에디터가 인터페이스를 우회합니다.
  - 샘플 정확도가 필요 없는 UI 음악도 게임플레이용 예약 API와 폴링을 그대로 씁니다. 자세한 내용은 8장에 있습니다.

**M12. `EditorFMODAudio`가 `FMODAudioManager`를 거의 그대로 복제했고, 이미 두 구현이 벌어짐** (CE-02)
- **위치:** `EditorFMODAudio.cs:27-44 ↔ FMODAudioManager.cs:116-133`, `:73-105 ↔ :171-204`, `:145-161 ↔ :294-301`, `:168-173 ↔ :308-313`
- **설명:** 폴링, 로드, Stop/Pause/Resume, GetDSPTime이 복붙되어 있습니다. 게임 쪽에만 sampleRate 캐싱, 버스 그룹, `setPriority(0)`, 원샷 기능이 추가되었습니다.

**M13. 에디터 PCM 추출이 PCM16/PCMFLOAT만 지원해 24비트 WAV에서 자동 채보가 실패함** (CE-07 부분, CFG-11)
- **위치:** `EditorFMODAudio.cs:198, 224-244`
- **설명:**
  - `CREATESAMPLE`은 PCM WAV를 원본 포맷(PCM8/24/32) 그대로 보관합니다.
  - 저장소의 `Una Alarm_preview.wav`가 PCM24이고, DAW 기본 내보내기도 24비트인 경우가 많습니다.
- **리팩터링 시 고려사항:** 포맷별로 변환하거나, 허용 포맷 정책을 정하세요.

**M14. [종합 검토 중 추가 발견, 교차 검증 전] 자동 채보가 PCM 추출에 실패해도 기존 채보를 이미 지운 상태로 끝남** — 에디터 한정
- **위치:** `Assets/Scripts/ChartEditor/ChartEditorManager.cs:350, 363-367`, `EditorChartData.cs:78-89`
- **설명:**
  - `ChartData.Clear()`(`bars.Clear()`)를 `GetMonoSamples` **호출 전에** 실행합니다. 추출에 실패하면 경고만 띄우고 return하므로, 기존 마디 데이터가 사라집니다.
  - Undo 기능도 없습니다. M13(24비트 WAV)과 겹치면 쉽게 재현됩니다.
- **리팩터링 시 고려사항:** 분석이 성공한 뒤에 교체하도록 순서를 바꾸세요.

**M15. 판정 오프셋이 miss 컷오프에는 적용되지 않음** (GP-10) — 오디오 레이어 밖이지만 시간 모델 인접
- **위치:** `ChartManager.cs:891` 대 `:709`
- **설명:** 판정 창 중심은 `_judgmentOffsetSec`만큼 옮겨지지만, miss 처리는 오프셋 없이 `noteTime + JUDGE_UMM`입니다. +60ms 오프셋이면 늦은 쪽 판정 창이 66ms로 줄어듭니다.
- **리팩터링 시 고려사항:** 시간 모델을 정리할 때 오프셋 적용 지점을 하나로 모으세요.

### 심각도: 낮음

| ID | 제목 | 위치 | 요지 / 리팩터링 시 고려사항 |
|---|---|---|---|
| L1 | 일시정지/재개가 블록 경계에서 반영되고 위치 재동기가 없음 (CORE-12, GP-04 부분) | `FMODAudioManager.cs:300-301`, `GameManager.cs:193-194, 226-227` | 사이클마다 최대 ±1블록 오차가 랜덤워크로 누적됩니다. `getPosition`으로 재앵커링하거나 미래 DSP 클록에 unpause를 예약하세요. |
| L2 | 종료 해제 순서가 RuntimeManager 파괴 순서에 의존함 (CORE-09, 부분 확인) | `FMODAudioManager.cs:135-150`, `RuntimeManager.cs:750-753` | System이 먼저 해제되면 이미 해제된 핸들에 release를 호출할 수 있습니다. 명시적인 Shutdown 순서를 두세요. |
| L3 | GetDSPTime 기준(System Master)과 setDelay 기준(`_bgmGroup`)이 같다고 암묵적으로 가정 (CORE-14) | `FMODAudioManager.cs:15, 82-84, 220-224` | H2 해결책(그룹 pause)이나 배속, 피치를 도입하면 parentclock 기준으로 바꿔야 합니다. |
| L4 | `AudioSettings.dspTime` 폴백이 남아 있음. 내장 오디오가 꺼져 있고 기준점도 다름 (CORE-16, GP-14, CFG-08) | `InputManager.cs:90`, `BGAController.cs:165, 87`(옛 주석) | 틀린 값을 주는 폴백보다 명시적 실패가 낫습니다. `_hasSyncPoint`도 곡이 바뀔 때 초기화되지 않습니다. |
| L5 | `ConfigureOutput`/ASIO 관련 타입이 호출처 없는 죽은 코드 (CORE-13 부분, SUI-14, CFG-13) | `FMODAudioManager.cs:50-54, 89-91, 315-320`, `IAudioManager.cs:3-16, 38` | 출력 타입은 init 전에만 바꿀 수 있고 `Platform.OutputTypeName`은 internal입니다(`Platform.cs:95`). 제거하거나 pre-init 경로로 재설계하세요(M5). `ISettingsManager.ResetToDefault`도 쓰이지 않습니다. |
| L6 | PlayerPrefs 키, 버퍼 표, 파싱 경로 중복과 검증 부재 (CORE-15, SUI-08 부분, CFG-10 부분, SUI-09 부분) | `FMODAudioPreInit.cs:13, 22` 대 `SettingsManager.cs:10`, `SoundSettingUI.cs:48, 162`, `SettingsManager.cs:21-24` | 키를 올리면 PreInit이 조용히 256으로 돌아갑니다. `Load`에 try/catch가 없어 설정이 깨지면 오디오 매니저 생성 전에 부트가 중단됩니다. `:162`는 인덱스 범위 검사가 없습니다. |
| L7 | 원샷 세부 문제 (CORE-17, SUI-11) | `FMODAudioManager.cs:289-291`, `ChartManager.cs:753`, `SoundSettingUI.cs:90` | 재생 시작 후 `setVolume`을 거는 레이스가 있고, bus 경계 검사가 없으며, `playSound` 결과를 무시합니다. 언로드 API가 없어 스킨 TODO(`ChartManager.cs:110`)와 충돌합니다. Sfx 버스와 슬라이더는 실제 소리가 없습니다. |
| L8 | Studio를 쓰지 않는데 초기화하고, 에디터와 빌드 설정이 다르며, 리스너가 섞여 있고, 리포 위생 문제가 있음 (CORE-18, CFG-04 부분, CFG-05, CFG-14, CFG-15) | `FMODStudioSettings.asset:380-419, 535, 594-636`, `MainScene.unity:252-265`, `GameScene.unity:3231-3260, 4799` | 빌드는 Real 32 / Virtual 128이라 보이스 스틸링이 빌드에서만 나타날 수 있습니다. GameScene에는 StudioListener가 없어 경고가 1회 뜹니다. Studio의 `.cache`/`.unsaved`가 추적되고, 쓰지 않는 애드온이 있습니다. Studio를 유지할지 결정할 때 함께 정리하세요. |
| L9 | StreamingAssets 직접 경로라 데스크톱 전용 (CORE-19) | `FMODAudioManager.cs:184, 252-254` | Android(jar)와 WebGL에서는 동작하지 않습니다. 파일 소스를 추상화할지 결정하세요. |
| L10 | 경로 규약 주석 불일치와 방치된 테스트 코드 (CORE-20 부분, GP-15, CFG-17, SUI-14) | `MusicSO.cs:29`, `GameSceneTester.cs:11, 27`, `FMODAudioManager.cs:184` | 주석은 `"Music/…"`를 예로 들지만 구현이 `Music`을 이미 붙이므로, 주석대로 넣으면 `Music/Music/…`가 되어 M1(무한 대기)로 이어집니다. 경로 규약을 인터페이스 한 곳에 명시하세요. |
| L11 | 곡 종료를 `IsPlaying` 폴링으로 감지하고, 채보가 끝난 뒤 매 프레임 로그를 남김 (GP-09, 부분 확인) | `ChartManager.cs:195, 230, 276-279` | 로그만 고치면 종료 감지가 늦어집니다. 채널 END 콜백이나 곡 길이 기반 종료로 분리하세요. |
| L12 | 볼륨이 선형이고 미리듣기가 없음 (SUI-12) | `FMODAudioManager.cs:336-339`, `SoundSettingUI.cs:179-194` | 50%가 −6dB입니다. Save해야 반영되고, 설정 화면에서는 BGM이 꺼져 있어 변화를 들을 수 없습니다. |
| L13 | 버퍼 변경이 재시작해야 반영된다는 안내가 없고 실제 적용값도 표시하지 않음 (SUI-10) | `SoundSettingUI.cs:210-211` | Count 4는 하드코딩입니다. 실제 적용값은 로그로만 확인할 수 있습니다. |
| L14 | `Apply()`가 일괄 push 방식 (SUI-13, 부분 확인) | `SettingsManager.cs:40-84`, `Managers.cs:30` | 사운드를 저장해도 해상도까지 다시 적용합니다. "Apply는 IAudioManager 등록 뒤에"라는 순서 의존이 있고, `OnSettingsChanged` 구독자가 0입니다. |
| L15 | 에디터 기타 문제 (CE-04 부분, CE-06 부분, CE-08 부분, CE-10 부분, CE-13, CE-14, CE-15) | `EditorFMODAudio.cs:93, 129, 134-135, 228-257`, `EditorPreviewManager.cs:134-154`, `ChartFileIO.cs:16-17`, `EditorFilePanel.cs:129-130` | 전체 디코드와 3중 배열로 메모리가 튀고, `audioOffsetMs`가 적용되지 않습니다. 게임(ogg)과 다른 파일(mp3)을 쓰고, 채보에 음원 참조가 없습니다. 탐색 결과를 무시하고, System Master 직결로 볼륨과 음소거를 우회합니다. `audioFilePath`는 쓰기 전용 죽은 데이터이고, "타임라인만 재생" 주석이 동작과 다릅니다. |

---

## 8. 리팩터링 관점에서의 관찰

**중복된 로직**
- NONBLOCKING 로드 폴링 상태기계: `FMODAudioManager.cs:116-133`과 `EditorFMODAudio.cs:27-44`
- 대기 루프 `while(!IsLoaded)`: 호출자 세 곳(`GameDataLoader.cs:52`, `MainUI.cs:81`, `AdventureUI.cs:167`)
- UI 음악 재생 시퀀스(`Stop → Load → wait → PlayScheduled(now, loop)`): `MainUI`와 `AdventureUI`
- 일시정지 보정 산술: `GameManager.cs:171, 193, 226`과 `EditorTimeProvider.cs:36-53`
- 음원 시간과 채보 시간 변환(barDuration 오프셋): `ChartManager.cs:183`, `EditorPreviewManager.cs:153`, `AutoChartGenerator.cs:40`. 마지막 곳은 규칙을 어기고 있습니다.
- 설정 키, 버퍼 표, JSON 파싱: `FMODAudioPreInit`, `SettingsManager`, `SoundSettingUI`
- 포커스 처리: FMODAudioManager(뮤트), RuntimeManager(suspend), GameManager(자동 Pause)

**추상화 누수**
- `IAudioManager`가 "절대 DSP 초"를 입출력으로 노출합니다. 그 결과 곡 원점, 오프셋, 일시정지 보정, 입력 변환, BGA 예약을 GameManager, BGAController, InputManager, ChartManager가 각자 계산합니다. `StartAt / Pause / Resume / SongTime / InputTimeToSongTime / OnEnded` 같은 SongClock(Conductor) 계층이 없습니다.
- 음악 예약을 ChartManager.Init 안에서 GameManager.StartMusic으로 **역방향 호출**합니다. 원점과 예약을 한 곳이 소유하지 않습니다.
- 채널 pause가 곧 게임 일시정지라는 FMOD 고유 의미를 전제합니다. `setDelay`와의 상호작용(H2)도 호출자에게 숨겨져 있습니다.
- `IsPlaying`이 "재생 중"과 "곡 종료 판정"을 겸하고, `IsLoaded`는 "마지막 요청"의 상태만 뜻합니다.
- 오디오 → 설정 역의존: `ServiceLocator.Get<ISettingsManager>()`(`FMODAudioManager.cs:111`)
- 경로 정책(StreamingAssets/Music, StreamingAssets/HitSound, 에디터는 절대 경로)이 구현 곳곳에 흩어져 있습니다.
- 에디터는 인터페이스에 탐색, PCM, 로드 상태가 없어 FMOD 타입을 직접 다룹니다. 재생(CREATESAMPLE)과 분석이 사운드 하나에 묶여 있습니다.

**확장 포인트와 제약**
- `PlatformCallbackHandler.PreInitialize`(`Platform.cs:38-46`, `RuntimeManager.cs:372-379`)
  - `setDSPBufferSize` 뒤, System init 앞에서 호출되는 공식 훅입니다.
  - 버퍼, 출력 타입(ASIO), 드라이버 사전 설정을 벤더 패치 없이 여기서 처리할 수 있습니다.
- Studio를 제거하고 Core System을 직접 소유하는 경우
  - `coreSystem.update()`를 매 프레임 직접 불러야 합니다(현재는 `studioSystem.update()` 안에서 처리됨).
  - `mixerSuspend` 정책도 직접 정해야 합니다.
  - 빈 뱅크가 막아 주던 Addressables 빌드 실패(8ea2d73)를 다시 확인해야 합니다.
- FMOD 기능 중 아직 쓰지 않는 것
  - Channel END 콜백: 곡 종료 감지
  - `Channel.getPosition`: 재생 위치 기반 곡 시계, BGA 재동기
  - `getDSPClock`의 parentclock
  - `SYSTEM_CALLBACK_TYPE.DEVICELISTCHANGED`: 장치 목록 변경 감지
  - `getDriverInfo`의 GUID
  - `TIMEUNIT.PCM` 탐색
  - `Sound.readData`: 청크 디코드
- UniTask가 도입되어 있어 로드 API를 `UniTask<LoadResult>`나 취소 토큰 기반으로 바꿀 수 있습니다. DOTween은 볼륨 페이드와 크로스페이드에 쓸 수 있습니다.
- `AudioBus` enum 값이 `_busGroups` 배열 인덱스와 1:1이라(`FMODAudioManager.cs:86-87`), 버스를 추가하거나 순서를 바꿀 때 조용히 깨질 수 있습니다.

---

## 9. 열린 질문

1. **FMOD System 소유 방식:** RuntimeManager(Studio 포함)를 유지하면서 정리할 것인가(BankLoadType None, Listener와 Overlay 정리), 아니면 Core System을 직접 만들고 소유할 것인가? 후자라면 Addressables 빌드 문제를 어떻게 처리할 것인가?
2. **ASIO와 출력 타입 지원이 여전히 목표인가?** 목표라면 pre-init 경로(M5)와 SettingsData 필드(재시작 필요 설정)를 함께 설계해야 합니다.
3. **곡 시계의 기준을 무엇으로 할 것인가?** System Master DSP 클록인가, BGM 채널 재생 위치(`getPosition`)인가? 출력 지연(버퍼 × (count−1.5))을 자동 보정할 것인가, 계속 `audioOffsetMs`에 맡길 것인가?
4. **입력 동기점 재보정 정책:** Resume마다만 할 것인가, 주기적으로 재샘플링해 회귀할 것인가? 키보드 `ctx.time`이 OS 이벤트 시각인지 프레임 단위로 양자화되는지도 확인해야 합니다. `targetFrameRate` 60이면 해상도가 16.7ms입니다.
5. **FMOD 2.02.33 동작 실측 항목**
   - (a) `setDelay`로 대기 중인 채널을 pause한 상태에서 예약 시각이 지난 뒤 unpause하면 즉시 0부터 재생되는가? BGM 그룹을 pause하면 예약이 보존되는가?
   - (b) Windows 스탠드얼론에서 `runInBackground=0`일 때 포커스를 잃으면 `OnApplicationPause(true)`가 와서 `mixerSuspend`가 걸리는가?
   - (c) 로딩 중인 NONBLOCKING 사운드를 release하면 메인 스레드가 얼마나 멈추는가?
   - (d) 해제된 System의 핸들에 release를 호출하면 안전하게 `ERR_INVALID_HANDLE`이 나는가?
   - (e) 런타임 `setDriver` 이후에도 DSP 클록이 단조 증가하는가?
6. **`runInBackground=0`은 의도된 설정인가?** `playInBackground` 옵션을 살릴 것인가, 제거할 것인가?
7. **BGM 슬롯 구조:** 단일 슬롯을 유지할 것인가, 용도별 재생기(로비/프리뷰/게임 곡)로 나눌 것인가? 크로스페이드, 설정 화면에서 BGM 유지, 로비 BGM 이어 재생 같은 UX 요구가 있는가?
8. **원샷 정책:** 스킨별 타격음(`ChartManager.cs:110` TODO)을 위한 언로드와 교체 API를 둘 것인가? 32 슬롯 영구 상주 정책을 유지할 것인가? Sfx 버스(UI 효과음)를 실제로 도입할 것인가, 아니면 슬라이더와 함께 제거할 것인가?
9. **출력 장치:** GUID로 저장하고 복원할 것인가? 복원 시점은 init 직후로 충분한가, pre-init이어야 하는가? 장치 변경 콜백을 지원할 것인가?
10. **채보 에디터 범위:** `EditorFMODAudio`를 이번 리팩터 범위에 넣을 것인가(`TimelineController.cs:38`에 "처음부터 다시 만들 예정"이라는 주석이 있음)? 에디터에 `audioOffsetMs`, 볼륨, 타격음, 메트로놈을 적용할 것인가? 재생(STREAM)과 분석(청크 디코드)을 분리할 것인가?
11. **에셋 정책:** 대상 플랫폼은 Windows 데스크톱 전용인가? 허용할 음원 포맷(16/24/float 비트, 44.1k/48k 혼용)은 무엇인가? 에디터와 게임이 같은 음원 파일을 쓰게 할 것인가(채보 헤더에 음원 참조를 추가할지)?
12. **빌드 FMOD 설정:** 빌드의 Real 32 / Virtual 128, 샘플레이트 미설정을 playInEditor(256/1024/48k)와 맞출 것인가? 커밋된 `DSPBufferLength=256`을 초기화할 것인가?
13. **볼륨:** dB나 지각 곡선으로 매핑할 것인가? 드래그 중 실시간 미리듣기를 지원할 것인가?
