#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP1: FMOD Core System을 직접 만들고 해제하는 수명주기가 안정적인지 확인한다.
    public static class SpikeLifecycle
    {
        // Play를 누를 때마다 한 번 실행한다. 에디터에서 Play/Exit를 20회 반복할 때 매번 한 줄씩 남는다.
        public static void BootCheck(SpikeOutputConfig config)
        {
            bool runtimeManagerBefore = FMODUnity.RuntimeManager.IsInitialized;
            SpikeFmodSystem system = SpikeFmodSystem.Create(config, out string error);
            string context = "RuntimeManager 초기화: " + runtimeManagerBefore;
            if (system == null)
            {
                SpikeReport.Summary("SP1-boot", SpikeReport.Result.Fail, "init OK", error, context);
                return;
            }

            context = system.Describe() + ", " + context;
            string playError = PlayClick(system);
            system.Dispose();

            bool pass = playError == null && !runtimeManagerBefore;
            string measured = "init " + SpikeReport.Ms(system.InitSeconds);
            if (playError != null) measured += ", 재생 실패: " + playError;
            SpikeReport.Summary("SP1-boot", SpikeReport.PassIf(pass), "init OK, RuntimeManager 미초기화", measured, context);
        }

        // 생성 → 클릭 재생 → 몇 프레임 update → 해제를 cycles번 반복한다.
        public static IEnumerator RunCycles(SpikeOutputConfig config, int cycles, Action<string> progress)
        {
            FMOD.Memory.GetStats(out int memoryBefore, out _);
            int failures = 0;
            int outputAllocated = 0;
            double maxInit = 0;
            string lastError = "";
            string context = "";

            for (int i = 0; i < cycles; i++)
            {
                progress(string.Format("SP1 반복 {0}/{1}", i + 1, cycles));
                SpikeFmodSystem system = SpikeFmodSystem.Create(config, out string error);
                if (system == null)
                {
                    failures++;
                    lastError = error;
                    if (error.Contains("ERR_OUTPUT_ALLOCATED")) outputAllocated++;
                    yield return null;
                    continue;
                }

                context = system.Describe();
                if (system.InitSeconds > maxInit) maxInit = system.InitSeconds;
                string playError = PlayClick(system);
                if (playError != null)
                {
                    failures++;
                    lastError = playError;
                }

                for (int frame = 0; frame < 3; frame++)
                {
                    system.CoreSystem.update();
                    yield return null;
                }
                system.Dispose();
                yield return null;
            }

            FMOD.Memory.GetStats(out int memoryAfter, out _);
            int memoryDelta = memoryAfter - memoryBefore;
            bool runtimeManager = FMODUnity.RuntimeManager.IsInitialized;
            bool pass = failures == 0 && outputAllocated == 0 && !runtimeManager && memoryDelta < 1024 * 1024;

            string measured = string.Format("실패 {0}/{1}, ERR_OUTPUT_ALLOCATED {2}회, 최대 init {3}, FMOD 메모리 변화 {4}KB, RuntimeManager 초기화: {5}",
                failures, cycles, outputAllocated, SpikeReport.Ms(maxInit), memoryDelta / 1024, runtimeManager);
            if (failures > 0) measured += ", 마지막 오류: " + lastError;
            SpikeReport.Summary("SP1-cycles", SpikeReport.PassIf(pass),
                "실패 0, ERR_OUTPUT_ALLOCATED 0, RuntimeManager 미초기화, 메모리 변화 < 1MB", measured, context);
            progress("SP1 완료");
        }

        // 메모리 WAV 클릭을 마스터에 한 번 재생한다. 성공하면 null.
        public static string PlayClick(SpikeFmodSystem system)
        {
            byte[] data = SpikeWav.BuildClick(system.MixerRate);
            var info = new FMOD.CREATESOUNDEXINFO();
            info.cbsize = Marshal.SizeOf(info);
            info.length = (uint)data.Length;

            FMOD.MODE mode = FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF;
            FMOD.RESULT result = system.CoreSystem.createSound(data, mode, ref info, out FMOD.Sound sound);
            if (result != FMOD.RESULT.OK) return "createSound: " + result;

            result = system.CoreSystem.playSound(sound, system.Master, false, out _);
            if (result != FMOD.RESULT.OK) return "playSound: " + result;
            return null;
        }
    }
}
#endif
