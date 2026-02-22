namespace SCOdyssey.ChartEditor
{
    public enum EditorTool
    {
        None,
        NoteInsert,
        DirectionSelect
    }

    public enum PlayMode
    {
        Single,     // 현재 1마디
        Partial,    // 현재 마디 + 앞뒤 1마디 (총 3마디)
        Full        // 0번 마디부터 전체
    }
}
