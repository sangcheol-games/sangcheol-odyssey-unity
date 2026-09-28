> **문서 안내**: 재설계 결정의 근거가 된 조사 요약(2026-09-25, 2차 조사). FMOD·Unity 동작에 대한 검증된 사실(F1~F37)과 출처, System 소유 방식 비교, 스파이크(SP1~SP15)의 근거가 있다. 이후 사용자 결정으로 바뀐 부분(예: 폴더 배치, setDriver 경로, 세이프 모드, 워치독)은 `Assets/Scripts/Audio/Audio_architecture.md`가 우선한다.

---

# 오디오 레이어 재설계 결정 브리프 (FMOD / ASIO / 판정 정확도)

- 기준: 2026-09-25, 브랜치 `refactor-FMOD`, FMOD for Unity **2.02.33**(`Assets/Plugins/FMOD/src/fmod.cs:22` `number = 0x00020233`), Unity 6000.3.13f1, Input System 1.19.0
- 범위: 게임 경로(MainScene, GameScene)의 오디오, 클록, 입력 타임스탬프, 판정. `Assets/Scripts/ChartEditor/**`는 범위 밖이며, 제약 조건으로만 다룹니다.
- 상태 표기
  - **확인**: 1차 자료(FMOD·Unity·MS 문서, 벤더 소스, 저장소)로 검증했습니다.
  - **부분**: 원래 주장 일부를 정정했습니다. 정정한 내용으로 적었습니다.
  - **추론**: 근거에서 이끌어 낸 결론이며 직접 확인하지는 않았습니다.
  - **미확인**: 확인할 수 없어 실측이 필요합니다(7절).
  - **직접 확인**: 이 브리프를 쓰면서 저장소를 다시 대조한 항목입니다.

---

## 0. 요약

1. **FMOD 소유 방식은 (B) Core System을 직접 소유하는 방식을 권고합니다.** 필요한 기능은 샘플 단위로 정확한 곡 시작, 최저 지연 타격음, ASIO, 런타임 출력 타입·장치 전환이며 모두 Core API 기능입니다. 지금 게임 코드도 Studio 기능을 하나도 쓰지 않습니다. 출력 타입, 버퍼, 샘플레이트를 런타임에 바꾸는 방법 중 문서로 보장된 것은 `close → 재설정 → init`뿐입니다. 이 경로는 Core만 소유할 때 가장 깔끔하게 쓸 수 있습니다.
2. **FMOD Studio는 지금 도입하지 않습니다.** Studio는 이벤트를 샘플 단위로 정확히 예약하지 못하고, 명령을 20ms 주기로 비동기 처리합니다. 판정과 지연 측면에서는 손해입니다. 사운드 디자이너가 Studio로 믹스를 직접 만드는 것이 제작 요구가 될 때만 하이브리드(C)로 넘어갑니다. 그 경우에도 곡, 타격음, SongClock은 Core에 둡니다.
3. **ASIO와 런타임 전환은 구현할 수 있습니다.** 변경을 세 등급으로 나눕니다: 즉시(볼륨), `setDriver`(같은 출력 타입 안에서 장치 변경), 전체 재초기화(출력 타입, 버퍼, 레이트, ASIO 블록). 전환은 게임플레이 밖(로비, 설정 화면)에서만 허용합니다. init 후 `setOutput`으로 바로 전환하는 빠른 경로는 FMOD 측이 권하지 않으므로, 실측을 통과했을 때만 켜는 기능 플래그로 둡니다.
4. **판정 정확도는 오디오보다 클록과 입력 쪽 문제가 큽니다.** 지금 구조는 동기점 1개, 약 10ms 단위로 계단식으로 오르는 WASAPI DSP 클록, 프레임 단위로 양자화되는 키보드 타임스탬프, 드리프트 미보정, 출력 지연 미보정이 겹쳐 있습니다. 이 오차가 Perfect 창(±21ms)을 잠식합니다. 필요한 것은 연속 앵커 회귀로 만드는 SongClock, epoch 모델, 타임스탬프 기반 JudgeEngine입니다. Raw Input 스레드는 2단계에서 스파이크를 통과하면 도입합니다.
5. **착수 전 게이트 스파이크는 두 개입니다.** SP1(Core 호스트 수명주기와 에디터 정리), SP2(Unity 메인 스레드 COM 아파트먼트와 ASIO init)입니다.

---

## 1. 핵심 사실

결정에 영향을 주는 사실만 모았습니다. 번호(F#)는 뒤 절에서 참조합니다.

### 1-1. FMOD System 수명주기와 재초기화

| # | 사실 | 출처 · 인용 | 상태 |
|---|---|---|---|
| F1 | `setDSPBufferSize`, `setSoftwareFormat`, `setSoftwareChannels`는 init 전이나 close 후에만 호출할 수 있습니다. 따라서 버퍼, 샘플레이트, ASIO 블록 크기를 바꾸려면 재초기화가 필요합니다. | fmod.com/docs/2.02/api/core-api-system.html#system_setdspbuffersize: "It must be called before System::init, or after System::close." | 확인 |
| F2 | `System::close`는 그 System으로 만든 Sound, ChannelGroup, DSP를 무효화합니다. pre-init 설정은 남으므로 다시 init할 수 있습니다. `release`는 내부적으로 close를 호출합니다. | core-api-system.html#system_close: "Closing renders objects created with this System invalid… All pre-initialize configuration settings will remain and the System can be reinitialized as needed." | 확인 |
| F3 | Studio System 밑의 Core만 close/init하는 것은 지원되지 않습니다. Studio와 Core를 모두 release한 뒤 다시 만들어야 하고, 기존 객체는 전부 잃습니다. Unity에서 `RuntimeManager.CoreSystem.close()` 뒤 assert가 난 사례가 있습니다. | qa.fmod.com/t/15922 (cameron-fmod, 2020): "tearing down the Core System out from underneath the Studio API, which is not supported." | 확인 |
| F4 | RuntimeManager는 자동으로 부트스트랩되지 않습니다. 처음 접근할 때 숨김 DDOL GameObject로 지연 생성됩니다. 공식 재초기화 API는 없습니다. `OnDestroy`가 static 상태(`initException`, `instance`)를 초기화하므로, GameObject를 `DestroyImmediate`하면 다음 접근 때 다시 초기화됩니다. | `Assets/Plugins/FMOD/src/RuntimeManager.cs:157-212`, `:750-762` (`initException = null; instance = null;`) | 확인(코드). 파괴 후 재초기화가 누수나 크래시 없이 되는지는 **미확인** |
| F5 | RuntimeManager는 `studioSystem.initialize`가 실패하면 `outputType = NOSOUND`로 `goto retry`합니다. 이때 `PlatformCallbackHandler.PreInitialize`를 다시 호출하고, 반환된 `initResult`는 쓰지 않아 조용히 무음 모드로 동작합니다. 두 번째도 실패하면 예외가 static에 캐시되어 이후 접근마다 다시 던져집니다. | `RuntimeManager.cs:372-390` ("defaulting to no-sound mode" … `goto retry;`), `:151-153`, `:212` | 확인, 직접 확인 |
| F6 | FMOD for Unity 설정 UI에서 Windows 출력으로 고를 수 있는 것은 WASAPI와 WinSonic뿐이고 ASIO는 없습니다. `OutputTypeName`, `GetOutputType`, `FindCurrentPlatform`은 internal입니다. 공개된 사전 초기화 훅은 `PlatformCallbackHandler.PreInitialize` 하나입니다. | `platforms/win/src/PlatformWindows.cs:154-157`, `Platform.cs:38-46, 95`, `Settings.cs:498`. fmod.com/docs/2.02/unity/examples-callback-handler.html: "FMOD will now call the PreInitialize method on your object just before initializing the FMOD Studio system." | 확인 |

### 1-2. 출력 타입과 장치

| # | 사실 | 출처 · 인용 | 상태 |
|---|---|---|---|
| F7 | Windows에서는 `setOutput`을 init 후에도 호출할 수 있다고 문서에 적혀 있습니다. 그러나 FMOD 스태프는 WASAPI→ASIO를 init 후에 전환하는 것을 권하지 않습니다. init 후 `setOutput`은 항상 driver 0으로 전환하고, 그 장치가 실패하면 NOSOUND로 떨어집니다. `OK`를 반환하면서 NOSOUND가 된 로그도 있습니다. | core-api-system.html#system_init: "System::setOutput / System::setOutputByPlugin can be called before or after System::init on … Windows". qa.fmod.com/t/14047 (brett, 2019): "you should forget that feature exists". qa.fmod.com/t/12318 (mathew, 2022): "we switch to device zero". 같은 스레드 로그: "setOutput to ASIO : OK … getOutput Result : NOSOUND" | 부분(정정 반영) |
| F8 | init 후 `setDriver`를 호출하면 현재 드라이버를 내리고 새 드라이버를 시작합니다. FMOD 스태프가 안내하는 일반적인 장치 변경 경로입니다. 재생 중인 채널이 유지되는지, DSP 클록이 이어지는지는 문서에 없습니다. | core-api-system.html#system_setdriver: "the current driver will be shutdown and the newly selected driver will be initialized / started." qa.fmod.com/t/22448 (Leah_FMOD, 2025) | 부분(채널·클록은 **미확인**) |
| F9 | `getNumDrivers`와 `getDriverInfo`는 현재 선택된 출력 타입의 장치만 돌려줍니다. ASIO 목록을 보려면 그 System이 `setOutput(ASIO)` 상태여야 합니다. 초기화하지 않은 별도 System으로 열거하는 방식을 권장합니다. | core-api-system.html#system_getnumdrivers: "…available for the selected output type." qa.fmod.com/t/12318 (mathew, 2022): "use a new (uninitialized) System object to build the list" | 확인 |
| F10 | 장치는 GUID로 식별해야 합니다. 목록이 바뀌면 인덱스가 밀립니다. 마지막 장치를 빼면 출력이 NOSOUND로 바뀌고 `Guid.Empty`인 "NoSound Driver" 하나만 보입니다. ASIO도 1.08.01부터 GUID를 줍니다. | getDriverInfo: "A GUID that uniquely identifies the device." qa.fmod.com/t/17294, t/21742 (li_fmod, 2026) | 확인 |
| F11 | System 콜백을 `DEVICELISTCHANGED`나 `ALL` 마스크로 등록하면 FMOD의 자동 장치 전환이 꺼집니다. System당 콜백은 하나만 저장됩니다. C# `setCallback`의 기본 마스크가 `ALL`이라, 마스크를 빠뜨리면 자동 전환이 꺼집니다. | core-api-system.html: "will disable any automated device ejection/insertion handling". `fmod.cs:1125` `callbackmask = SYSTEM_CALLBACK_TYPE.ALL` | 확인, 직접 확인 |
| F12 | 장치 콜백(`DEVICELISTCHANGED`, `DEVICEREINITIALIZE`)은 Core API나 Studio 동기 모드에서는 update를 부른 스레드에서 옵니다. Studio async 모드(RuntimeManager 기본값)에서는 Studio Update Thread에서 옵니다. `ERROR` 콜백은 지연 실행된 async 함수의 오류도 포함하므로 호출 스레드를 가정하면 안 됩니다. | FMOD_SYSTEM_CALLBACK_TYPE: "Called from the main (calling) thread when set from the Core API… and from the Studio Update Thread when in default / async mode." ERROR: "including delayed async functions". `RuntimeManager.cs:321`(SYNCHRONOUS_UPDATE 없음) | 확인 / ERROR 스레드는 부분 |
| F13 | `DEVICELISTCHANGED`를 등록한 상태에서 마지막 장치가 빠지면 NOSOUND로 떨어지고 자동으로 복구되지 않습니다. 콜백에서 `setOutput`으로 복구하라는 것은 스태프가 "should be able to attempt"라고 제안한 수준이며 검증되지 않았습니다. FMOD는 이 동작을 버그 후보로 검토 중입니다. | qa.fmod.com/t/21742 (li_fmod, 2026): "it does not automatically switch back" | 부분 |
| F14 | Unity에서 `DEVICELISTCHANGED` 콜백 안에서 `getNumDrivers`/`getDriverInfo`를 부르다 에디터가 멈춘 사례가 있습니다. 해결책은 콜백에서 플래그만 세우고 메인 스레드에서 처리하는 것이었습니다(FMOD는 재현하지 못함). | qa.fmod.com/t/19021 (Reification, 2022): "Set (in a thread-safe way) a flag to trigger updating devices on the main Unity thread." | 확인 |

### 1-3. ASIO

| # | 사실 | 출처 · 인용 | 상태 |
|---|---|---|---|
| F15 | ASIO는 `fmodstudio.dll` 안에 들어 있어 별도 DLL이 필요 없습니다. Windows ARM64에서는 지원되지 않습니다. | FMOD_OUTPUTTYPE: "FMOD_OUTPUTTYPE_ASIO Win - Low latency ASIO 2.0." platforms-win.html(ARM64): "The following are not supported: ASIO output mode". `platforms/win/lib/{x86,x86_64,arm64}` | 확인 |
| F16 | 한 프로세스에서 동시에 활성화할 수 있는 ASIO 인스턴스는 하나뿐입니다. 이미 쓰고 있으면 `FMOD_ERR_OUTPUT_ALLOCATED`가 납니다. | revision history 1.05.06: "This is not supported by ASIO and is now disabled." qa.fmod.com/t/24274 로그: "You can only have a single instance of ASIO active at a time." `fmod_errors.cs:67` | 확인 |
| F17 | ASIO는 장치를 독점할 수 있습니다. 그래서 개발 중에 다른 WASAPI·ASIO 앱(FMOD Studio 툴, DAW 등)과 동시에 쓰지 못할 수 있으며, 드라이버마다 다릅니다. | qa.fmod.com/t/15894 (brett, 2020): "you cannot have a WASAPI and ASIO application (or 2 ASIO applications) running simultaneously" | 확인(드라이버 의존) |
| F18 | C# 래퍼로 ASIO를 쓰려면 FMOD가 STA 스레드에서 돌아야 합니다. WASAPI도 열거와 init을 UI 스레드에서 호출한다고 가정합니다. COM을 초기화하지 않으면 FMOD가 경고와 함께 필요할 때 초기화합니다. Unity 메인 스레드가 STA인지는 1차 자료로 확인하지 못했습니다. 다만 Unity 메인 스레드에서 ASIO 초기화에 성공한 실무 사례는 여럿 있습니다. | platforms-win.html: "FMOD will need to be running on the STA thread to ensure COM is intialized correctly." | 확인 / Unity STA는 **미확인** |
| F19 | ASIO 버퍼 크기는 사용자가 드라이버 제어판에서 정하고, FMOD API로는 조회할 수 없습니다. FMOD 블록 크기는 드라이버가 보고하는 min~max 범위 안에 있으면서 granularity의 배수여야 합니다. 맞으면 로그에 "low latency mode"가 찍히고 버퍼 개수가 2로 강제됩니다. 맞지 않을 때 2.02.33이 추가 버퍼링으로 동작하는지는 문서에 없습니다. COM으로 `IASIO::getBufferSize`를 직접 호출하는 우회 방법은 있습니다. | qa.fmod.com/t/14079 (brett, 2018): "it is up to the user to set the buffer size, not the programmer", "within ASIO's min … max … a multiple of the granularity". t/22347 로그(2.02.25): "low latnecy mode enabled. Forcing DSP buffer count = 2." | 부분 |
| F20 | ASIO는 1.07.07부터 요청한 샘플레이트와 버퍼를 받아들이도록 개선됐습니다. 드라이버가 요청 레이트를 거부하면 실패하는지 리샘플하는지는 확인하지 못했습니다. | revision history 1.07.07 | 신뢰도 낮음 / **미확인** |
| F21 | FMOD 2.02.33에는 관련 ASIO 수정이 모두 들어 있습니다(2.02.18, 2.02.25 "crash with certain ASIO driver after System::close", 2.02.27 빈 CLSID). Unity 6.3 LTS는 2.02 지원 목록에 있으므로 2.03 업그레이드가 필요 없습니다. | revision history, fmod.com/docs/2.02/unity/welcome.html | 확인 |

### 1-4. 타이밍과 지연

| # | 사실 | 출처 · 인용 | 상태 |
|---|---|---|---|
| F22 | 대기 중인 Core 명령은 core premix에서 실행되고 DSP 클록도 premix에서 갱신됩니다. 그래서 `getDSPClock`은 믹스 블록 단위로 오르는 계단 함수이고, `playSound`는 다음 premix에 반영됩니다. | studio-api-threads.html: "It is the core premix that executes any enqueued core commands and updates DSP clocks." | 확인 |
| F23 | FMOD의 WASAPI 출력은 shared 모드로만 초기화되고, 드라이버 수준의 최소 버퍼가 10ms입니다. DSP 버퍼를 더 줄여도 시간 해상도는 약 10ms에 머뭅니다. exclusive 모드는 1.x 시절에 이미 제거됐습니다. | qa.fmod.com/t/22650 (Leah_FMOD, 2025): "minimum buffer size of 10ms (e.g. 480 samples at 48kHz)". t/11949 (2015): "We removed support for WASAPI exclusive mode in FMOD 5." ('FMOD 5'는 2.x만이 아니라 Studio 시대 Core 전체를 가리킴) | 확인(세대 표기는 정정) |
| F24 | FMOD 문서가 주는 지연 계산은 소프트웨어 믹서 링버퍼 몫인 `(numbuffers − 1.5) × bufferlength`뿐입니다. OS나 하드웨어 출력 지연을 조회하는 API는 없습니다. WASAPI에서만 `getOutputHandle`이 `IAudioClient` 포인터를 주고, ASIO는 그 표에 없습니다. | setDSPBufferSize: "the latency is typically more like the (number of buffers - 1.5) multiplied by the buffer length." qa.fmod.com/t/14097: "there isn't a way to query the latency through FMOD". getOutputHandle: "FMOD_OUTPUTTYPE_WASAPI Pointer to type IAudioClient is returned." | 확인 |
| F25 | `setDelay`는 부모 ChannelGroup의 DSP 클록을 기준으로 합니다. 샘플 단위로 정확히 예약하려면 `parentclock`을 읽어야 합니다. 지금 코드는 **시스템 마스터 그룹** 클록으로 시작 시각을 계산한 뒤 `_bgmGroup` 자식 채널에 `setDelay`합니다. `addGroup`의 기본값이 `propagatedspclock = true`라서 값이 같을 가능성은 높지만, 런타임으로 확인하지는 않았습니다. | core-api-channelcontrol.html: "To perform sample accurate scheduling … query the parentclock value." `FMODAudioManager.cs:220-224, 311`. `fmod.cs:2654` `addGroup(ChannelGroup group, bool propagatedspclock = true)` | 문서는 확인 / 현재 코드 영향은 **추론·미확인**, 직접 확인 |
| F26 | `setPaused`는 그 객체의 `getDSPClock`과 `getPosition`을 멈춥니다. 부모 그룹을 pause하면 자식도 멈춥니다. 대기 중인 `setDelay`가 있는 채널을 채널 단위로 멈췄을 때의 동작은 문서에 없습니다. | core-api-channelcontrol.html: "Pause halts playback which effectively freezes Channel::getPosition and ChannelControl::getDSPClock values." | 확인 / 대기 delay 동작은 **미확인** |
| F27 | `Channel::getPosition`은 비동기로 갱신되는 섀도 위치이므로, 프레임마다 쓰는 시간원으로는 적합하지 않습니다. | qa.fmod.com/t/20268 (Leah_FMOD, 2023): "grabs a shadow position, which is updated asynchronously" | 확인 |
| F28 | Core API는 기본 설정에서 스레드 안전하므로 입력 스레드에서 바로 `playSound`를 호출할 수 있습니다. | managing-resources-in-the-core-api.html: "the API can be called from any game thread at any time." | 확인 |
| F29 | Studio API에는 샘플 단위로 정확한 이벤트 스케줄링이 없습니다. 기본 async 모드에서 명령은 다음 `Studio::System::update` 끝에 제출되고, Studio 처리는 20ms 주기로 돕니다. 동기 모드에서도 한 블록 뒤로 예약합니다. | qa.fmod.com/t/21414 (jeff_fmod, 2024): "We don't have any sample accurate event scheduling exposed in the FMOD Studio API". studio-api-threads.html: "the Studio processing occurs every 20ms" | 확인 |
| F30 | Unity Input System에서 `ctx.time`은 `realtimeSinceStartup` 타임라인 값입니다. Windows에서는 Unity 저수준 코드가 이벤트를 **처리하는 시점**의 성능 카운터로 찍습니다. 키보드는 사실상 프레임당 한 번 수집됩니다. `pollingFrequency`는 Win32에서 XInput 패드에만 적용됩니다. 한 프레임 안의 이벤트는 시각순으로 정렬되지 않습니다. | discussions.unity.com/t/862248 (Unity staff, 2021): "timestamped using the performance counter at the time we get around to processing the event". `com.unity.inputsystem@…/InputSystem.cs:1352-1353`: "On Win32, for example, only XInput gamepads are polled." `InputSystem.cs:2299` | 확인 / Unity 6000.3의 펌프 순서는 **미확인** |
| F31 | Windows Raw Input은 장치 클래스마다 프로세스당 한 창만 등록할 수 있습니다. Unity 6000.3은 직접 받은 Raw Input을 Unity로 돌려주는 `UnityEngine.Windows.Input.ForwardRawInput`을 제공합니다. | learn.microsoft.com RegisterRawInputDevices: "Only one window per raw input device class may be registered…". docs.unity3d.com/6000.3/…/Windows.Input.ForwardRawInput.html | 확인 |
| F32 | `runInBackground: 0`이고, RuntimeManager는 `OnApplicationPause`에서 `mixerSuspend`와 `mixerResume`을 호출합니다. 그런데 스탠드얼론에서 `OnApplicationPause`가 온다는 보장은 창 모드이면서 전체 화면보다 작을 때뿐입니다. suspend 중 DSP 클록이 어떻게 되는지는 문서에 없습니다. | `RuntimeManager.cs:813-828`, `ProjectSettings/ProjectSettings.asset:86`. Unity 6000.3 OnApplicationPause: "the running Player must be windowed and smaller than the full screen." | 부분 / 클록 동작은 **미확인** |

### 1-5. 코드베이스

| # | 사실 | 출처 | 상태 |
|---|---|---|---|
| F33 | 게임 오디오는 순수 Core만 씁니다. 자체 ChannelGroup(Master/BGM/HitSound/SFX)을 두고, BGM은 `CREATESTREAM`에 paused 상태 `playSound` 후 `setDelay`를 걸며, 타격음은 `CREATESAMPLE` 원샷입니다. Studio에는 빈 Master 뱅크만 로드되는데, Addressables 빌드 오류를 피하기 위한 것입니다. | `FMODAudioManager.cs:42-47, 78-84, 190-229`, 커밋 `8ea2d73` | 확인 |
| F34 | 게임 경로에서 RuntimeManager를 초기화시키는 곳은 `FMODAudioManager.Awake`(`RuntimeManager.CoreSystem`)와 MainScene의 `StudioListener` 둘뿐입니다. 범위 밖인 `EditorFMODAudio.cs:23`도 `RuntimeManager.CoreSystem`을 씁니다. 하지만 **ChartEditorScene은 EditorBuildSettings에 없고, 런타임 코드 어디서도 그 씬을 LoadScene하지 않습니다.** 따라서 FMOD System이 두 개 생기는 문제는 에디터에서만 생깁니다. | `ProjectSettings/EditorBuildSettings.asset:7-16`, `Assets/Scripts`의 `LoadScene(` grep 결과(GameScene, MainScene만 있음), `MainScene.unity:261` | 확인, 직접 확인 |
| F35 | 입력 시각을 DSP 시각으로 바꾸는 동기점은 StartGame 때 한 번만 잡습니다. 재개나 mixerResume 때 다시 잡지 않고 드리프트도 보정하지 않습니다. 동기점이 없을 때의 대체값은 `AudioSettings.dspTime`인데, Unity 내장 오디오가 꺼져 있어 의미가 없습니다. | `InputManager.cs:81-92`, `GameManager.cs:144`, `ProjectSettings/AudioManager.asset:18` `m_DisableAudio: 1` | 확인 |
| F36 | 음원 예약과 차트 원점이 서로 다른 DSP 읽기에서 나옵니다. 두 읽기 사이에 믹스 블록이 넘어가면 최대 1블록 어긋납니다. | `GameManager.cs:161`(StartMusic 안), `:140`(`globalStartTime`) | 확인(코드) / 영향 크기는 추론 |
| F37 | 장치는 인덱스로 저장되고 부팅 때 적용되지 않습니다. 버퍼는 `FMODAudioPreInit`과 벤더 패치(`Platform.cs`의 `SetDSPBufferLength/Count`)로 주입하는데, 그 결과가 `FMODStudioSettings.asset`에 커밋되기까지 했습니다. | `SoundSettingUI.cs:202-205`, `SettingsManager.cs:71-78`, `Platform.cs:871-878`("[SCOdyssey 추가]"), 커밋 `8ea2d73` | 확인 |

### 1-6. 조사 결과 사이의 상충과 처리

| 상충 | 처리 |
|---|---|
| 코드베이스 조사는 ChartEditor 호환을 이유로 RuntimeManager 유지(A) 쪽으로 기울었고, 소유 방식 조사는 B를 권고했습니다. | 직접 확인한 결과 ChartEditorScene은 빌드에 포함되지 않고 런타임에 로드되지도 않습니다(F34). 두 번째 System 위험은 에디터에만 있으므로 B 권고를 유지합니다. 타이밍·코드베이스 조사의 "PreInitialize에서 적용" 권고는 A를 전제로 한 것이라, B에서는 자체 init 코드로 옮깁니다. |
| ASIO 단일 인스턴스: 타이밍 조사는 FMOD 1차 출처를 찾지 못했다고 했습니다. | 출력 조사가 revision history 1.05.06과 로그로 확인했으므로 **확인**으로 채택합니다(F16). |
| 그룹 클록이 로컬 값인지 부모에서 전파된 값인지 | `propagatedspclock = true`가 기본값입니다(직접 확인, F25). 전파될 가능성이 높지만 런타임으로 확인하지 않았습니다. 설계에서는 절대값에 의존하지 않고 같은 객체의 클록 차이만 씁니다(SP4). |
| 스트림 이벤트의 schedule delay가 약 170ms인지 약 85ms인지 | 해결하지 못했습니다. Studio로 곡을 재생하지 않는다는 결론에는 영향이 없습니다. |
| StudioListener와 FMODAudioManager 중 누가 먼저 초기화하는가 | `StudioListener.cs.meta`의 `executionOrder: 80`을 직접 확인했습니다. Managers(0)가 먼저 돕니다. 이 순서는 암묵적이며, B에서는 StudioListener를 제거하므로 문제가 없어집니다. |
| 기본 DSP 버퍼가 1024×4인지 512인지, 2.03 문서끼리도 서로 모순됨 | 기본값에 의존하지 않고 항상 명시적으로 설정합니다. |

---

## 2. FMOD System 소유 방식 비교

| 항목 | (A) RuntimeManager 유지 + 래핑 | (B) Core System 직접 소유 | (C) Studio System 직접 소유(+Core) |
|---|---|---|---|
| 초기화 제어(출력 타입, 버퍼) | `PlatformCallbackHandler.PreInitialize`에서 setOutput, setDriver, setDSPBufferSize, setSoftwareFormat을 덮어쓸 수 있습니다(F6). NOSOUND retry 때 핸들러가 다시 불리므로 재시도를 감지하는 코드가 필요합니다(F5). 에디터 UI에는 ASIO가 없습니다. | 전부 직접 합니다. 순서는 create → setOutput → setDriver(GUID로 해석) → setSoftwareFormat → setDSPBufferSize → init이고, 폴백 체인도 직접 설계합니다. | B와 같습니다(`Studio.System.create` → `getCoreSystem` → Core 설정 → `initialize`). |
| 런타임 출력 타입 전환(WASAPI↔ASIO) | 공식 경로가 없습니다. (1) RuntimeManager GameObject를 `DestroyImmediate`하고 다시 접근하는 비공식 방법(F4, 누수 여부 **미확인**)이나 (2) init 후 `setOutput`(FMOD 비권장, F7)을 써야 합니다. Core만 close하는 것은 금지입니다(F3). | `close → 재설정 → init`이 문서화된 경로입니다(F1, F2). init 후 `setOutput`은 선택적인 빠른 경로로만 둡니다. | Studio와 Core를 모두 release하고 다시 만들어야 하며, 뱅크, 버스, Channel을 전부 재구성합니다(F3). |
| 런타임 장치 전환 | `setDriver` 가능(F8) | `setDriver` 가능 | `setDriver` 가능 |
| 레이턴시 | Core 재생 경로는 현행과 같습니다. 쓰지 않는 Studio async 스레드와 빈 뱅크가 함께 돕니다(판정 경로 영향은 추론상 없음). | 가장 좋습니다. `playSound`가 다음 premix에 반영되고(F22), Studio 명령 큐 지연이 없습니다. | 곡과 타격음을 Core Channel로 재생하면 B와 같습니다. Studio 이벤트로 내면 update 양자화(최대 20ms)와 1블록이 더해집니다(F29). |
| 장치 콜백 스레드 | Studio Update Thread(F12) | update를 부른 스레드, 즉 메인 스레드(F12) | async면 Studio 스레드, 동기면 메인 스레드 |
| 에디터 통합, Live Update, Profiler | 모두 유지됩니다(도메인 리로드 정리, 에디터 음소거, 오버레이, Live Update). | Studio Live Update는 없습니다(쓸 Studio 콘텐츠도 없음). `FMOD_INIT_PROFILE_ENABLE`로 Core 프로파일링은 할 수 있습니다(core-api-system.html FMOD_INITFLAGS). 도메인 리로드와 플레이 종료 정리는 직접 구현해야 합니다. | Live Update와 Profiler를 쓸 수 있지만 LIVEUPDATE 플래그와 뱅크 경로 해석(internal API)을 직접 처리해야 합니다. |
| 유지보수, FMOD 업그레이드 부담 | 벤더 패치는 PreInitialize로 없앨 수 있습니다. 대신 RuntimeManager 내부 동작(retry, mixerSuspend, OnDestroy 정리)에 기대므로 업그레이드마다 다시 검증해야 합니다. 현재 로컬 RuntimeManager는 2.02 원본과 같습니다. | C# 래퍼(`fmod.cs`)와 네이티브 DLL에만 의존하므로 의존 표면이 가장 작습니다. FMOD for Unity 패키지는 바이너리, 래퍼, 빌드 처리용으로 남깁니다. | B에 더해 Studio API와 뱅크, Studio 툴 버전 정합성까지 관리해야 합니다. |
| 구현 비용 | 낮습니다. | 중간입니다(호스트, update 드라이버, 수명주기 상태머신, 콜백 허브, 폴백). | 높습니다(B에 더해 뱅크, 버스, `lockChannelGroup`+`flushCommands`, Studio 수명주기). |
| 리스크 | 재초기화 해킹의 누수나 크래시가 검증되지 않았습니다. retry에서 ASIO를 다시 강제하면 영구 실패합니다(F5). 포커스를 잃을 때 mixerSuspend가 강제됩니다(F32). 전역 static 접근이 ServiceLocator 원칙과 맞지 않습니다. | 에디터 도메인 리로드 때 해제가 누락되면 ASIO가 다음 세션에서 실패합니다(F16). RuntimeManager가 우발적으로 초기화되면(StudioListener, ChartEditor) System이 두 개가 됩니다. STA 여부가 확인되지 않았습니다(F18). | B의 리스크에 더해 모노 다운믹스 함정, 버스 ChannelGroup 생성 순서, NRT 전환 제약(F12 맥락), 2.02 소형 버퍼 스케줄링 잔여 이슈가 있습니다. |

### 권고: (B) Core System 직접 소유

근거는 다음과 같습니다.
1. 필요한 기능은 모두 Core API이고, 지금도 Studio 기능을 쓰지 않으므로 기능 손실이 없습니다(F33, F29).
2. 출력 타입, 버퍼, 레이트를 런타임에 바꾸는 문서화된 경로는 `close → init`뿐입니다(F1, F2). A와 C에서는 이 경로를 쓸 수 없거나(F3) 비용이 큽니다.
3. ASIO → WASAPI → NOSOUND 폴백을 RuntimeManager의 retry 구조(F5)에 끼워 맞추지 않고 명시적으로 설계할 수 있습니다.
4. 장치 콜백이 메인 스레드에서 오고(F12), 포커스와 suspend 정책을 GameManager와 일관되게 직접 정할 수 있습니다(F32).
5. 벤더 패치(F37)가 없어지고 FMOD 의존 표면이 가장 작아집니다.
6. ChartEditor와의 System 이중화는 에디터에서만 생기는 문제입니다(F34).

B를 실행하기 위한 필수 조치는 다음과 같습니다.
- MainScene의 `StudioListener`를 제거합니다.
- 게임 코드에서 `FMODUnity.RuntimeManager` 참조를 금지합니다. C# 래퍼가 같은 `FMODUnity` asmdef 안에 있어 asmdef로는 막을 수 없으므로 코드 리뷰나 분석기로 막습니다.
- 부트 시와 씬 로드 시 `RuntimeManager.IsInitialized`를 검사합니다. 이 검사는 초기화를 일으키지 않습니다.
- 빈 Master 뱅크와 FMOD 설정 에셋은 Addressables 빌드용으로 남깁니다.
- 호스트는 전역 `RuntimeInitializeOnLoadMethod`가 아니라 **MainScene의 Managers에서 생성**합니다. 그래야 에디터에서 ChartEditorScene을 직접 열었을 때 System이 하나만 뜹니다.
- 호스트가 대신 맡아야 할 RuntimeManager의 책임이 있습니다. 매 프레임 `system.update()`, `AssemblyReloadEvents.beforeAssemblyReload`, `AppDomain.DomainUnload`, `playModeStateChanged(ExitingPlayMode)`, `OnApplicationQuit`에서의 release, 에디터와 개발 빌드의 `FMOD.Debug.Initialize` 로그 라우팅입니다. 로그 라우팅이 없으면 ASIO 진단 로그를 볼 수 없습니다. 릴리스 DLL에는 로깅이 없습니다.

게이트: SP1(수명주기)과 SP2(STA, ASIO init)를 통과해야 합니다. SP2가 실패해도 A와 B 모두 메인 스레드에서 init하므로 소유 방식 결정은 바뀌지 않습니다. 대신 ASIO 지원 방식을 다시 검토해야 합니다.

### 결정이 뒤집히는 조건

| 조건 | 전환 |
|---|---|
| 전담 사운드 디자이너가 합류해 UI SFX 변주, 스냅샷, 덕킹을 Studio에서 직접 만드는 것이 제작 요구가 된다 | **C(하이브리드)**로 갑니다. 곡과 타격음은 계속 Core Channel이며 Studio Bus의 ChannelGroup에 재생합니다. B에서 `IMixBus` 이음새를 지켜 두면 전환 비용이 줄어듭니다. |
| 출력 타입과 버퍼 변경을 "재시작 후 적용"으로 완화하고 장치 변경만 런타임 `setDriver`로 해도 된다고 결정한다, 또는 SP1이 실패한다 | **A**가 최소 비용 해법입니다. PreInitialize 핸들러에 retry 감지와 WASAPI 폴백을 넣습니다. |
| 모바일이나 콘솔로 플랫폼을 넓힌다 | RuntimeManager의 플랫폼별 초기화 가치가 커지므로 **A나 C를 다시 평가**합니다. |
| 루프백 실측(SP13)에서 Studio 이벤트 지연이 타격음에서도 체감되지 않는다 | C의 비용 대비 효용이 오릅니다. 그래도 곡 시계는 Core에 둡니다. |

---

## 3. FMOD Studio 도입 필요성

| Studio 기능 | 이 게임에 주는 것 | 비용과 제약 | 근거 |
|---|---|---|---|
| 이벤트로 타격음 재생 | 없습니다(오히려 손해). | async update 양자화 0~20ms와 한 블록 뒤 예약이 더해집니다. `flushCommands`로 줄이면 메인 스레드가 블록됩니다. 스태프도 리듬게임 지연이 예상보다 클 수 있다고 인정했습니다. | F29. qa.fmod.com/t/20973 (Leah_FMOD, 2023): "you may observe a higher amount of latency than you expect" |
| 곡 스케줄링 | 불가능합니다. | 샘플 단위로 정확한 이벤트 시작이 없습니다. 이벤트 ChannelGroup에 `setDelay`를 거는 것은 Studio 자체 스케줄을 덮어쓰는 해킹입니다. 스트림 이벤트의 schedule delay는 85~170ms로, 문서와 스태프 설명이 어긋나 미해결입니다. | F29. qa.fmod.com/t/16222 (joseph, 2020): "using ChannelControl::setDelay would override those delays" |
| Programmer sound(런타임 곡 파일) | 가능하지만 이점이 없습니다. | 콜백이 Studio 스레드에서 옵니다. async instrument는 seek할 수 없습니다. 공식 예제는 트리거 시점에 로드합니다. 모노 다운믹스 보고가 있습니다. 스태프도 사용자 음악은 Core로 재생해 Bus ChannelGroup에 붙이라고 권합니다. | qa.fmod.com/t/20210 (Leah_FMOD, 2023): "directly use the Core API … play the Core API Sound on that ChannelGroup" |
| 버스, VCA, 스냅샷, 사이드체인 | 사운드 디자이너가 만들 때만 가치가 있습니다. | 현재 요구(볼륨 4개, UI SFX, 로비 BGM과 프리뷰)는 Core ChannelGroup 볼륨과 fade point로 충족됩니다. Core Sound는 Bus로 라우팅해야 Studio 믹서에 보입니다. | fmod.com/docs/2.02/studio/mixing.html, qa.fmod.com/t/21079 |
| Live Update, Profiler | Studio 콘텐츠가 없으면 이점이 작습니다. | Core에서는 `FMOD_INIT_PROFILE_ENABLE`로 대체할 수 있습니다. | core-api-system.html FMOD_INITFLAGS |
| 비트, 마커 콜백 | 판정 시간원으로 부적합합니다. | `DEFERRED_CALLBACKS`로 메인 스레드 update에서 오므로 프레임 단위로 양자화됩니다. | studio-api-eventinstance.html, `RuntimeManager.cs:321` |
| 출력, ASIO, 장치 | 필요 없습니다. | 오히려 콜백이 Studio Update Thread로 가고, 재초기화할 때 Studio와 Core를 모두 다시 만들어야 합니다. | F3, F12 |

**권고: 지금은 도입하지 않습니다.**
- 곡, 타격음, SongClock은 어떤 경우에도 Core API로 둡니다.
- 나중에 도입한다면 형태는 C입니다. Studio는 UI SFX와 믹스, 스냅샷, 덕킹을 맡고, 곡과 타격음은 `Bus.getChannelGroup`에서 Core Channel로 재생합니다. 이때 준비 순서는 `loadBank → getBus → lockChannelGroup → flushCommands(로딩 단계) → getChannelGroup`입니다. 버스 입력 포맷은 Stereo로 명시합니다.
- 이 전환에 대비해 백엔드 경계를 `IAudioEngine` + `IMixBus` + `IVoice/ISoundAsset`로 설계합니다. Studio를 도입할 때는 `IMixBus` 구현만 바꾸면 되도록 합니다.

---

## 4. ASIO와 런타임 출력 장치·타입 변경

### 4-1. 실현 가능성

- ASIO는 x64에서 추가 바이너리 없이 쓸 수 있습니다(F15). ARM64에서는 UI에서 숨깁니다.
- 장치 변경은 런타임 `setDriver`로 할 수 있습니다(F8).
- 출력 타입 변경은 B에서 `close → init`으로 할 수 있습니다(F1, F2).
- init 후 `setOutput` 빠른 경로는 문서상 허용되지만 FMOD가 권하지 않고, 조용히 NOSOUND로 떨어지는 사례가 있습니다(F7). 기본값은 끄고, SP7을 통과했을 때만 켜는 기능 플래그로 둡니다.

### 4-2. 변경 등급

| 등급 | 대상 | 방식 | 허용 시점 |
|---|---|---|---|
| 1. 즉시 | 버스 볼륨, 음소거 | ChannelGroup 볼륨 | 언제나 |
| 2. setDriver | 같은 출력 타입 안에서의 장치 변경 | `setDriver(GUID로 해석한 인덱스)` 후 클록과 지연 재동기화(epoch++) | 곡 사이(로비, 설정 화면). SP5가 실패하면 3등급으로 올립니다. |
| 3. 전체 재초기화 | 출력 타입(WASAPI↔ASIO), DSP 버퍼 길이와 개수, 믹서 샘플레이트, ASIO 블록과 채널 매핑 | 아래 트랜잭션 | 곡 사이만 |

이 구조로 바꾸면 지금의 "다음 실행 시 적용"(`SoundSettingUI.cs:210` 주석)을 "곡 사이 즉시 적용"으로 바꿀 수 있습니다.

### 4-3. 재초기화 트랜잭션 (B 기준)

1. **전제 확인**: 진행 중인 GameScene 곡 세션이 없어야 합니다. 설정 변경 전에 세이프 모드 플래그(`pendingSwitch`)를 별도 PlayerPrefs 키에 기록합니다. 변경 중 크래시가 나면 다음 부팅은 WASAPI 기본 장치로 엽니다.
2. `OnEngineReconfiguring` 이벤트를 보냅니다. 소비자는 복원할 상태를 스냅샷합니다(로비 BGM 위치, 볼륨 등).
3. 모든 Channel을 멈추고 Sound, ChannelGroup, DSP를 release합니다. 캐시된 핸들을 무효화하고 엔진 세대 번호를 올립니다. 지금 코드는 `_coreSystem`, `_masterGroup`, `_sampleRate`를 Awake에서 한 번만 캐싱합니다(`FMODAudioManager.cs:64-92`). 외부에는 FMOD 핸들 대신 슬롯 ID나 추상 핸들만 노출합니다.
4. `system.close()`를 호출합니다.
5. 설정을 적용합니다: `setOutput(type)` → DeviceCatalog에서 GUID를 인덱스로 해석 → `setDriver` → `getDriverInfo`로 systemrate를 읽고 `setSoftwareFormat(systemrate, …)` → `setDSPBufferSize(len, count)`. ASIO라면 필요할 때 `setAdvancedSettings`로 `ASIOSpeakerList`를 지정합니다(`Marshal.AllocHGlobal`, init이 끝날 때까지 유지).
6. 메인 스레드에서 `init`합니다.
7. **검증**: `getOutput`, 현재 GUID, `getDSPBufferSize`(ASIO에서 개수가 2로 강제됐는지), `getSoftwareFormat`을 확인합니다. 결과가 OK여도 실제 상태를 반드시 확인합니다(F7). 실패하거나 결과가 다르면 폴백 체인을 탑니다: 요청 설정 → WASAPI와 저장된 GUID → WASAPI driver 0 → NOSOUND(UI 경고). `ERR_OUTPUT_ALLOCATED`나 `ERR_OUTPUT_INIT`이면 "다른 앱이 장치를 사용 중" 안내를 띄웁니다(F16, F17).
8. ChannelGroup 트리를 재구성하고, 원샷 세트를 다시 로드하고, 볼륨을 다시 적용하고, 콜백을 **명시적 마스크로 다시 등록**합니다. close 후 콜백이 유지되는지는 확인하지 않았으므로 항상 다시 등록합니다.
9. `OnEngineReinitialized(epoch, OutputInfo)` 이벤트를 보냅니다. AudioClock과 LatencyInfo를 리셋하고, 장치별 캘리브레이션 프로필로 교체하고, UI에 실제 적용된 값을 표시합니다.
10. 세이프 모드 플래그를 지우고 설정을 확정 저장합니다.

SP6에서 close/init이 특정 드라이버에서 불안정하면, 같은 트랜잭션에서 `close/init` 대신 `release → 새 System create`로 바꿉니다. System을 우리가 소유하므로 비용은 같습니다.

### 4-4. 곡 클록이 전환을 넘기는 방식

- 원칙은 **곡 도중에는 전환하지 않는 것**입니다. 따라서 SongClock은 전환을 넘길 필요가 없습니다. 재초기화 뒤 DSP 클록은 0부터 다시 시작한다고 가정합니다(**미확인**, SP6). SongClock은 epoch 안에서의 클록 차이만 씁니다.
- 곡 도중에 장치를 잃으면(핫플러그) 자동으로 일시정지합니다. 복구(setDriver 또는 재초기화)하면 epoch가 바뀝니다. 재개는 "예약 재시작"으로 합니다: pause 상태에서 `setPosition` → 준비 완료 확인 → 미래 `parentclock`에 `setDelay` → unpause. 스트림은 seek할 때 reflush가 느릴 수 있으므로 프리롤 시간을 확보합니다(core-api-channel.html#channel_setposition).
- 로비 BGM은 스냅샷해 둔 위치에서 다시 시작합니다.

### 4-5. 버퍼와 샘플레이트 처리

아래 링버퍼 지연은 48kHz에서 FMOD 공식으로 계산한 값이며 실제 지연이 아닙니다(F24).

| 설정 | 블록 | 링버퍼 추정 (count−1.5)×블록 | 비고 |
|---|---|---|---|
| 256 × 4 (현재 기본, `audioBufferIndex=2`) | 5.33ms | 약 13.3ms | WASAPI에서는 엔진 주기 10ms 하한(F23) |
| 480 × 4 | 10ms | 약 25ms | WASAPI 주기와 맞는 값 |
| 512 × 4 | 10.67ms | 약 26.7ms | |
| 1024 × 4 | 21.3ms | 약 53.3ms | |
| ASIO (개수 2 강제) | 드라이버 값 | 공식상 0.5블록이라 의미 없음(추론) | 사용자 캘리브레이션에 의존 |

- **WASAPI**: 64와 128은 10ms 주기 아래로 내려가지 못해 효과가 제한적일 것으로 봅니다(추론). CPU와 언더런 위험만 늘릴 수 있습니다. 프리셋은 256/480/512/1024를 권장하고, 최종값은 SP3 결과로 정합니다. FMOD가 10ms 하한 때문에 요청값을 내부적으로 바꾸는지는 **미확인**이므로 init 후 `getDSPBufferSize`를 표시합니다.
- **ASIO**: 블록 크기 선택 UI에 "ASIO 제어판 값과 같게"라는 안내를 붙입니다. FMOD로는 조회할 수 없습니다(F19). 맞지 않으면 저지연 모드가 꺼지거나 init이 실패할 수 있습니다. 필요하면 COM으로 `IASIO::getBufferSize`를 읽어 preferred 값을 추천하는 기능을 선택적으로 둡니다(구현 비용이 있어 결정 사항 D6).
- **샘플레이트**: init 전에 선택한 장치의 `systemrate`로 믹서 레이트를 맞춥니다. 레이트가 다르면 지연이 늘 수 있습니다(core-api-system.html#system_setsoftwareformat: "may incur extra latency"). ASIO에서 레이트가 맞지 않을 때의 동작은 **미확인**입니다(F20, SP8). 런타임 `setDriver`로 레이트가 다른 장치로 옮겨 가면 "곡 사이 재초기화 권장" 플래그를 세우고, `getSoftwareFormat`을 다시 조회해 캐시를 갱신합니다.

### 4-6. 장치 저장과 열거

- 저장 형식은 `(outputType, deviceGuid, deviceName, systemRate)`입니다. `setDriver`나 init 직전에 **매번 GUID로 인덱스를 다시 찾고**, 이름은 보조 키로 씁니다. GUID가 없으면 driver 0으로 대체하고 UI에 알립니다. `Guid.Empty`는 "장치 없음"으로 취급합니다(F10).
- 현재 표시할 장치는 `getDriver`에 의존하지 않고 앱이 추적한 GUID로 보여 줍니다(F11 맥락: 콜백을 override하면 `getDriver`가 갱신되지 않음).
- 열거(DeviceCatalog)
  - 현재 출력 타입은 메인 System으로 열거합니다.
  - 다른 타입은 초기화하지 않은 임시 System으로 `setOutput → getNumDrivers/getDriverInfo → release`합니다(F9).
  - **메인이 ASIO일 때는 ASIO 임시 System을 만들지 않습니다**(F16). 메인이 ASIO일 때 WASAPI 임시 열거가 안전한지는 SP9로 확인합니다.
  - ASIO 열거는 드라이버 COM 객체를 생성하므로 비용이 크고 드라이버 품질에 좌우됩니다. 설정 화면을 열 때만 메인 스레드에서 하고, 드라이버별 실패는 격리해 로그로 남깁니다(qa.fmod.com/t/22347 `CoCreateInstance returned 0x80040154`).

### 4-7. 핫플러그와 콜백

| 모드 | 등록 마스크 | 동작 |
|---|---|---|
| Follow-Default (GUID 비움 = driver 0) | `DEVICEREINITIALIZE \| OUTPUTUNDERRUN \| ERROR` (`DEVICELISTCHANGED` 제외) | FMOD 자동 전환에 맡깁니다(F11). `DEVICEREINITIALIZE`를 받으면 재동기화(epoch++)하고 UI를 갱신합니다. |
| Pinned (GUID 고정) | 위 마스크 + `DEVICELISTCHANGED` | GUID가 사라지면 driver 0으로 폴백합니다. `getOutput == NOSOUND`면 재초기화로 복구합니다. 콜백에서의 `setOutput` 복구는 검증되지 않았으므로(F13), 주기적으로 NOSOUND를 확인하는 복구 경로를 함께 둡니다. 원래 GUID가 돌아오면 복귀할지 사용자에게 묻습니다. |

콜백 구현 규칙은 다음과 같습니다.
- `[AOT.MonoPInvokeCallback]` static 메서드를 쓰고, 델리게이트는 static 필드에 붙잡아 둡니다(`RuntimeManager.cs:117-118, 355-356`과 같은 패턴).
- 본문에서는 `Interlocked`로 비트 플래그만 세웁니다. **FMOD API, Unity API, Debug.Log는 호출하지 않습니다**(F14, LoggerDriver 재진입 가드).
- 메인 스레드 `Update`에서 플래그를 소비해 열거, setDriver, UI 갱신, 재동기화를 처리합니다.
- `setCallback`의 마스크는 반드시 명시합니다(기본값이 ALL, F11).
- 곡 도중 장치 이벤트가 오면 게임을 자동 일시정지합니다.

### 4-8. ASIO 전용 UX 제약

- x64에서만 노출합니다.
- System 생성, init, 열거, setOutput, setDriver는 메인 스레드에서만 호출하도록 스레드 가드를 둡니다. UniTask 스레드풀 전환은 금지합니다. 시작할 때 `CoGetApartmentType`을 로깅합니다(F18).
- `extradriverdata`(HWND)는 기본으로 `IntPtr.Zero`를 넘깁니다(RuntimeManager와 공식 예제도 같음). 필요한 드라이버에 대비해 창 핸들을 넘기는 옵션만 남겨 둡니다.
- 개발 워크플로 가이드: ASIO 사용 중에는 FMOD Studio 툴, DAW, 다른 게임과 장치 충돌이 날 수 있습니다(F17).
- 포커스 손실: B에서는 mixerSuspend 여부를 우리가 정합니다. ASIO를 다시 잡는 데 실패할 수 있으므로(다른 앱이 점유) 복귀 후 출력이 살아 있는지 검사합니다(SP14).

---

## 5. 정확한 판정과 레이턴시를 위한 요구사항

### 5-1. 현재 구조의 오차 예산 (Perfect 창 ±21ms)

| 오차원 | 크기 | 현재 코드 | 근거 · 상태 |
|---|---|---|---|
| 키보드 타임스탬프의 프레임 양자화 | 0~1프레임(60fps에서 0~16.7ms, 평균 +8.3ms) | 영향을 받습니다(Unity 입력만 사용). | F30. 이 빌드에서는 **미측정**(SP11) |
| 동기점 1개와 DSP 계단 | 0~1스텝(WASAPI는 약 10ms) 크기의 고정 편향 | 영향을 받습니다(`InputManager.cs:81-92`). | F23, F35, 추론 |
| DSP와 QPC 사이 드리프트 | 100ppm이면 3분 곡에서 약 18ms(계산값) | 보정하지 않습니다. | 크리스털 오차 ±30~50ppm(learn.microsoft.com acquiring-high-resolution-time-stamps). 오디오 장치 드리프트는 **실측 필요**(SP3) |
| 음원 예약과 차트 원점의 이중 DSP 읽기 | 0~1블록 | 영향을 받습니다. | F36 |
| mixerSuspend 이후 매핑 붕괴 | suspend된 시간만큼 | 다시 동기화하지 않습니다. | F32, F35. 클록 정지 여부는 **미확인** |
| 출력 지연 미보정 | 수 ms에서 수십 ms | 사용자가 오프셋으로 수동 보정합니다. | F24 |
| 홀드 몸통 판정의 프레임 샘플링 | 히치가 42ms를 넘으면 창을 건너뜁니다. | `ChartManager.cs:789` | 확인(코드) |
| miss 컷오프에 오프셋 미적용 | 오프셋만큼 늦은 쪽 창이 사라집니다. | `ChartManager.cs:891` | 확인(코드) |

### 5-2. SongClock 요구사항

- **C1. 벽시계 기준 하나**: 모든 벽시계 계산은 `Stopwatch.GetTimestamp()`(QPC)로 합니다. `AudioSettings.dspTime`, `Time.*`, `realtimeSinceStartup`은 타이밍 계산에 쓰지 않습니다. `InputManager.cs:90`과 `BGAController.cs:165`의 대체 경로는 없앱니다.
- **C2. 곡 도메인**: 곡에 동기화되는 모든 오디오(BGM, 선예약 타격음, 키음)를 전용 `SongGroup`(ChannelGroup) 아래에 둡니다. 예약은 대상 채널의 `parentclock`(= SongGroup 클록) 기준으로 하고, 마스터 클록 값을 섞지 않습니다(F25). 절대값에 의존하지 않고 같은 객체의 클록 차이만 씁니다.
- **C3. 앵커 하나**: 음원 시작 샘플과 차트 0초를 **한 번의 DSP 읽기**로 함께 정합니다(F36 해결). 모든 `setDelay` 시작 시각은 현재 parentclock + max(2블록, 스트림 프리롤) 이상으로 잡고, 과거 시각을 예약하지 않도록 막습니다(F22).
- **C4. 연속 앵커와 회귀**: `(dspClock, QPC)` 쌍을 계속 모읍니다. 1안은 FMOD `MIDMIX` 콜백("Called from the mixer thread after clocks have been updated", 할당·락·로그 없이 락프리 링버퍼에 기록)이고, 폴백은 메인 스레드에서 클록이 바뀌는 순간을 검출하는 방식입니다. 최근 32~64점으로 강건 선형회귀를 해 기울기(실효 샘플레이트, ppm)를 추적하고, 잔차가 1블록을 넘는 점은 버립니다. Rhythm Quest(rhythmquestgame.com/devlog/04.html)가 Unity에서 같은 방식을 씁니다. MIDMIX 관리 코드 콜백이 GC 때문에 언더런을 일으키는지는 **미확인**입니다(SP15).
- **C5. 평가기 두 개**
  - `SongTimeAt(qpc)`: 판정 전용입니다. 회귀 모델을 그대로 평가하며 결정적이어야 합니다.
  - `RenderSongTime(frame)`: 렌더용입니다. osu! `InterpolatingFramedClock` 방식을 따릅니다. QPC 경과로 보간하고, 모델 쪽으로 반감기 50~80ms로 수렴하고, 오차가 약 2블록 또는 30ms를 넘으면 스냅하며, 단조 증가를 보장합니다. 평가 시점은 예상 표시 시각입니다. 근거: github.com/ppy/osu-framework `InterpolatingFramedClock.cs`, osu `FramedBeatmapClock.cs:76-81` "on windows there's generally only 5-10ms reporting intervals".
  - 한 프레임 안의 모든 소비자는 프레임 시작 때 한 번 만든 스냅샷을 읽습니다. 지금은 소비자마다 P/Invoke로 새로 읽습니다(`FMODAudioManager.cs:308-313`).
- **C6. epoch 경계**: Start, Pause, Resume, Seek, MixerSuspend/Resume(포커스 손실), OUTPUTUNDERRUN, DEVICEREINITIALIZE, setDriver/setOutput, 재초기화는 모두 epoch 경계입니다. 경계에서는 앵커를 버리고 새로 적합하며 렌더 시계를 스냅합니다. 입력 이벤트에는 epoch id를 붙입니다.
- **C7. 일시정지와 재개**: 채널이 아니라 SongGroup을 pause합니다(F26). pause 시점의 곡 시간은 "들리는 시간"(`songTimeMix − L_out`)으로 고정합니다. 리드인 중 pause처럼 대기 중인 delay가 걸린 경우의 동작은 SP4로 확인합니다. 실패하면 예약 재시작 방식으로 갑니다.
- **C8. 샘플레이트 재조회**: 장치나 출력을 바꾼 뒤와 재초기화 뒤에는 `getSoftwareFormat`을 다시 읽습니다.

### 5-3. 입력 타임스탬프

Unity Input System이 Windows에서 줄 수 있는 것과 없는 것:

| 줄 수 있음 | 줄 수 없음 |
|---|---|
| `ctx.time`: `realtimeSinceStartup` 타임라인, Windows에서는 QPC 기반(F30) | 물리 입력 시각(OS가 장치나 드라이버 타임스탬프를 주지 않음, F30 출처) |
| XInput 패드에 대한 `pollingFrequency` 폴링 | 키보드의 프레임 이하 해상도. 프레임당 1회 펌프이므로 `inputPollingRateHz`(`SettingsManager.cs:81`)는 키보드에 효과가 없습니다. |
| 한 프레임 분량의 이벤트 일괄 처리 | 이벤트 시각순 정렬(직접 정렬해야 함, F30) |
| `ProcessEventsManually` 등으로 처리 시점 조정 | 처리 시점을 바꿔도 타임스탬프 찍는 시점이 개선되는지는 1차 근거가 없습니다(추론상 개선되지 않음). |

주의할 점도 있습니다. Game 맵을 Disable하면(`Pause()`의 `SwitchToUI`) 누르고 있던 레인이 `canceled`되고, 그 time에는 현재 시각이 들어갑니다(`InputActionState` `ResetActionState`). 이 이벤트가 IsPaused 검사 없이 `TryJudgeRelease`까지 갑니다(`GameManager.cs:243-254`).

**권장 접근(단계적)**
- **I1 (1단계, 필수)**: `IInputTimestampSource`로 추상화합니다. 이벤트 형식은 `{lane, isDown, qpc, sourceId, epoch}`입니다. `UnityInputSystemSource`는 `ctx.time`을 QPC 도메인으로 바꿉니다(`realtimeSinceStartupAsDouble`과 Stopwatch의 오프셋을 주기적으로 샘플링). 해상도가 프레임 간격이라는 사실은 메타데이터로 노출합니다. 이 단계에서는 높은 FPS 운영을 유지합니다. 지금 vSync를 끈 이유와 같습니다(`SettingsManager.cs:43-49`).
- **I2 (2단계, SP11·SP12 통과 시)**: `WindowsRawInputSource`를 추가합니다.
  - 전용 스레드에 메시지 전용 창을 만들고, `WM_INPUT`을 받는 즉시 QPC를 찍습니다. `GetRawInputBuffer`로 모아 읽습니다.
  - 원본 RAWINPUT은 메인 스레드에서 `ForwardRawInput`으로 Unity에 넘겨 UI 입력이 계속 동작하게 합니다(F31).
  - Unity가 등록을 되가져가는지 감시하고 다시 등록합니다.
  - 저수준 키보드 훅(`WH_KEYBOARD_LL`)은 쓰지 않습니다. 타임아웃을 넘기면 조용히 제거되고, MS가 Raw Input을 권장합니다(learn.microsoft.com lowlevelkeyboardproc).
  - 입력 이벤트에 메시지 시간(`GetMessageTime`)은 쓰지 않습니다. ms 단위 틱이라 부정확합니다.
- **I3**: 입력 스레드는 타임스탬프를 찍어 SPSC 큐에 넣기만 합니다. 판정은 메인 스레드 프레임 시작에서 타임스탬프 순으로 소비합니다. 그러면 CharacterAnimator의 "같은 프레임 = 동시 입력" 전제(`CharacterAnimator.cs:109-128`)가 유지됩니다.

### 5-4. 판정 엔진

- **J1**: UnityEngine에 의존하지 않는 순수 C# `JudgeEngine`으로 만듭니다. 입력은 `SongClock.SongTimeAt(qpc)`로 변환합니다. 프레임 시각이나 처리 시각은 쓰지 않습니다. StepMania `Player.cpp`의 `tm.Ago()` 방식과 같습니다.
- **J2**: 불변식은 "현재 시각 이하의 입력을 모두 소비한 뒤 miss 판정"입니다.
- **J3**: 판정 창은 `double` 상수로 바꿉니다(`Constants.cs:10-14`는 지금 float). 오프셋이 적용된 노트 기준 시각을 한 번만 계산해 입력 판정과 miss 판정이 함께 쓰게 합니다(`ChartManager.cs:891` 수정).
- **J4**: 홀드 몸통 판정은 프레임 샘플링 대신 구간 교차(`prev < t ≤ curr`이고 그 구간에 누르고 있었는지)로 바꿔 프레임레이트와 무관하게 만듭니다.
- **J5**: 판정 오차(ms)를 기록해 통계, 캘리브레이션 제안, 진단 오버레이에 씁니다.

### 5-5. 타격음 파이프라인

- **H1**: 원샷은 `CREATESAMPLE`로 미리 로드합니다(현행 결정 유지, 커밋 `bec9071`). 멱등 등록, 할당·문자열 없는 슬롯 재생을 유지합니다.
- **H2**: 볼륨은 버스 ChannelGroup에서 처리합니다. unpaused `playSound` 직후 채널별 `setVolume`(`FMODAudioManager.cs:291`)은 없앱니다. 첫 블록이 기본 볼륨으로 나갈 수 있기 때문입니다(추론).
- **H3**: priority는 등급 몇 개만 씁니다(BGM 0 유지, 타격음 64, SFX 128). 최악의 동시 타격음 수와 BGM을 합쳐 실채널 수를 정합니다. 현재 기본값은 실채널 32입니다(`Platform.cs:911-915`).
- **H4**: Studio 이벤트, `flushCommands`, 비트 콜백은 판정·타격음 경로에서 쓰지 않습니다(F29).
- **H5**: 2단계에서는 입력 스레드에서 즉시 `playSound`하는 방식을 선택할 수 있습니다(F28). 최대 1프레임의 대기가 사라집니다. 다만 등급별 타격음(`ChartManager.cs:743-753`)을 유지하려면 판정을 입력 스레드에서 스레드 안전하게 해야 합니다. 대안은 공통 타격음은 즉시 내고 등급 연출은 메인 스레드에서 처리하는 것입니다(결정 D8).
- **H6**: `HitSoundMode`는 전략 패턴으로 둡니다.
  - OnInput: 즉시 재생(현행)
  - SnapEarly: 노트 시각 전에 들어온 입력이면 노트 시각에 `setDelay`로 예약하고, 늦으면 즉시 재생
  - Prescheduled: 선예약 후 miss가 확정되면 취소. Rhythm Quest 기본 방식이며 miss에도 소리가 난다는 대가가 있습니다.
- **H7**: 지연 하한. WASAPI는 대략 FMOD 링 + 10ms 엔진 주기 + 드라이버·하드웨어입니다(F23, F24). 10ms 미만이 필요한 사용자에게는 ASIO를 권장하는 UX를 둡니다.

### 5-6. 지연 모델과 캘리브레이션

- **L1**: `songTimeAudible = songTimeMix − L_out`으로 둡니다. `L_out` 초기값은 FMOD 링 추정치에 출력 계층 추정치를 더한 값입니다. WASAPI는 선택적으로 `getOutputHandle → IAudioClient::GetStreamLatency`를 씁니다(F24, SP13으로 신뢰성 확인). ASIO는 알 수 없습니다. 여기에 사용자 캘리브레이션을 더합니다.
- **L2**: 캘리브레이션 값은 `(outputType, deviceGuid, sampleRate, blockSize, blockCount)` 키로 저장합니다. 장치나 버퍼를 바꾸면 자동으로 교체합니다.
- **L3**: 오프셋을 세 가지로 나눕니다: AudioOffset(판정과 비주얼을 오디오 기준으로 이동, 장치 프로필별), VisualOffset(렌더 시각 이동), 차트 오프셋(채보 싱크). 캘리브레이션 씬은 두 단계입니다. 먼저 탭 테스트(중앙값과 MAD로 이상치 제거)로 오디오와 입력의 합을 재고, 다음에 비주얼 정렬을 합니다(rhythmquestgame.com/devlog/10.html: 탭 테스트로는 "the sum of audio latency + input latency"만 관찰할 수 있음). 게임 중 평균 판정 오차는 표준편차가 30ms 미만일 때만 결과 화면에서 자동 제안합니다(StepMania `AdjustSync.cpp` `stddev < .03f`).
- **L4**: `IAudioLatencyInfo`를 노출합니다: OutputType, Driver GUID와 이름, SampleRate, BlockSize, NumBlocks, EstimatedBufferLatencyMs, MeasuredDeviceLatencyMs(선택), EffectiveClockStepMs(실측), DriftPpm, UnderrunCount, Epoch, 그리고 `OnOutputChanged` 이벤트입니다.
- **L5**: 개발 오버레이에 다음을 보여 줍니다: 앵커 잔차 분포, DriftPpm, 클록 스텝 히스토그램, 언더런 수, 입력 소스별 지연, 판정 오차 히스토그램. 루프백으로 종단 지연을 재는 모드도 둡니다.

---

## 6. 코드베이스 경계

### 6-1. 모듈 배치와 이음새

- 인터페이스는 규약대로 `Assets/Scripts/App/Interfaces/`에 둡니다. 구현은 새 폴더와 네임스페이스 `Assets/Scripts/Audio/` → `SCOdyssey.Audio`에 둡니다.
- 이번에는 asmdef를 도입하지 않습니다. Assembly-CSharp의 ServiceLocator, SettingsData, 인터페이스를 참조할 수 없게 되기 때문입니다(`Assets/Scripts`에 asmdef 없음).
- 등록은 `Managers`에서 합니다. 순서는 `SettingsManager` 로드 → **AudioEngineHost 생성·init** → … → `settingsManager.Apply()` → `uiManager.Init()`입니다. 설정을 먼저 읽고 UI 초기화 전에 준비된다는 기존 규칙(`Managers.cs:27-59`)을 지킵니다.
- 로깅: 게임 경로에는 CoreLogger가 등록되지 않습니다(BootEntryPoint가 없음). `TryGet`으로 시도하고 없으면 Debug.Log로 대체합니다. FMOD 콜백 안에서는 로그를 남기지 않습니다.

```
SettingsManager ──AudioOutputSettings──▶ AudioEngineHost  (FMOD.System 유일 소유자, update 드라이버, 상태머신
                                          │                 Uninitialized/Starting/Running/Reconfiguring/Failed)
                                          ├─ DeviceCatalog      (타입별 열거, GUID↔index)
                                          ├─ SystemCallbackHub  (명시 마스크, Interlocked 플래그 → 메인 스레드 소비)
                                          ├─ AudioMixer/IMixBus (ChannelGroup 트리: Master / Song / HitSound / SFX / UI)
                                          ├─ OneShotPlayer      (CREATESAMPLE 사운드 세트)
                                          ├─ MusicPlayer × N    (로비·프리뷰용, 게임 곡용 분리)
                                          ├─ AudioClock         (DSP↔QPC 회귀, epoch)
                                          └─ AudioLatencyInfo
GameScene: SongSession (MusicPlayer + SongClock + OneShot 세트 + BGA 구독, 씬 언로드 시 Dispose)
            └─▶ GameManager / ChartManager(JudgeEngine) / BGAController / TimelineController
Input:     IInputTimestampSource (UnityInputSystem | WindowsRawInput) ──SPSC──▶ JudgeEngine (메인 스레드, 시각순 소비)
```

### 6-2. 옮길 소비자 (ChartEditor 제외)

| 소비자 | 지금 쓰는 것 | 옮길 곳 |
|---|---|---|
| `GameManager` (`:62, 83, 140, 144, 154-232, 346`) | GetDSPTime, globalStartTime/_pauseDspTime 계산, StartMusic+audioOffset, Pause/Resume, IsAudioPlaying, Stop | `SongSession`과 `ISongClock`(앵커 하나, 그룹 pause, epoch). 곡 완료는 이벤트로 받습니다. |
| `ChartManager` (`:150, 690, 750-754, 777-797, 885-900`) | PlayOneShot, 매 프레임 GetCurrentTime을 새로 읽음, 판정 오프셋, miss 컷오프 | `IOneShotPlayer`, `JudgeEngine`, 프레임 스냅샷, 오프셋 한 번 적용, 구간 교차 판정 |
| `InputManager` / `IInputManager` (`:28-36, 81-92`) | SetTimeSyncPoint, ConvertToDspTime, AudioSettings.dspTime 대체값 | 타임스탬프만 내보냅니다(`IInputTimestampSource`). SetTimeSyncPoint는 인터페이스에서 뺍니다. |
| `BGAController` (`:18, 89-93, 139-172`) | GetDSPTime 폴링, SchedulePlay, 대체 dspTime, pause 무시 | `ISongClock` 구독(음악 위치와 epoch). 드리프트 보정은 BGA 책임으로 남깁니다. |
| `TimelineController` (`:54, 90-92`) | 판정선마다 `ServiceLocator.Get<IGameManager>().GetCurrentTime()` | 이미 있는 `Func<double> timeProvider` 주입을 씁니다. **ChartEditor가 쓰므로 시그니처를 유지**합니다. |
| `GameDataLoader` (`:45-53`) | LoadAudio 후 `IsLoaded`를 무한 대기(실패하면 멈춤) | 성공·실패·취소 결과 타입(UniTask 등) + 오류 UX |
| `MainUI`, `AdventureUI` (`MainUI.cs:36-85`, `AdventureUI.cs:62-172`) | 게임 곡과 같은 `_sound/_channel` 슬롯, 이전 코루틴을 취소하지 않음 | 별도 `IMusicPlayer` 인스턴스 + CancellationToken으로 마지막 요청만 유효하게 |
| `SoundSettingUI` (`:62-131, 198-214`) | 인덱스 장치, 버퍼 슬라이더, Save 때마다 SetAudioDevice | 출력 타입 Arrow Selector 추가(프리팹 수정), GUID 목록, 바뀐 값만 적용, 적용 결과와 실제 값 표시, 장치 변경 이벤트로 목록 갱신 |
| `SettingsManager.Apply` (`:71-81`) | 볼륨 4개, inputPollingRateHz | 출력 설정 적용 요청(등급 판단은 엔진이 함) |
| `Managers` (`:27-59`) | `AddComponent<FMODAudioManager>()` | AudioEngineHost 등록 |
| `FMODAudioPreInit`, `Platform.cs:871-878` 벤더 패치 | SubsystemRegistration 때 버퍼 주입 | **제거**. 자체 init으로 옮기고 `FMODStudioSettings.asset`의 DSPBufferLength 변경분(커밋 `8ea2d73`)은 되돌릴지 검토합니다. |
| MainScene `StudioListener` (`MainScene.unity:261`) | RuntimeManager 초기화 유발 | **제거** |
| `CharacterAnimator` (`:109-128, 147`) | 같은 프레임 판단, 입력마다 Debug.Log | 프레임 일괄 디스패치 유지, 로그 제거 |
| `PauseUI`, `ResultUI`, `GameManager.HandleRestart` | LoadScene만 하고 음원 정지 없음 | 씬 언로드 때 `SongSession.Dispose`로 정지·해제 |
| GameScene 잔여 `AudioListener`, `Audio Source`, `Assets/Audios/*.mp3`, `AudioOutputConfig`/`ConfigureOutput`(호출자 없음) | 사용하지 않음 | 정리 |

보존할 결정: 타격음 `CREATESAMPLE`, BGM `setPriority(0)`, DSP 버퍼 Length와 Count 짝 설정(이제 자체 init에서), Addressables용 빈 Master 뱅크(`Assets/FMOD/sangcheol-odyssey/Build/Desktop/Master*.bank`, 커밋됨).

### 6-3. 설정 필드 (SettingsData Sound 섹션)

| 필드 | 타입 | 용도 | 기존 필드와 마이그레이션 |
|---|---|---|---|
| `settingsVersion` | int | v1 → v2 마이그레이션 분기 | 지금은 버전 분기가 없습니다(`SettingsManager.cs:10` `v1`). JsonUtility는 없는 필드에 초기값을 넣습니다. |
| `outputType` | string(enum 이름) | "WASAPI" / "ASIO" | 새 필드, 기본값 WASAPI |
| `deviceGuid` | string | 비어 있으면 Follow-Default(driver 0), 값이 있으면 Pinned | `audioDeviceIndex`를 대체합니다. 인덱스는 믿을 수 없으므로 옮기지 않고 기본값으로 초기화합니다. |
| `deviceName` | string | 보조 키, 표시 | 새 필드 |
| `deviceSystemRate` | int | 마지막으로 확인한 레이트(불일치 감지) | 새 필드 |
| `dspBufferLength` | int | 실제 블록 크기 | `audioBufferIndex`(0~4 → 64~1024)를 대체합니다. 매핑 후 WASAPI 프리셋으로 맞춥니다. |
| `dspBufferCount` | int | WASAPI 기본 4(ASIO에서는 FMOD가 2로 강제) | 사용자에게 노출하지 않을 수 있습니다. |
| `asioOutputChannelPair` | int(선택) | `ASIOSpeakerList` 채널 쌍 | 새 필드(선택) |
| `latencyProfiles` | List<{outputType, deviceGuid, sampleRate, blockSize, blockCount, audioOffsetMs}> | 장치별 오프셋 | `audioOffsetMs`의 의미를 다시 정합니다(D9). |
| `visualOffsetMs` | int | 렌더 오프셋 | 새 필드. `judgmentOffset`(1단위 = 3ms)과 관계를 다시 정합니다(D9). |
| `hitSoundMode` | enum | OnInput / SnapEarly / Prescheduled | 새 필드(D8) |
| 세이프 모드 플래그 | 별도 PlayerPrefs 키 | 전환 중 크래시 대비 | 새 키 |
| `inputPollingRateHz` | – | 키보드에 효과가 없음(F30) | 게임패드 전용으로 한정하거나 제거 |
| `playInBackground` | – | runInBackground=0, 포커스 정책과 충돌 | D10에서 결정 |

---

## 7. 런타임 실측이 필요한 항목 (스파이크)

"(제안)"이 붙은 기준은 팀이 목표치(D14)를 정하면 조정합니다.

| ID | 목적 | 방법 | 통과 기준 | 실패 시 |
|---|---|---|---|---|
| **SP1 (게이트)** | Core 전용 호스트 수명주기 | 최소 AudioEngineHost로 WASAPI와 ASIO init → 재생 → 플레이모드 종료, 도메인 리로드, OnApplicationQuit에서 release. 에디터 20회, 플레이어에서도 반복. 에디터 미리듣기 System(EditorUtils)과 함께 둘 때의 동작도 관찰합니다. | 매번 init OK, `ERR_OUTPUT_ALLOCATED` 없음, MainScene과 GameScene에서 `RuntimeManager.IsInitialized == false` 유지, 메모리 추세 안정 | 해제 경로 보강. 안 되면 A 재검토 |
| **SP2 (게이트)** | COM 아파트먼트와 ASIO | 에디터와 플레이어 메인 스레드에서 `CoGetApartmentType`, fmodstudioL 로그와 함께 ASIO init, 열거, close/init 10회 | STA이거나, STA가 아니어도 COM 경고 외에 동작 이상이 없음 | ASIO 전용 STA 스레드 설계가 필요한지 검토하고 ASIO 범위를 다시 결정 |
| SP3 | DSP 클록 계단과 드리프트 | WASAPI 256/480/512/1024 × 4(와 2), 대상 ASIO 장치. 프레임과 MIDMIX에서 `(clock, QPC)`를 5분 기록. init 후 `getDSPBufferSize` 확인 | 스텝 간격과 ppm 수치 확보, 회귀 잔차 p99 ≤ 1블록, 선택한 기본 버퍼에서 5분간 OUTPUTUNDERRUN 0회(제안) | 앵커 방식이나 필터 조정, 기본 버퍼 상향 |
| SP4 | 그룹 클록 도메인과 pause | `_bgmGroup`(SongGroup)과 마스터 `getDSPClock`을 동시 로깅. 대기 delay가 있는 상태에서 채널 pause와 그룹 pause 비교, 루프백으로 실제 시작 시각 측정 | 그룹 pause 후 재개 시 시작이 pause 길이만큼 ±1블록 안에서 밀림 | 예약 재시작 방식으로 재개 구현 |
| SP5 | 런타임 `setDriver` | 재생 중 장치 A→B. 클록 연속성, 공백 ms, 채널 생존, `getSoftwareFormat` 변화, DEVICEREINITIALIZE 발생과 `commanddata2` 확인 | 채널이 살아남고 클록이 단조 증가(점프 크기 기록) | setDriver를 재초기화 등급으로 격상 |
| SP6 | 전체 재초기화 | `close → 재설정 → init`(WASAPI↔ASIO, 버퍼 변경) 50회. 소요 시간, 메모리, 클록이 0부터 다시 시작하는지, 재구성 완결성 | 크래시 0회, 1회 소요 < 1초(제안), 원샷과 BGM 정상 | `release → create` 방식과 비교 |
| SP7 | init 후 `setOutput` 빠른 경로 | 재생 중 WASAPI→ASIO→WASAPI. Channel, Sound, 사용자 ChannelGroup, setDelay 예약, 클록, `getOutput`, 저지연 모드 로그, `getDSPBufferSize` 확인 | 전부 유지되고 ASIO 저지연 모드가 켜짐 | 빠른 경로 비활성(기본 예상) |
| SP8 | ASIO 드라이버 매트릭스 | ASIO4ALL, Realtek ASIO, 팀 보유 인터페이스. 블록 불일치, 44.1k 장치와 48k 믹서, 제어판 버퍼 변경, 다른 앱 점유, 포커스 복귀, close 후 크래시 확인 | 모든 실패가 명확한 RESULT로 드러나고 폴백 체인이 항상 들리는 WASAPI로 끝남. 성공 조합의 실제 블록과 레이트 기록 | 지원 드라이버 목록 제한, UI 안내 강화 |
| SP9 | 장치 열거 | 메인 WASAPI 중 임시 System으로 ASIO 열거, 메인 ASIO 중 임시 System으로 WASAPI 열거. 메인 출력 글리치(루프백), 소요 시간, 드라이버별 실패 | 메인 출력에 영향 없음, 실패 드라이버 격리 | 열거는 재초기화 시점에만 |
| SP10 | 핫플러그 | Follow-Default에서 기본 장치 변경과 USB 분리. Pinned에서 장치 분리와 마지막 장치 분리(NOSOUND) | 두 모드 모두 사용자 개입 없이 들리는 상태로 복귀하고, 게임 중이면 자동 일시정지 | 주기적 NOSOUND 확인 후 재초기화 경로 강화 |
| SP11 | Unity 입력 양자화 | `ctx.time − 해당 프레임 realtimeSinceStartupAsDouble` 분포를 60/144/무제한 fps에서 측정. `realtimeSinceStartupAsDouble`과 Stopwatch 오프셋 안정성 30분 | 분포 형태로 양자화 여부 확정. 오프셋 변동 < 0.1ms면 단순 오프셋 변환 채택(제안) | 회귀 방식으로 변환 |
| SP12 | Raw Input 스레드 | 보조 스레드 메시지 전용 창 + 키보드 등록 + WM_INPUT에서 QPC + 메인 스레드 `ForwardRawInput`. 포그라운드에서 `RIDEV_INPUTSINK`가 필요한지, Unity가 재등록하는 조건 확인 | UI와 Input System 정상, 중복 입력 없음, 타임스탬프가 프레임과 무관하게 분포하며 `ctx.time` 이하 | 1단계(Unity 입력 + 고 FPS) 유지 |
| SP13 | 종단 지연 | 루프백이나 마이크로 키 입력 → 타격음. WASAPI 버퍼별과 ASIO, 메인 스레드와 입력 스레드의 `playSound` 비교. WASAPI `GetStreamLatency`와 비교(Studio를 검토한다면 `EventInstance.start`도) | 수치 확보, 목표(D14)를 충족하는 조합 식별. `GetStreamLatency`가 측정치와 ±3ms 이내면 자동 초기값으로 채택(제안) | 캘리브레이션에만 의존 |
| SP14 | 포커스 손실 | Exclusive 전체화면, Borderless, 창 모드에서 alt-tab. `OnApplicationFocus`/`OnApplicationPause` 호출, mixerSuspend 중 클록 정지 여부, ASIO 재획득 | 모든 모드에서 게임 자동 일시정지와 복귀 후 재동기화 정상 | 포커스 정책 재설계(D10) |
| SP15 | MIDMIX 관리 콜백 | GC 부하 상태에서 MIDMIX C# 콜백을 켰을 때와 껐을 때의 언더런 비교 | 추가 언더런 0 | 메인 스레드 에지 검출이나 네이티브 플러그인 |

권장 순서: SP1, SP2 → SP3, SP4, SP6, SP11(설계 확정) → SP5, SP7~SP10, SP12~SP15(기능별)

---

## 8. 남은 결정 사항

| # | 결정 | 선택지 | 권고 기본값 / 의존 |
|---|---|---|---|
| D1 | FMOD 소유 방식 확정 | A / B / C | **B**, SP1·SP2 통과가 조건 |
| D2 | Studio 도입 | 안 함 / C 하이브리드 | **지금은 안 함**. 사운드 디자이너 합류 계획이 있는지가 기준 |
| D3 | 출력 타입·버퍼 변경 적용 시점 | 곡 사이 즉시(재초기화) / 재시작 후 적용 | 곡 사이 즉시(B). 재시작 후 적용을 고르면 A로 뒤집힐 수 있음 |
| D4 | 장치 변경 허용 범위 | 로비·설정만 / 게임 중(자동 일시정지 후) | 로비·설정만. 게임 중 장치 손실은 자동 일시정지 |
| D5 | 장치 정책 기본값 | Follow-Default / Pinned | Follow-Default(GUID 비움), Pinned는 선택 |
| D6 | ASIO 지원 범위 | x64 전용 여부, 지원·QA 드라이버 목록, 블록 크기 UI(수동 안내 / COM `IASIO::getBufferSize` 추천) | x64 전용, 수동 안내부터 시작. SP8 결과로 목록 확정 |
| D7 | 입력 경로 | Unity 입력만 / Raw Input 스레드 도입 | 1단계 Unity 입력 + 연속 클록, SP11·SP12 통과 시 Raw Input |
| D8 | 타격음 방식 | OnInput / SnapEarly / Prescheduled, 입력 스레드 재생 여부(등급별 음과 절충) | OnInput 유지 + 전략 패턴. 입력 스레드 재생은 Raw Input 도입 때 결정 |
| D9 | 오프셋 모델 | `audioOffsetMs`/`judgmentOffset`를 AudioOffset(장치 프로필)/VisualOffset/차트 오프셋으로 재정의, 자동 제안 적용 방식, 기존 값 마이그레이션 | 재정의 권고. 마이그레이션 규칙은 팀 결정 |
| D10 | 포커스와 백그라운드 정책 | runInBackground, playInBackground 유지·삭제, 포커스 손실 시 정지·음소거·유지 | 게임 중에는 자동 일시정지와 재동기화. 나머지는 SP14 후 결정 |
| D11 | 프레임 페이싱 기본값 | vSync 끔(현행) / vSync 켬 + `maxQueuedFrames` 옵션 | Raw Input을 도입하면 vSync 켬을 검토. 도입 전에는 현행 유지 |
| D12 | 게임 곡 로딩 방식 | `CREATESTREAM`(현행) / `CREATESAMPLE`(3분 스테레오 16bit 48kHz 약 35MB, seek 즉시·정확) | 재개와 연습 모드 seek 요구에 따라 결정 |
| D13 | ChartEditor FMOD 소유권 통합 시점 | 계속 RuntimeManager 사용(에디터 한정 이중 System 허용) / 나중에 새 호스트로 이전 | 당분간 허용하고 알려진 위험으로 문서화(ChartEditorScene은 빌드에 없음) |
| D14 | 지연·정확도 목표치 | 예: 타격음 종단 지연 상한, 판정 오차 표준편차 목표 | **스파이크 통과 기준에 필요**하므로 먼저 정해야 합니다. |
| D15 | 비동기·어셈블리 규약 | UniTask 채택(설치만 되어 있고 미사용), asmdef 분리 시점 | 로드 API에 UniTask 채택, asmdef는 Core/Domain 분리와 묶어 별도 작업 |
| D16 | FMOD 버전 | 2.02.33 유지 / 2.02.35 패치 / 2.03 | 2.02.33 유지(필요하면 2.02.35까지만). C를 택할 때 2.03 재검토 |
| D17 | 로비 BGM, 프리뷰, 게임 곡 전환 | 크로스페이드, 동시 재생, 게임 씬 진입 전 미리 로드 | MusicPlayer를 여러 인스턴스로 두는 전제로 UX 결정 |
