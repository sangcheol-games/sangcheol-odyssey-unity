using System;
using System.IO;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SCOdyssey.Game
{
    // 배경 영상(BGA). GameManager가 Follow(session)로 곡 세션을 넘기면 매 프레임 곡 시각 - 음원 시작 곡 시각으로
    // 영상 위치를 정한다. 곡 시계가 멈추면(일시정지) 영상도 멈추고, 100ms 넘게 벌어질 때만 위치를 다시 맞춘다.
    // 세션 이벤트는 구독하지 않는다(읽기만 한다).
    public class BGAController : MonoBehaviour
    {
        private const double ResyncThresholdSeconds = 0.1;

        [Header("참조")]
        public VideoPlayer videoPlayer;
        public RawImage bgaScreen;      // BGA 영상 표시용 RawImage
        public Image backgroundArt;     // 배경아트 스프라이트 표시용 Image (BGA 꺼진 경우에만 표시)
        public Image alphaOverlay;      // BGA 위에 올린 검정 Image (투명도 조절용)

        private bool isPrepared = false;
        private bool bgaEnabled = true;
        private ISongSession _session;

        /// <summary>
        /// 배경 초기화. GameDataLoader에서 호출.
        /// </summary>
        public void Init(string videoFileName, Sprite backgroundArtSprite)
        {
            // backgroundArt 스프라이트 설정 (BGA 꺼진 경우를 위해 스프라이트는 항상 세팅)
            if (backgroundArt != null)
            {
                backgroundArt.sprite = backgroundArtSprite;
            }

            // 저장된 BGA 투명도 적용
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                SetOpacity(settingsManager.Current.bgaOpacity);

            if (bgaEnabled)
            {
                // BGA 활성화 상태: backgroundArt는 표시하지 않음
                if (backgroundArt != null) backgroundArt.gameObject.SetActive(false);

                if (string.IsNullOrEmpty(videoFileName))
                {
                    bgaScreen.enabled = false;
                    return;
                }

                string path = Path.Combine(Application.streamingAssetsPath, "BGA", videoFileName);

                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[BGAController] 영상 파일을 찾을 수 없음: {path}");
                    bgaScreen.enabled = false;
                    return;
                }

                bgaScreen.enabled = false; // Prepare 완료 전까지 숨김 (VideoPlayer는 활성 유지)

                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = path;
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                videoPlayer.skipOnDrop = false;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.playOnAwake = false;

                videoPlayer.prepareCompleted += OnPrepared;
                videoPlayer.Prepare();
            }
            else
            {
                // BGA 비활성화 상태: bgaScreen 숨기고 backgroundArt만 표시
                bgaScreen.enabled = false;
                if (backgroundArt != null)
                    backgroundArt.gameObject.SetActive(backgroundArtSprite != null);
            }
        }

        /// <summary>
        /// 곡 시계 모드로 전환한다. GameManager가 곡 세션을 얻은 뒤 호출. Stop()이나 null로 푼다.
        /// </summary>
        public void Follow(ISongSession session)
        {
            _session = session;
        }

        /// <summary>
        /// BGA 투명도 설정. 1 = BGA 완전 표시 (overlay 투명), 0 = BGA 완전 차단 (overlay 불투명 검정).
        /// </summary>
        public void SetOpacity(float opacity)
        {
            if (alphaOverlay == null) return;
            var c = alphaOverlay.color;
            c.a = 1f - Mathf.Clamp01(opacity);
            alphaOverlay.color = c;
        }

        /// <summary>
        /// BGA 켜기/끄기. 미래 곡 선택화면 토글 연동용.
        /// </summary>
        public void SetBGAEnabled(bool enabled)
        {
            bgaEnabled = enabled;

            if (!enabled)
            {
                if (videoPlayer.isPlaying) videoPlayer.Stop();
                bgaScreen.enabled = false;
                if (backgroundArt != null && backgroundArt.sprite != null)
                    backgroundArt.gameObject.SetActive(true);
            }
            else
            {
                if (backgroundArt != null) backgroundArt.gameObject.SetActive(false);
                if (isPrepared) bgaScreen.enabled = true;
            }
        }

        /// <summary>
        /// 게임 종료 시 정지. GameManager.OnGameFinished()에서 호출.
        /// </summary>
        public void Stop()
        {
            _session = null;
            if (videoPlayer.isPlaying) videoPlayer.Stop();
        }

        private void OnPrepared(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared;
            isPrepared = true;

            // RenderTexture를 RawImage에 연결
            bgaScreen.texture = vp.texture;
        }

        private void Update()
        {
            if (_session != null) FollowSongClock();
        }

        private void FollowSongClock()
        {
            if (!isPrepared || !bgaEnabled) return;
            if (_session.State == SongSessionState.Disposed || _session.State == SongSessionState.Stopped)
            {
                Stop();
                return;
            }

            SongFrame frame = _session.Clock.Frame;
            double videoTime = frame.SongTime - _session.AudioStartSongTime;
            bool inRange = videoTime >= 0 && (videoPlayer.length <= 0 || videoTime < videoPlayer.length);
            if (!frame.IsRunning || !inRange)
            {
                if (videoPlayer.isPlaying) videoPlayer.Pause();
                return;
            }

            if (!videoPlayer.isPlaying)
            {
                videoPlayer.time = videoTime;
                videoPlayer.Play();
                bgaScreen.enabled = true;
                return;
            }
            if (Math.Abs(videoPlayer.time - videoTime) > ResyncThresholdSeconds) videoPlayer.time = videoTime;
        }
    }
}
