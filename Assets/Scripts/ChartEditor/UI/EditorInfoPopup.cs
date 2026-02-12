using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.UI
{
    /// <summary>
    /// 기본정보 팝업 - 제목, 작곡가, 난이도, 레벨, BPM 입력.
    /// 화면 중앙에 표시되는 모달 팝업.
    /// </summary>
    public class EditorInfoPopup : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        [Header("UI 요소")]
        public TMP_InputField titleInput;
        public TMP_InputField artistInput;
        public TMP_Dropdown difficultyDropdown;
        public TMP_InputField levelInput;
        public TMP_InputField bpmInput;
        public Button btnOK;
        public Button btnCancel;
        public GameObject dimBackground;    // 반투명 배경 (클릭 차단)

        private void Start()
        {
            if (btnOK != null)
                btnOK.onClick.AddListener(OnClickOK);
            if (btnCancel != null)
                btnCancel.onClick.AddListener(OnClickCancel);

            // 난이도 드롭다운 옵션 설정
            InitDifficultyDropdown();

            // 초기 상태: 비활성화
            gameObject.SetActive(false);
        }

        private void InitDifficultyDropdown()
        {
            if (difficultyDropdown == null) return;

            difficultyDropdown.ClearOptions();
            string[] names = Enum.GetNames(typeof(Difficulty));
            difficultyDropdown.AddOptions(new System.Collections.Generic.List<string>(names));
        }

        private void OnEnable()
        {
            // 팝업 열릴 때 현재 값으로 초기화
            if (editorManager != null && editorManager.ChartData != null)
            {
                var data = editorManager.ChartData;

                if (titleInput != null)
                    titleInput.text = data.title;
                if (artistInput != null)
                    artistInput.text = data.artist;
                if (difficultyDropdown != null)
                    difficultyDropdown.value = (int)data.difficulty;
                if (levelInput != null)
                    levelInput.text = data.level.ToString();
                if (bpmInput != null)
                    bpmInput.text = data.bpm.ToString();

                // 첫 번째 입력 필드 포커스
                if (titleInput != null)
                    titleInput.Select();
            }

            if (dimBackground != null)
                dimBackground.SetActive(true);
        }

        private void OnDisable()
        {
            if (dimBackground != null)
                dimBackground.SetActive(false);
        }

        private void OnClickOK()
        {
            // BPM 유효성 검사
            if (!int.TryParse(bpmInput.text, out int bpm) || bpm <= 0 || bpm > 999)
            {
                editorManager.ShowWarning("유효한 BPM 값을 입력해주세요. (1~999)");
                return;
            }

            // 레벨 유효성 검사
            if (!int.TryParse(levelInput.text, out int level) || level < 1 || level > 99)
            {
                editorManager.ShowWarning("유효한 레벨 값을 입력해주세요. (1~99)");
                return;
            }

            // 값 적용
            var data = editorManager.ChartData;
            data.title = titleInput != null ? titleInput.text.Trim() : "";
            data.artist = artistInput != null ? artistInput.text.Trim() : "";
            data.difficulty = difficultyDropdown != null
                ? (Difficulty)difficultyDropdown.value
                : Difficulty.Normal;
            data.level = level;
            data.bpm = bpm;

            Debug.Log($"[EditorInfo] Info updated: {data.title} / {data.artist} / {data.difficulty} Lv.{data.level} / BPM {data.bpm}");
            gameObject.SetActive(false);
        }

        private void OnClickCancel()
        {
            gameObject.SetActive(false);
        }
    }
}
