namespace SCOdyssey.Game
{
    // 홀드 종점 노트(채보 5, 떼는 판정 O): 헤드만 표시. 판정범위 내에서 키를 떼는 타이밍으로 판정(TryJudgeRelease)
    public class HoldReleaseNote : NoteController
    {
        protected override void SetVisual()
        {
            // 릴리즈 판정 노트: 헤드만 표시 (홀드바 없음)
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
