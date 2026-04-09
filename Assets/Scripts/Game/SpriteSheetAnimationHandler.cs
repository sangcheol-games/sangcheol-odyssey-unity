using UnityEngine;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class SpriteSheetAnimationHandler : ICharacterAnimationHandler
    {
        private Animator _animator;
        private Transform _spriteTransform;

        public void Initialize(GameObject spriteRoot, CharacterSO so)
        {
            _animator = spriteRoot.GetComponent<Animator>();
            if (_animator == null)
                _animator = spriteRoot.AddComponent<Animator>();

            _spriteTransform = spriteRoot.transform;

            if (so is SpriteSheetCharacterSO spriteSheetSO && spriteSheetSO.animatorController != null)
                _animator.runtimeAnimatorController = spriteSheetSO.animatorController;
        }

        public void SetState(CharacterState state)
        {
            if (_animator == null) return;
            _animator.SetTrigger(state.ToString());
        }

        public void SetDirection(bool isLTR)
        {
            if (_spriteTransform == null) return;
            Vector3 scale = _spriteTransform.localScale;
            scale.x = isLTR ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            _spriteTransform.localScale = scale;
        }
    }
}
