using System;
using System.Collections.Generic;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    public class ScoreManager : MonoBehaviour
    {
        [Header("Settings")]
        private const int MAX_SCORE = 1000000;

        // 런타임 데이터
        private float scorePerNote;
        private float currentScore;  // 현재 점수 (표기 점수)
        private float exScore;    // Perfect 보너스
        private float theoreticalScore; // 이론치
        
        private int currentCombo;
        private int maxCombo;
        private int totalNoteCount;
        
        // UI 갱신을 위한 이벤트
        public event Action<int> OnScoreChanged;      // 점수 (int 표기)
        public event Action<float> OnGaugeChanged;    // 게이지 (0.0 ~ 100.0)
        public event Action<int> OnComboChanged;      // 콤보

        Dictionary<JudgeType, int> judgeCounts = new Dictionary<JudgeType, int>()
        {
            { JudgeType.Perfect, 0 },
            { JudgeType.Master, 0 },
            { JudgeType.Ideal, 0 },
            { JudgeType.Kind, 0 },
            { JudgeType.Uhm, 0 }
        };

        public void Init(int totalNotes)
        {
            totalNoteCount = totalNotes;
            if (totalNoteCount > 0)
            {
                scorePerNote = MAX_SCORE / totalNoteCount;
            }
            else
            {
                Debug.LogError("Total note count is zero. Score per note set to zero.");
                scorePerNote = 0f;
            }

            currentScore = 0f;
            exScore = 0f;
            theoreticalScore = 0f;
            currentCombo = 0;
            maxCombo = 0;

            // 초기 UI 갱신
            UpdateUI();
        }

        public void ProcessJudge(JudgeType type)
        {
            float multiplier = 0f;
            bool comboBreak = false;

            switch (type)
            {
                case JudgeType.Perfect:
                    multiplier = 1.0f;   // 기본 100%
                    comboBreak = false;
                    break;

                case JudgeType.Master:
                    multiplier = 1.0f;
                    comboBreak = false;
                    break;

                case JudgeType.Ideal:
                    multiplier = 0.7f;
                    comboBreak = false;
                    break;

                case JudgeType.Kind:
                    multiplier = 0.5f;
                    comboBreak = true;
                    break;

                case JudgeType.Uhm:
                    multiplier = 0.0f;
                    comboBreak = true;
                    break;
            }

            // 1. 점수 계산
            currentScore += scorePerNote * multiplier;
            exScore += (type == JudgeType.Perfect) ? scorePerNote * 0.2f : 0f;
            theoreticalScore += scorePerNote * 1.0f;


            // 3. 콤보 처리
            if (comboBreak)
            {
                currentCombo = 0;
            }
            else
            {
                currentCombo++;
                if (currentCombo > maxCombo) maxCombo = currentCombo;
            }

            Debug.Log($"Score: {currentScore}, Combo: {currentCombo}");

            UpdateUI();
        }

        private void UpdateUI()
        {
            OnScoreChanged?.Invoke((int)currentScore);
            OnComboChanged?.Invoke(currentCombo);

            float gaugePercent = 100f;
            if (theoreticalScore > 0)
            {
                float ratio = currentScore / theoreticalScore;
                gaugePercent = ratio * 100f;
            }
            
            OnGaugeChanged?.Invoke(gaugePercent);
        }

        // 게임 종료 시 최종 점수 계산 (All Master 체크)
        public int GetFinalScore()
        {
            float finalScore = currentScore;

            bool isOverMillion = false;
            if (judgeCounts[JudgeType.Master] + judgeCounts[JudgeType.Perfect] == totalNoteCount)
            {
                isOverMillion = true;
            }

            if (isOverMillion)
            {
                finalScore = MAX_SCORE;
                finalScore += exScore;
                Debug.Log("Over Million Bonus Applied! (Perfect ExScore Added)");
            }

            return (int)finalScore;
        }
    }
}