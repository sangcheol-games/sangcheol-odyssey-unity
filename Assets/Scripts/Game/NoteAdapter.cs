using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 노트 어댑터(레거시): 하나의 노트 프리팹에 모든 타입 컴포넌트를 붙여두고, 풀에서 꺼낼 때
    // 타입에 맞는 컨트롤러만 활성화해 반환한다. ChartManager.SpawnNextNotes가 ActivateAndGet으로 사용.
    public class NoteAdapter : MonoBehaviour
    {
        [Header("Components")]
        public NormalNote normalNote;
        public HoldStartNote holdStartNote;
        public HoldingNote holdingNote;
        public HoldEndNote holdEndNote;
        public HoldReleaseNote holdReleaseNote;

        private void Awake()    // 컴포넌트 연결 안된 경우 자동으로 할당
        {
            if (!normalNote) normalNote = GetComponent<NormalNote>();
            if (!holdStartNote) holdStartNote = GetComponent<HoldStartNote>();
            if (!holdingNote) holdingNote = GetComponent<HoldingNote>();
            if (!holdEndNote) holdEndNote = GetComponent<HoldEndNote>();
            if (!holdReleaseNote) holdReleaseNote = GetComponent<HoldReleaseNote>();
        }

        // 타입에 맞는 노트 컨트롤러만 켜서 반환. 나머지는 모두 끔(하나의 GameObject를 타입 전환해 재사용)
        public NoteController ActivateAndGet(NoteType type)
        {
            normalNote.enabled = false;
            holdStartNote.enabled = false;
            holdingNote.enabled = false;
            holdEndNote.enabled = false;
            holdReleaseNote.enabled = false;

            NoteController selected = null;

            switch (type)
            {
                case NoteType.Normal: selected = normalNote; break;
                case NoteType.HoldStart: selected = holdStartNote; break;
                case NoteType.Holding: selected = holdingNote; break;
                case NoteType.HoldEnd: selected = holdEndNote; break;
                case NoteType.HoldRelease: selected = holdReleaseNote; break;
                default: selected = normalNote; break;
            }

            if (selected != null) selected.enabled = true;
            return selected;
        }
    }
}
