using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 텍스트 이펙트(PERFECT/MASTER/...)를 노트 자리에 띄운다. 재생이 끝나면 이펙트가 스스로 풀에 돌아온다
    public sealed class JudgeEffectSpawner
    {
        private readonly GameObjectPool _pool;

        public JudgeEffectSpawner(GameObjectPool pool)
        {
            _pool = pool;
        }

        // 꺼 두면 Perfect도 Master로 보여준다(설정)
        public bool ShowPerfect { get; set; }

        public void SpawnHit(JudgeType judge, NoteController at)
        {
            Spawn(!ShowPerfect && judge == JudgeType.Perfect ? JudgeType.Master : judge, at);
        }

        public void Spawn(JudgeType type, NoteController at)
        {
            GameObject effect = _pool.Get();
            effect.GetComponent<EffectController>().Setup(type,
                at.GetComponent<RectTransform>().anchoredPosition, returned => _pool.Return(returned.gameObject));
        }
    }
}
