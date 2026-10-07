using UnityEngine;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.Game
{
    /// <summary>
    /// CharacterState를 실제 애니메이션 재생으로 옮기는 백엔드.
    /// 어떤 상태를 언제 재생할지는 CharacterAnimator가 정하고, 이 인터페이스는 재생만 책임진다.
    /// </summary>
    public interface ICharacterAnimationHandler
    {
        /// <param name="mixDuration">
        /// 상태 전환 시 블렌딩 시간(초). 리듬게임이라 타격감을 위해 짧게 쓴다.
        /// </param>
        void Initialize(GameObject spriteRoot, CharacterSO so, float mixDuration);

        /// <summary>
        /// state를 재생한다.
        /// </summary>
        /// <param name="follow">
        /// state 재생이 끝난 시점에 재생되고 있어야 할 상태. Run 또는 Hold 중 하나다.
        /// ★ 구현체가 지켜야 할 사후조건이다. 호출부(CharacterAnimator)가 "지금 화면에 남아 있는 상태"를
        ///   추적할 때 이 약속을 전제로 하므로, 지키지 못하면 루프 재시작 방지가 오작동한다.
        /// state 자체가 루프 상태이면 follow는 state와 같고 이어붙일 것이 없다.
        /// </param>
        void Play(CharacterState state, CharacterState follow);
    }
}
