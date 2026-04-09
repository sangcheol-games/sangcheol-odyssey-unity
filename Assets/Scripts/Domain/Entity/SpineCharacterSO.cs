using Spine.Unity;
using UnityEngine;

namespace SCOdyssey.Domain.Entity
{
    [CreateAssetMenu(menuName = "Skins/SpineCharacterSO", fileName = "SpineCharacterSO_")]
    public class SpineCharacterSO : CharacterSO
    {
        public SkeletonDataAsset skeletonDataAsset;
    }
}
