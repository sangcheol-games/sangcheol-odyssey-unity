namespace SCOdyssey.Game
{
    // 일반 노트: 판정범위 내 키 입력으로 판정. 헤드만 표시(홀드 없음)
    public class NormalNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = true;
        }

        // 판정 즉시 확정하고, 히트 애니메이션이 끝나면 풀에 반환
        public override void OnHit()
        {
            if (isJudged) return;
            isJudged = true;
            PlayHitAnim(DeleteNote);
        }
    }
}
