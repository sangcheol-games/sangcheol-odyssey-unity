using UnityEngine;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public interface ICharacterAnimationHandler
    {
        void Initialize(GameObject spriteRoot, CharacterSO so);
        void SetState(CharacterState state);
        void SetDirection(bool isLTR);
    }
}
