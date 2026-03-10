using System;
using System.Security.Cryptography;
using UnityEngine;

namespace SCOdyssey.Core
{
    /// <summary>
    /// AES-256-CBC 기반 오디오 파일 복호화 유틸리티.
    ///
    /// [보안 구조]
    /// - 키를 두 부분으로 분리하여 저장:
    ///   - PART_A: 코드에 임베드 (IL로 컴파일됨)
    ///   - PART_B: Resources/crypto_manifest 에 바이너리 에셋으로 저장
    /// - 런타임에 두 파트를 결합하여 32바이트 AES 키 구성
    ///
    /// [암호화 포맷]
    /// - 암호화된 파일 구조: [IV(16바이트)] + [AES-256-CBC 암호문]
    /// - 에디터 암호화 도구(AudioEncryptionTool)가 이 포맷으로 파일 생성
    ///
    /// [테스트/개발]
    /// - Project Settings > Player > Scripting Define Symbols에 SKIP_CRYPTO 추가하면
    ///   복호화 없이 원본 바이트를 그대로 반환 (StreamingAssets 평문 파일로 테스트 가능)
    /// </summary>
    public static class AudioCrypto
    {
        // 키 분할 - PART_A: 코드에 임베드 (16바이트 = 32자 hex)
        // 실제 배포 전 여기에 고유 키 설정 필요
        private const string PART_A_HEX = "0102030405060708090A0B0C0D0E0F10";

        // 키 분할 - PART_B: Resources/crypto_manifest 에서 로드 (16바이트)
        private const string MANIFEST_RESOURCE_PATH = "crypto_manifest";

        private static byte[] _cachedKey;

        /// <summary>
        /// AES 키를 초기화합니다. 앱 시작 시 또는 첫 복호화 전에 호출.
        /// Resources/crypto_manifest 에셋이 있어야 합니다.
        /// </summary>
        public static void Initialize()
        {
#if SKIP_CRYPTO
            return;
#else
            if (_cachedKey != null) return;

            TextAsset manifest = Resources.Load<TextAsset>(MANIFEST_RESOURCE_PATH);
            if (manifest == null)
            {
                Debug.LogError("[AudioCrypto] crypto_manifest 에셋을 찾을 수 없습니다. " +
                               "Resources/ 폴더에 crypto_manifest.bytes 파일을 추가하세요.");
                return;
            }

            byte[] partA = HexToBytes(PART_A_HEX);
            byte[] partB = manifest.bytes;

            if (partA.Length != 16 || partB.Length != 16)
            {
                Debug.LogError("[AudioCrypto] 키 파트 길이 오류. 각 16바이트여야 합니다.");
                return;
            }

            _cachedKey = new byte[32];
            Buffer.BlockCopy(partA, 0, _cachedKey, 0, 16);
            Buffer.BlockCopy(partB, 0, _cachedKey, 16, 16);
#endif
        }

        /// <summary>
        /// 암호화된 데이터를 복호화합니다.
        /// SKIP_CRYPTO가 정의된 경우 cipherData를 그대로 반환합니다.
        /// </summary>
        /// <param name="cipherData">[IV(16B)] + [암호문] 형식의 바이트 배열</param>
        /// <returns>복호화된 원본 바이트 배열</returns>
        public static byte[] Decrypt(byte[] cipherData)
        {
#if SKIP_CRYPTO
            // 개발/테스트 모드: 복호화 없이 raw 바이트 반환 (평문 파일 그대로 사용)
            return cipherData;
#else
            if (_cachedKey == null)
            {
                Initialize();
                if (_cachedKey == null)
                {
                    Debug.LogError("[AudioCrypto] 키 초기화 실패. 복호화를 수행할 수 없습니다.");
                    return null;
                }
            }

            if (cipherData == null || cipherData.Length < 17)
            {
                Debug.LogError("[AudioCrypto] 유효하지 않은 암호화 데이터입니다.");
                return null;
            }

            // 선두 16바이트 = IV
            byte[] iv = new byte[16];
            Buffer.BlockCopy(cipherData, 0, iv, 0, 16);

            // 나머지 = 암호문
            byte[] cipher = new byte[cipherData.Length - 16];
            Buffer.BlockCopy(cipherData, 16, cipher, 0, cipher.Length);

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _cachedKey;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                }
            }
#endif
        }

        private static byte[] HexToBytes(string hex)
        {
            int len = hex.Length / 2;
            byte[] bytes = new byte[len];
            for (int i = 0; i < len; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
