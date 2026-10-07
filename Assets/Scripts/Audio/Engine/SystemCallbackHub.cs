using System;
using System.Runtime.InteropServices;

namespace SCOdyssey.Audio.Engine
{
    // 메인 스레드가 한 프레임에 한 번 가져가는 콜백 기록.
    internal sealed class CallbackSnapshot
    {
        public readonly FMOD.RESULT[] Errors = new FMOD.RESULT[SystemCallbackHub.ErrorCapacity];
        public readonly FMOD.ERRORCALLBACK_INSTANCETYPE[] ErrorSources = new FMOD.ERRORCALLBACK_INSTANCETYPE[SystemCallbackHub.ErrorCapacity];
        public int ErrorCount;
        public int DroppedErrors;
        public bool DeviceLost;
        public bool DeviceListChanged;
        public bool DeviceReinitialized;
    }

    // FMOD System 콜백을 받는 유일한 곳.
    // ERROR는 FMOD 스레드에서도 오므로 짧은 lock 안에서 고정 배열에 복사만 한다(Unity API·FMOD API·로그 금지).
    // 장치 콜백은 system.update() 안(메인 스레드)에서 오지만 같은 함수를 타므로 같은 방식으로 받는다.
    // 마스크는 항상 명시한다. 기본값 ALL은 FMOD의 기본 장치 자동 전환을 꺼 버린다.
    internal static class SystemCallbackHub
    {
        public const int ErrorCapacity = 32;

        private static readonly object Gate = new object();
        private static readonly FMOD.RESULT[] s_errors = new FMOD.RESULT[ErrorCapacity];
        private static readonly FMOD.ERRORCALLBACK_INSTANCETYPE[] s_errorSources = new FMOD.ERRORCALLBACK_INSTANCETYPE[ErrorCapacity];
        private static int s_errorCount;
        private static int s_droppedErrors;
        private static bool s_deviceLost;
        private static bool s_deviceListChanged;
        private static bool s_deviceReinitialized;

        // 네이티브가 델리게이트를 들고 있는 동안 GC가 수거하지 않도록 static으로 보관한다.
        private static FMOD.SYSTEM_CALLBACK s_callback;

        // Follow-Default: FMOD 자동 전환에 맡긴다. Pinned: 목록 변경을 받아 고정 장치가 사라졌는지 확인한다.
        public static FMOD.SYSTEM_CALLBACK_TYPE MaskFor(bool pinned)
        {
            FMOD.SYSTEM_CALLBACK_TYPE mask = FMOD.SYSTEM_CALLBACK_TYPE.ERROR
                | FMOD.SYSTEM_CALLBACK_TYPE.DEVICELOST
                | FMOD.SYSTEM_CALLBACK_TYPE.DEVICEREINITIALIZE;
            if (pinned) mask |= FMOD.SYSTEM_CALLBACK_TYPE.DEVICELISTCHANGED;
            return mask;
        }

        // init 전에 부른다. 세대가 바뀌면 이전 기록을 버린다.
        public static FMOD.RESULT Install(FMOD.System system, bool pinned)
        {
            if (s_callback == null) s_callback = OnCallback;
            Clear();
            return system.setCallback(s_callback, MaskFor(pinned));
        }

        public static void Uninstall(FMOD.System system)
        {
            if (system.hasHandle()) system.setCallback(null, 0);
        }

        public static void Drain(CallbackSnapshot into)
        {
            lock (Gate)
            {
                Array.Copy(s_errors, into.Errors, s_errorCount);
                Array.Copy(s_errorSources, into.ErrorSources, s_errorCount);
                into.ErrorCount = s_errorCount;
                into.DroppedErrors = s_droppedErrors;
                into.DeviceLost = s_deviceLost;
                into.DeviceListChanged = s_deviceListChanged;
                into.DeviceReinitialized = s_deviceReinitialized;
                ClearLocked();
            }
        }

        private static void Clear()
        {
            lock (Gate)
            {
                ClearLocked();
            }
        }

        private static void ClearLocked()
        {
            s_errorCount = 0;
            s_droppedErrors = 0;
            s_deviceLost = false;
            s_deviceListChanged = false;
            s_deviceReinitialized = false;
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.SYSTEM_CALLBACK))]
        private static FMOD.RESULT OnCallback(IntPtr system, FMOD.SYSTEM_CALLBACK_TYPE type, IntPtr data1, IntPtr data2, IntPtr userData)
        {
            if (type == FMOD.SYSTEM_CALLBACK_TYPE.ERROR)
            {
                FMOD.RESULT result = FMOD.RESULT.OK;
                FMOD.ERRORCALLBACK_INSTANCETYPE source = FMOD.ERRORCALLBACK_INSTANCETYPE.NONE;
                if (data1 != IntPtr.Zero)
                {
                    FMOD.ERRORCALLBACK_INFO info = Marshal.PtrToStructure<FMOD.ERRORCALLBACK_INFO>(data1);
                    result = info.result;
                    source = info.instancetype;
                }
                lock (Gate)
                {
                    if (s_errorCount < ErrorCapacity)
                    {
                        s_errors[s_errorCount] = result;
                        s_errorSources[s_errorCount] = source;
                        s_errorCount++;
                    }
                    else
                    {
                        s_droppedErrors++;
                    }
                }
                return FMOD.RESULT.OK;
            }

            lock (Gate)
            {
                if (type == FMOD.SYSTEM_CALLBACK_TYPE.DEVICELOST) s_deviceLost = true;
                if (type == FMOD.SYSTEM_CALLBACK_TYPE.DEVICELISTCHANGED) s_deviceListChanged = true;
                if (type == FMOD.SYSTEM_CALLBACK_TYPE.DEVICEREINITIALIZE) s_deviceReinitialized = true;
            }
            return FMOD.RESULT.OK;
        }
    }
}
