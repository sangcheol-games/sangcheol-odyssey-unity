namespace SCOdyssey.Game
{
    // 홀드 종점 노트(채보 4, 떼는 판정 X): 비주얼 없음. 판정 시점까지 누르고 있으면 자동 Perfect
    public class HoldEndNote : NoteController
    {
        protected override void SetVisual()
        {
            // 끝점 플래그 전용 노트: 시각 표시 없음, 누르고 있는지 판정만 담당
            noteImage.enabled = false;
        }

        public override void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }
    }
}
