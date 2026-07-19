using UnityEngine;

namespace SCOdyssey.Game
{
    // 일반 노트: 판정범위 내 키 입력으로 판정. 헤드만 표시(홀드 없음)
    public class NormalNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = true;
        }
    }
}