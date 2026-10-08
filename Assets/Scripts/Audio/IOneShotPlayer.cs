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

    // 타격음 원샷. StreamingAssets/HitSound/ 기준, SCO.HitSound 버스로 나간다.
    public interface IOneShotPlayer
    {
        OneShotId Register(string fileName);    // 파일명으로 멱등. 실패하면 OneShotId.None
        void Play(OneShotId id);                // 할당 없음. 잘못된 id는 무시
    }

    // UI 효과음 원샷. StreamingAssets/Sfx/ 기준, SCO.Sfx 버스로 나간다.
    // ServiceLocator가 타입을 키로 쓰므로 타격음 뱅크와 구분하려고 별도 타입으로 둔다. id는 뱅크끼리 섞어 쓰지 않는다.
    public interface ISfxPlayer : IOneShotPlayer
    {
    }
}
