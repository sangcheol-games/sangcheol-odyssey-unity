using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.ChartEditor.UI
{
    /// <summary>
    /// 기본정보 팝업 - BPM 입력.
    /// 화면 중앙에 표시되는 모달 팝업.
    /// </summary>
    public class EditorInfoPopup : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        [Header("UI 요소")]
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

            // 초기 상태: 비활성화
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // 팝업 열릴 때 현재 BPM 값으로 초기화
            if (editorManager != null && bpmInput != null)
            {
                bpmInput.text = editorManager.ChartData.bpm.ToString();
                bpmInput.Select();
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
            if (int.TryParse(bpmInput.text, out int bpm) && bpm > 0 && bpm <= 999)
            {
                editorManager.ChartData.bpm = bpm;
                Debug.Log($"[EditorInfo] BPM set to {bpm}");
            }
            else
            {
                editorManager.ShowWarning("유효한 BPM 값을 입력해주세요. (1~999)");
                return;
            }

            gameObject.SetActive(false);
        }

        private void OnClickCancel()
        {
            gameObject.SetActive(false);
        }
    }
}
