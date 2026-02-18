using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    public class MusicListUI : MonoBehaviour
    {
        private TMP_Text titleText;
        private TMP_Text artistText;
        private TMP_Text[] levelTexts = new TMP_Text[4]; // Easy, Normal, Hard, Extreme
        private Image backgroundImage;

        // 난이도별 배경 이미지 (선택 강조용)
        private Image[] difficultyBgImages = new Image[4];

        private static readonly Color SELECTED_BG_COLOR = new Color(0.15f, 0.15f, 0.4f, 1f);
        private static readonly Color DEFAULT_BG_COLOR = new Color(0.047f, 0.047f, 0.192f, 1f);
        private static readonly Color SELECTED_DIFFICULTY_COLOR = Color.yellow;
        private static readonly Color DEFAULT_DIFFICULTY_COLOR = Color.white;

        public void Init()
        {
            titleText = FindText("Title");
            artistText = FindText("Artist");
            levelTexts[0] = FindText("Level_Easy");
            levelTexts[1] = FindText("Level_Normal");
            levelTexts[2] = FindText("Level_Hard");
            levelTexts[3] = FindText("Level_Extreme");
            backgroundImage = GetComponent<Image>();

            // 난이도 컨테이너의 배경 이미지
            difficultyBgImages[0] = FindImage("Easy");
            difficultyBgImages[1] = FindImage("Normal");
            difficultyBgImages[2] = FindImage("Hard");
            difficultyBgImages[3] = FindImage("Extreme");
        }

        /// <summary>
        /// 곡 데이터를 표시합니다.
        /// </summary>
        public void SetData(MusicSO music)
        {
            if (music == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            titleText.text = music.title.GetLocalizedString();
            artistText.text = music.producer.GetLocalizedString();

            // 4단계 난이도 레벨 표시
            for (int i = 0; i < 4; i++)
            {
                Difficulty diff = (Difficulty)i;
                if (music.level != null && music.level.ContainsKey(diff))
                    levelTexts[i].text = music.level[diff].ToString();
                else
                    levelTexts[i].text = "-";
            }
        }

        /// <summary>
        /// 선택 상태 및 난이도 하이라이트를 설정합니다.
        /// </summary>
        public void SetSelected(bool isSelected, Difficulty selectedDifficulty)
        {
            // 곡 선택 하이라이트
            if (backgroundImage != null)
                backgroundImage.color = isSelected ? SELECTED_BG_COLOR : DEFAULT_BG_COLOR;

            // 난이도 하이라이트
            for (int i = 0; i < 4; i++)
            {
                bool isDiffSelected = isSelected && (int)selectedDifficulty == i;

                if (levelTexts[i] != null)
                    levelTexts[i].color = isDiffSelected ? SELECTED_DIFFICULTY_COLOR : DEFAULT_DIFFICULTY_COLOR;

                if (difficultyBgImages[i] != null)
                    difficultyBgImages[i].color = isDiffSelected ? SELECTED_BG_COLOR : DEFAULT_BG_COLOR;
            }
        }

        private TMP_Text FindText(string name)
        {
            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.gameObject.name == name)
                    return text;
            }
            Debug.LogWarning($"[MusicListUI] TMP_Text '{name}' not found");
            return null;
        }

        private Image FindImage(string name)
        {
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject.name == name)
                    return img;
            }
            return null;
        }
    }
}
