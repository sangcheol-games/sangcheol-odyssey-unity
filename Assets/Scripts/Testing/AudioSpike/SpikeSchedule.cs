#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP4: 곡 스트림과 메트로놈 클릭을 같은 곡 시계로 예약하고, 일시정지·재개를 반복해도 두 소리가 맞는지 본다.
    // 곡 파일은 0.5초마다 왼쪽 채널에 클릭이 있고, 메트로놈은 오른쪽으로 패닝한다.
    // 루프백 녹음(Audacity)에서 왼쪽과 오른쪽 클릭의 시작 차이를 재면 예약 정확도가 나온다.
    public sealed class SpikeSchedule
    {
        private const double LeadInSeconds = 1.0;
        private const double ClickInterval = 0.5;
        private const double TrackSeconds = 60.0;
        private const double RunSeconds = 3.0;
        private const double PauseSeconds = 1.0;
        private const double ScheduleHorizonSeconds = 0.2;
        private const double SeekTimeoutSeconds = 3.0;

        private SpikeFmodSystem _system;
        private FMOD.ChannelGroup _songGroup;
        private FMOD.Sound _song;
        private FMOD.Sound _click;
        private FMOD.Channel _songChannel;
        private int _songRate;

        // 현재 세그먼트: DSP 시각 _segmentStartDsp부터 곡 시각 _segmentStartTime에서 흐른다.
        private bool _running;
        private double _segmentStartTime;
        private ulong _segmentStartDsp;
        private int _nextClickIndex;
        private readonly List<FMOD.Channel> _pendingClicks = new List<FMOD.Channel>();

        private readonly List<double> _seekSeconds = new List<double>();
        private readonly List<double> _positionErrors = new List<double>();
        private readonly List<bool> _positionSeeked = new List<bool>();
        private bool _positionChecked;
        private bool _segmentSeeked;
        private int _lateClicks;
        private int _pauseCount;
        private int _seekFailures;
        private int _notReadyRetries;
        private double _worstSeekError;
        private StreamWriter _log;

        public IEnumerator Run(SpikeFmodSystem system, int pauseCycles, Action<string> progress)
        {
            _system = system;
            string error = Setup();
            if (error != null)
            {
                SpikeReport.Summary("SP4", SpikeReport.Result.Fail, "준비 성공", error, system.Describe());
                Cleanup();
                yield break;
            }

            progress("SP4 곡 여는 중");
            double openWait = 0;
            while (!IsSongOpen())
            {
                openWait += Time.unscaledDeltaTime;
                if (openWait > 10.0)
                {
                    SpikeReport.Summary("SP4", SpikeReport.Result.Fail, "곡 열기 10초 이내", "시간 초과", system.Describe());
                    Cleanup();
                    yield break;
                }
                yield return null;
            }
            _song.getDefaults(out float frequency, out _);
            _songRate = (int)frequency;

            // 시작: 곡 시각 0에서 커밋한다(리드인 1초 뒤에 음원 시작).
            yield return PrepareAndCommit(0.0, progress);

            // 첫 일시정지는 리드인 중(0.5초)에 걸어 H2 경로를 확인한다.
            yield return RunFor(0.5);
            for (int cycle = 0; cycle < pauseCycles; cycle++)
            {
                progress(string.Format("SP4 일시정지·재개 {0}/{1}", cycle + 1, pauseCycles));
                double pausedAt = Pause();
                yield return Wait(PauseSeconds);
                yield return PrepareAndCommit(pausedAt, progress);
                yield return RunFor(RunSeconds);
            }

            progress("SP4 마무리 재생");
            yield return RunFor(5.0);

            WriteSummary();
            Cleanup();
            progress("SP4 완료");
        }

        private string Setup()
        {
            FMOD.RESULT result = _system.CoreSystem.createChannelGroup("SPIKE.Song", out _songGroup);
            if (result != FMOD.RESULT.OK) return "createChannelGroup: " + result;
            result = _system.Master.addGroup(_songGroup, true);
            if (result != FMOD.RESULT.OK) return "addGroup: " + result;

            Directory.CreateDirectory(SpikeReport.Folder);
            string trackPath = Path.Combine(SpikeReport.Folder, "spike_clicktrack.wav");
            SpikeWav.WriteClickTrack(trackPath, 48000, TrackSeconds, ClickInterval);

            var info = new FMOD.CREATESOUNDEXINFO();
            info.cbsize = Marshal.SizeOf(info);
            FMOD.MODE songMode = FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING | FMOD.MODE.ACCURATETIME | FMOD.MODE.LOOP_OFF;
            result = _system.CoreSystem.createSound(trackPath, songMode, ref info, out _song);
            if (result != FMOD.RESULT.OK) return "곡 createSound: " + result;

            byte[] clickData = SpikeWav.BuildClick(_system.MixerRate);
            var clickInfo = new FMOD.CREATESOUNDEXINFO();
            clickInfo.cbsize = Marshal.SizeOf(clickInfo);
            clickInfo.length = (uint)clickData.Length;
            FMOD.MODE clickMode = FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF;
            result = _system.CoreSystem.createSound(clickData, clickMode, ref clickInfo, out _click);
            if (result != FMOD.RESULT.OK) return "클릭 createSound: " + result;

            _log = SpikeReport.OpenCsv("sp4_events", "event,qpc_ticks,song_time,dsp_clock");
            return null;
        }

        // 곡 채널을 songTime 위치로 준비하고, 준비되면 미래 DSP 시각에 예약해 재생을 시작한다.
        private IEnumerator PrepareAndCommit(double songTime, Action<string> progress)
        {
            var watch = Stopwatch.StartNew();
            FMOD.RESULT result = _system.CoreSystem.playSound(_song, _songGroup, true, out _songChannel);
            if (result != FMOD.RESULT.OK)
            {
                _seekFailures++;
                LogEvent("playSound_fail=" + result, songTime, SongGroupClock());
            }
            _songChannel.setPriority(0);

            // NONBLOCKING 스트림은 playSound 직후 처음으로 되감는 비동기 seek를 한다.
            // 그동안 setPosition은 ERR_NOTREADY로 거부되므로, 준비된 뒤 seek하고 다시 준비를 기다린다.
            yield return WaitSongReady(watch, progress);

            double audioPosition = songTime - LeadInSeconds;
            if (audioPosition > 0)
            {
                uint target = (uint)Math.Round(audioPosition * _songRate);
                result = _songChannel.setPosition(target, FMOD.TIMEUNIT.PCM);
                while (result == FMOD.RESULT.ERR_NOTREADY && watch.Elapsed.TotalSeconds <= SeekTimeoutSeconds)
                {
                    _notReadyRetries++;
                    _system.CoreSystem.update();
                    yield return null;
                    result = _songChannel.setPosition(target, FMOD.TIMEUNIT.PCM);
                }
                yield return WaitSongReady(watch, progress);

                // seek가 실제로 반영됐는지 재생 전 채널 위치로 확인한다.
                _songChannel.getPosition(out uint reported, FMOD.TIMEUNIT.PCM);
                double seekError = ((double)reported - target) / _songRate;
                if (result != FMOD.RESULT.OK || Math.Abs(seekError) > _system.BlockSeconds)
                {
                    _seekFailures++;
                }
                if (Math.Abs(seekError) > _worstSeekError) _worstSeekError = Math.Abs(seekError);
                LogEvent("seek result=" + result + " target=" + target + " reported=" + reported, songTime, SongGroupClock());
            }
            _seekSeconds.Add(watch.Elapsed.TotalSeconds);

            Commit(songTime);
        }

        private IEnumerator WaitSongReady(Stopwatch watch, Action<string> progress)
        {
            while (!IsSongReady())
            {
                if (watch.Elapsed.TotalSeconds > SeekTimeoutSeconds)
                {
                    progress("SP4 seek 준비 시간 초과");
                    yield break;
                }
                _system.CoreSystem.update();
                yield return null;
            }
        }

        private void Commit(double songTime)
        {
            ulong now = SongGroupClock();
            ulong lead = 3 * (ulong)_system.BufferLength;
            ulong minimumLead = (ulong)Math.Round(0.015 * _system.MixerRate);
            if (lead < minimumLead) lead = minimumLead;

            ulong segmentStart = now + lead;
            ulong soundStart = segmentStart;
            double audioPosition = songTime - LeadInSeconds;
            if (audioPosition < 0)
            {
                // 리드인이 남았으면 남은 만큼 뒤에 소리가 시작된다.
                soundStart = segmentStart + (ulong)Math.Round(-audioPosition * _system.MixerRate);
            }

            _songChannel.setDelay(soundStart, 0, false);
            _songChannel.setPaused(false);

            _segmentStartTime = songTime;
            _segmentStartDsp = segmentStart;
            _running = true;
            _positionChecked = false;
            _segmentSeeked = audioPosition > 0;
            _nextClickIndex = (int)Math.Ceiling((songTime - LeadInSeconds) / ClickInterval);
            if (_nextClickIndex < 0) _nextClickIndex = 0;
            LogEvent("commit", songTime, soundStart);
        }

        private double Pause()
        {
            double pausedAt = SongTimeNow();
            _running = false;
            _songChannel.stop();
            for (int i = 0; i < _pendingClicks.Count; i++) _pendingClicks[i].stop();
            _pendingClicks.Clear();
            _pauseCount++;
            LogEvent("pause", pausedAt, SongGroupClock());
            return pausedAt;
        }

        private IEnumerator RunFor(double seconds)
        {
            double elapsed = 0;
            while (elapsed < seconds)
            {
                ScheduleClicks();
                CheckPosition();
                _system.CoreSystem.update();
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator Wait(double seconds)
        {
            double elapsed = 0;
            while (elapsed < seconds)
            {
                _system.CoreSystem.update();
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // 앞으로 0.2초 안에 올 메트로놈 클릭을 곡 시계 기준 DSP 시각에 예약한다.
        private void ScheduleClicks()
        {
            if (!_running) return;
            ulong now = SongGroupClock();
            ulong horizon = now + (ulong)Math.Round(ScheduleHorizonSeconds * _system.MixerRate);
            double lastClickTime = LeadInSeconds + TrackSeconds - 1.0;

            while (true)
            {
                double clickTime = LeadInSeconds + _nextClickIndex * ClickInterval;
                if (clickTime > lastClickTime) break;
                ulong clickDsp = DspForSongTime(clickTime);
                if (clickDsp > horizon) break;

                if (clickDsp <= now + _system.BufferLength)
                {
                    // 이미 믹스 커서가 지나간 시각이면 늦은 예약으로 세고 건너뛴다.
                    _lateClicks++;
                }
                else
                {
                    _system.CoreSystem.playSound(_click, _songGroup, true, out FMOD.Channel channel);
                    channel.setPan(1.0f);
                    channel.setDelay(clickDsp, 0, false);
                    channel.setPaused(false);
                    _pendingClicks.Add(channel);
                }
                _nextClickIndex++;
            }

            if (_pendingClicks.Count > 32) _pendingClicks.RemoveRange(0, _pendingClicks.Count - 32);
        }

        private double SongTimeNow()
        {
            ulong now = SongGroupClock();
            if (!_running || now <= _segmentStartDsp) return _segmentStartTime;
            return _segmentStartTime + (double)(now - _segmentStartDsp) / _system.MixerRate;
        }

        private ulong DspForSongTime(double songTime)
        {
            double offset = (songTime - _segmentStartTime) * _system.MixerRate;
            return _segmentStartDsp + (ulong)Math.Round(offset);
        }

        // 곡 그룹의 DSP 클록 = 곡 그룹 안 채널들의 부모 클록(setDelay 기준).
        private ulong SongGroupClock()
        {
            _songGroup.getDSPClock(out ulong clock, out _);
            return clock;
        }

        private bool IsSongOpen()
        {
            _system.CoreSystem.update();
            _song.getOpenState(out FMOD.OPENSTATE state, out _, out _, out _);
            return state == FMOD.OPENSTATE.READY;
        }

        // 스트림이 채널에 물리면 상태가 READY가 아니라 PLAYING이 된다. 둘 다 준비 완료로 본다.
        private bool IsSongReady()
        {
            _song.getOpenState(out FMOD.OPENSTATE state, out _, out bool starving, out _);
            bool opened = state == FMOD.OPENSTATE.READY || state == FMOD.OPENSTATE.PLAYING;
            return opened && !starving;
        }

        // 음원이 재생되기 시작하고 0.5초가 지나면 한 번, 곡 시계가 기대하는 음원 위치와
        // FMOD가 보고하는 채널 재생 위치를 비교한다. 녹음 없이 seek 어긋남을 잴 수 있다.
        private void CheckPosition()
        {
            if (!_running || _positionChecked) return;

            double songTime = SongTimeNow();
            double checkAfter = _segmentStartTime;
            if (checkAfter < LeadInSeconds) checkAfter = LeadInSeconds;
            if (songTime < checkAfter + 0.5) return;

            _positionChecked = true;
            FMOD.RESULT result = _songChannel.getPosition(out uint frames, FMOD.TIMEUNIT.PCM);
            if (result != FMOD.RESULT.OK) return;

            double expected = songTime - LeadInSeconds;
            double actual = (double)frames / _songRate;
            double error = actual - expected;
            _positionErrors.Add(error);
            _positionSeeked.Add(_segmentSeeked);
            LogEvent("position_error_ms=" + SpikeReport.Num(error * 1000.0, "0.000"), songTime, SongGroupClock());
        }

        private void LogEvent(string name, double songTime, ulong dsp)
        {
            if (_log == null) return;
            _log.WriteLine(name + "," + Stopwatch.GetTimestamp() + "," + SpikeReport.Num(songTime, "0.000000") + "," + dsp);
        }

        private void WriteSummary()
        {
            double maxSeek = 0;
            double sumSeek = 0;
            for (int i = 0; i < _seekSeconds.Count; i++)
            {
                sumSeek += _seekSeconds[i];
                if (_seekSeconds[i] > maxSeek) maxSeek = _seekSeconds[i];
            }
            double averageSeek = 0;
            if (_seekSeconds.Count > 0) averageSeek = sumSeek / _seekSeconds.Count;

            string seekedErrors = DescribeErrors(true, out double seekedWorst);
            string plainErrors = DescribeErrors(false, out double plainWorst);

            // 채널 위치는 믹스 블록 단위로 갱신되므로 2블록 + 1ms까지는 오차로 보지 않는다.
            double tolerance = 2 * _system.BlockSeconds + 0.001;
            bool positionOk = seekedWorst <= tolerance && plainWorst <= tolerance;
            bool pass = maxSeek < 1.0 && _lateClicks == 0 && _seekFailures == 0 && positionOk;
            string measured = string.Format("일시정지 {0}회, 늦은 예약 {1}회, seek 실패 {2}회(NOTREADY 재시도 {3}회, 반영 오차 최대 {4}), seek 준비 평균 {5} 최대 {6}, 위치 오차(seek 있음) {7}, 위치 오차(seek 없음) {8}, 허용 {9}, 곡 레이트 {10}Hz",
                _pauseCount, _lateClicks, _seekFailures, _notReadyRetries, SpikeReport.Ms(_worstSeekError),
                SpikeReport.Ms(averageSeek), SpikeReport.Ms(maxSeek),
                seekedErrors, plainErrors, SpikeReport.Ms(tolerance), _songRate);
            SpikeReport.Summary("SP4", SpikeReport.PassIf(pass), "seek 준비 < 1초, 늦은 예약 0, seek 실패 0, 위치 오차 ≤ 2블록+1ms, (루프백) 시작 차 평균 ≤ 1ms", measured, _system.Describe());
        }

        // seek가 있었던(또는 없었던) 구간의 위치 오차를 "n회 평균 x 최대 y" 형식으로 만든다. worst는 절댓값 최대.
        private string DescribeErrors(bool seeked, out double worst)
        {
            worst = 0;
            double sum = 0;
            int count = 0;
            for (int i = 0; i < _positionErrors.Count; i++)
            {
                if (_positionSeeked[i] != seeked) continue;
                double error = _positionErrors[i];
                sum += error;
                count++;
                if (Math.Abs(error) > worst) worst = Math.Abs(error);
            }
            if (count == 0) return "측정 없음";
            return count + "회 평균 " + SpikeReport.Ms(sum / count) + " 최대 " + SpikeReport.Ms(worst);
        }

        private void Cleanup()
        {
            _running = false;
            if (_songChannel.hasHandle()) _songChannel.stop();
            for (int i = 0; i < _pendingClicks.Count; i++) _pendingClicks[i].stop();
            _pendingClicks.Clear();
            if (_song.hasHandle()) _song.release();
            if (_click.hasHandle()) _click.release();
            if (_songGroup.hasHandle()) _songGroup.release();
            if (_log != null)
            {
                _log.Dispose();
                _log = null;
            }
        }
    }
}
#endif
