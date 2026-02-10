using System.Collections;
using SCOdyssey.ChartEditor.Data;
using SCOdyssey.ChartEditor.IO;
using UnityEngine;
using UnityEngine.Networking;
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

            // 현재 BPM 유지하여 변환 (기본정보에서 BPM을 별도로 설정)
            int bpm = editorManager.ChartData.bpm;
            EditorChartData loaded = EditorChartConverter.FromChartText(chartText, bpm);
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

            // 코루틴은 editorManager에서 실행 (이 패널은 바로 비활성화되므로)
            editorManager.StartCoroutine(LoadAudioFile(path));
            gameObject.SetActive(false);
        }

        /// <summary>
        /// UnityWebRequest를 사용하여 외부 오디오 파일 로드
        /// </summary>
        private IEnumerator LoadAudioFile(string filePath)
        {
            // file:// 프로토콜로 로컬 파일 접근
            string url = "file:///" + filePath.Replace("\\", "/");

            AudioType audioType = GetAudioType(filePath);

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    clip.name = System.IO.Path.GetFileNameWithoutExtension(filePath);

                    editorManager.ChartData.audioClip = clip;

                    if (editorManager.audioSource != null)
                        editorManager.audioSource.clip = clip;

                    Debug.Log($"[EditorFile] Audio loaded: {clip.name} ({clip.length:F1}s)");
                }
                else
                {
                    editorManager.ShowWarning($"음원 파일 로드 실패: {request.error}");
                }
            }
        }

        private AudioType GetAudioType(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".wav" => AudioType.WAV,
                ".mp3" => AudioType.MPEG,
                ".ogg" => AudioType.OGGVORBIS,
                _ => AudioType.UNKNOWN
            };
        }
    }
}
