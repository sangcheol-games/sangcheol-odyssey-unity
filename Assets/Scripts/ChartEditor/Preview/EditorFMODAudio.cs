using System;
using System.Runtime.InteropServices;
using FMODUnity;
using UnityEngine;

namespace SCOdyssey.ChartEditor.Preview
{
    /// <summary>
    /// 채보 에디터 전용 FMOD Low-Level 오디오 플레이어.
    /// FMODAudioManager를 수정하지 않고 에디터에서 독립적으로 FMOD를 사용한다.
    /// CREATESAMPLE로 전체 디코딩하여 재생과 PCM 데이터 추출을 모두 지원.
    /// </summary>
    public class EditorFMODAudio : MonoBehaviour
    {
        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private FMOD.ChannelGroup _masterGroup;
        private bool _isLoaded;
        private bool _isLoading;

        private void Awake()
        {
            RuntimeManager.CoreSystem.getMasterChannelGroup(out _masterGroup);
        }

        // NONBLOCKING 로드 완료 폴링
        private void Update()
        {
            if (!_isLoading) return;

            _sound.getOpenState(out FMOD.OPENSTATE state, out _, out _, out _);

            if (state == FMOD.OPENSTATE.READY)
            {
                _isLoaded = true;
                _isLoading = false;
                Debug.Log("[EditorFMODAudio] 오디오 로드 완료.");
            }
            else if (state == FMOD.OPENSTATE.ERROR)
            {
                _isLoading = false;
                Debug.LogError("[EditorFMODAudio] 오디오 로드 실패 (OPENSTATE.ERROR).");
            }
        }

        private void OnDestroy()
        {
            Stop();
            if (_sound.hasHandle()) _sound.release();
        }

        // --- 속성 ---

        public bool IsLoaded => _isLoaded;
        public bool IsLoading => _isLoading;

        public bool IsPlaying
        {
            get
            {
                if (!_channel.hasHandle()) return false;
                _channel.isPlaying(out bool playing);
                return playing;
            }
        }

        // --- 오디오 로드 ---

        /// <summary>
        /// 임의 경로의 오디오 파일을 FMOD CREATESAMPLE + NONBLOCKING으로 로드.
        /// 전체 디코딩하여 PCM 직접 접근 가능.
        /// </summary>
        public void LoadAudio(string fullPath)
        {
            // 이전 사운드 해제
            if (_sound.hasHandle())
            {
                Stop();
                _sound.release();
                _sound = default;
            }

            _isLoaded = false;
            _isLoading = false;

            FMOD.CREATESOUNDEXINFO exInfo = new FMOD.CREATESOUNDEXINFO();
            exInfo.cbsize = Marshal.SizeOf(exInfo);

            // CREATESAMPLE: 전체 디코딩 (PCM lock 접근 가능)
            // NONBLOCKING: 비동기 로드 (Update에서 폴링)
            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
                fullPath,
                FMOD.MODE.CREATESAMPLE | FMOD.MODE.NONBLOCKING,
                ref exInfo,
                out _sound);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"[EditorFMODAudio] createSound 실패: {result} | 경로: {fullPath}");
                return;
            }

            _isLoading = true;
            Debug.Log($"[EditorFMODAudio] 오디오 로딩 시작: {fullPath}");
        }

        // --- 재생 제어 ---

        /// <summary>
        /// 오디오 재생.
        /// audioStartTime < 0: 해당 초만큼 딜레이 후 처음부터 재생 (0번/1번 마디)
        /// audioStartTime >= 0: 해당 위치로 시크 후 즉시 재생 (2번 마디 이상)
        /// </summary>
        public void Play(double audioStartTime)
        {
            if (!_isLoaded)
            {
                Debug.LogWarning("[EditorFMODAudio] Play: 오디오가 로드되지 않았습니다.");
                return;
            }

            // 이전 채널 정지
            if (_channel.hasHandle() && IsPlaying)
                _channel.stop();

            RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out _, out _);

            // 일시정지 상태로 채널 생성
            RuntimeManager.CoreSystem.playSound(_sound, _masterGroup, true, out _channel);

            // 시크 (audioStartTime >= 0인 경우)
            if (audioStartTime >= 0)
            {
                uint posMs = (uint)(audioStartTime * 1000.0);
                _channel.setPosition(posMs, FMOD.TIMEUNIT.MS);
            }

            // DSP 클록 기반 sample-accurate 스케줄링
            double delaySeconds = audioStartTime < 0 ? -audioStartTime : 0;
            ulong startDspClock = (ulong)((GetDSPTime() + delaySeconds) * sampleRate);
            _channel.setDelay(startDspClock, 0, false);
            _channel.setPaused(false);
        }

        public void Pause()
        {
            if (_channel.hasHandle())
                _channel.setPaused(true);
        }

        public void Resume()
        {
            if (_channel.hasHandle())
                _channel.setPaused(false);
        }

        public void Stop()
        {
            if (_channel.hasHandle() && IsPlaying)
                _channel.stop();
        }

        // --- 시간 ---

        /// <summary>
        /// FMOD DSP 클록 기반 현재 시간 (초). AudioSettings.dspTime 대체.
        /// </summary>
        public double GetDSPTime()
        {
            _masterGroup.getDSPClock(out ulong clock, out _);
            RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out _, out _);
            return (double)clock / sampleRate;
        }

        // --- PCM 데이터 추출 (onset 분석용) ---

        /// <summary>
        /// 로드된 오디오의 PCM 데이터를 모노 float[]로 반환.
        /// IsLoaded = true 이후에만 호출 가능.
        /// </summary>
        /// <param name="sampleRate">오디오의 샘플레이트 (Hz)</param>
        /// <returns>모노 믹스된 PCM float[] (없으면 null)</returns>
        public float[] GetMonoSamples(out int sampleRate)
        {
            sampleRate = 44100;

            if (!_isLoaded)
            {
                Debug.LogError("[EditorFMODAudio] GetMonoSamples: 오디오가 로드되지 않았습니다.");
                return null;
            }

            // 샘플레이트 조회
            _sound.getDefaults(out float defaultFreq, out _);
            sampleRate = (int)defaultFreq;

            // 채널 수 및 포맷 조회
            _sound.getFormat(out _, out FMOD.SOUND_FORMAT fmt, out int channels, out _);

            // PCM 바이트 길이 조회
            _sound.getLength(out uint lengthBytes, FMOD.TIMEUNIT.PCMBYTES);

            if (lengthBytes == 0)
            {
                Debug.LogError("[EditorFMODAudio] GetMonoSamples: PCM 길이가 0입니다.");
                return null;
            }

            // PCM 데이터 lock
            FMOD.RESULT lockResult = _sound.@lock(0, lengthBytes,
                out IntPtr ptr1, out IntPtr ptr2,
                out uint len1, out uint len2);

            if (lockResult != FMOD.RESULT.OK)
            {
                Debug.LogError($"[EditorFMODAudio] sound.lock 실패: {lockResult}");
                return null;
            }

            float[] rawSamples = null;

            try
            {
                if (fmt == FMOD.SOUND_FORMAT.PCMFLOAT)
                {
                    // float32: 4바이트 per sample
                    int totalFloats = (int)(len1 / 4);
                    rawSamples = new float[totalFloats];
                    Marshal.Copy(ptr1, rawSamples, 0, totalFloats);
                }
                else if (fmt == FMOD.SOUND_FORMAT.PCM16)
                {
                    // PCM16: 2바이트 per sample → float 변환
                    int totalShorts = (int)(len1 / 2);
                    short[] shorts = new short[totalShorts];
                    Marshal.Copy(ptr1, shorts, 0, totalShorts);
                    rawSamples = new float[totalShorts];
                    for (int i = 0; i < totalShorts; i++)
                        rawSamples[i] = shorts[i] / 32768f;
                }
                else
                {
                    Debug.LogError($"[EditorFMODAudio] 지원하지 않는 PCM 포맷: {fmt}");
                }
            }
            finally
            {
                _sound.unlock(ptr1, ptr2, len1, len2);
            }

            if (rawSamples == null) return null;

            // 모노 믹스 (이미 모노면 그대로 반환)
            if (channels == 1) return rawSamples;

            int monoLength = rawSamples.Length / channels;
            float[] mono = new float[monoLength];
            for (int i = 0; i < monoLength; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                    sum += rawSamples[i * channels + ch];
                mono[i] = sum / channels;
            }

            return mono;
        }
    }
}
