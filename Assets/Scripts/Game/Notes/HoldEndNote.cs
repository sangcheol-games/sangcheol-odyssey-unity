namespace SCOdyssey.Game
{
    // 홀드 꼬리 노트(채보 4): 비주얼 없음. 5와 똑같이 떼는 타이밍으로 판정한다
    public class HoldEndNote : NoteController
    {
        protected override void SetVisual()
        {
            // 끝점: 시각 표시 없음
            noteImage.enabled = false;
        }

        public override void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }
    }
}
