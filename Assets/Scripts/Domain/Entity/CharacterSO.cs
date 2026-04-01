using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Domain.Entity
{
    [CreateAssetMenu(menuName = "Skins/CharacterSO", fileName = "CharacterSO_")]
    public class CharacterSO : SkinSO
    {
        [Header("Animation")]
        public AnimationType animationType;                     // SpriteSheet | Spine
        public RuntimeAnimatorController animatorController;    // SpriteSheet용
    }
}
