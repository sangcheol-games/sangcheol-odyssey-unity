using System.IO;
using System.Threading.Tasks;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;

namespace SCOdyssey.App
{
    /// <summary>
    /// IContentProvider 로컬 구현체.
    /// StreamingAssets에서 직접 파일을 읽어 반환합니다.
    ///
    /// [현재 활성 구현체]
    /// CDN 인프라 준비 전까지 이 구현체를 사용합니다.
    /// Managers.cs에서 USE_CDN_DELIVERY 미정의 시 이 클래스가 등록됩니다.
    ///
    /// [CDN 전환 후]
    /// AddressablesContentProvider로 교체되며 이 파일은 삭제됩니다.
    /// 마이그레이션 절차: Assets/Scripts/Game/CONTENT_DELIVERY_MIGRATION.md
    ///
    /// [SKIP_CRYPTO]
    /// AudioCrypto.Decrypt가 raw bytes를 반환하므로 평문 .ogg 파일로 테스트 가능.
    /// 암호화 적용 시: AudioEncryptionTool 실행 후 MusicSO.audioFilePath를 .ogg.dat로 변경.
    /// </summary>
    public class LocalContentProvider : IContentProvider
    {
        public bool IsInitialized { get; private set; }

        public Task InitializeAsync()
        {
            // 로컬 모드는 초기화 불필요
            AudioCrypto.Initialize();
            IsInitialized = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// StreamingAssets/Music/{music.audioFilePath} 에서 읽어 복호화된 바이트 반환.
        /// SKIP_CRYPTO 정의 시 복호화 없이 raw bytes 반환.
        /// </summary>
        public Task<byte[]> LoadAudioBytesAsync(MusicSO music)
        {
            if (string.IsNullOrEmpty(music.audioFilePath))
            {
                Debug.LogWarning("[LocalContentProvider] audioFilePath가 비어있습니다.");
                return Task.FromResult<byte[]>(null);
            }

            string fullPath = Path.Combine(Application.streamingAssetsPath, "Music", music.audioFilePath);

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[LocalContentProvider] 오디오 파일 없음: {fullPath}");
                return Task.FromResult<byte[]>(null);
            }

            byte[] rawBytes = File.ReadAllBytes(fullPath);
            byte[] plainBytes = AudioCrypto.Decrypt(rawBytes);

            return Task.FromResult(plainBytes);
        }

        /// <summary>
        /// StreamingAssets/BGA/{music.videoFileName} 경로를 반환.
        /// BGA 파일이 암호화된 경우(.bga), 복호화하여 persistentDataPath에 캐시 후 경로 반환.
        /// 평문 .mp4 파일인 경우(SKIP_CRYPTO 또는 미암호화) 경로 그대로 반환.
        /// </summary>
        public Task<string> GetBGAPathAsync(MusicSO music)
        {
            if (string.IsNullOrEmpty(music.videoFileName))
                return Task.FromResult<string>(null);

            string fullPath = Path.Combine(Application.streamingAssetsPath, "BGA", music.videoFileName);

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[LocalContentProvider] BGA 파일 없음: {fullPath}");
                return Task.FromResult<string>(null);
            }

#if SKIP_CRYPTO
            // 개발 모드: 평문 파일 경로 그대로 반환
            return Task.FromResult(fullPath);
#else
            // 암호화 모드: 복호화 후 persistentDataPath에 캐시
            // 해시 대신 파일 수정 시각을 사용하여 업데이트 감지
            string cacheKey = GetBGACacheKey(music, fullPath);
            string cachedPath = Path.Combine(Application.persistentDataPath, cacheKey);

            if (!File.Exists(cachedPath))
            {
                // 이전 버전 캐시 삭제
                CleanOldBGACache(music.id);

                byte[] rawBytes = File.ReadAllBytes(fullPath);
                byte[] plainBytes = AudioCrypto.Decrypt(rawBytes);

                if (plainBytes == null)
                {
                    Debug.LogError("[LocalContentProvider] BGA 복호화 실패.");
                    return Task.FromResult<string>(null);
                }

                File.WriteAllBytes(cachedPath, plainBytes);
                Debug.Log($"[LocalContentProvider] BGA 캐시 저장: {cachedPath}");
            }

            return Task.FromResult(cachedPath);
#endif
        }

        public Task<long> GetDownloadSizeAsync(MusicSO music)
        {
            // 로컬 모드는 다운로드 없음
            return Task.FromResult(0L);
        }

        private static string GetBGACacheKey(MusicSO music, string sourcePath)
        {
            // 파일 수정 시각 기반 캐시 키 (CDN 해시와 동일한 역할)
            long modTime = File.GetLastWriteTime(sourcePath).Ticks;
            return $"bga_{music.id}_{modTime}.mp4";
        }

        private static void CleanOldBGACache(int musicId)
        {
            string pattern = $"bga_{musicId}_*.mp4";
            foreach (string old in Directory.GetFiles(Application.persistentDataPath, pattern))
            {
                try { File.Delete(old); }
                catch { /* 무시 */ }
            }
        }
    }
}
