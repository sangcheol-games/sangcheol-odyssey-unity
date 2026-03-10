// AddressablesContentProvider.cs
// CDN 기반 콘텐츠 제공 구현체.
//
// [현재 상태]
// 미활성 - USE_CDN_DELIVERY 스크립팅 심볼이 정의될 때만 Managers.cs에서 등록됨.
// Addressables 패키지가 설치되어 있지 않으면 컴파일 에러 발생.
// CDN 전환 준비가 완료될 때까지 이 파일을 수정하지 마세요.
//
// [활성화 조건]
// 1. Unity Package Manager에서 Addressables 패키지 설치 (com.unity.addressables)
// 2. Addressables 그룹 구성 (MusicMetadata, MusicAudio, MusicBGA)
// 3. CDN (Firebase Storage / AWS S3) 설정 완료
// 4. MusicSO 에셋에 audioAddressableKey, bgaAddressableKey 입력
// 5. Scripting Define Symbols에 USE_CDN_DELIVERY 추가
//
// 자세한 절차: Assets/Scripts/Game/CONTENT_DELIVERY_MIGRATION.md

#if USE_CDN_DELIVERY

using System;
using System.IO;
using System.Threading.Tasks;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace SCOdyssey.App
{
    /// <summary>
    /// IContentProvider CDN 구현체.
    /// Addressables Remote CDN에서 암호화된 음원/BGA를 다운로드합니다.
    ///
    /// [번들 구조]
    /// - MusicAudio 그룹 (Pack Separately): 곡별 암호화 OGG TextAsset
    /// - MusicBGA 그룹 (Pack Separately): 곡별 암호화 MP4 TextAsset
    /// - MusicMetadata 그룹 (Pack Separately): MusicSO, 채보, 앨범아트
    ///
    /// [캐시 정책]
    /// - 오디오: 번들에서 로드 → 복호화 → FMOD OPENMEMORY (메모리에만 존재)
    /// - BGA: 번들에서 로드 → 복호화 → persistentDataPath/{id}_{hash}.mp4 캐시
    ///        콘텐츠 해시 변경 시 자동 재복호화 (CDN 업데이트 감지)
    /// </summary>
    public class AddressablesContentProvider : IContentProvider
    {
        public bool IsInitialized { get; private set; }

        public async Task InitializeAsync()
        {
            AudioCrypto.Initialize();

            // CDN에서 최신 카탈로그 확인 및 업데이트
            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            await checkHandle.Task;

            if (checkHandle.Status == AsyncOperationStatus.Succeeded && checkHandle.Result.Count > 0)
            {
                var updateHandle = Addressables.UpdateCatalogs(checkHandle.Result, false);
                await updateHandle.Task;
                Addressables.Release(updateHandle);
            }

            Addressables.Release(checkHandle);

            IsInitialized = true;
            Debug.Log("[AddressablesContentProvider] 카탈로그 업데이트 완료.");
        }

        /// <summary>
        /// CDN 번들에서 암호화된 OGG를 로드하여 복호화된 바이트 반환.
        /// 번들은 Addressables 캐시(persistentDataPath/aa/)에 암호화 상태로 저장됨.
        /// </summary>
        public async Task<byte[]> LoadAudioBytesAsync(MusicSO music)
        {
            if (string.IsNullOrEmpty(music.audioAddressableKey))
            {
                Debug.LogError($"[AddressablesContentProvider] audioAddressableKey가 없습니다: {music.name}");
                return null;
            }

            var handle = Addressables.LoadAssetAsync<TextAsset>(music.audioAddressableKey);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[AddressablesContentProvider] 오디오 로드 실패: {music.audioAddressableKey}");
                Addressables.Release(handle);
                return null;
            }

            byte[] encryptedBytes = handle.Result.bytes;
            Addressables.Release(handle);

            return AudioCrypto.Decrypt(encryptedBytes);
        }

        /// <summary>
        /// CDN 번들에서 암호화된 MP4를 로드하여 복호화, persistentDataPath에 캐시 후 경로 반환.
        /// 콘텐츠 해시 기반 캐시 무효화로 CDN 업데이트 시 자동 재복호화.
        /// </summary>
        public async Task<string> GetBGAPathAsync(MusicSO music)
        {
            if (string.IsNullOrEmpty(music.bgaAddressableKey))
                return null;

            // Addressables 콘텐츠 해시 조회 (번들 버전 감지)
            string contentHash = await GetContentHashAsync(music.bgaAddressableKey);
            string cachedPath = Path.Combine(
                Application.persistentDataPath,
                $"bga_{music.id}_{contentHash}.mp4");

            if (File.Exists(cachedPath))
                return cachedPath;

            // 이전 버전 캐시 삭제
            CleanOldBGACache(music.id);

            // CDN에서 로드 및 복호화
            var handle = Addressables.LoadAssetAsync<TextAsset>(music.bgaAddressableKey);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[AddressablesContentProvider] BGA 로드 실패: {music.bgaAddressableKey}");
                Addressables.Release(handle);
                return null;
            }

            byte[] encryptedBytes = handle.Result.bytes;
            Addressables.Release(handle);

            byte[] plainBytes = AudioCrypto.Decrypt(encryptedBytes);
            if (plainBytes == null)
            {
                Debug.LogError("[AddressablesContentProvider] BGA 복호화 실패.");
                return null;
            }

            File.WriteAllBytes(cachedPath, plainBytes);
            Debug.Log($"[AddressablesContentProvider] BGA 캐시 저장: {cachedPath}");

            return cachedPath;
        }

        /// <summary>
        /// 해당 곡의 미캐시 콘텐츠 다운로드 용량(바이트) 반환.
        /// 이미 캐시됐으면 0 반환.
        /// </summary>
        public async Task<long> GetDownloadSizeAsync(MusicSO music)
        {
            // 오디오 + BGA 번들의 미캐시 용량 합산
            long audioSize = 0;
            long bgaSize = 0;

            if (!string.IsNullOrEmpty(music.audioAddressableKey))
            {
                var handle = Addressables.GetDownloadSizeAsync(music.audioAddressableKey);
                await handle.Task;
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    audioSize = handle.Result;
                Addressables.Release(handle);
            }

            if (!string.IsNullOrEmpty(music.bgaAddressableKey))
            {
                var handle = Addressables.GetDownloadSizeAsync(music.bgaAddressableKey);
                await handle.Task;
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    bgaSize = handle.Result;
                Addressables.Release(handle);
            }

            return audioSize + bgaSize;
        }

        private async Task<string> GetContentHashAsync(string addressableKey)
        {
            // Addressables ResourceLocation에서 번들 해시 추출 시도
            // 실패 시 타임스탬프 대체값 사용
            try
            {
                var locHandle = Addressables.LoadResourceLocationsAsync(addressableKey);
                await locHandle.Task;

                if (locHandle.Status == AsyncOperationStatus.Succeeded && locHandle.Result.Count > 0)
                {
                    string internalId = locHandle.Result[0].InternalId;
                    Addressables.Release(locHandle);
                    // InternalId에서 번들 파일명 해시 부분 추출
                    return Math.Abs(internalId.GetHashCode()).ToString();
                }

                Addressables.Release(locHandle);
            }
            catch { /* 폴백으로 진행 */ }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
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

#endif // USE_CDN_DELIVERY
