#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP2: Unity 메인 스레드의 COM 아파트먼트와 ASIO 초기화를 확인한다.
    public static class SpikeAsio
    {
        [DllImport("ole32.dll")]
        private static extern int CoGetApartmentType(out int aptType, out int aptQualifier);

        // APTTYPE: 0 STA, 1 MTA, 2 NA, 3 MAINSTA
        public static string DescribeApartment(out bool isSta)
        {
            isSta = false;
            int hr;
            int type;
            int qualifier;
            try
            {
                hr = CoGetApartmentType(out type, out qualifier);
            }
            catch (Exception e)
            {
                return "조회 실패: " + e.GetType().Name;
            }

            if (hr != 0) return string.Format("COM 미초기화(HRESULT 0x{0:X8})", hr);

            string name = "알 수 없음(" + type + ")";
            if (type == 0) name = "STA";
            if (type == 1) name = "MTA";
            if (type == 2) name = "NA";
            if (type == 3) name = "MAINSTA";
            isSta = type == 0 || type == 3;
            return name + ", qualifier " + qualifier;
        }

        // 초기화하지 않은 임시 System으로 출력 타입별 장치 목록을 읽는다.
        public static List<string> ListDrivers(FMOD.OUTPUTTYPE output, out string error)
        {
            var names = new List<string>();
            error = null;
            FMOD.RESULT result = FMOD.Factory.System_Create(out FMOD.System temp);
            if (result != FMOD.RESULT.OK)
            {
                error = "System_Create: " + result;
                return names;
            }

            result = temp.setOutput(output);
            if (result != FMOD.RESULT.OK)
            {
                error = "setOutput: " + result;
                temp.release();
                return names;
            }

            result = temp.getNumDrivers(out int driverCount);
            if (result != FMOD.RESULT.OK)
            {
                error = "getNumDrivers: " + result;
                temp.release();
                return names;
            }

            for (int i = 0; i < driverCount; i++)
            {
                result = temp.getDriverInfo(i, out string name, 256, out _, out int rate, out _, out _);
                if (result == FMOD.RESULT.OK) names.Add(name + " (" + rate + "Hz)");
                else names.Add("[" + i + "] 오류: " + result);
            }
            temp.release();
            return names;
        }

        // 선택한 ASIO 드라이버로 init을 cycles번, 같은 System close→init을 cycles번 한다.
        public static IEnumerator RunInitCycles(SpikeOutputConfig config, int cycles, Action<string> progress)
        {
            string apartment = DescribeApartment(out bool isSta);
            List<string> drivers = ListDrivers(FMOD.OUTPUTTYPE.ASIO, out string listError);
            string driverText = string.Join(" / ", drivers);
            if (listError != null) driverText = listError;
            SpikeReport.Summary("SP2-list", SpikeReport.Result.Info, "ASIO 목록 조회", "드라이버 " + drivers.Count + "개: " + driverText, "아파트먼트: " + apartment);

            if (drivers.Count == 0)
            {
                SpikeReport.Summary("SP2-init", SpikeReport.Result.Fail, "ASIO init 성공", "ASIO 드라이버 없음", "아파트먼트: " + apartment);
                progress("SP2: ASIO 드라이버 없음");
                yield break;
            }

            config.Output = FMOD.OUTPUTTYPE.ASIO;
            int initFailures = 0;
            string lastError = "";
            string context = "";
            for (int i = 0; i < cycles; i++)
            {
                progress(string.Format("SP2 init {0}/{1}", i + 1, cycles));
                SpikeFmodSystem system = SpikeFmodSystem.Create(config, out string error);
                if (system == null)
                {
                    initFailures++;
                    lastError = error;
                }
                else
                {
                    context = system.Describe();
                    SpikeLifecycle.PlayClick(system);
                    system.CoreSystem.update();
                    system.Dispose();
                }
                yield return null;
            }

            int reinitFailures = 0;
            SpikeFmodSystem shared = SpikeFmodSystem.Create(config, out string sharedError);
            if (shared == null)
            {
                reinitFailures = cycles;
                lastError = sharedError;
            }
            else
            {
                for (int i = 0; i < cycles; i++)
                {
                    progress(string.Format("SP2 close→init {0}/{1}", i + 1, cycles));
                    string error = shared.CloseAndReinit();
                    if (error != null)
                    {
                        reinitFailures++;
                        lastError = error;
                        break;
                    }
                    shared.CoreSystem.update();
                    yield return null;
                }
                context = shared.Describe();
                shared.Dispose();
            }

            bool pass = initFailures == 0 && reinitFailures == 0;
            string measured = string.Format("init 실패 {0}/{1}, close→init 실패 {2}/{1}", initFailures, cycles, reinitFailures);
            if (!pass) measured += ", 마지막 오류: " + lastError;
            if (shared != null && shared.BufferCount == 2) measured += ", 버퍼 개수 2(저지연 모드 추정)";
            SpikeReport.Summary("SP2-init", SpikeReport.PassIf(pass), "ASIO init·close→init 모두 성공",
                measured, context + ", 아파트먼트: " + apartment + ", STA: " + isSta);
            progress("SP2 완료");
        }
    }
}
#endif
