#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP15: FMOD ERROR 콜백과 Debug 콜백이 어느 스레드에서 오는지 기록한다.
    // 콜백 안에서는 lock + 고정 배열에 복사만 한다(Unity API, FMOD API, 로그 호출 금지).
    public static class SpikeCallbacks
    {
        private const int Capacity = 64;
        private const int KindError = 0;
        private const int KindDebug = 1;

        private static readonly object Gate = new object();
        private static readonly int[] ThreadIds = new int[Capacity];
        private static readonly int[] Kinds = new int[Capacity];
        private static readonly FMOD.RESULT[] Results = new FMOD.RESULT[Capacity];
        private static int _count;

        // 네이티브 쪽이 델리게이트를 들고 있는 동안 GC가 수거하지 않도록 static으로 보관한다.
        private static FMOD.SYSTEM_CALLBACK s_systemCallback;
        private static FMOD.DEBUG_CALLBACK s_debugCallback;
        private static bool s_debugInstalled;

        [AOT.MonoPInvokeCallback(typeof(FMOD.SYSTEM_CALLBACK))]
        private static FMOD.RESULT OnSystemCallback(IntPtr system, FMOD.SYSTEM_CALLBACK_TYPE type, IntPtr data1, IntPtr data2, IntPtr userData)
        {
            FMOD.RESULT errorResult = FMOD.RESULT.OK;
            if (type == FMOD.SYSTEM_CALLBACK_TYPE.ERROR && data1 != IntPtr.Zero)
            {
                FMOD.ERRORCALLBACK_INFO info = Marshal.PtrToStructure<FMOD.ERRORCALLBACK_INFO>(data1);
                errorResult = info.result;
            }
            Record(KindError, errorResult);
            return FMOD.RESULT.OK;
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.DEBUG_CALLBACK))]
        private static FMOD.RESULT OnDebugCallback(FMOD.DEBUG_FLAGS flags, IntPtr file, int line, IntPtr func, IntPtr message)
        {
            Record(KindDebug, FMOD.RESULT.OK);
            return FMOD.RESULT.OK;
        }

        private static void Record(int kind, FMOD.RESULT result)
        {
            lock (Gate)
            {
                if (_count >= Capacity) return;
                ThreadIds[_count] = Environment.CurrentManagedThreadId;
                Kinds[_count] = kind;
                Results[_count] = result;
                _count++;
            }
        }

        public static IEnumerator Run(SpikeFmodSystem system, Action<string> progress)
        {
            progress("SP15 콜백 기록 중");
            int mainThread = Environment.CurrentManagedThreadId;
            lock (Gate) { _count = 0; }

            s_systemCallback = OnSystemCallback;
            system.CoreSystem.setCallback(s_systemCallback, FMOD.SYSTEM_CALLBACK_TYPE.ERROR);

            s_debugCallback = OnDebugCallback;
            FMOD.RESULT debugResult = FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.WARNING, FMOD.DEBUG_MODE.CALLBACK, s_debugCallback, null);
            s_debugInstalled = debugResult == FMOD.RESULT.OK;

            // 동기 오류: 없는 파일을 바로 연다.
            system.CoreSystem.createSound("__spike_missing_sync__.wav", FMOD.MODE.DEFAULT, out FMOD.Sound syncSound);

            // 비동기 오류: 없는 파일을 NONBLOCKING으로 연다. 오류는 FMOD 비동기 스레드에서 올 수 있다.
            var info = new FMOD.CREATESOUNDEXINFO();
            info.cbsize = Marshal.SizeOf(info);
            FMOD.MODE asyncMode = FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING;
            system.CoreSystem.createSound("__spike_missing_async__.wav", asyncMode, ref info, out FMOD.Sound asyncSound);

            for (int frame = 0; frame < 60; frame++)
            {
                system.CoreSystem.update();
                yield return null;
            }

            if (syncSound.hasHandle()) syncSound.release();
            if (asyncSound.hasHandle()) asyncSound.release();
            Shutdown(system);

            int count;
            var entries = new List<string>();
            int errorCount = 0;
            int offMainCount = 0;
            lock (Gate)
            {
                count = _count;
                for (int i = 0; i < count; i++)
                {
                    string kind = "ERROR";
                    if (Kinds[i] == KindDebug) kind = "Debug";
                    if (Kinds[i] == KindError) errorCount++;
                    if (ThreadIds[i] != mainThread) offMainCount++;
                    string entry = kind + "@" + ThreadIds[i] + "(" + Results[i] + ")";
                    if (!entries.Contains(entry)) entries.Add(entry);
                }
            }

            string measured = string.Format("메인 스레드 {0}, 콜백 {1}건(ERROR {2}, 메인 외 스레드 {3}), Debug.Initialize: {4}, 종류: {5}",
                mainThread, count, errorCount, offMainCount, debugResult, string.Join(" / ", entries));
            SpikeReport.Summary("SP15", SpikeReport.PassIf(errorCount > 0), "ERROR 콜백 수신, 크래시 없음", measured, system.Describe());
            progress("SP15 완료");
        }

        // 콜백을 해제하고 FMOD Debug를 원래 모드로 되돌린다. 도메인 리로드 전에도 호출한다.
        public static void Shutdown(SpikeFmodSystem system)
        {
            if (system != null && system.CoreSystem.hasHandle()) system.CoreSystem.setCallback(null, 0);
            if (!s_debugInstalled) return;
#if UNITY_EDITOR
            // FMOD 에디터 코드(EditorUtils)와 같은 설정으로 되돌린다.
            FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.LOG, FMOD.DEBUG_MODE.FILE, null, "fmod_editor.log");
#else
            FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.NONE, FMOD.DEBUG_MODE.TTY, null, null);
#endif
            s_debugInstalled = false;
        }
    }
}
#endif
