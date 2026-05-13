using FMODUnity;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SCOdyssey.App
{
    // 통합 장치 목록의 한 항목 — 설정 UI에서 표시/선택용
    public struct AudioDeviceEntry
    {
        public int OutputType;      // SCOdyssey.App.AudioOutputType: 0=Default / 1=WASAPI / 2=ASIO
        public int DriverIndex;     // 해당 OutputType 내 FMOD 드라이버 인덱스
        public string DisplayName;  // 예: "ASIO: ASIO4ALL v2"
    }

    // FMOD 초기화 전에 (DSP 버퍼·출력 타입) 적용 + 모드별 디바이스 enumerate.
    // setDSPBufferSize / setOutput은 FMOD system.init() 전에만 유효하므로
    // SubsystemRegistration(가장 이른 초기화 단계)에서 실행.
    internal static class FMODAudioPreInit
    {
        private static readonly int[] BufferSizes = { 64, 128, 256, 512, 1024 };

        // SCOdyssey.App.AudioOutputType 인덱스 → Platform.OutputTypeName에 넣을 문자열
        // ""(빈 문자열)은 Enum.IsDefined 실패 → FMOD가 AUTODETECT로 폴백
        private static readonly string[] OutputTypeNames = { "", "WASAPI", "ASIO" };
        private static readonly FMOD.OUTPUTTYPE[] EnumOutputTypes =
        {
            FMOD.OUTPUTTYPE.AUTODETECT, FMOD.OUTPUTTYPE.WASAPI, FMOD.OUTPUTTYPE.ASIO
        };

        // SettingsManager.PREFS_KEY와 동일하게 유지
        private const string PrefsKey = "SCOdyssey.Settings.v1";

        // 모든 출력 모드의 디바이스를 통합한 목록 — SoundSettingUI에서 참조
        public static List<AudioDeviceEntry> AllDevices { get; private set; } = new List<AudioDeviceEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ApplyPreInit()
        {
            // 1) 모든 출력 모드의 드라이버 enumerate — 임시 FMOD 시스템 사용
            //    이 시점엔 RuntimeManager가 아직 메인 시스템을 만들기 전이라 충돌 없음
            EnumerateAllDevices();

            // 2) 사용자 설정 읽기
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;

            var data = JsonAdapter.FromJson<SettingsData>(json);
            var fmodSettings = Settings.Instance;

            // 3) DSP 버퍼 길이 적용
            if (data.audioBufferIndex >= 0 && data.audioBufferIndex < BufferSizes.Length)
            {
                int bufferSize = BufferSizes[data.audioBufferIndex];
                foreach (var platform in fmodSettings.Platforms)
                    platform.SetDSPBufferLength(bufferSize);
                fmodSettings.DefaultPlatform.SetDSPBufferLength(bufferSize);
            }

            // 4) 출력 타입 적용 — Platform.OutputTypeName(internal)을 리플렉션으로 설정
            //    RuntimeManager.Initialize()가 currentPlatform.GetOutputType()을 읽어
            //    coreSystem.setOutput()을 호출함 (RuntimeManager.cs L294, L335)
            if (data.audioOutputType >= 0 && data.audioOutputType < OutputTypeNames.Length)
            {
                string outputTypeName = OutputTypeNames[data.audioOutputType];
                var field = typeof(Platform).GetField("OutputTypeName",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    foreach (var platform in fmodSettings.Platforms)
                        field.SetValue(platform, outputTypeName);
                    field.SetValue(fmodSettings.DefaultPlatform, outputTypeName);
                }
                else
                {
                    Debug.LogWarning("[FMODAudioPreInit] Platform.OutputTypeName 필드를 찾지 못했습니다. FMOD 버전 변경 가능성.");
                }
            }
        }

        // 임시 FMOD 시스템을 출력 모드별로 만들어 드라이버 목록을 수집.
        // setOutput 후 init 없이도 getNumDrivers/getDriverInfo는 동작 (드라이버 오픈은 init 시점).
        // Windows에서 AUTODETECT는 사실상 WASAPI를 고르므로 enumerate하지 않음 (중복 방지).
        // 저장값 audioOutputType=0(Default)도 OutputTypeName=""로 AUTODETECT 폴백 → 실 동작은 WASAPI와 동일.
        private static void EnumerateAllDevices()
        {
            AllDevices.Clear();

            // i=0(AUTODETECT) 스킵 — WASAPI와 결과 동일
            for (int i = 1; i < EnumOutputTypes.Length; i++)
            {
                FMOD.System sys = default;
                try
                {
                    if (FMOD.Factory.System_Create(out sys) != FMOD.RESULT.OK) continue;

                    if (sys.setOutput(EnumOutputTypes[i]) != FMOD.RESULT.OK) continue;

                    if (sys.getNumDrivers(out int count) != FMOD.RESULT.OK) continue;

                    for (int d = 0; d < count; d++)
                    {
                        if (sys.getDriverInfo(d, out string name, 256, out _, out _, out _, out _) != FMOD.RESULT.OK)
                            continue;
                        AllDevices.Add(new AudioDeviceEntry
                        {
                            OutputType = i,
                            DriverIndex = d,
                            DisplayName = name
                        });
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FMODAudioPreInit] {EnumOutputTypes[i]} 드라이버 열거 실패: {e.Message}");
                }
                finally
                {
                    if (sys.hasHandle()) sys.release();
                }
            }
        }
    }
}
