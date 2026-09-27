using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio.Engine;

namespace SCOdyssey.Audio.Output
{
    // 출력 설정 적용(로비·설정 화면 전용). 항상 close → init이다.
    //   1. 진행 중이면 Busy. 2. 한 프레임 양보한 뒤 전제를 검사한다(지원 타입, 곡 진행 중 아님, 바뀐 것이 있는지).
    //   3. 요청 구성 → 직전 구성 → WASAPI 기본 512×4 → NOSOUND 순서로 재구성한다.
    //   4. 요청 구성으로 성공했을 때만 Applied다. 호출자는 Applied일 때만 설정을 저장한다
    //      (적용 중 크래시가 나도 다음 부팅이 같은 구성으로 크래시하지 않게 하기 위해서다).
    internal sealed class AudioOutputService : IAudioOutputService
    {
        private readonly AudioEngine _engine;
        private readonly DeviceCatalog _catalog;
        private readonly Func<string> _blockingReason;
        private readonly Func<IReadOnlyList<BootAttempt>, string, int> _reconfigure;
        private readonly CancellationToken _moduleToken;
        private bool _applying;

        public AudioOutputService(AudioEngine engine, DeviceCatalog catalog, Func<string> blockingReason,
            Func<IReadOnlyList<BootAttempt>, string, int> reconfigure, CancellationToken moduleToken)
        {
            _engine = engine;
            _catalog = catalog;
            _blockingReason = blockingReason;
            _reconfigure = reconfigure;
            _moduleToken = moduleToken;
        }

        public bool IsApplying
        {
            get { return _applying; }
        }

        public bool IsSupported(AudioOutputKind kind)
        {
            if (kind == AudioOutputKind.Wasapi) return true;
            if (kind == AudioOutputKind.Asio) return AsioPolicy.IsAllowed;
            return false;
        }

        public IReadOnlyList<AudioDeviceInfo> GetCachedDevices(AudioOutputKind kind)
        {
            return _catalog.GetCached(kind);
        }

        public async UniTask<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(AudioOutputKind kind, bool refresh, CancellationToken ct)
        {
            IReadOnlyList<AudioDeviceInfo> cached = _catalog.GetCached(kind);
            if (!refresh && cached.Count > 0) return cached;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (_moduleToken.IsCancellationRequested) return cached;
            return _catalog.Enumerate(kind);
        }

        public async UniTask<AudioApplyResult> ApplyAsync(AudioOutputRequest request, CancellationToken ct)
        {
            AudioThread.AssertMain("IAudioOutputService.ApplyAsync");
            if (_applying) return Result(AudioApplyOutcome.Busy, "다른 적용이 진행 중");
            _applying = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (_moduleToken.IsCancellationRequested) return Result(AudioApplyOutcome.Failed, "모듈 종료");
                if (!IsSupported(request.Kind)) return Result(AudioApplyOutcome.Rejected, "지원하지 않는 출력 타입: " + request.Kind);
                string blocked = _blockingReason();
                if (blocked != null) return Result(AudioApplyOutcome.Rejected, blocked);

                BootAttempt requested = BootPlan.FromRequest(request, "요청 구성");
                _engine.SetRequested(request);
                bool running = _engine.Status == EngineStatus.Running || _engine.Status == EngineStatus.Degraded;
                if (running && _engine.CurrentAttempt.SameConfig(requested)) return Result(AudioApplyOutcome.Unchanged, "바뀐 것 없음");

                List<BootAttempt> attempts = BootPlan.BuildApply(request, _engine.CurrentAttempt, _engine.Generation > 0);
                int index = _reconfigure(attempts, "설정 적용");
                if (index == 0) return Result(AudioApplyOutcome.Applied, _engine.BootSummary);
                if (index > 0) return Result(AudioApplyOutcome.AppliedWithFallback, _engine.BootSummary);
                return Result(AudioApplyOutcome.Failed, _engine.BootSummary);
            }
            catch (OperationCanceledException)
            {
                return Result(AudioApplyOutcome.Failed, "취소됨");
            }
            finally
            {
                _applying = false;
            }
        }

        private AudioApplyResult Result(AudioApplyOutcome outcome, string message)
        {
            return new AudioApplyResult(outcome, _engine.CurrentOutput, message);
        }
    }
}
