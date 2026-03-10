using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;
using UnityEngine.Localization;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Domain.Entity
{
    [CreateAssetMenu(menuName = "MusicData/MusicSO", fileName ="MusicSO_")]
    public class MusicSO : SerializedScriptableObject
    {
        public int id;

        [Header("Music Info")]
        public LocalizedString title;
        public LocalizedString producer;
        public LocalizedString vocal;
        public string illustrator;
        public string animator;
        public int bpm;

        [Header("Game Info")]
        public Dictionary<Difficulty, int> level;

        [Header("Resource Info")]

        public Dictionary<Difficulty, TextAsset> chartFile;

        // [로컬 모드] LocalContentProvider에서 사용 (현재 활성)
        public string audioFilePath;    // StreamingAssets/Music/ 기준 파일명 (예: "song_0001.ogg" 또는 "song_0001.ogg.dat")
        public string videoFileName;    // StreamingAssets/BGA/ 폴더 내 파일명 (예: "BGA_0001.mp4" 또는 "BGA_0001.bga")

        // [CDN 모드] AddressablesContentProvider에서 사용 (USE_CDN_DELIVERY 활성화 후)
        // Addressables 그룹에서 해당 에셋에 지정한 Address 값과 일치해야 함
        public string audioAddressableKey;  // 예: "audio_0001"
        public string bgaAddressableKey;    // 예: "bga_0001"

        public Sprite backgroundArt;    // 게임 배경 아트 (BGA 없거나 꺼진 경우 표시)
        public Sprite albumArt;

    }
}
