using System;
using SCOdyssey.Audio.Engine;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 오디오 모듈 설치(Managers.InitServices에서, 설정을 읽은 뒤 부른다).
    //   1. 에디터 정리 훅을 가장 먼저 등록한다.
    //   2. 메인 스레드와 COM 아파트먼트를 기록한다.
    //   3. 부팅 시도를 순서대로 실행하고 믹서·원샷을 만든다.
    //   4. 계약을 ServiceLocator에 등록하고, host에 AudioEngineRunner를 붙인다.
    // 예외는 호출자(Managers)가 잡아 InstallDisabled(무음 모듈)로 대신한다.
    public static class AudioModuleInstaller
    {
        public const string SafeModeArgument = "-sco-audio-safe";

        public static AudioModule Install(GameObject host, AudioModuleOptions options)
        {
            return InstallCore(host, options, null);
        }

        // 설치 실패 대비: FMOD를 열지 않은 무음 모듈을 같은 계약으로 등록한다(곡 세션은 QPC 시계로 진행).
        public static AudioModule InstallDisabled(GameObject host, AudioModuleOptions options, string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "설치 실패";
            return InstallCore(host, options, reason);
        }

        private static AudioModule InstallCore(GameObject host, AudioModuleOptions options, string disabledReason)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (options == null) options = new AudioModuleOptions();

            AudioThread.CaptureMain();
            var module = new AudioModule(options);
            EditorAudioLifecycle.Register(module);

            Debug.Log("[Audio] 설치 시작: 메인 스레드 " + AudioThread.MainThreadId + ", COM " + AudioThread.DescribeApartment());
            RuntimeManagerGuard.Enabled = options.EnforceRuntimeManagerGuard;
            RuntimeManagerGuard.Check("부팅");

            // 도중에 예외가 나면 만든 것(System, 등록)을 모두 해제하고 다시 던진다.
            try
            {
                if (disabledReason != null)
                {
                    module.BootDisabled(disabledReason);
                }
                else
                {
                    bool safeMode = options.SafeMode || HasSafeModeArgument();
                    module.Boot(safeMode);
                }
                module.RegisterServices();

                AudioEngineRunner runner = host.GetComponent<AudioEngineRunner>();
                if (runner == null) runner = host.AddComponent<AudioEngineRunner>();
                runner.Bind(module);
            }
            catch
            {
                module.Shutdown();
                throw;
            }
            return module;
        }

        private static bool HasSafeModeArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], SafeModeArgument, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
