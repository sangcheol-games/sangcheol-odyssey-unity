using System;
using System.IO;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;
using UnityEngine;

namespace SCOdyssey.App
{
    [Serializable]
    public class MusicScoreEntry
    {
        public string title;
        public Difficulty difficulty;
        public int bestScore;
    }

    [Serializable]
    public class SaveData
    {
        public List<MusicScoreEntry> records = new();
    }

    public static class ScoreSaveSystem
    {
        public static string SavePath =>
            Path.Combine(Application.persistentDataPath, "scores.json");

        public static void Save(Dictionary<(string, Difficulty), int> dict)
        {
            var data = FromDict(dict);
            var json = JsonUtility.ToJson(data, prettyPrint: true);
           
            File.WriteAllText(SavePath, json);
        }

        private static SaveData FromDict(Dictionary<(string title, Difficulty difficulty), int> dict)
        {
            var data = new SaveData();

            foreach(var (key, score) in dict)
            {
                data.records.Add(new MusicScoreEntry
                {
                    title = key.title,
                    difficulty = key.difficulty,
                    bestScore = score
                });
            }

            return data;
        }

        public static Dictionary<(string, Difficulty), int> Load()
        {
            if (!File.Exists(SavePath)) return new Dictionary<(string, Difficulty), int>();

            var json = File.ReadAllText(SavePath);
            var data = JsonUtility.FromJson<SaveData>(json);
            return ToDict(data);
        }

        private static Dictionary<(string, Difficulty), int> ToDict(SaveData data)
        {
            var dict = new Dictionary<(string title, Difficulty diffuculty), int>();

            foreach(var record in data.records)
            {
                dict.Add((record.title, record.difficulty), record.bestScore);
            }

            return dict;
        }
    }
}
