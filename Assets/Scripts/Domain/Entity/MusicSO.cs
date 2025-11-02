using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Domain.Entity
{
    [CreateAssetMenu(menuName = "MusicData/MusicSO", fileName ="MusicSO_")]
    public class MusicSO : ScriptableObject
    {
        public int id;

        [Header("Music Info")]
        public Dictionary<Language, string> title;
        public Dictionary<Language, string> producer;
        public Dictionary<Language, string> vocal;
        public string illustrator;
        public string animator;
        public int bpm;

        [Header("Game Info")]
        public Dictionary<Difficulty, int> level;

        [Header("Resource Info")]

        public string chartFile;
        public AudioClip musicFile;
        public VideoClip videoFile;
        public Sprite albumArt;

    }
}
