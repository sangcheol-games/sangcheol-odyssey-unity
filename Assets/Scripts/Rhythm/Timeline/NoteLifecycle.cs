namespace SCOdyssey.Rhythm
{
    public enum NotePhase : byte
    {
        NotSpawned,
        Ghost,      // 한 마디 앞서 미리 보이는 중(판정은 이미 될 수 있다)
        Active,     // 판정선이 지나가는 마디
    }

    // 노트가 화면에 언제 나타나고, 언제 Active가 되고, 언제 빠지는지 정한다.
    // 시각은 마디 시계로, 빠질지는 판정 엔진 상태로 정한다. 좌표와 Hidden(판정선 겹침)은 뷰가 따로 본다.
    public sealed class NoteLifecycle
    {
        private readonly BarClock _bars;
        private readonly IJudgeStateReader _judge;

        public NoteLifecycle(BarClock bars, IJudgeStateReader judge)
        {
            _bars = bars;
            _judge = judge;
        }

        // 마디 bar의 노트는 한 마디 앞(bar - 1 시작)에 띄우고, bar가 시작될 때 Active로 올린다
        public double SpawnAt(int bar) => _bars.BarStart(bar - 1);
        public double ActiveAt(int bar) => _bars.BarStart(bar);

        public NotePhase PhaseAt(int bar, double time)
        {
            if (time < SpawnAt(bar)) return NotePhase.NotSpawned;
            return time < ActiveAt(bar) ? NotePhase.Ghost : NotePhase.Active;
        }

        public bool IsDecided(int noteId)
        {
            if (noteId < 0) return false;
            NoteStatus status = _judge.StatusOf(noteId);
            return status == NoteStatus.Judged || status == NoteStatus.Missed;
        }

        // 마디 시작 때 Active로 올릴지. 판정이 끝난 탭·꼬리는 이미 화면에서 빠졌다.
        // 홀드 머리는 판정이 끝나도 홀드바가 판정선을 따라 줄어들어야 해서 올린다
        public bool ShouldActivate(int noteId)
        {
            return !IsDecided(noteId) || _judge.NoteAt(noteId).Kind == NoteKind.HoldHead;
        }
    }
}
