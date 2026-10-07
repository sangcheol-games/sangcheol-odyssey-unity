using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 텍스트 이펙트(PERFECT/MASTER/...)를 노트 자리에 실제 등급 그대로 띄운다. 재생이 끝나면 이펙트가 스스로 풀에 돌아온다
    public sealed class JudgeEffectSpawner
    {
        private readonly GameObjectPool _pool;

        public JudgeEffectSpawner(GameObjectPool pool)
        {
            _pool = pool;
        }

        public void Spawn(JudgeType type, Vector3 world)
        {
            GameObject effect = _pool.Get();
            effect.GetComponent<EffectController>().Setup(type, world, returned => _pool.Return(returned.gameObject));
        }
    }
}
