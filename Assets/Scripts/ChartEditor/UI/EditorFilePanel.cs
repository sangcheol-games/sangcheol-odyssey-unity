using SCOdyssey.ChartEditor.Data;
using SCOdyssey.ChartEditor.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.ChartEditor.UI
{
    /// <summary>
    /// 파일 드롭다운 패널.
    /// 새로만들기, 불러오기, 저장, 다른이름으로 저장, 음원파일 불러오기.
    /// </summary>
    public class EditorFilePanel : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        [Header("버튼")]
        public Button btnNew;
        public Button btnLoad;
        public Button btnSave;
        public Button btnSaveAs;
        public Button btnLoadAudio;

        private void Start()
        {
            if (btnNew != null)
                btnNew.onClick.AddListener(OnClickNew);
            if (btnLoad != null)
                btnLoad.onClick.AddListener(OnClickLoad);
            if (btnSave != null)
                btnSave.onClick.AddListener(OnClickSave);
            if (btnSaveAs != null)
                btnSaveAs.onClick.AddListener(OnClickSaveAs);
            if (btnLoadAudio != null)
                btnLoadAudio.onClick.AddListener(OnClickLoadAudio);

            gameObject.SetActive(false);
        }

        private void OnClickNew()
        {
            editorManager.ChartData.Clear();
            editorManager.LoadBar(0);
            gameObject.SetActive(false);
            Debug.Log("[EditorFile] New chart created");
        }

        private void OnClickLoad()
        {
            string path = ChartFileIO.ShowOpenDialog();
            if (path == null)
            {
                gameObject.SetActive(false);
                return;
            }

            string chartText = ChartFileIO.LoadFromFile(path);
            if (chartText == null)
            {
                editorManager.ShowWarning("채보 파일을 읽을 수 없습니다.");
                gameObject.SetActive(false);
                return;
            }

            // 파일 헤더의 BPM을 그대로 사용
            EditorChartData loaded = EditorChartConverter.FromChartText(chartText);
            loaded.filePath = path;

            // 에디터 데이터 교체
            editorManager.ReplaceChartData(loaded);
            editorManager.LoadBar(0);

            gameObject.SetActive(false);
            Debug.Log($"[EditorFile] Chart loaded from: {path}");
        }

        private void OnClickSave()
        {
            var chartData = editorManager.ChartData;

            // 기존 경로가 있으면 덮어쓰기, 없으면 다른이름으로 저장
            if (string.IsNullOrEmpty(chartData.filePath))
            {
                OnClickSaveAs();
                return;
            }

            SaveToPath(chartData.filePath);
            gameObject.SetActive(false);
        }

        private void OnClickSaveAs()
        {
            string path = ChartFileIO.ShowSaveDialog();
            if (path == null)
            {
                gameObject.SetActive(false);
                return;
            }

            SaveToPath(path);
            editorManager.ChartData.filePath = path;
            gameObject.SetActive(false);
        }

        private void SaveToPath(string path)
        {
            string chartText = EditorChartConverter.ToChartText(editorManager.ChartData);
            if (ChartFileIO.SaveToFile(path, chartText))
            {
                Debug.Log($"[EditorFile] Chart saved to: {path}");
            }
            else
            {
                editorManager.ShowWarning("저장에 실패했습니다.");
            }
        }

        private void OnClickLoadAudio()
        {
            string path = ChartFileIO.ShowAudioOpenDialog();
            if (path == null)
            {
                gameObject.SetActive(false);
                return;
            }

            // FMOD 비동기 로드 시작 (Update에서 IsLoaded 폴링)
            editorManager.ChartData.audioFilePath = path;
            editorManager.fmodAudio?.LoadAudio(path);

            gameObject.SetActive(false);
            Debug.Log($"[EditorFile] Audio loading started: {path}");
        }
    }
}
