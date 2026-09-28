using UnityEngine;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.Game
{
    /// <summary>
    /// 스프라이트시트(Unity Animator) 기반 캐릭터 애니메이션 핸들러.
    /// 현재 프로젝트에 SpriteSheetCharacterSO 에셋이 하나도 없어 실행 경로가 없다.
    /// 2D 스프라이트 캐릭터를 추가할 여지를 남겨두기 위해 파일만 유지한다.
    /// </summary>
    public class SpriteSheetAnimationHandler : ICharacterAnimationHandler
    {
        private Animator _animator;

        public void Initialize(GameObject spriteRoot, CharacterSO so, float mixDuration)
        {
            _animator = spriteRoot.GetComponent<Animator>();
            if (_animator == null)
                _animator = spriteRoot.AddComponent<Animator>();

            if (so is SpriteSheetCharacterSO spriteSheetSO && spriteSheetSO.animatorController != null)
                _animator.runtimeAnimatorController = spriteSheetSO.animatorController;

            // mixDuration은 쓰지 않는다. 전환 블렌딩은 AnimatorController의 Transition Duration이 담당한다.
        }

        public void Play(CharacterState state, CharacterState follow)
        {
            if (_animator == null) return;

            // follow를 무시한다. 이 백엔드는 전이를 AnimatorController에 위임하므로
            // "원샷이 끝나면 follow로 돌아간다"는 사후조건을 코드로 표현할 방법이 없다.
            // 스프라이트시트 캐릭터를 실제로 추가할 때, 원샷 상태에서 Run/Hold로 돌아오는 전이를
            // 컨트롤러에 직접 만들어 이 규칙을 지켜야 한다.
            _animator.SetTrigger(state.ToString());
        }
    }
}
