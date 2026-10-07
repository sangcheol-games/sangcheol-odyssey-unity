using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    public class MusicListUI : BaseUI
    {
        private enum Texts
        {
            Title,
            Artist
        }

        private enum DiffImages  // 난이도별 최고기록 클리어 타입 아이콘
        {
            Easy,
            Normal,
            Hard,
            Extreme
        }

        // 곡 패널. 선택된 곡만 selectedPanelSprite로 교체(PauseUI 버튼과 같은 방식)
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite selectedPanelSprite;

        // 클리어 타입 별 아이콘. ClearType enum 순서(Fail, Clear, FullCombo, OverMillion, AllPerfect)와 인덱스가 일치해야 함
        // Fail 칸에는 회색 별(None)을 넣는다. 기록이 없는 난이도도 Fail 칸을 쓴다.
        [SerializeField] private Sprite[] clearTypeSprites;

        // 준비되지 않은 난이도(레벨 -1) 칸에 별 대신 넣는 표시(MusicListUI_ClearType_Null)
        [SerializeField] private Sprite unavailableSprite;

        private Image backgroundImage;  // 루트 배경 이미지

        protected override void Awake()
        {
            base.Awake();
            BindText(typeof(Texts));
            BindImage(typeof(DiffImages));
            backgroundImage = GetComponent<Image>();

            WarnIfSpritesMissing();
        }

        // 슬롯은 입력을 직접 처리하지 않음 - AdventureUI가 담당
        protected override void OnEnable() { }
        protected override void OnDisable() { }
        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() { }
        protected override void HandleCancel() { }

        /// <summary>
        /// 곡 데이터 및 선택 상태를 표시합니다.
        /// </summary>
        public void SetData(MusicSO music, bool isSelected = false)
        {
            if (music == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            GetText((int)Texts.Title).text = LocalizedTextUtil.Get(music.title, music.name);
            GetText((int)Texts.Artist).text = LocalizedTextUtil.Get(music.producer);

            // 곡 선택 하이라이트
            SetPanelSprite(isSelected);

            // 4단계 난이도별 최고기록 클리어 타입 표시. 레벨 숫자와 선택 난이도 표시는 AdventureUI 몫이다
            ServiceLocator.TryGet(out IUserDataManager userDataManager);
            for (int i = 0; i < 4; i++)
            {
                Difficulty diff = (Difficulty)i;
                int lv = 0;
                bool isAvailable = music.level != null && music.level.TryGetValue(diff, out lv) && lv != -1;

                Image clearTypeIcon = GetImage(i);
                if (clearTypeIcon == null) continue;

                // 준비되지 않은 난이도는 별 대신 Null 표시를 넣는다. 회색 별은 '기록 없음·Fail'로만 읽히게 한다
                if (!isAvailable)
                {
                    SetUnavailableSprite(clearTypeIcon);
                    continue;
                }

                ClearType clearType = ClearType.Fail;
                if (userDataManager != null && userDataManager.TryGetRecord(music.id, diff, out var record))
                {
                    clearType = record.bestClearType;
                }

                SetClearTypeSprite(clearTypeIcon, clearType);
            }
        }

        /// <summary>
        /// 선택 여부에 맞는 패널 스프라이트로 교체합니다. 지정되지 않았으면 프리팹 그림을 그대로 둡니다.
        /// </summary>
        private void SetPanelSprite(bool isSelected)
        {
            if (backgroundImage == null) return;

            Sprite panel;
            if (isSelected)
                panel = selectedPanelSprite;
            else
                panel = panelSprite;

            if (panel != null)
                backgroundImage.sprite = panel;
        }

        /// <summary>
        /// 클리어 타입에 맞는 별 아이콘으로 교체합니다(ResultUI의 등급 도장과 같은 방식).
        /// 배열이 덜 채워졌으면 지금 그림을 유지합니다. 경고는 Awake에서 한 번만 남깁니다.
        /// </summary>
        private void SetClearTypeSprite(Image icon, ClearType clearType)
        {
            int index = (int)clearType;
            if (clearTypeSprites == null || index >= clearTypeSprites.Length || clearTypeSprites[index] == null) return;

            icon.sprite = clearTypeSprites[index];
        }

        /// <summary>
        /// 준비되지 않은 난이도 표시로 교체합니다. 지정되지 않았으면 지금 그림을 유지합니다.
        /// </summary>
        private void SetUnavailableSprite(Image icon)
        {
            if (unavailableSprite == null) return;

            icon.sprite = unavailableSprite;
        }

        // SetData는 입력마다 슬롯 전부에 대해 돌기 때문에 거기서 경고하면 로그가 쏟아진다. 인스펙터 누락은 생성 시 한 번만 알린다.
        private void WarnIfSpritesMissing()
        {
            if (panelSprite == null || selectedPanelSprite == null)
            {
                Debug.LogWarning("[MusicListUI] 패널 스프라이트가 지정되지 않았습니다.");
            }

            int clearTypeCount = System.Enum.GetValues(typeof(ClearType)).Length;
            if (clearTypeSprites == null || clearTypeSprites.Length != clearTypeCount)
            {
                Debug.LogWarning($"[MusicListUI] 클리어 타입 스프라이트는 ClearType 순서대로 {clearTypeCount}개가 필요합니다.");
            }

            if (unavailableSprite == null)
            {
                Debug.LogWarning("[MusicListUI] 준비되지 않은 난이도 스프라이트(ClearType_Null)가 지정되지 않았습니다.");
            }
        }
    }
}
