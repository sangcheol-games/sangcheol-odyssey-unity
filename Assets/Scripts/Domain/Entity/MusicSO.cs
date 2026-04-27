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
        public string audioFilePath;    // StreamingAssets 기준 상대 경로 (예: "Music/song_0001.ogg")
        public string previewAudioFilePath;
        public string videoFileName;    // StreamingAssets/BGA/ 폴더 내 파일명 (예: "BGA_0001.mp4")
        public Sprite backgroundArt;    // 게임 배경 아트 (BGA 없거나 꺼진 경우 표시)
        public Sprite albumArt;

    }
}
