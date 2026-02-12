using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.ChartEditor.Analysis
{
    /// <summary>
    /// 음원 PCM 데이터를 분석하여 onset(음 시작점)을 감지하는 클래스.
    /// 에너지 기반 스펙트럼 플럭스 알고리즘 사용.
    /// </summary>
    public static class AudioOnsetDetector
    {
        private const int FrameSize = 1024;  // 분석 프레임 크기 (샘플 수)
        private const int HopSize = 512;     // 프레임 이동 간격

        /// <summary>
        /// onset 정보
        /// </summary>
        public struct OnsetInfo
        {
            public double time;       // onset 시간 (초)
            public float strength;    // 강도 (0~1 정규화)
        }

        /// <summary>
        /// AudioClip에서 onset을 감지하여 반환
        /// </summary>
        /// <param name="clip">분석할 AudioClip</param>
        /// <param name="sensitivity">감도 (낮을수록 더 많은 onset 감지, 기본 1.5)</param>
        /// <param name="minInterval">onset 간 최소 간격 (초, 기본 0.05)</param>
        public static List<OnsetInfo> DetectOnsets(AudioClip clip, float sensitivity = 1.5f, float minInterval = 0.05f)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioOnsetDetector] AudioClip is null");
                return new List<OnsetInfo>();
            }

            // 모노 믹스 샘플 추출
            float[] samples = GetMonoSamples(clip);
            int sampleRate = clip.frequency;

            // 프레임별 에너지 계산
            float[] energies = CalculateFrameEnergies(samples);

            if (energies.Length < 3)
            {
                Debug.LogWarning("[AudioOnsetDetector] Audio too short for analysis");
                return new List<OnsetInfo>();
            }

            // 스펙트럼 플럭스 (에너지 변화량)
            float[] flux = CalculateFlux(energies);

            // 적응형 임계값
            float mean = CalculateMean(flux);
            float stddev = CalculateStdDev(flux, mean);
            float threshold = mean + sensitivity * stddev;

            // 피크 추출 → onset
            List<OnsetInfo> onsets = PeakPick(flux, threshold, sampleRate, minInterval);

            Debug.Log($"[AudioOnsetDetector] Detected {onsets.Count} onsets (sensitivity={sensitivity}, threshold={threshold:F6})");
            return onsets;
        }

        #region 샘플 처리

        /// <summary>
        /// AudioClip에서 모노 믹스 샘플 추출
        /// </summary>
        private static float[] GetMonoSamples(AudioClip clip)
        {
            float[] rawSamples = new float[clip.samples * clip.channels];
            clip.GetData(rawSamples, 0);

            // 이미 모노면 그대로 반환
            if (clip.channels == 1) return rawSamples;

            // 스테레오 → 모노 믹스
            float[] mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < clip.channels; ch++)
                {
                    sum += rawSamples[i * clip.channels + ch];
                }
                mono[i] = sum / clip.channels;
            }
            return mono;
        }

        #endregion

        #region 에너지 분석

        /// <summary>
        /// 프레임별 RMS 에너지 계산
        /// </summary>
        private static float[] CalculateFrameEnergies(float[] samples)
        {
            int frameCount = (samples.Length - FrameSize) / HopSize + 1;
            if (frameCount <= 0) return new float[0];

            float[] energies = new float[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                int start = i * HopSize;
                float energy = 0f;
                int count = 0;

                for (int j = 0; j < FrameSize && start + j < samples.Length; j++)
                {
                    energy += samples[start + j] * samples[start + j];
                    count++;
                }

                energies[i] = count > 0 ? energy / count : 0f;
            }

            return energies;
        }

        /// <summary>
        /// 스펙트럼 플럭스 계산 (양의 에너지 변화량만 추출)
        /// </summary>
        private static float[] CalculateFlux(float[] energies)
        {
            float[] flux = new float[energies.Length];
            for (int i = 1; i < energies.Length; i++)
            {
                float diff = energies[i] - energies[i - 1];
                flux[i] = Mathf.Max(0f, diff); // onset은 에너지 증가 방향만
            }
            return flux;
        }

        #endregion

        #region 피크 추출

        /// <summary>
        /// 임계값 이상의 로컬 피크를 onset으로 추출
        /// </summary>
        private static List<OnsetInfo> PeakPick(float[] flux, float threshold, int sampleRate, float minInterval)
        {
            var onsets = new List<OnsetInfo>();
            double lastOnsetTime = -1.0;
            float maxFlux = CalculateMax(flux);

            for (int i = 1; i < flux.Length - 1; i++)
            {
                // 로컬 최대값 + 임계값 초과
                if (flux[i] > threshold && flux[i] > flux[i - 1] && flux[i] >= flux[i + 1])
                {
                    double time = (double)i * HopSize / sampleRate;

                    // 최소 간격 확인
                    if (time - lastOnsetTime < minInterval) continue;

                    onsets.Add(new OnsetInfo
                    {
                        time = time,
                        strength = maxFlux > 0f ? flux[i] / maxFlux : 0f
                    });
                    lastOnsetTime = time;
                }
            }

            return onsets;
        }

        #endregion

        #region 통계 유틸

        private static float CalculateMean(float[] values)
        {
            if (values.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        private static float CalculateStdDev(float[] values, float mean)
        {
            if (values.Length == 0) return 0f;
            float sumSqDiff = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                float diff = values[i] - mean;
                sumSqDiff += diff * diff;
            }
            return Mathf.Sqrt(sumSqDiff / values.Length);
        }

        private static float CalculateMax(float[] values)
        {
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max) max = values[i];
            }
            return max;
        }

        #endregion
    }
}
