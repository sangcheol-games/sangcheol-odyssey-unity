using UnityEngine;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    /// <summary>
    /// Spine 애니메이션 핸들러 — Spine SDK 설치 후 구현 예정
    /// </summary>
    public class SpineAnimationHandler : ICharacterAnimationHandler
    {
        public void Initialize(GameObject spriteRoot, CharacterSO so)
        {
            // TODO: Spine SDK 설치 후 SkeletonAnimation 컴포넌트 초기화
            Debug.LogWarning("[SpineAnimationHandler] Spine SDK가 설치되지 않아 초기화할 수 없습니다.");
        }

        public void SetState(CharacterState state)
        {
            // TODO: Spine 애니메이션 트랙에 state 이름으로 애니메이션 설정
        }

        public void SetDirection(bool isLTR)
        {
            // TODO: Spine 스켈레톤 scale.x 반전
        }
    }
}
