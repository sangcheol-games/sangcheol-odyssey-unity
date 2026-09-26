#if SCO_AUDIO_HARNESS
using System;
using System.IO;
using System.Text;

namespace SCOdyssey.Testing.AudioSpike
{
    // 스파이크용 PCM16 WAV를 코드로 만든다. 클릭은 시작이 날카로운 5ms 2kHz 버스트라서
    // 루프백 녹음에서 시작 시각을 눈으로 재기 쉽다.
    public static class SpikeWav
    {
        public const double ClickSeconds = 0.005;
        public const double ClickFrequency = 2000.0;

        // 짧은 클릭 한 개(모노).
        public static byte[] BuildClick(int sampleRate)
        {
            int frames = (int)Math.Round(ClickSeconds * sampleRate);
            short[] samples = new short[frames];
            WriteClick(samples, 0, 1, 0, sampleRate);
            return Encode(samples, sampleRate, 1);
        }

        // interval초마다 왼쪽 채널에만 클릭이 있는 스테레오 트랙을 파일로 쓴다.
        public static void WriteClickTrack(string path, int sampleRate, double lengthSeconds, double interval)
        {
            int frames = (int)Math.Round(lengthSeconds * sampleRate);
            short[] samples = new short[frames * 2];
            for (double t = 0; t < lengthSeconds - ClickSeconds; t += interval)
            {
                int startFrame = (int)Math.Round(t * sampleRate);
                WriteClick(samples, startFrame, 2, 0, sampleRate);
            }
            File.WriteAllBytes(path, Encode(samples, sampleRate, 2));
        }

        private static void WriteClick(short[] samples, int startFrame, int channels, int channel, int sampleRate)
        {
            int frames = (int)Math.Round(ClickSeconds * sampleRate);
            int totalFrames = samples.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                int frame = startFrame + i;
                if (frame >= totalFrames) break;
                double value = 0.8 * Math.Sin(2.0 * Math.PI * ClickFrequency * i / sampleRate);
                samples[frame * channels + channel] = (short)(value * short.MaxValue);
            }
        }

        private static byte[] Encode(short[] samples, int sampleRate, int channels)
        {
            int dataBytes = samples.Length * 2;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++) writer.Write(samples[i]);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
#endif
