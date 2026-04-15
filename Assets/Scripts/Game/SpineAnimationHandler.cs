using Spine.Unity;
using UnityEngine;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    /// <summary>
    /// Spine 4.2 기반 캐릭터 애니메이션 핸들러.
    /// 상태 이름은 CharacterState.ToString()과 정확히 일치해야 함.
    /// </summary>
    public class SpineAnimationHandler : ICharacterAnimationHandler
    {
        private SkeletonAnimation _skeletonAnimation;
        private Spine.AnimationState _animationState;

        public void Initialize(GameObject spriteRoot, CharacterSO so)
        {
            if (so is not SpineCharacterSO spineSO || spineSO.skeletonDataAsset == null)
            {
                Debug.LogError("[SpineAnimationHandler] SpineCharacterSO 또는 skeletonDataAsset이 없습니다.");
                return;
            }

            _skeletonAnimation = spriteRoot.GetComponent<SkeletonAnimation>();
            if (_skeletonAnimation == null)
                _skeletonAnimation = spriteRoot.AddComponent<SkeletonAnimation>();

            _skeletonAnimation.skeletonDataAsset = spineSO.skeletonDataAsset;
            _skeletonAnimation.Initialize(true);    // skeletonDataAsset 교체 후 강제 재초기화

            _animationState = _skeletonAnimation.AnimationState;
            
            var meshRenderer = spriteRoot.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.sortingOrder = 3;

            _animationState.SetAnimation(0, nameof(CharacterState.Idle), true);
        }

        public void SetState(CharacterState state)
        {
            if (_animationState == null) return;

            string name = state.ToString();

            switch (state)
            {
                // ── 루프 상태 ──────────────────────────────
                case CharacterState.Idle:
                case CharacterState.TopHold:
                case CharacterState.MiddleHold:
                case CharacterState.BottomHold:
                    _animationState.SetAnimation(0, name, true);
                    break;

                // ── 원샷 → Idle ────────────────────────────
                case CharacterState.Hit0:
                case CharacterState.Hit1:
                case CharacterState.Hit2:
                case CharacterState.Hit3:
                case CharacterState.Hit_Kind:
                case CharacterState.Hit_Umm:
                case CharacterState.Attack:
                case CharacterState.Top:
                case CharacterState.Middle:
                case CharacterState.Bottom:
                    _animationState.SetAnimation(0, name, false);
                    _animationState.AddAnimation(0, nameof(CharacterState.Idle), true, 0f);
                    break;

                // ── 원샷 → BottomHold ──────────────────────
                case CharacterState.TopHitWhileBottomHold:
                    _animationState.SetAnimation(0, name, false);
                    _animationState.AddAnimation(0, nameof(CharacterState.BottomHold), true, 0f);
                    break;

                // ── 원샷 → TopHold ─────────────────────────
                case CharacterState.BottomHitWhileTopHold:
                    _animationState.SetAnimation(0, name, false);
                    _animationState.AddAnimation(0, nameof(CharacterState.TopHold), true, 0f);
                    break;
            }
        }

    }
}
