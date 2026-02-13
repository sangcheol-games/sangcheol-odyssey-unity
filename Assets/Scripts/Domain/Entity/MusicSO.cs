using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;
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

        public TextAsset chartFile;
        public AudioClip musicFile;
        public VideoClip videoFile;
        public Sprite albumArt;

    }
}
