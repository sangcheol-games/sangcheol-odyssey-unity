using UnityEngine;
using UnityEngine.UI;


namespace SCOdyssey.Game
{
    // 일반 노트: 판정범위 내 키 입력으로 판정. 헤드만 표시(홀드 없음)
    public class NormalNote : NoteController
    {
        public Animator noteAnim;


        protected override void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null)
                noteImage = GetComponentInChildren<Image>();
            if (noteAnim == null)
            {
                Debug.LogWarning("NormalNote: No attached Animator component"); 
                DeleteNote();
            }
        }


        protected override void SetVisual()
        {
            noteImage.enabled = true;
        }

        public override void OnMiss()
        {
            if (isJudged) return;
            noteAnim.enabled = true;
            noteAnim.Play("Miss");
            // TODO DeleteNote()는 애니메 에디터 쪽에서 연결해줘야할듯
        }

        public override void OnHit()
        {
            if (isJudged) return;
            noteAnim.enabled = true;
            noteAnim.Play("Hit");
        }
    }
}