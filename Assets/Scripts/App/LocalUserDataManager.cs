using System;
using System.Collections.Generic;
using System.IO;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // ── 흐름 (임시 로컬 최고기록) ──────────────────────────────────────────
    //
    //  서버 연동 전까지 쓰는 임시 구현. 서버가 생기면 IUserDataManager의 서버 구현으로 교체한다.
    //
    //  부팅: Managers가 Load()를 한 번 호출해 persistentDataPath/records.json을 메모리 캐시에 올린다.
    //        이후 조회(AdventureUI)는 캐시에서만 읽는다.
    //
    //  저장: GameManager.OnGameFinished가 SubmitResult()로 이번 판 결과를 넘긴다.
    //        네 항목을 각각 비교해 하나라도 갱신되면 파일에 쓴다.
    //        중도 이탈·재시작한 판은 OnGameFinished를 거치지 않으므로 기록되지 않는다.
    // ──────────────────────────────────────────────────────────────────────────
    public class LocalUserDataManager : IUserDataManager
    {
        private const string FILE_NAME = "records.json";
        private const string CORRUPT_FILE_NAME = "records.corrupt.json";

        private readonly Dictionary<(int musicId, Difficulty difficulty), UserMusicRecord> _records
            = new Dictionary<(int musicId, Difficulty difficulty), UserMusicRecord>();

        private static string SavePath => Path.Combine(Application.persistentDataPath, FILE_NAME);
        private static string CorruptPath => Path.Combine(Application.persistentDataPath, CORRUPT_FILE_NAME);


        public void Load()
        {
            _records.Clear();

            if (!File.Exists(SavePath)) return;

            UserRecordData data = null;
            try
            {
                var json = File.ReadAllText(SavePath);
                data = JsonAdapter.FromJson<UserRecordData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UserData] 기록 파일을 읽지 못했습니다: " + e.Message);
            }

            if (data == null || data.records == null)
            {
                // 손상된 원문은 남겨 두고 빈 기록으로 시작한다. 다음 저장이 원래 파일을 덮어쓴다.
                BackupCorruptFile();
                return;
            }

            foreach (var record in data.records)
            {
                if (record == null) continue;
                Merge(record.musicId, record.difficulty, record.bestScore, record.bestCombo, record.bestRate, record.bestClearType);
            }
        }

        public bool TryGetRecord(int musicId, Difficulty difficulty, out UserMusicRecord record)
        {
            return _records.TryGetValue((musicId, difficulty), out record);
        }

        public bool SubmitResult(int musicId, Difficulty difficulty, int score, int maxCombo, float rate, ClearType clearType)
        {
            bool isUpdated = Merge(musicId, difficulty, score, maxCombo, rate, clearType);
            if (isUpdated)
            {
                Save();
            }
            return isUpdated;
        }

        // 항목별로 더 높은 값만 남긴다. 처음 보는 키면 그대로 추가한다. 하나라도 바뀌면 true.
        // (Load 시 중복 키가 있어도 이 경로로 합쳐진다)
        private bool Merge(int musicId, Difficulty difficulty, int score, int maxCombo, float rate, ClearType clearType)
        {
            var key = (musicId, difficulty);

            if (!_records.TryGetValue(key, out var record))
            {
                _records[key] = new UserMusicRecord
                {
                    musicId = musicId,
                    difficulty = difficulty,
                    bestScore = score,
                    bestCombo = maxCombo,
                    bestRate = rate,
                    bestClearType = clearType
                };
                return true;
            }

            bool isUpdated = false;

            if (score > record.bestScore)
            {
                record.bestScore = score;
                isUpdated = true;
            }

            if (maxCombo > record.bestCombo)
            {
                record.bestCombo = maxCombo;
                isUpdated = true;
            }

            if (rate > record.bestRate)
            {
                record.bestRate = rate;
                isUpdated = true;
            }

            if (clearType > record.bestClearType)
            {
                record.bestClearType = clearType;
                isUpdated = true;
            }

            return isUpdated;
        }

        private void Save()
        {
            var data = new UserRecordData();
            foreach (var record in _records.Values)
            {
                data.records.Add(record);
            }

            // 임시 파일에 다 쓴 뒤 교체한다. 쓰는 도중 꺼져도 기존 기록 파일은 온전히 남는다.
            var tempPath = SavePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonAdapter.ToJson(data, true));

                if (File.Exists(SavePath))
                {
                    File.Replace(tempPath, SavePath, null);
                }
                else
                {
                    File.Move(tempPath, SavePath);
                }
            }
            catch (Exception e)
            {
                // 저장 실패가 게임 흐름(결과 화면 진입)을 막으면 안 된다. 이번 기록은 메모리에만 남는다.
                Debug.LogError("[UserData] 기록을 저장하지 못했습니다: " + e.Message);
            }
        }

        private void BackupCorruptFile()
        {
            try
            {
                File.Copy(SavePath, CorruptPath, true);
                Debug.LogWarning("[UserData] 기록 파일이 손상되어 빈 기록으로 시작합니다. 원문: " + CorruptPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UserData] 손상된 기록 파일을 백업하지 못했습니다: " + e.Message);
            }
        }
    }
}
