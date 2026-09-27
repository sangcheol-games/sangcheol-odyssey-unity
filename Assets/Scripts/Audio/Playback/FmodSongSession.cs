using System;
using System.Collections.Generic;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Audio.Playback
{
    // 게임 곡 한 곡의 세션. 곡 시계와 음원 채널을 함께 다룬다(Audio_architecture.md 7장).
    //
    //   Ready ─Start→ Starting ─커밋→ LeadIn ─곡 시각 ≥ Z→ Playing (음원이 끝나도 Playing, IsAudioFinished)
    //   Starting·LeadIn·Playing·Resuming ─Pause→ Paused ─Resume→ Resuming ─커밋→ LeadIn 또는 Playing
    //   Paused ─재구성→ Recovering ─다시 열기·seek 완료→ Paused(Recovered)
    //   모든 상태 ─Stop→ Stopped, ─Dispose→ Disposed
    //
    // 시작과 재개는 같은 앵커 커밋을 쓴다: 채널을 paused로 만들고 τ0 위치로 seek한 뒤, 준비되면 미래 DSP 시각에 예약한다.
    // 매번 샘플 단위로 다시 예약하므로 일시정지를 반복해도 오차가 쌓이지 않는다.
    // 음원이 없거나 끝났거나 엔진을 쓸 수 없으면 채널 없이 세그먼트만 기록한다(무음 커밋).
    internal sealed class FmodSongSession : ISongSession
    {
        public const double StartTimeoutSeconds = 3.0;
        private const double StallMarginSeconds = 0.5;
        private const double EndSafetySeconds = 0.25;
        private const double SameStallToleranceSeconds = 0.1;
        private const int SongPriority = 0;
        private const FMOD.MODE StreamMode = FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING | FMOD.MODE.ACCURATETIME | FMOD.MODE.IGNORETAGS | FMOD.MODE.LOOP_OFF;

        private enum OpenPhase
        {
            Closed,
            Opening,
            Open,
            Failed,
            Silent
        }

        private enum PreparePhase
        {
            None,
            WaitRewind,
            WaitSeek,
            Ready
        }

        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly ClockSampler _sampler;
        private readonly string _path;
        private readonly SongClock _clock;
        private readonly List<SongSessionEvent> _pendingEvents = new List<SongSessionEvent>(4);
        private bool _flushing;

        private OpenPhase _open = OpenPhase.Closed;
        private string _openError = "";
        private FMOD.Sound _sound;
        private int _soundRate;
        private double _audioLength;

        private FMOD.Channel _channel;
        private PreparePhase _prepare = PreparePhase.None;
        private long _seekFrames;

        private SongSessionState _state = SongSessionState.Ready;
        private PauseReason _pauseReason = PauseReason.None;
        private bool _isAudioFinished;
        private bool _audioStartedRaised;
        private double _audioZero;
        private double _pendingTau0;
        private double _pausedAt;
        private bool _resumeRequested;
        private long _startingSinceQpc;
        private double _lastStallSongTime = -1;

        // 마지막 커밋. 진단 메트로놈이 곡 시각을 DSP로 바꿀 때 쓴다.
        private double _committedTau0;
        private ulong _committedClockStart;
        private int _committedRate;

        public FmodSongSession(AudioEngine engine, FmodMixer mixer, ClockSampler sampler, string path, int ownerScene)
        {
            _engine = engine;
            _mixer = mixer;
            _sampler = sampler;
            _path = path;
            OwnerScene = ownerScene;
            _clock = new SongClock(sampler.Model);
            BeginOpen();
        }

        public event Action<SongSessionEvent> Changed;

        public int OwnerScene { get; }

        public SongSessionState State
        {
            get { return _state; }
        }

        public PauseReason PauseReason
        {
            get { return _pauseReason; }
        }

        public bool IsAudioFinished
        {
            get { return _isAudioFinished; }
        }

        public ISongClock Clock
        {
            get { return _clock; }
        }

        internal SongClock ClockInternal
        {
            get { return _clock; }
        }

        internal bool IsOpening
        {
            get { return _open == OpenPhase.Opening || _open == OpenPhase.Closed; }
        }

        internal bool OpenFailed
        {
            get { return _open == OpenPhase.Failed; }
        }

        internal string OpenError
        {
            get { return _openError; }
        }

        internal double AudioZero
        {
            get { return _audioZero; }
        }

        internal double AudioLengthSeconds
        {
            get { return _audioLength; }
        }

        public void Start(double leadInSeconds, int audioOffsetMs)
        {
            AudioThread.AssertMain("ISongSession.Start");
            if (_state != SongSessionState.Ready)
            {
                Debug.LogWarning("[Audio] Start는 Ready에서만 받습니다: " + _state);
                return;
            }

            _audioZero = SongAnchor.AudioZero(leadInSeconds, audioOffsetMs);
            _state = SongSessionState.Starting;
            _startingSinceQpc = Qpc.Now;
            if (_open == OpenPhase.Silent || _open == OpenPhase.Failed) _isAudioFinished = true;
            Emit(SongSessionEvent.Started);

            BeginPrepare(0);
            TryCommitStart();
            Flush();
        }

        public void Pause(PauseReason reason)
        {
            AudioThread.AssertMain("ISongSession.Pause");
            bool pausable = _state == SongSessionState.Starting || _state == SongSessionState.LeadIn
                || _state == SongSessionState.Playing || _state == SongSessionState.Resuming;
            if (!pausable) return;

            double pausedAt = _pendingTau0;
            if (_state == SongSessionState.LeadIn || _state == SongSessionState.Playing) pausedAt = SongTimeNow();

            ReleaseChannel();
            _clock.Freeze(pausedAt, Qpc.Now, DiscontinuityReason.Pause);
            _pausedAt = pausedAt;
            _pauseReason = reason;
            _resumeRequested = false;
            _state = SongSessionState.Paused;
            Emit(SongSessionEvent.Paused);

            // 음원이 남아 있으면 새 채널을 paused로 만들어 T_p로 미리 seek해 둔다.
            BeginPrepare(pausedAt);
            Flush();
        }

        public void Resume()
        {
            AudioThread.AssertMain("ISongSession.Resume");
            if (_state == SongSessionState.Recovering)
            {
                _resumeRequested = true;
                return;
            }
            if (_state != SongSessionState.Paused) return;

            _state = SongSessionState.Resuming;
            if (_prepare == PreparePhase.None) BeginPrepare(_pausedAt);
            TryCommitResume();
            Flush();
        }

        public void Stop()
        {
            AudioThread.AssertMain("ISongSession.Stop");
            if (_state == SongSessionState.Stopped || _state == SongSessionState.Disposed) return;

            double songTime = _clock.Frame.SongTime;
            ReleaseChannel();
            _prepare = PreparePhase.None;
            _clock.Freeze(songTime, Qpc.Now, DiscontinuityReason.Stop);
            _state = SongSessionState.Stopped;
            Emit(SongSessionEvent.Stopped);
            Flush();
        }

        public void Dispose()
        {
            if (_state == SongSessionState.Disposed) return;
            ReleaseChannel();
            ReleaseSound();
            _open = OpenPhase.Closed;
            _prepare = PreparePhase.None;
            _state = SongSessionState.Disposed;
            Emit(SongSessionEvent.Disposed);
            Flush();
        }

        // 매 프레임 한 번(모듈 Tick). 프레임 스냅샷을 만든 뒤 열기, 준비, 커밋, 상태 전이, 음원 종료를 처리한다.
        internal void Tick()
        {
            if (_state == SongSessionState.Disposed) return;
            _clock.UpdateFrame(Qpc.Now);
            PollOpen();

            if (_state == SongSessionState.Starting) TryCommitStart();
            else if (_state == SongSessionState.Resuming) TryCommitResume();
            else if (_state == SongSessionState.Recovering) TryFinishRecovery();
            else if (_state == SongSessionState.Paused) StepPrepare();
            else if (_state == SongSessionState.LeadIn) CheckAudioStarted();
            else if (_state == SongSessionState.Playing) DetectAudioEnd();

            Flush();
        }

        // 재구성 직전. 흐르는 세션은 먼저 일시정지하고, 채널과 Sound를 해제한다.
        internal void OnEngineClosing()
        {
            if (_state == SongSessionState.Disposed) return;
            if (_state == SongSessionState.LeadIn || _state == SongSessionState.Playing || _state == SongSessionState.Resuming)
            {
                Pause(PauseReason.DeviceChanged);
            }
            if (_state == SongSessionState.Paused) _state = SongSessionState.Recovering;

            ReleaseChannel();
            _prepare = PreparePhase.None;
            ReleaseSound();
            if (_open != OpenPhase.Failed) _open = OpenPhase.Closed;
            Flush();
        }

        // 재구성 직후. 새 세대에서 다시 열고, 열리면 상태에 맞게 seek·커밋을 이어 간다.
        internal void OnEngineOpened()
        {
            if (_state == SongSessionState.Disposed || _state == SongSessionState.Stopped) return;
            if (_open == OpenPhase.Failed) return;
            BeginOpen();
            if (_open == OpenPhase.Silent && _state != SongSessionState.Ready) _isAudioFinished = true;
            Flush();
        }

        // 진단용: 지금 흐르는 세그먼트에서 곡 시각의 DSP 시각. 멈춰 있거나 Cf 전이면 false.
        internal bool TryDspForSongTime(double songTime, out ulong dsp)
        {
            dsp = 0;
            if (_state != SongSessionState.LeadIn && _state != SongSessionState.Playing) return false;
            if (songTime < _committedTau0) return false;
            dsp = _committedClockStart + (ulong)Math.Round((songTime - _committedTau0) * _committedRate);
            return true;
        }

        private void BeginOpen()
        {
            ReleaseSound();
            if (!_engine.IsUsable)
            {
                _open = OpenPhase.Silent;
                OnOpened();
                return;
            }

            FMOD.RESULT result = _engine.CoreSystem.createSound(_path, StreamMode, out _sound);
            if (result != FMOD.RESULT.OK)
            {
                _sound = default;
                _open = OpenPhase.Failed;
                _openError = "createSound: " + result;
                return;
            }
            _open = OpenPhase.Opening;
            PollOpen();
        }

        private void PollOpen()
        {
            if (_open != OpenPhase.Opening) return;
            StreamLoader.Phase phase = StreamLoader.Poll(_sound);
            if (phase == StreamLoader.Phase.Opening) return;
            if (phase == StreamLoader.Phase.Failed)
            {
                ReleaseSound();
                _open = OpenPhase.Failed;
                _openError = "열기 실패(형식 오류 또는 읽기 실패)";
                return;
            }

            _sound.getLength(out uint lengthFrames, FMOD.TIMEUNIT.PCM);
            _sound.getDefaults(out float frequency, out _);
            _soundRate = (int)frequency;
            if (_soundRate <= 0) _soundRate = ClockSampler.VirtualSampleRate;
            _audioLength = (double)lengthFrames / _soundRate;
            _sound.setDefaults(frequency, SongPriority);
            _open = OpenPhase.Open;
            OnOpened();
        }

        private void OnOpened()
        {
            if (_state == SongSessionState.Starting) BeginPrepare(_pendingTau0);
            else if (_state == SongSessionState.Recovering || _state == SongSessionState.Resuming) BeginPrepare(_pausedAt);
        }

        private bool HasAudioLeft(double songTime)
        {
            if (_open != OpenPhase.Open || _isAudioFinished) return false;
            return SongAnchor.AudioPosition(songTime, _audioZero) < _audioLength;
        }

        private void BeginPrepare(double tau0)
        {
            ReleaseChannel();
            _pendingTau0 = tau0;
            _prepare = PreparePhase.None;
            if (_open == OpenPhase.Opening || _open == OpenPhase.Closed) return;   // 열린 뒤 OnOpened에서 다시 부른다

            if (!HasAudioLeft(tau0))
            {
                _prepare = PreparePhase.Ready;
                return;
            }

            FMOD.RESULT result = _engine.CoreSystem.playSound(_sound, _mixer.SongGroup, true, out _channel);
            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning("[Audio] 곡 채널을 만들지 못해 무음으로 진행합니다: " + result);
                _channel = default;
                _prepare = PreparePhase.Ready;
                return;
            }
            _seekFrames = SongAnchor.SeekFrames(SongAnchor.AudioPosition(tau0, _audioZero), _soundRate);
            _prepare = PreparePhase.WaitRewind;
            StepPrepare();
        }

        // NONBLOCKING 스트림은 playSound 뒤 처음으로 되감는 비동기 seek를 한다. 그동안 setPosition은 ERR_NOTREADY다.
        // 되감기가 끝나면 seek하고, seek가 끝나 굶주리지 않으면 준비 완료다.
        private void StepPrepare()
        {
            if (_prepare == PreparePhase.WaitRewind)
            {
                if (!StreamLoader.IsPlayable(_sound)) return;
                if (_seekFrames <= 0)
                {
                    _prepare = PreparePhase.Ready;
                    return;
                }
                FMOD.RESULT result = _channel.setPosition((uint)_seekFrames, FMOD.TIMEUNIT.PCM);
                if (result == FMOD.RESULT.ERR_NOTREADY) return;
                if (result != FMOD.RESULT.OK)
                {
                    Debug.LogWarning("[Audio] 곡 seek 실패, 무음으로 진행합니다: " + result);
                    ReleaseChannel();
                    _prepare = PreparePhase.Ready;
                    return;
                }
                _prepare = PreparePhase.WaitSeek;
                return;
            }

            if (_prepare == PreparePhase.WaitSeek && StreamLoader.IsPlayable(_sound)) _prepare = PreparePhase.Ready;
        }

        private void TryCommitStart()
        {
            StepPrepare();
            if (_prepare == PreparePhase.Ready)
            {
                Commit(DiscontinuityReason.Start);
                return;
            }

            // 재구성이 겹쳐 Starting이 길어지면 무음 세션으로 바꾼다(채보는 그대로 진행).
            if (Qpc.ToSeconds(Qpc.Now - _startingSinceQpc) > StartTimeoutSeconds)
            {
                Debug.LogWarning("[Audio] 곡 준비가 " + StartTimeoutSeconds + "초를 넘겨 무음으로 진행합니다.");
                ReleaseChannel();
                _isAudioFinished = true;
                _prepare = PreparePhase.Ready;
                Commit(DiscontinuityReason.Start);
            }
        }

        private void TryCommitResume()
        {
            StepPrepare();
            if (_prepare != PreparePhase.Ready) return;
            Commit(DiscontinuityReason.Resume);
            _pauseReason = PauseReason.None;
            Emit(SongSessionEvent.Resumed);
        }

        private void TryFinishRecovery()
        {
            if (_open == OpenPhase.Opening || _open == OpenPhase.Closed) return;
            if (_prepare == PreparePhase.None) BeginPrepare(_pausedAt);
            StepPrepare();
            if (_prepare != PreparePhase.Ready) return;

            _state = SongSessionState.Paused;
            Emit(SongSessionEvent.Recovered);
            if (_resumeRequested)
            {
                _resumeRequested = false;
                Resume();
            }
        }

        // 앵커 커밋. DSP는 한 번만 읽는다.
        //   Cf = p + lead,  S = pos < 0 이면 Cf + round(−pos·R), 아니면 Cf
        private void Commit(DiscontinuityReason reason)
        {
            double tau0 = _pendingTau0;
            int rate = _sampler.SampleRate;
            ulong clockStart = _sampler.ReadDsp() + SongAnchor.CommitLead(_sampler.BlockLength, rate);

            if (_channel.hasHandle())
            {
                double position = SongAnchor.AudioPosition(tau0, _audioZero);
                ulong soundStart = SongAnchor.SoundStartDsp(clockStart, position, rate);
                _channel.setDelay(soundStart, 0, false);
                _channel.setPaused(false);
            }

            _clock.CommitRunning(tau0, clockStart, Qpc.Now, reason);
            _committedTau0 = tau0;
            _committedClockStart = clockStart;
            _committedRate = rate;
            _prepare = PreparePhase.None;

            if (tau0 < _audioZero)
            {
                _state = SongSessionState.LeadIn;
            }
            else
            {
                _state = SongSessionState.Playing;
                RaiseAudioStartedOnce();
            }
        }

        private void CheckAudioStarted()
        {
            if (_clock.Frame.SongTime < _audioZero) return;
            _state = SongSessionState.Playing;
            RaiseAudioStartedOnce();
        }

        private void RaiseAudioStartedOnce()
        {
            if (_audioStartedRaised) return;
            _audioStartedRaised = true;
            Emit(SongSessionEvent.AudioStarted);
        }

        // 채널이 멈췄고 끝 무렵이면 종료. 끝까지 0.5초 넘게 남았으면 스트림 끊김으로 보고 일시정지한다
        // (같은 위치에서 두 번 끊기면 종료). 곡 시각이 끝보다 0.25초 넘게 지나면 채널과 관계없이 종료한다.
        private void DetectAudioEnd()
        {
            if (_isAudioFinished) return;
            double songTime = _clock.Frame.SongTime;
            double end = _audioZero + _audioLength;
            if (songTime >= end + EndSafetySeconds)
            {
                FinishAudio();
                return;
            }
            if (!_channel.hasHandle()) return;

            _channel.isPlaying(out bool playing);
            if (playing) return;

            double endMargin = 2.0 * _sampler.BlockLength / _sampler.SampleRate;
            if (songTime >= end - endMargin)
            {
                FinishAudio();
                return;
            }
            if (songTime >= end - StallMarginSeconds) return;

            if (_lastStallSongTime >= 0 && Math.Abs(songTime - _lastStallSongTime) < SameStallToleranceSeconds)
            {
                Debug.LogWarning("[Audio] 같은 위치에서 스트림이 다시 끊겨 음원 종료로 처리합니다: " + songTime.ToString("F3"));
                FinishAudio();
                return;
            }
            _lastStallSongTime = songTime;
            Debug.LogWarning("[Audio] 곡 스트림이 끊겨 일시정지합니다: " + songTime.ToString("F3"));
            Pause(PauseReason.StreamStalled);
        }

        private void FinishAudio()
        {
            _isAudioFinished = true;
            ReleaseChannel();
            Emit(SongSessionEvent.AudioEnded);
        }

        private double SongTimeNow()
        {
            SongTimePoint point;
            if (_clock.TrySongTimeAt(Qpc.Now, out point)) return point.SongTime;
            return _clock.Frame.SongTime;
        }

        private void ReleaseChannel()
        {
            if (_channel.hasHandle()) _channel.stop();
            _channel = default;
        }

        private void ReleaseSound()
        {
            if (_sound.hasHandle()) _sound.release();
            _sound = default;
        }

        // 상태 전이 중에 생긴 이벤트는 모았다가 전이가 끝난 뒤 발행한다. 구독자 안에서 다시 호출해도 순서가 지켜진다.
        private void Emit(SongSessionEvent sessionEvent)
        {
            _pendingEvents.Add(sessionEvent);
        }

        private void Flush()
        {
            if (_flushing) return;
            _flushing = true;
            try
            {
                while (_pendingEvents.Count > 0)
                {
                    SongSessionEvent sessionEvent = _pendingEvents[0];
                    _pendingEvents.RemoveAt(0);
                    Action<SongSessionEvent> handler = Changed;
                    if (handler == null) continue;
                    try
                    {
                        handler(sessionEvent);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                _flushing = false;
            }
        }
    }
}
