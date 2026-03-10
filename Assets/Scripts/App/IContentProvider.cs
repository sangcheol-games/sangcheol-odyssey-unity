using System.Threading.Tasks;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App
{
    /// <summary>
    /// 음원/BGA 콘텐츠 로딩 추상화 인터페이스.
    ///
    /// [구현체]
    /// - LocalContentProvider: StreamingAssets 직접 읽기 (현재 활성, 개발/테스트용)
    /// - AddressablesContentProvider: CDN 다운로드 (인프라 준비 후 전환)
    ///
    /// [전환 방법]
    /// Managers.cs에서 #if USE_CDN_DELIVERY 로 등록 구현체 전환.
    /// 이 인터페이스를 사용하는 GameDataLoader 등 상위 코드는 변경 불필요.
    ///
    /// 자세한 마이그레이션 절차: Assets/Scripts/Game/CONTENT_DELIVERY_MIGRATION.md
    /// </summary>
    public interface IContentProvider
    {
        /// <summary>
        /// 초기화. LocalContentProvider: no-op. AddressablesContentProvider: UpdateCatalogs 실행.
        /// </summary>
        Task InitializeAsync();

        bool IsInitialized { get; }

        /// <summary>
        /// 음원 파일을 읽어 복호화된 PCM 가능한 바이트 배열로 반환.
        /// (SKIP_CRYPTO 정의 시 복호화 없이 raw bytes 반환)
        /// </summary>
        Task<byte[]> LoadAudioBytesAsync(MusicSO music);

        /// <summary>
        /// BGA 영상을 Unity VideoPlayer가 재생할 수 있는 파일 경로로 반환.
        /// LocalContentProvider: StreamingAssets 경로 그대로 반환.
        /// AddressablesContentProvider: 복호화 후 persistentDataPath 캐시 경로 반환.
        /// </summary>
        Task<string> GetBGAPathAsync(MusicSO music);

        /// <summary>
        /// 미캐시 상태의 다운로드 필요 용량(바이트)을 반환.
        /// LocalContentProvider: 항상 0 반환.
        /// </summary>
        Task<long> GetDownloadSizeAsync(MusicSO music);
    }
}
