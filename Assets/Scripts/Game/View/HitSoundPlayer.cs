using System;
using SCOdyssey.Audio;
using SCOdyssey.Rhythm;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 등급별 타격음. 버스(NoteJudged)를 구독해 적중마다 그 등급의 소리를 내고, miss는 조용하다.
    // 헛침(칠 노트가 없는 누름)은 판정이 없으므로 GameManager가 PlayWhiff()로 Umm 소리를 낸다.
    // 이펙트 스폰(풀이 비면 Instantiate)보다 소리가 먼저 나도록 다른 NoteJudged 구독자보다 먼저 만든다.
    public sealed class HitSoundPlayer : IDisposable
    {
        // 인덱스 = (int)JudgeType (Perfect/Master/Ideal/Kind/Umm 순). StreamingAssets/HitSound/ 기준.
        // 소리를 바꾸려면 같은 이름으로 WAV를 덮어쓴다. AudioBuildValidator가 빌드 전에 파일 유무를 확인한다
        public static readonly string[] HitSoundFiles =
        {
            "hit_perfect.wav",
            "hit_master.wav",
            "hit_ideal.wav",
            "hit_kind.wav",
            "hit_umm.wav",
        };

        private readonly IJudgementBus _bus;
        private readonly IOneShotPlayer _oneShots;   // 없으면 조용히 무시한다
        private readonly OneShotId[] _ids = new OneShotId[HitSoundFiles.Length];

        public HitSoundPlayer(IJudgementBus bus, IOneShotPlayer oneShots)
        {
            _bus = bus;
            _oneShots = oneShots;

            // Register는 파일명 기준 멱등이라 다시 만들어도 재로드가 없다
            if (_oneShots != null)
            {
                for (int i = 0; i < HitSoundFiles.Length; i++)
                    _ids[i] = _oneShots.Register(HitSoundFiles[i]);
            }

            _bus.NoteJudged += OnNoteJudged;
        }

        public void Dispose()
        {
            _bus.NoteJudged -= OnNoteJudged;
        }

        // 칠 노트가 없는 누름. 등급 소리 중 Umm을 낸다
        public void PlayWhiff() => Play(JudgeType.Umm);

        private void OnNoteJudged(JudgeEvent e)
        {
            if (e.IsMiss) return;
            Play(e.Judge);
        }

        private void Play(JudgeType judge)
        {
            if (_oneShots == null) return;
            _oneShots.Play(_ids[(int)judge]);
        }
    }
}
