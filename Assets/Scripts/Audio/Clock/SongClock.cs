using System;
using UnityEngine;

namespace SCOdyssey.Audio.Clock
{
    // ISongClock 구현. 세션이 커밋·일시정지 때 세그먼트를 넣고, 매 프레임 모델에 샘플을 넣은 뒤 UpdateFrame을 부른다.
    // DSP 값은 모델과 세그먼트(Cf)가 같은 도메인(곡 그룹의 부모 클록)이어야 한다.
    internal sealed class SongClock : ISongClock
    {
        private readonly DspQpcModel _model;
        private readonly SongTimeline _timeline = new SongTimeline();
        private SongFrame _frame;

        public SongClock(DspQpcModel model)
        {
            _model = model;
        }

        public event Action<ClockDiscontinuity> Discontinuity;

        public SongFrame Frame
        {
            get { return _frame; }
        }

        public int Epoch
        {
            get { return _timeline.Epoch; }
        }

        // 매 프레임 한 번. 세그먼트 안에서는 곡 시각이 뒤로 가지 않는다(포락선 창이 밀릴 때의 작은 역행을 막는다).
        public void UpdateFrame(long qpcTicks)
        {
            SongTimePoint point;
            if (!TrySongTimeAt(qpcTicks, out point))
            {
                _frame = new SongFrame(0, qpcTicks, _timeline.Epoch, false);
                return;
            }

            double songTime = point.SongTime;
            if (point.Epoch == _frame.Epoch && songTime < _frame.SongTime) songTime = _frame.SongTime;
            _frame = new SongFrame(songTime, qpcTicks, point.Epoch, point.IsRunning);
        }

        public bool TrySongTimeAt(long qpcTicks, out SongTimePoint point)
        {
            ClockSegment segment;
            if (!_timeline.TryFind(qpcTicks, out segment))
            {
                point = default;
                return false;
            }

            double songTime = segment.SongTimeAtStart;
            if (segment.IsRunning)
            {
                // 모델이 리셋되기 전에 만든 흐르는 구간은 DSP 도메인이 달라 계산할 수 없다.
                if (segment.ModelVersion != _model.Version)
                {
                    point = default;
                    return false;
                }
                double dsp;
                if (_model.TryDspAt(qpcTicks, out dsp)) songTime = segment.SongTimeAt(dsp);
            }

            point = new SongTimePoint(songTime, segment.Epoch, segment.IsRunning);
            return true;
        }

        // 시작·재개 커밋: DSP startDsp(Cf)부터 곡 시각 songTimeAtStart로 흐른다.
        public void CommitRunning(double songTimeAtStart, double startDsp, long qpcTicks, DiscontinuityReason reason)
        {
            int previous = _timeline.Epoch;
            _timeline.Run(songTimeAtStart, startDsp, _model.SampleRate, _model.Version, qpcTicks);
            Raise(reason, previous, songTimeAtStart);
        }

        public void Freeze(double songTime, long qpcTicks, DiscontinuityReason reason)
        {
            int previous = _timeline.Epoch;
            _timeline.Freeze(songTime, qpcTicks);
            Raise(reason, previous, songTime);
        }

        // 세대 변경: 흐르던 구간은 지금 곡 시각에서 멈춘 뒤 모델을 새 레이트·버퍼로 리셋한다.
        public void ResetModel(int sampleRate, int blockLength, int blockCount, long qpcTicks)
        {
            if (_timeline.HasSegment && _timeline.Current.IsRunning)
            {
                double songTime = _frame.SongTime;
                SongTimePoint point;
                if (TrySongTimeAt(qpcTicks, out point)) songTime = point.SongTime;
                Freeze(songTime, qpcTicks, DiscontinuityReason.Reset);
            }
            _model.Reset(sampleRate, blockLength, blockCount);
        }

        private void Raise(DiscontinuityReason reason, int previousEpoch, double songTime)
        {
            Action<ClockDiscontinuity> handler = Discontinuity;
            if (handler == null) return;
            try
            {
                handler(new ClockDiscontinuity(reason, previousEpoch, _timeline.Epoch, songTime));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
