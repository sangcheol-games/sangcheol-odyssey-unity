# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Rhythm game client for Sangcheol Odyssey. Unity **6000.3.13f1** (URP, Input System, Localization, Timeline). Uses **FMOD** for audio, **Spine** for character animation, and **Odin Inspector** (Sirenix) for editor tooling. Root namespace: `SCOdyssey.*`.

## Build / Run

The `.csproj` and `.sln` files at the repo root are Unity-generated and gitignored — open the project through Unity Hub (Unity 6000.3.13f1). There is no command-line build, lint, or test pipeline; iteration happens inside the editor. The Unity test framework package is installed (`com.unity.test-framework`) but no test assemblies currently exist.

Scenes (in `Assets/Scenes/`):
- `MainScene` — lobby / menus (UI flow entry point)
- `GameScene` — gameplay
- `ChartEditorScene` — in-game chart editor
- `APITestScene` — isolated scene for testing the `IApiClient` backend

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

`ISettingsManager` → `IInputManager` → `IUIManager` → `IMusicManager` → `ICharacterManager` → `IAudioManager` (FMOD, attached as component)

Settings must load first so other managers see `audioOffsetMs`, `targetFrameRate`, resolution etc. during their init.

### ServiceLocator

`Assets/Scripts/Core/ServiceLocator.cs` is a thread-safe `Dictionary<Type, object>` that gets reset on `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`. **All cross-component dependencies go through this** — avoid adding new `static Instance` singletons. Use `TryRegister<T>(inst)` (won't overwrite) when the service is created opportunistically.

## Rhythm engine (`Assets/Scripts/Game/`)

### Time model — **FMOD DSP clock is authoritative**

`GameManager` uses `IAudioManager.GetDSPTime()` (FMOD's DSP clock) as the single time source. **Never use `AudioSettings.dspTime`** — its epoch differs from FMOD's and will cause desync.

- `globalStartTime` is captured at `StartGame()` from the DSP clock.
- Chart time = `GetDSPTime() - globalStartTime` (frozen to `_pauseDspTime - globalStartTime` during pause).
- On resume, `globalStartTime += GetDSPTime() - _pauseDspTime` so chart position is preserved.
- `InputManager.SetTimeSyncPoint(dspNow, realtimeNow)` records one sync sample; input timestamps (`InputAction.context.time`, OS realtime) are converted back to DSP time by linear offset in `ConvertToDspTime`.
- Audio offset from `SettingsManager.Current.audioOffsetMs` is applied in `StartMusic()` (positive = music starts later).

### Chart format and parsing

Charts live in `Assets/Charts/` as text files. Format (see `ChartParser.cs`):

```
#NOTES 123;                  # header: total note count
#001:02:01020020;            # data: bar=001, channel=0 (LTR), lane=2, sequence=01020020
```

`channel` = 0 (left-to-right) or 1 (right-to-left). `NoteType` values in the sequence: `0=None, 1=Normal, 2=HoldStart, 3=Holding, 4=HoldEnd (invisible, release-judged), 5=HoldRelease (visible head, release-judged)`. 4/4 time is hardcoded — `barDuration = 60/bpm * 4`. `ChartManager` streams `LaneData` into `currentBarLanes` / `nextBarLanes` queues as the song progresses and owns all note / timeline / effect object pools.

### Judgement & character animation

Judge windows (seconds) are in `Assets/Scripts/Domain/Service/Constants.cs`: `Perfect=0.021 / Master=0.042 / Ideal=0.084 / Kind=0.105 / Umm=0.126`. 4 lanes (indices 1–4 from Input System) map to 2 groups: lanes 1–2 = `NotePosition.Bottom`, lanes 3–4 = `NotePosition.Top`. `Middle` is derived only when both holds are active simultaneously.

Character animation: `CharacterAnimator` subscribes to `GameManager.OnNoteJudgedEvent / OnHoldStartEvent / OnHoldEndEvent` and drives a 14-state machine (Idle, Hit0-3, Top/Middle/Bottom, Fall, *Hold, *HitWhile*Hold). It sets `_targetY` and lerps the root in `Update()` — animation clips provide only relative motion. See `Assets/Scripts/Game/Animation_mechanic.md` for the full state machine spec, AnimatorController setup, and CharacterSO authoring checklist — read it before touching animation code or creating character assets.

## UI flow

`IUIManager` maintains a `Stack<BaseUI>` under a persistent `@UI_Root` GameObject (`DontDestroyOnLoad`). On scene load, `UIManager.OnSceneLoaded` **keeps the UI stack intact** but toggles visibility: only `MainScene` shows the UI; other scenes hide it. This means UI prefabs loaded from `Resources/Prefabs/UI/{TypeName}` (via `ResourceLoader.PrefabInstantiate`) are reused across scene transitions. Canvas worldCamera is re-bound to the new `Camera.main` on each load because `@UI_Root` is persistent.

`PauseUI` during gameplay sets its Canvas `sortingOrder = 3` manually to stay above the game-scene head layer (which uses 2).

## Conventions

- **Namespaces mirror folders**: `SCOdyssey.Boot`, `SCOdyssey.Core`, `SCOdyssey.App`, `SCOdyssey.App.Interfaces`, `SCOdyssey.Game`, `SCOdyssey.Domain.Dto`, `SCOdyssey.Domain.Service`, `SCOdyssey.Net`, `SCOdyssey.UI`, `SCOdyssey.Testing.*`.
- **Interfaces for managers live separately** in `Assets/Scripts/App/Interfaces/` — consumers always depend on `I*Manager`, not the concrete class, so the API/mock can be swapped via `TestingConfig.useMockApi`.
- **Do not use `using FMOD;`** — `FMOD.System` collides with `System`. Always fully qualify: `FMOD.Sound`, `FMOD.Channel`, `FMOD.ChannelGroup` (see `FMODAudioManager.cs`).
- **Logging**: call `CoreLogger` from `ServiceLocator` (tag strings like `"boot"`, `"unity"`). `LoggerDriver` forwards `Application.logMessageReceivedThreaded` to `CoreLogger` so Debug.Log reaches the file/ring/console sinks, but has a reentrancy guard — don't call Debug.Log while draining.
- **Comments and identifiers are mixed Korean/English**; match the surrounding file's style when editing rather than translating.

## Git / PR workflow

- Default branch is `master`; active development on `develop`.
- Branch names use conventional prefixes (`feature/`, `fix/`, `hotfix/`, `refactor/`, `chore/`, `test/`, `docs/`) — `.github/workflows/auto-label-branch.yml` auto-labels PRs from the prefix.
- `require-linked-issue.yml` enforces that PRs link an issue.
- Commit messages in this repo use the same prefix convention in Korean (e.g. `refactor: CharacterAnimator 상태 머신 재설계`).
