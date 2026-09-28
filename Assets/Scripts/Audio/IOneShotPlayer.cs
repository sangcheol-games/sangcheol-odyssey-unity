namespace SCOdyssey.Audio
{
    // 원샷 슬롯 번호. 0은 무효(None)이다. 재구성 뒤에도 같은 id가 유효하다.
    public readonly struct OneShotId
    {
        public static readonly OneShotId None = default;

        internal readonly int Slot;

        internal OneShotId(int slot)
        {
            Slot = slot;
        }

        public bool IsValid
        {
            get { return Slot > 0; }
        }
    }

    public interface IOneShotPlayer
    {
        OneShotId Register(string fileName);    // 파일명으로 멱등. 실패하면 OneShotId.None
        void Play(OneShotId id);                // 할당 없음. 잘못된 id는 무시
    }
}
