using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class NoteAdapter : MonoBehaviour
    {
        [Header("Components")]
        public NormalNote normalNote; 
        public HoldStartNote holdStartNote;
        public HoldingNote holdingNote;
        public HoldEndNote holdEndNote;

        private void Awake()    // 컴포넌트 연결 안된 경우 자동으로 할당
        {
            if (!normalNote) normalNote = GetComponent<NormalNote>();
            if (!holdStartNote) holdStartNote = GetComponent<HoldStartNote>();
            if (!holdingNote) holdingNote = GetComponent<HoldingNote>();
            if (!holdEndNote) holdEndNote = GetComponent<HoldEndNote>();
        }

        public NoteController ActivateAndGet(NoteType type)
        {
            normalNote.enabled = false;
            holdStartNote.enabled = false;
            holdingNote.enabled = false;
            holdEndNote.enabled = false;

            NoteController selected = null;

            switch (type)
            {
                case NoteType.Normal: selected = normalNote; break;
                case NoteType.HoldStart: selected = holdStartNote; break;
                case NoteType.Holding: selected = holdingNote; break;
                case NoteType.HoldEnd: selected = holdEndNote; break;
                default: selected = normalNote; break;
            }

            if (selected != null) selected.enabled = true;
            return selected;
        }
    }
}