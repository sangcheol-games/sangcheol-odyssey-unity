using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor
{
    /// <summary>
    /// 에디터의 현재 상태를 관리하는 클래스
    /// </summary>
    public class EditorState
    {
        public int currentBar = 0;          // 현재 작업 중인 마디 번호
        public int currentBeat = 4;         // 현재 비트 분할수 (기본 4)
        public EditorTool currentTool = EditorTool.None;
        public NoteType selectedNoteType = NoteType.Normal;  // 노트삽입 모드에서 선택된 노트 타입
        public bool isPlaying = false;      // 프리뷰 재생 중 여부
        public bool isPaused = false;       // 프리뷰 일시정지 여부
        public PlayMode playMode = PlayMode.Single;
    }
}
