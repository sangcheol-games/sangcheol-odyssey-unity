using System;
using SCOdyssey.Audio;
using UnityEngine;

namespace SCOdyssey.Game.Timing
{
    public delegate void JudgedInputHandler(in JudgedInput input);

    // 게임플레이(GameManager) 쪽 얇은 어댑터. 델리게이트만 받고 판정 규칙은 모른다.
    //   - 판정할 수 없는 입력은 release만 넘긴다(눌림 상태 해제용). 판정할 수 있는 입력과 Advance는 isRunning일 때만 넘긴다.
    //     일시정지는 isRunning에 넣지 않는다. 일시정지가 곡 시계를 멈추므로 그 뒤 입력은 판정할 수 없게 되고 진행도 멈춘다.
    //   - 세션이 사용자 사유가 아닌 일시정지(포커스, 장치, 스트림)에 들어가면 onExternalPause를 한 번 부른다(일시정지 UI 표시용).
    //   - 곡 시작(Started)과 재개(Resumed) 때 포커스가 없으면 곧바로 포커스 사유로 일시정지한다.
    public sealed class GameplayTimingBinding : IJudgementClient, IDisposable
    {
        private readonly ISongSession _session;
        private readonly Action<double, double> _onAdvance;
        private readonly JudgedInputHandler _onLaneInput;
        private readonly Func<bool> _isRunning;
        private readonly Action<PauseReason> _onExternalPause;
        private readonly Func<bool> _isFocused;
        private readonly Action<SongSessionEvent> _onSessionChanged;
        private JudgementDriver _driver;
        private bool _externalPauseNotified;
        private bool _isDisposed;

        internal GameplayTimingBinding(ISongSession session, Action<double, double> onAdvance, JudgedInputHandler onLaneInput,
            Func<bool> isRunning, Action<PauseReason> onExternalPause, Func<bool> isFocused)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            _session = session;
            _onAdvance = onAdvance;
            _onLaneInput = onLaneInput;
            _isRunning = isRunning;
            _onExternalPause = onExternalPause;
            _isFocused = isFocused;
            _onSessionChanged = OnSessionChanged;
            _session.Changed += _onSessionChanged;
        }

        public ISongSession Session
        {
            get { return _session; }
        }

        // onAdvance(songTime, judgeTime): 마디 진행은 songTime, miss·홀드는 judgeTime. isRunning은 "게임 진행 중"(일시정지 여부 제외).
        public static GameplayTimingBinding Attach(JudgementDriver driver, ISongSession session, Action<double, double> onAdvance,
            JudgedInputHandler onLaneInput, Func<bool> isRunning, Action<PauseReason> onExternalPause)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            var binding = new GameplayTimingBinding(session, onAdvance, onLaneInput, isRunning, onExternalPause, ReadApplicationFocus);
            binding._driver = driver;
            driver.Attach(binding, session);
            return binding;
        }

        void IJudgementClient.OnLaneInput(in JudgedInput input)
        {
            if (_isDisposed || _onLaneInput == null) return;
            if (!input.Judgeable)
            {
                if (!input.IsDown) _onLaneInput(in input);
                return;
            }
            if (IsRunning()) _onLaneInput(in input);
        }

        void IJudgementClient.Advance(double songTime, double judgeTime)
        {
            if (_isDisposed || _onAdvance == null) return;
            if (IsRunning()) _onAdvance(songTime, judgeTime);
        }

        void IJudgementClient.OnFrame(ISongSession session)
        {
            if (_isDisposed) return;
            SongSessionState state = session.State;
            bool paused = state == SongSessionState.Paused || state == SongSessionState.Recovering;
            if (!paused)
            {
                _externalPauseNotified = false;
                return;
            }
            if (_externalPauseNotified || session.PauseReason == PauseReason.User) return;
            _externalPauseNotified = true;
            if (_onExternalPause != null) _onExternalPause(session.PauseReason);
        }

        // 멱등.
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _session.Changed -= _onSessionChanged;
            if (_driver != null) _driver.Detach(this);
            _driver = null;
        }

        private void OnSessionChanged(SongSessionEvent sessionEvent)
        {
            if (_isDisposed) return;
            bool committing = sessionEvent == SongSessionEvent.Started || sessionEvent == SongSessionEvent.Resumed;
            if (committing && !_isFocused()) _session.Pause(PauseReason.FocusLost);
        }

        private bool IsRunning()
        {
            if (_isRunning == null) return true;
            return _isRunning();
        }

        private static bool ReadApplicationFocus()
        {
            return Application.isFocused;
        }
    }
}
