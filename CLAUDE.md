# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Rhythm game client for Sangcheol Odyssey. Unity **6000.3.13f1** (URP, Input System, Localization, Timeline). Uses **FMOD** (Core API, owned by the `SCOdyssey.Audio` module) for audio, **Spine** for character animation, **DOTween** for tweens, **UniTask** for async, and **Odin Inspector** (Sirenix) for editor tooling. Unity's built-in audio is disabled. Root namespace: `SCOdyssey.*`.

## Build / Run

The `.csproj`, `.sln` and `.slnx` files at the repo root are Unity-generated and gitignored — open the project through Unity Hub (Unity 6000.3.13f1). CI runs only CodeQL static analysis (`.github/workflows/codeql.yml`, C# in build-mode none); there is no build or test pipeline, so iteration happens inside the editor. EditMode tests run from Window → General → Test Runner → EditMode (or MCP `run_tests`) and live in four places:

- `Assets/Tests/EditMode/` (`SCOdyssey.Rhythm.Tests`) — judge engine, chart parser, bus, score model, scenarios, plus settings-asset and prefab-wiring checks. A test assembly cannot reference Assembly-CSharp, so anything worth testing there must live in `SCOdyssey.Rhythm` / `SCOdyssey.Domain.Service`; the asset/prefab tests find components by type name and read fields through `SerializedObject`.
- `Assets/Scripts/Audio/Tests/Editor/` (`SCOdyssey.Audio.Tests`) — DSP↔QPC model, song clock, anchors, session state, boot plan.
- `Assets/Scripts/Game/Timing/Tests/Editor/` (`SCOdyssey.Game.Timing.Tests`) — input timestamps (QPC mapper), judgement timeline/pump/log, binding.
- `Assets/Scripts/Editor/Tests/` (Assembly-CSharp-Editor) — settings migration.

`Assets/Scripts/Testing/AudioHarness/` compiles only with the `SCO_AUDIO_HARNESS` define (audio module harness on `AudioSpikeScene`, no game).

### Assemblies

Two independent chains; Assembly-CSharp references both and is the only place they meet.

- `SCOdyssey.Core` (`Assets/Scripts/Core`: `ServiceLocator`, `CoreLogger`, `Qpc`) ← `SCOdyssey.Audio` (`Assets/Scripts/Audio`: sole owner of the FMOD Core System, song session and song clock; references Core, FMODUnity, UniTask) ← `SCOdyssey.Game.Timing` (`Assets/Scripts/Game/Timing`: input timestamp → song time, `JudgementDriver`, `GameplayTimingBinding`; references Core, Audio, Unity.InputSystem). See `Assets/Scripts/Audio/Audio_architecture.md`.
- `SCOdyssey.Domain.Service` (`Constants.cs`) ← `SCOdyssey.Rhythm` (`Assets/Scripts/Rhythm/`: judge engine, chart parser, judgement bus, score model). Both are `noEngineReferences` — no `UnityEngine` there; parser diagnostics go to the caller through `ChartParseReport`.

Never add a reference between the chains: `Game.Timing` does not know judging rules, `Rhythm` does not know Unity or audio. All glue (`GameManager`, `SongRhythmClock`, `HitSoundPlayer`, `JudgementTimingRecorder`, the views) is Assembly-CSharp. If glue ever needs tests, keep the logic in `Rhythm` behind primitive or `Func<>` arguments, or add a glue asmdef with its own test asmdef.

Scenes (in `Assets/Scenes/`):
- `MainScene` — lobby / menus (UI flow entry point)
- `GameScene` — gameplay
- `ChartEditorScene` — in-game chart editor; opened on its own without `Managers` and not in build settings (`EditorFMODAudio` drives `FMODUnity.RuntimeManager` directly)
- `APITestScene` — isolated scene for testing the `IApiClient` backend (listed in build settings but disabled)
- `JudgeSandboxScene` — judge engine sandbox, opened on its own without `Managers` and not in build settings (`Game/Sandbox/`). UI Toolkit widget UI (`SandboxPanel`, styles in `Assets/Scenes/JudgeSandbox/`), FMOD sound of its own (`SandboxAudio` on `RuntimeManager.CoreSystem` — it needs seek, speed and DSP-scheduled clicks, which the audio module does not offer; song from `MusicSO`, hitsound, metronome). Runs the shared `JudgeScenarios` catalog or any `MusicSO` chart with autoplay/script/keyboard input; keyboard binds `InputSystem_Actions.Game.Lane1–4` directly and maps `ctx.time` onto the sandbox clock. The timeline (`JudgeTimelineElement`) reads only `IJudgeStateReader`. The engine steps at a fixed 60 Hz regardless of render fps; `SUMMARY` lines go to the console.

## Architecture: two parallel boot paths

The project has **two distinct bootstrap systems**. Don't mix them — know which scene you're in.

### 1. Phased installer boot (Testing / API scenes, `Assets/Scripts/Boot/`)

Used by `APITestScene` via `TestBootEntryPoint` + `TestBootInstaller`. `BootOrchestrator` (`DefaultExecutionOrder(VeryEarly)`) scans child `MonoBehaviour`s implementing `IInstaller`, then calls `Install()` in topological order determined by:

1. `BootPhase` (`Core=0 → Telemetry=10 → Storage=20 → Network=30 → Auth=40 → Content=50 → UI=100`)
2. `Priority` (higher runs first within a phase)
3. `Requires` dependency IDs (must exist in `ServiceLocator` before this installer runs)

Each installer registers services (`CoreLogger`, `GameClock`, `ServerTimeSkew`, `TokenStore`, `IApiClient`, etc.) into `ServiceLocator`. Pattern: `if (!ServiceLocator.TryGet(out X x)) { x = new X(...); ServiceLocator.Register(x); }` — idempotent so re-entry on scene reload is safe.

`LoggerInstaller` (Phase=Core, Id=`core.logger`) is required by most other installers. `AppInstaller` is Phase=UI and depends on `core.logger`.

### 2. MonoBehaviour manager boot (MainScene / GameScene, `Assets/Scripts/App/Managers.cs`)

`Managers` is a `DontDestroyOnLoad` singleton placed in `MainScene` that directly instantiates and registers the gameplay-side managers into the same `ServiceLocator`, in this exact order:

`ISettingsManager` → `IInputManager` → `IUIManager` → `IMusicManager` → `IUserDataManager` (temporary local best-record store, `LocalUserDataManager` → `persistentDataPath/records.json`; swap for a server-backed implementation later) → `ICharacterManager` → audio module (`AudioModuleInstaller.Install`, registers `IAudioEngine`/`IAudioOutputService`/`IAudioMixer`/`IOneShotPlayer`/`IMusicPlayers`/`ISongPlayer`; falls back to `InstallDisabled`, a silent module, if install throws) → `JudgementDriver` (+ `IJudgementTimingLog`) → `SettingsManager.Apply()` → `UIManager.Init()`

Settings must load first so other managers see `audioOffsetMs`, `targetFrameRate`, resolution etc. during their init; `Apply()` runs after the audio module so volumes reach the mixer.

### ServiceLocator

`Assets/Scripts/Core/ServiceLocator.cs` (`SCOdyssey.Core`) is a thread-safe `Dictionary<Type, object>` that gets reset on `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`. **All cross-component dependencies go through this** — avoid adding new `static Instance` singletons. Use `TryRegister<T>(inst)` (won't overwrite) when the service is created opportunistically.

## Rhythm engine (`Assets/Scripts/Game/`, `Assets/Scripts/Rhythm/`)

### Time model — **the song clock is authoritative**

Gameplay time comes from the song session's clock (`ISongPlayer` → `ISongSession.Clock`, `SCOdyssey.Audio`), built on FMOD's DSP clock and QPC — see `Audio_architecture.md` §7–8. **Never derive song time from Unity's audio clock, `Time.*` or raw FMOD calls.**

- `GameDataLoader` (`Game/`) opens the song with `ISongPlayer.LoadAsync` (`CreateSilent` if the chart has no audio; back to the lobby if loading fails), then calls `GameManager.StartGame()`.
- `GameManager` (`App/`) owns the bus and creates `HitSoundPlayer` on it in `Awake`, before any other subscriber can attach (`CharacterAnimator` subscribes in `Start`, `ScoreManager`/`PlayfieldView` in their `Init` from `StartGame`). `StartGame()` builds the `RhythmSession` (`JudgeEngine` + bus), calls `ScoreManager.Init(totalNotes, bus)` and `PlayfieldView.Init(...)`, attaches `GameplayTimingBinding.Attach(driver, session, OnTimingAdvance, OnTimingLaneInput, isRunning, onExternalPause)` and calls `StartMusic(barDuration)` → `ISongSession.Start(leadIn, audioOffsetMs)`. Song time 0 is game start; the audio starts at `leadIn + audioOffsetMs` (positive = music later). Charts are written so that bar 1 begins at audio 0, so the lead-in is one empty bar.
- `SongRhythmClock` (`App/`, registered as `IRhythmClock`) returns `ISongSession.Clock.Frame.SongTime` with no gating; `TimelineController` reads it for judge-line movement. There is no `GameManager.Update` — nothing polls the clock to drive the engine.
- Each frame `JudgementDriver` (execution order −900, right after `AudioEngineRunner` at −1010) drains lane inputs (`InputManager.LaneTimestampSource`, `ctx.time` → QPC → song time) and, through the binding, calls `OnTimingLaneInput(in JudgedInput)` once per input in time order, then `OnTimingAdvance(songTime, judgeTime)`. `JudgedInput` carries `Lane` (1–4), `IsDown`, `QpcTicks`, `SongTime`, `JudgeTime`, `Judgeable`, `Epoch`. The handlers: press → `RhythmSession.Press(LaneMap.FromInputIndex(lane), JudgeTime)` (its `PressOutcome` tells whether anything was hit; `NoTarget` → `HitSoundPlayer.PlayWhiff()`), release → `Release(...)`; a non-judgeable release (ESC, input-map switch, focus loss) → `ReleaseUnjudged`, which only publishes the key event and never touches the engine. `OnTimingAdvance` → `_rhythm.Advance(judgeTime)` → `PlayfieldView.Tick(songTime)` → end check (`IsFinished && !IsAudioPlaying`). Bar progression uses `songTime`; windows, misses and holds compare `judgeTime`.
- `judgeTime = songTime − judgmentOffset × 3 ms` is applied in exactly one place, `JudgementTimeline` (latched once per song). The engine's `OffsetSec` stays 0 — never apply the offset a second time.
- Pause/resume go through `ISongSession.Pause(PauseReason.User)` / `Resume()`, which freeze and re-anchor the clock; no manual time correction. Pauses the session starts itself (focus, device, stream) reach `GameManager` through the binding's `onExternalPause`.
- BGA follows the session (`BGAController.Follow(session)`).

### Chart format and parsing

Charts live in `Assets/Charts/` as text files. Format (see `ChartParser.cs`):

```
#NOTES 123;                  # header: total note count
#001:02:01020020;            # data: bar=001, channel=0 (LTR), lane=2, sequence=01020020
```

`channel` = 0 (left-to-right) or 1 (right-to-left). `NoteType` values in the sequence: `0=None, 1=Normal, 2=HoldStart, 3=Holding (hold continues; not judged, not spawned in game), 4=HoldEnd (tail, no head drawn), 5=HoldRelease (tail, head drawn)`. The parser keeps every character as `NoteData` (the chart editor preview shares `LaneData`); `ChartData.BuildJudgeTrack` maps them to `NoteKind` (`1→Tap, 2→HoldHead, 4/5→HoldTail`, `3` dropped) and pairs each head with the next tail on its lane (`PairId`). `ChartData.totalNotes` is the number of judgeable notes, not the `#NOTES` header. 4/4 time is hardcoded in one place — `BarClock.FromBpm` (`Rhythm/Timeline/`, `(60f / bpm) * 4` in float; the parser, the view and the sandbox all use it). `PlayfieldView` (`Game/View/`, the GameScene playfield) pulls lanes bar by bar from `BarStreamer` (chart lanes must be in bar order), spawns the next bar one bar ahead as Ghost and promotes it to Active at its bar start (`NoteLifecycle`), and delegates to plain C# collaborators in `Game/View/`: `JudgeLineDirector` (judge lines, U-turn), `NoteFieldSpawner` (notes, Ghost/Hidden, `noteId → view`, hold-break dimming), `CountdownView`, `JudgeEffectSpawner`, `NoteGeometry` (coordinates), `GameObjectPool`.

### Judgement & character animation

Judge windows are tuned in `Assets/Resources/Config/JudgeSettings.asset` (`JudgeSettingsSO`, milliseconds: `Perfect=21 / Master=42 / Ideal=84 / Kind=105 / Umm=126`). `GameManager` resolves it through `ConfigLocator` (Inspector → ServiceLocator → Resources) and falls back to `JudgeWindows.Default`. Grade edges are inclusive (`<=`); the outer Umm edge is exclusive.

**Hold model**: a hold is two judgements — head (press timing) and tail (release timing, window `Umm × tailWindowScale`). Releasing before the tail window → tail Miss at that moment (hold broken, `HoldStartNote.OnHoldBroken()` dims the bar), no recovery; missing the head kills the tail; holding past the tail window → tail Miss. `JudgeEngine` advances to each input's timestamp before handling it, so results do not depend on frame rate.

**Playfield presentation**: `Assets/Resources/Config/PlayfieldSettings.asset` (`PlayfieldSettingsSO.Shared`, ServiceLocator → Resources → code defaults) holds `ghostBrightnessFallback`, `hiddenToGhostOffsetPx`, `brokenHoldAlpha` (an alpha ceiling for a broken hold bar), `timelineScreenMarginPx`, `countdownBeats`/`countdownEpsilonBeats`, `clearBannerSec`, `resumeCountFrom`/`resumeCountSec`; `NoteController`, `HoldStartNote` and `TimelineController` read it too (also in the chart editor preview). Ghost notes are drawn with a brightness tint (`NoteController.ApplyTint`), not alpha. 4/4 (`BarClock.BeatsPerBar`) and the one-bar music lead-in are chart conventions, not settings.

**Score**: rules live in `Assets/Resources/Config/ScoreSettings.asset` (`ScoreSettingsSO` → `ScoreRules`: max 1,000,000, multipliers Perfect/Master 1, Ideal 0.7, Kind 0.5, Umm 0, Kind/Umm break combo, Perfect Ex bonus 0.2, Fail below 700,000, rank thresholds). `ScoreModel` (`SCOdyssey.Rhythm`) computes score from per-grade counts as `max × Σmultiplier / totalNotes` (no integer division); all Master-or-better → max + Ex, so all Perfect = 1,200,000. `ScoreManager` is the MonoBehaviour adapter — the only scoring path, fed by the bus — that `HudView` (score/combo/gauge) and `ResultUI` read. `GameBannerView` shows the clear banner and the resume countdown. Best records go through `IUserDataManager`; `GameManager.OnGameFinished` saves and passes `isNewBestScore` to `ResultUI.Init(finalScore, ClearType, ScoreRank, maxCombo, totalNotes, judgeCounts, gaugePercent, isNewBestScore)`.

**Lane numbering is 1-based at the edges, 0-based internally.** Both the Input System (`JudgedInput.Lane`, from the `Lane1`–`Lane4` actions) and the chart file (`#001:`**`02`**`:...`) use lanes 1–4, but the `Lane` enum is 0-based (`L1=0 … L4=3`, in the `SCOdyssey.Rhythm` assembly) — `LaneMap.FromInputIndex` / `FromChartLine` (`Rhythm/Lane.cs`) convert at the boundary. Two *different* mappings derive from a lane; do not conflate them:

- **`LaneGroup`** — which judgement line / character the lane belongs to. Lanes 1–2 = `Top`, lanes 3–4 = `Bottom` (`LaneLayout.GroupOf`, 0-based `(int)lane < 2`).
- **`NotePosition`** — position *within* the group; drives character Y. Alternates: lanes 1,3 = `Top`, lanes 2,4 = `Bottom` (`LaneLayout.PositionOf`, `(int)lane % 2 == 0`).

So `Lane.L2` = group `Top`, position `Bottom`. `LaneLayout` (`Assets/Scripts/Game/View/`) is the only place that derives group / position / countdown slot from a `Lane`. `NotePosition.Middle` is never parsed from a chart — `CharacterAnimator` derives it at runtime (both holds of a group active, or opposite-position inputs within the same frame).

**Judgement bus** (`IJudgementBus`, registered in `ServiceLocator` by `GameManager`) carries engine vocabulary only: `LaneInput(LaneKeyEvent{Lane, IsPressed, Time})`, published on every key press/release before judging (synthetic releases included), and `NoteJudged(JudgeEvent)` for every hit and miss (events from one `Advance` are in `Time` order). `RhythmSession` (`Rhythm/Session/`, wraps `JudgeEngine`, built by `GameManager`) is the only publisher. Do not confuse `LaneKeyEvent` with `Game.Timing`'s `LaneInputEvent` — that one is the raw QPC-stamped input the timestamp source hands to `JudgementDriver`, before any judging. Subscribers derive group/position with `LaneLayout` (`LaneGroup` lives in `Game/View/LaneLayout.cs`):

- `HitSoundPlayer` (`Game/View/`) — constructed before every other `NoteJudged` subscriber so the sound leads the picture; registers `HitSoundFiles` (one per grade, `StreamingAssets/HitSound`, checked by `Editor/AudioBuildValidator`) with `IOneShotPlayer` and plays the grade of each non-miss judgement; `PlayWhiff()` for a press that hit nothing. Hit sounds live nowhere else.
- `JudgementTimingRecorder` (`App/`) — `JudgeSamples.Classify` → `TimingKind.Press/Release/Miss` into `IJudgementTimingLog` (the timing overlay); `Skip` samples are dropped.
- `ScoreManager`, `PlayfieldView` (treats `NoteJudged` as a wake-up and reads the result through `IJudgeStateReader`: `NoteLifecycle.IsDecided`, `GradeOf`), `CharacterAnimator`.

Character animation: `CharacterAnimator` subscribes to the bus (`LaneInput` → buffered press/release, `NoteJudged` → hit grade, hold pose on a `HoldHead` hit, hold release on the tail's result), keeps only its own group (`SetGroup(LaneGroup)`, called from `TimelineController.Init`; the chart editor preview spawns the same judge lines, just without a bus), and resolves once per frame in `LateUpdate` (a short pair window waits for the opposite lane so simultaneous hits register). With no bus in `ServiceLocator` it stays silent. It drives a 17-state machine whose names match the Spine animation names exactly (`Run`, `Hold`, `Hit_master_1~3`, `Hit_1~3`, `Hit_umm`, `Miss`, `Double_hit`, `Up_hit`/`Down_hit`, `Up_hold`/`Down_hold`, `Up_hit_while_hold`/`Down_hit_while_hold`). Y is tweened with DOTween; the three heights are the only thing distinguishing Top/Middle/Bottom since clip names carry no position. **Read `Assets/Scripts/Game/Animation_mechanic.md` before touching animation code or creating character assets** — it is the canonical spec (transition tables, the input contract the judging layer must satisfy, and the Spine authoring checklist).

## UI flow

`IUIManager` (`UIManager`) keeps navigation in a `UIStack` of UI keys with a `PushMode` (`Push` hides what is below, `Overlay` keeps it visible, `Replace` swaps the top) and caches one instance per `BaseUI` type under a persistent `@UI_Root` GameObject (`DontDestroyOnLoad`); instances come from `Resources/Prefabs/UI/{TypeName}` (via `ResourceLoader.PrefabInstantiate`) and are never destroyed, so they are reused across scene transitions. On scene load, `UIManager.OnSceneLoaded` **keeps the stack intact** but moves its floor: `MainScene` shows the whole lobby stack; any other scene hides everything that was on the stack when it loaded and shows only UIs pushed afterwards (`GameLoadingUI`, `PauseUI`, `ResultUI`). Canvas worldCamera is re-bound to the new `Camera.main` on each load because `@UI_Root` is persistent.

`UIManager` assigns every visible UI Canvas `sortingOrder = 100 + depth` (`BaseSortingOrder`), above everything in `GameScene` (the Spine character mesh renders at 3); `PauseUI` and `GameLoadingUI` are pushed with `PushMode.Overlay` so the game stays visible beneath them. `ResultUI` is persistent too — it reads the current `MusicSO` in `Init`, not in `Awake`.

## Conventions

- **Namespaces mirror folders**: `SCOdyssey.Boot`, `SCOdyssey.Core` (+ `.Logging`), `SCOdyssey.Audio` (+ `.Clock`/`.Engine`/`.Hosting`/`.Playback`/…), `SCOdyssey.App`, `SCOdyssey.App.Interfaces`, `SCOdyssey.Game` (also `Game/View`, `Game/Notes`, `Game/Sandbox`), `SCOdyssey.Game.Timing`, `SCOdyssey.Rhythm` (all subfolders), `SCOdyssey.Config`, `SCOdyssey.ChartEditor.*`, `SCOdyssey.Domain.Dto`, `SCOdyssey.Domain.Entity`, `SCOdyssey.Domain.Service`, `SCOdyssey.Net`, `SCOdyssey.UI`, `SCOdyssey.Testing.*`.
- **Assemblies**: see *Assemblies* above. `SCOdyssey.Rhythm` and `SCOdyssey.Domain.Service` take no `UnityEngine`; `SCOdyssey.Audio` / `SCOdyssey.Game.Timing` take no App, Game, UI or Domain types.
- **Interfaces for managers live separately** in `Assets/Scripts/App/Interfaces/` — consumers always depend on `I*Manager`, not the concrete class, so the API/mock can be swapped via `TestingConfig.useMockApi`.
- **Audio goes through the module contracts** (`ISongPlayer`/`ISongSession`, `IOneShotPlayer`, `IMusicPlayers`, `IAudioMixer`). `FMODUnity.RuntimeManager` (the FMOD-for-Unity Studio system) is referenced only by `RuntimeManagerGuard` (read-only) and by the two scenes that run without `Managers`: `ChartEditor/Preview/EditorFMODAudio` and `Game/Sandbox/SandboxAudio`. The guard logs an error if it is initialized on the game path.
- **Do not use `using FMOD;`** — `FMOD.System` collides with `System`. Always fully qualify: `FMOD.Sound`, `FMOD.Channel`, `FMOD.ChannelGroup` (see `Assets/Scripts/Audio/`).
- **Logging**: call `CoreLogger` from `ServiceLocator` (tag strings like `"boot"`, `"unity"`). `LoggerDriver` forwards `Application.logMessageReceivedThreaded` to `CoreLogger` (editor and development builds only) so Debug.Log reaches the file/ring/console sinks, but has a reentrancy guard — don't call Debug.Log while draining. The `Managers` path has no `CoreLogger`; use `Debug.Log*` there.
- **Comments and identifiers are mixed Korean/English**; match the surrounding file's style when editing rather than translating. Comments describe the code, not the process — no stage labels, ticket numbers or integration-point markers.

## Git / PR workflow

- Default branch is `master`; active development on `develop`.
- Branch names use conventional prefixes (`feature/`, `fix/`, `hotfix/`, `refactor/`, `chore/`, `test/`, `docs/`) — `.github/workflows/auto-label-branch.yml` auto-labels PRs from the prefix.
- `require-linked-issue.yml` enforces that PRs link an issue.
- Commit messages in this repo use the same prefix convention in Korean (e.g. `refactor: CharacterAnimator 상태 머신 재설계`).
