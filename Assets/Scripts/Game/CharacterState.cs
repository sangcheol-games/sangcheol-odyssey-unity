namespace SCOdyssey.Game
{
    /// <summary>
    /// 캐릭터 애니메이션 상태. 연출 전용 개념이라 Domain이 아니라 Game에 둔다.
    ///
    /// ★ 각 이름은 Spine 스켈레톤의 애니메이션 이름과 대소문자·언더스코어까지 정확히 일치해야 한다.
    ///   기동 시 SpineAnimationHandler가 전부 조회해 캐시하며, 없는 이름은 LogError로 알린다.
    ///   그래서 C# 네이밍 관례(PascalCase)를 따르지 않는 이름이 섞여 있다 — 의도된 것이다.
    ///
    /// ★ Hit_master_1~3 과 Hit_1~3 은 각각 "연속" 배치여야 한다.
    ///   랜덤 변형 선택이 (Hit_master_1 + pick) 형태의 인덱스 덧셈을 쓰기 때문이다.
    /// </summary>
    public enum CharacterState
    {
        // ── 루프 (원샷이 끝나면 돌아갈 곳) ────────────────
        Run,                // 아무 입력 없는 기본 상태
        Hold,               // 홀드 유지 중. 위/아래/상하동시를 Y 좌표로만 구분한다

        // ── 제자리 히트: 판정 등급별 ──────────────────────
        Hit_master_1, Hit_master_2, Hit_master_3,   // Perfect / Master. 연속 배치 필수
        Hit_1, Hit_2, Hit_3,                        // Ideal / Kind. 연속 배치 필수
        Hit_umm,                                    // Umm

        // ── 헛침 ──────────────────────────────────────────
        Miss,               // 판정 윈도우에 칠 노트가 없는데 누름

        // ── 이동을 동반한 히트 ────────────────────────────
        Double_hit,         // 상하 동시 입력이고 둘 다 히트
        Up_hit, Down_hit,   // 이동하며 일반 노트 히트

        // ── 홀드 진입 (원샷 → Hold 루프) ──────────────────
        Up_hold, Down_hold,

        // ── 홀드 유지 중 반대편 히트 (Y는 고정, 원샷 → Hold) ─
        Up_hit_while_hold, Down_hit_while_hold,
    }

    public static class CharacterStateInfo
    {
        /// <summary>
        /// 루프 상태인지. 17개 중 Run과 Hold 둘뿐이고 나머지 15개는 원샷이다.
        /// 루프 상태는 곧 "원샷이 끝나면 돌아갈 곳"이기도 하다.
        /// CharacterAnimator와 재생 핸들러가 같은 기준을 쓰도록 여기 한 곳에만 둔다.
        /// </summary>
        public static bool IsLoop(CharacterState state)
        {
            return state == CharacterState.Run || state == CharacterState.Hold;
        }
    }
}
