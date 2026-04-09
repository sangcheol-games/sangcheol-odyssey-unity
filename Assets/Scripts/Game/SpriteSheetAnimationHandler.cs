using UnityEngine;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class SpriteSheetAnimationHandler : ICharacterAnimationHandler
    {
        private Animator _animator;

        public void Initialize(GameObject spriteRoot, CharacterSO so)
        {
            _animator = spriteRoot.GetComponent<Animator>();
            if (_animator == null)
                _animator = spriteRoot.AddComponent<Animator>();

            if (so is SpriteSheetCharacterSO spriteSheetSO && spriteSheetSO.animatorController != null)
                _animator.runtimeAnimatorController = spriteSheetSO.animatorController;
        }

        public void SetState(CharacterState state)
        {
            if (_animator == null) return;
            _animator.SetTrigger(state.ToString());
        }

    }
}
