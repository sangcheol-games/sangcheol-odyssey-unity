using System;
using System.Text;
using Spine.Unity;
using UnityEngine;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.Game
{
    /// <summary>
    /// Spine 4.2 기반 캐릭터 애니메이션 핸들러.
    /// CharacterState 이름이 곧 Spine 애니메이션 이름이다.
    ///
    /// 이름 조회는 Initialize에서 한 번만 하고 Spine.Animation 객체를 배열에 캐시한다.
    /// 재생할 때마다 이름으로 찾으면 매번 문자열을 할당하고 스켈레톤의 애니메이션 목록을 선형 스캔하게 된다.
    /// </summary>
    public class SpineAnimationHandler : ICharacterAnimationHandler
    {
        private SkeletonAnimation _skeletonAnimation;
        private Spine.AnimationState _animationState;

        // CharacterState 값을 인덱스로 쓰는 캐시. 스켈레톤에 없는 이름 자리는 null이다.
        private Spine.Animation[] _animations;

        public void Initialize(GameObject spriteRoot, CharacterSO so, float mixDuration)
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

            // 우리 LateUpdate가 SetAnimation을 부른 뒤 Spine이 같은 프레임에 반영하도록 한다.
            // 기본값(InUpdate)이면 입력 해석이 화면에 한 프레임 늦게 나타난다.
            // CharacterAnimator가 DefaultExecutionOrder(Early)라 우리 LateUpdate가 먼저 돈다.
            _skeletonAnimation.UpdateTiming = UpdateTiming.InLateUpdate;

            _animationState = _skeletonAnimation.AnimationState;

            // 에셋 기본 믹스가 0.2초인데 클립 길이가 0.533초라 전환마다 타격감이 뭉개진다.
            _animationState.Data.DefaultMix = mixDuration;

            var meshRenderer = spriteRoot.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.sortingOrder = 3;

            CacheAnimations();

            // 기본 상태 재생은 LoadCharacter가 Initialize 직후에 한다.
            // 여기서 같이 재생하면 "지금 화면에 무엇이 있는가"를 정하는 주체가 둘이 된다.
        }

        /// <summary>
        /// CharacterState 전체를 한 번 조회해 캐시하고, 스켈레톤에 없는 이름을 한 줄로 보고한다.
        /// Spine 자산 재작업 중 어떤 클립이 아직 없는지 기동 시점에 바로 알 수 있다.
        /// </summary>
        private void CacheAnimations()
        {
            var skeletonData = _skeletonAnimation.Skeleton.Data;
            Array states = Enum.GetValues(typeof(CharacterState));

            _animations = new Spine.Animation[states.Length];

            StringBuilder missing = null;
            foreach (CharacterState state in states)
            {
                string animationName = state.ToString();
                Spine.Animation animation = skeletonData.FindAnimation(animationName);
                _animations[(int)state] = animation;

                if (animation != null) continue;

                if (missing == null)
                    missing = new StringBuilder();
                else
                    missing.Append(", ");
                missing.Append(animationName);
            }

            if (missing != null)
            {
                Debug.LogError($"[SpineAnimationHandler] 스켈레톤에 없는 애니메이션: {missing}");
            }
        }

        public void Play(CharacterState state, CharacterState follow)
        {
            if (_animationState == null || _animations == null) return;

            Spine.Animation main = _animations[(int)state];

            // 자산에 없는 상태다. 여기서 null을 넘기면 Spine이 예외를 던져 LateUpdate가 통째로 멈춘다.
            // 기동 시 이미 LogError로 알렸으므로 조용히 건너뛰고 직전 애니메이션을 유지한다.
            if (main == null) return;

            if (CharacterStateInfo.IsLoop(state))
            {
                _animationState.SetAnimation(0, main, true);
                return;
            }

            // 원샷을 재생하고 그 뒤에 복귀할 루프를 큐에 이어붙인다.
            _animationState.SetAnimation(0, main, false);

            Spine.Animation followAnimation = _animations[(int)follow];
            if (followAnimation == null) return;

            _animationState.AddAnimation(0, followAnimation, true, 0f);
        }
    }
}
