namespace SCOdyssey.Audio
{
    // Boot.ExecutionOrder는 Assembly-CSharp에 있어 참조할 수 없으므로 따로 둔다.
    public static class AudioExecutionOrder
    {
        public const int EngineRunner = -1010;      // Boot의 Early(-1000)보다 먼저
        public const int JudgementDriver = -900;    // EngineRunner 다음
    }
}
