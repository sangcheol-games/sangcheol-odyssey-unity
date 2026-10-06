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

        public void SpawnHit(JudgeType judge, Vector3 world)
        {
            Spawn(!ShowPerfect && judge == JudgeType.Perfect ? JudgeType.Master : judge, world);
        }

        public void Spawn(JudgeType type, Vector3 world)
        {
            GameObject effect = _pool.Get();
            effect.GetComponent<EffectController>().Setup(type, world, returned => _pool.Return(returned.gameObject));
        }
    }
}
