using UnityEngine;
using UnityEngine.UI;


namespace SCOdyssey.Game
{
    // 홀드 종점 노트(채보 5, 떼는 판정 O): 헤드만 표시. 판정범위 내에서 키를 떼는 타이밍으로 판정(TryJudgeRelease)
    public class HoldReleaseNote : NoteController
    {
        public Animator noteAnim;


        protected override void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null)
                noteImage = GetComponentInChildren<Image>();
            if (noteAnim == null)
                Debug.LogWarning("HoldReleaseNote: No attached Animator component");
        }


        protected override void SetVisual()
        {
            // 릴리즈 판정 노트: 헤드만 표시 (홀드바 없음)
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
