using System;
using System.IO;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Audio.Hosting;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing;
using UnityEngine;

namespace SCOdyssey.App
{
    public class Managers : MonoBehaviour
    {
        private static Managers instance = null;


        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            InitServices();
        }


        private void InitServices()
        {
            // 설정 매니저를 가장 먼저 등록하여 다른 매니저 초기화에 설정값 반영
            // (Apply()는 오디오 모듈 설치 후에 호출해야 볼륨 설정이 믹서에 반영됨)
            var settingsManager = new SettingsManager();
            ServiceLocator.TryRegister<ISettingsManager>(settingsManager);
            settingsManager.Load();

            var inputManager = new InputManager();
            inputManager.Enable();
            ServiceLocator.TryRegister<IInputManager>(inputManager);

            // 등록만 먼저 하고 Init()(첫 UI 표시)은 모든 서비스 등록 후로 미룬다.
            // MainUI.OnEnable이 Instantiate 시점에 동기 실행되며 IMusicPlayers(로비 BGM)를 참조하기 때문.
            var uiManager = new UIManager();
            ServiceLocator.TryRegister<IUIManager>(uiManager);

            var musicManager = new MusicManager();
            ServiceLocator.TryRegister<IMusicManager>(musicManager);

            // 최고기록(서버 연동 전 임시 로컬 저장). 곡 선택 화면이 열리기 전에 불러 둔다
            var userDataManager = new LocalUserDataManager();
            ServiceLocator.TryRegister<IUserDataManager>(userDataManager);
            userDataManager.Load();

            var characterManager = new CharacterManager();
            ServiceLocator.TryRegister<ICharacterManager>(characterManager);

            InstallAudio(settingsManager, inputManager);

            // 모든 서비스가 준비된 뒤 설정 반영 (해상도/targetFrameRate/볼륨/입력 폴링)
            settingsManager.Apply();

            // 첫 UI(MainUI) 표시 — MainUI.OnEnable에서 BGM을 재생하므로 가장 마지막
            uiManager.Init();
        }

        // 오디오 모듈(FMOD Core 직접 소유)과 판정 타이밍을 이 GameObject에 설치한다(DontDestroyOnLoad 유지).
        // 설치가 실패하면 FMOD를 열지 않은 무음 모듈로 부팅을 계속한다.
        private void InstallAudio(SettingsManager settings, InputManager input)
        {
            try
            {
                var options = new AudioModuleOptions();
                options.Output = AudioSettingsMapper.ToBootRequest(settings.Current);
                options.HitSoundFolder = Path.Combine(Application.streamingAssetsPath, "HitSound");
                options.MusicFolder = Path.Combine(Application.streamingAssetsPath, "Music");
                options.PlayInBackground = () => settings.Current.playInBackground;
                // 게임 경로에서 FMOD for Unity의 Studio 시스템이 초기화되면 System이 두 개가 되므로 오류로 알린다(ChartEditor는 예외).
                options.EnforceRuntimeManagerGuard = true;

                if (InstallAudioModule(options) == null)
                    Debug.LogError("[Managers] 오디오 모듈을 등록하지 못했습니다. 곡을 시작할 수 없습니다.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[Managers] 오디오 설정을 만들지 못했습니다. 곡을 시작할 수 없습니다.");
            }

            // 입력 소스는 InputManager가 소유한다. 판정 싱크는 곡마다 한 번 읽는다.
            JudgementDriver driver = JudgementDriver.Install(gameObject, input.LaneTimestampSource,
                () => AudioSettingsMapper.ToJudgmentOffsetSteps(settings.Current));
            ServiceLocator.TryRegister(driver);
            ServiceLocator.TryRegister<IJudgementTimingLog>(driver.TimingLog);
        }

        // 정상 설치 → 실패하면 무음 모듈(InstallDisabled) → 그것도 실패하면 null.
        private AudioModule InstallAudioModule(AudioModuleOptions options)
        {
            try
            {
                return AudioModuleInstaller.Install(gameObject, options);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[Managers] 오디오 모듈 설치에 실패해 소리 없이 진행합니다.");
            }

            try
            {
                return AudioModuleInstaller.InstallDisabled(gameObject, options, "오디오 모듈 설치 실패");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }


    }
}