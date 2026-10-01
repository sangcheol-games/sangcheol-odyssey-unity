using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SCOdyssey.Game
{
    // 배경 영상(BGA). GameManager가 Follow(session)로 곡 세션을 넘기면 매 프레임 곡 시각 - 음원 시작 곡 시각을
    // VideoPlayer의 외부 시간 기준(externalReferenceTime)으로 넣는다. 플레이어가 프레임을 건너뛰거나 반복해 스스로 맞추므로
    // 평소에는 seek하지 않는다. 곡 시계가 멈추면(일시정지) 영상도 멈춘다. 세션 이벤트는 구독하지 않는다(읽기만 한다).
    //
    // 예전에는 100ms 넘게 벌어질 때마다 time을 다시 맞췄는데, 영상 시계가 조금씩 늦어지는 데다 1080p 고비트레이트 영상의
    // 비키프레임 seek가 느려서 seek가 끝나기 전에 또 seek가 걸렸다. 그 결과 영상이 멈추거나 끊겼다.
    public class BGAController : MonoBehaviour
    {
        // 외부 시간 기준으로도 따라잡지 못할 만큼 벌어졌을 때(로딩 히치 등)만 seek한다.
        private const double ResyncThresholdSeconds = 1.0;

        [Header("참조")]
        public VideoPlayer videoPlayer;
        public RawImage bgaScreen;      // BGA 영상 표시용 RawImage
        public Image backgroundArt;     // 배경아트 스프라이트 표시용 Image (BGA 꺼진 경우에만 표시)
        public Image alphaOverlay;      // BGA 위에 올린 검정 Image (투명도 조절용)

        private bool isPrepared = false;
        private bool bgaEnabled = true;
        private ISongSession _session;

        // Init이 Prepare()까지 도달했는지. BGA 설정이 꺼져 있거나 파일명이 비었거나 파일이 없으면
        // Prepare 자체를 안 하므로, 이 플래그가 false면 WaitPreparedAsync는 기다릴 것이 없다.
        private bool _prepareStarted;

        // 준비를 기다리다 타임아웃해 이번 곡은 포기했는지. 늦게 도착한 prepareCompleted를 무시하는 데 쓴다.
        private bool _abandoned;

        // seek 진행 중. 끝나기 전에 다시 seek하면 영상이 계속 멈춰 있으므로 seekCompleted가 올 때까지 막는다.
        private bool _seeking;

        private void Awake()
        {
            if (videoPlayer != null) videoPlayer.seekCompleted += OnSeekCompleted;
        }

        private void OnDestroy()
        {
            if (videoPlayer != null) videoPlayer.seekCompleted -= OnSeekCompleted;
        }

        /// <summary>
        /// 배경 초기화. GameDataLoader에서 호출.
        /// </summary>
        public void Init(string videoFileName, Sprite backgroundArtSprite)
        {
            _prepareStarted = false;
            _abandoned = false;
            _seeking = false;
            isPrepared = false;

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
                // 곡 시각을 외부 시간 기준으로 따라가게 한다. skipOnDrop은 그 기준에 맞춰 프레임을 건너뛰어 따라잡는 데 필요하다.
                // (예전 false는 영상이 자기 내부 시계로 재생되던 레거시 방식에서 필요했던 값이다)
                videoPlayer.timeReference = VideoTimeReference.ExternalTime;
                videoPlayer.skipOnDrop = true;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.playOnAwake = false;

                _prepareStarted = true;
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
            _seeking = false;
            if (videoPlayer.isPlaying) videoPlayer.Stop();
        }

        /// <summary>
        /// 영상 준비가 끝날 때까지 기다린다. 로딩 화면이 이 대기를 가린다.
        /// 기다리지 않으면 295MB짜리 영상이 플레이 도중 갑자기 켜지면서 비키프레임 seek 히치가 난다.
        ///
        /// Prepare가 시작되지도 않았으면(BGA 설정 off / 파일명 없음 / 파일 없음) 즉시 반환한다.
        /// 그러지 않으면 BGA가 없는 곡마다 타임아웃만큼 헛되이 기다리게 된다.
        /// </summary>
        /// <returns>영상을 쓸 수 있으면 true. 타임아웃해 배경아트로 진행하면 false.</returns>
        public async UniTask<bool> WaitPreparedAsync(float timeoutSeconds, CancellationToken ct)
        {
            if (!_prepareStarted || isPrepared) return true;

            float startedAt = Time.realtimeSinceStartup;
            while (!isPrepared)
            {
                if (Time.realtimeSinceStartup - startedAt > timeoutSeconds)
                {
                    // 포기. 늦게 도착할 prepareCompleted는 OnPrepared에서 무시된다.
                    _abandoned = true;
                    bgaScreen.enabled = false;
                    if (backgroundArt != null)
                        backgroundArt.gameObject.SetActive(backgroundArt.sprite != null);

                    Debug.LogWarning($"[BGAController] 영상 준비가 {timeoutSeconds}초를 넘겨 이번 곡은 배경아트로 진행합니다.");
                    return false;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            return true;
        }

        private void OnPrepared(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared;

            // 타임아웃으로 포기한 뒤 늦게 도착한 준비 완료. 여기서 켜면 플레이 도중 영상이 갑툭튀한다.
            if (_abandoned) return;

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

            videoPlayer.externalReferenceTime = videoTime;

            // 시작·일시정지 재개: 위치를 한 번 맞추고 재생한다.
            if (!videoPlayer.isPlaying)
            {
                SeekTo(videoTime);
                videoPlayer.Play();
                bgaScreen.enabled = true;
                return;
            }

            if (_seeking) return;

            double drift = videoPlayer.time - videoTime;
            if (Math.Abs(drift) > ResyncThresholdSeconds)
            {
                Debug.LogWarning($"[BGAController] 영상이 곡 시각과 {drift:F2}초 어긋나 위치를 다시 맞춥니다.");
                SeekTo(videoTime);
            }
        }

        private void SeekTo(double videoTime)
        {
            _seeking = true;
            videoPlayer.time = videoTime;
        }

        private void OnSeekCompleted(VideoPlayer vp)
        {
            _seeking = false;
        }
    }
}
