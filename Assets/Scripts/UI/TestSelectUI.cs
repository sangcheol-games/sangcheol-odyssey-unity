using UnityEngine;
using UnityEngine.SceneManagement;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    /// <summary>
    /// 테스트용 곡 선택 UI - 난이도만 선택 가능
    /// </summary>
    public class TestSelectUI : BaseUI
    {
        [Header("Test Music")]
        public MusicSO testMusicSO;

        private Difficulty currentDifficulty = Difficulty.Normal;

        private enum Texts
        {
            DifficultyText
        }

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            BindText(typeof(Texts));

            currentDifficulty = Difficulty.Normal;
            UpdateUI();
        }

        private void UpdateUI()
        {
            GetText((int)Texts.DifficultyText).text = currentDifficulty.ToString().ToUpper();
            GetText((int)Texts.DifficultyText).color = GetDifficultyColor(currentDifficulty);
        }

        private Color GetDifficultyColor(Difficulty difficulty)
        {
            return difficulty switch
            {
                Difficulty.Easy => Color.green,
                Difficulty.Normal => Color.cyan,
                Difficulty.Hard => Color.yellow,
                Difficulty.Extreme => Color.red,
                _ => Color.white
            };
        }

        protected override void HandleSelect(Vector2 direction)
        {
            if (direction.x > 0) // 오른쪽
            {
                if (currentDifficulty < Difficulty.Extreme)
                {
                    currentDifficulty++;
                    UpdateUI();
                }
            }
            else if (direction.x < 0) // 왼쪽
            {
                if (currentDifficulty > Difficulty.Easy)
                {
                    currentDifficulty--;
                    UpdateUI();
                }
            }
        }

        protected override void HandleSubmit()
        {
            if (testMusicSO == null)
            {
                Debug.LogError("[TestSelectUI] testMusicSO is null!");
                return;
            }

            if (ServiceLocator.TryGet<IMusicManager>(out var musicManager))
            {
                musicManager.SelectMusic(testMusicSO);
                SceneManager.LoadScene("GameScene");
            }
            else
            {
                Debug.LogError("[TestSelectUI] IMusicManager not found!");
            }
        }

        protected override void HandleCancel()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }
    }
}
