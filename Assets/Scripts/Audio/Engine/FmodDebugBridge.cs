using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SCOdyssey.Audio.Engine
{
    // FMOD 내부 경고를 Unity 콘솔로 옮긴다. 콜백은 FMOD 스레드에서 오므로 문자열만 고정 링에 넣고,
    // 로그는 메인 스레드가 Flush에서 남긴다. 릴리스용 FMOD 라이브러리에서는 Initialize가 실패하며 그때는 쓰지 않는다.
    internal static class FmodDebugBridge
    {
        private const int Capacity = 32;

        private static readonly object Gate = new object();
        private static readonly string[] s_messages = new string[Capacity];
        private static readonly string[] s_flushBuffer = new string[Capacity];
        private static int s_count;
        private static int s_dropped;
        private static bool s_installed;
        private static FMOD.DEBUG_CALLBACK s_callback;

        public static bool IsInstalled
        {
            get { return s_installed; }
        }

        public static void Install()
        {
            if (s_installed) return;
            if (s_callback == null) s_callback = OnDebug;
            FMOD.RESULT result = FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.WARNING, FMOD.DEBUG_MODE.CALLBACK, s_callback, null);
            s_installed = result == FMOD.RESULT.OK;
        }

        // FMOD 에디터 코드(EditorUtils)와 같은 설정으로 되돌린다.
        public static void Uninstall()
        {
            if (!s_installed) return;
#if UNITY_EDITOR
            FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.LOG, FMOD.DEBUG_MODE.FILE, null, "fmod_editor.log");
#else
            FMOD.Debug.Initialize(FMOD.DEBUG_FLAGS.NONE, FMOD.DEBUG_MODE.TTY, null, null);
#endif
            s_installed = false;
            Flush();
        }

        public static void Flush()
        {
            int count;
            int dropped;
            lock (Gate)
            {
                count = s_count;
                dropped = s_dropped;
                Array.Copy(s_messages, s_flushBuffer, count);
                Array.Clear(s_messages, 0, count);
                s_count = 0;
                s_dropped = 0;
            }

            for (int i = 0; i < count; i++)
            {
                Debug.LogWarning("[Audio] FMOD: " + s_flushBuffer[i]);
                s_flushBuffer[i] = null;
            }
            if (dropped > 0) Debug.LogWarning("[Audio] FMOD 경고 " + dropped + "건을 더 버렸습니다.");
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.DEBUG_CALLBACK))]
        private static FMOD.RESULT OnDebug(FMOD.DEBUG_FLAGS flags, IntPtr file, int line, IntPtr func, IntPtr message)
        {
            string text = "";
            if (message != IntPtr.Zero) text = Marshal.PtrToStringAnsi(message);
            lock (Gate)
            {
                if (s_count < Capacity)
                {
                    s_messages[s_count] = text.TrimEnd();
                    s_count++;
                }
                else
                {
                    s_dropped++;
                }
            }
            return FMOD.RESULT.OK;
        }
    }
}
