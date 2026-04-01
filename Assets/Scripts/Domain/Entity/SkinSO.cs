using UnityEngine;

namespace SCOdyssey.Domain.Entity
{
    public abstract class SkinSO : ScriptableObject
    {
        public int id;
        public string skinName;
        public Sprite thumbnailSprite;  // 선택 UI용 공통 필드
    }
}
