using SCOdyssey.ChartEditor.Preview;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.ChartEditor.UI
{
    /// <summary>
    /// 재생 드롭다운 패널.
    /// 재생(단일마디), 부분재생(3마디), 전체재생, 일시정지 모드 선택.
    /// </summary>
    public class EditorPlayPanel : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;
        public EditorPreviewManager previewManager;

        [Header("버튼")]
        public Button btnPlaySingle;    // 현재 1마디 재생
        public Button btnPlayPartial;   // 부분재생 (3마디)
        public Button btnPlayFull;      // 전체재생
        public Button btnStop;          // 정지

        private void Start()
        {
            if (btnPlaySingle != null)
                btnPlaySingle.onClick.AddListener(OnClickPlaySingle);
            if (btnPlayPartial != null)
                btnPlayPartial.onClick.AddListener(OnClickPlayPartial);
            if (btnPlayFull != null)
                btnPlayFull.onClick.AddListener(OnClickPlayFull);
            if (btnStop != null)
                btnStop.onClick.AddListener(OnClickStop);

            gameObject.SetActive(false);
        }

        private void OnClickPlaySingle()
        {
            if (!ValidateReferences()) return;
            previewManager.PlaySingle(editorManager.State.currentBar);
            gameObject.SetActive(false);
        }

        private void OnClickPlayPartial()
        {
            if (!ValidateReferences()) return;
            previewManager.PlayPartial(editorManager.State.currentBar);
            gameObject.SetActive(false);
        }

        private void OnClickPlayFull()
        {
            if (!ValidateReferences()) return;
            previewManager.PlayFull();
            gameObject.SetActive(false);
        }

        private void OnClickStop()
        {
            if (!ValidateReferences()) return;
            previewManager.StopPreview();
            gameObject.SetActive(false);
        }

        private bool ValidateReferences()
        {
            if (previewManager == null)
            {
                Debug.LogWarning("[EditorPlayPanel] previewManager가 연결되지 않았습니다!");
                return false;
            }
            if (editorManager == null)
            {
                Debug.LogWarning("[EditorPlayPanel] editorManager가 연결되지 않았습니다!");
                return false;
            }
            return true;
        }
    }
}
