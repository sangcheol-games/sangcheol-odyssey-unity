using System.Collections.Generic;

namespace SCOdyssey.App
{
    // UI 네비게이션의 순수 로직만 담는 자료구조로 분리(UnityEngine 의존 없음).
    // 인스턴스 소유/생성/활성화/Canvas 등 Unity 관련은 UIManager가 담당한다
    public sealed class UIStack
    {
        private readonly struct Entry
        {
            public readonly string key;
            public readonly PushMode mode;
            public Entry(string key, PushMode mode) { this.key = key; this.mode = mode; }
        }

        // _history : 기록만 보관 (UI 키 + 진입 모드). 뒤로가기 경로를 기억한다.
        private readonly Stack<Entry> _history = new Stack<Entry>();

        // 씬 바닥 경계: 이 Depth 하위의 UI는 현재 씬에서 표시하지 않는다.
        private int _floor = 0;

        public int Count => _history.Count;

        // 현재 최상단 UI 키 (없으면 null)
        public string TopKey => _history.Count == 0 ? null : _history.Peek().key;

        // 새 UI 기록 추가. Replace면 현재 최상단을 같은 Depth로 교체한다
        public void Push(string key, PushMode mode)
        {
            if (mode == PushMode.Replace && _history.Count > 0)
            {
                _history.Pop();
            }
            _history.Push(new Entry(key, mode));
        }

        // 최상단 기록 제거
        public void Pop()
        {
            if (_history.Count > 0)
            {
                _history.Pop();
            }
        }

        public void SetFloor(int floor)
        {
            _floor = floor;
        }

        // 표시할 UI 키 목록(최상단 우선)을 반환.
        // 최상단부터 아래로, Overlay인 동안 그 아래까지 포함하고 Overlay가 아닌 첫 UI를 포함한 뒤 정지.
        // 단, 바닥 경계(_floor) 밑으로는 내려가지 않는다.
        public IReadOnlyList<string> GetVisibleKeys()
        {
            Entry[] arr = _history.ToArray();       // arr[0] = 최상단
            int aboveFloor = arr.Length - _floor;   // 바닥 경계 위(현재 씬 소유) 기록 수

            var keys = new List<string>();
            for (int i = 0; i < arr.Length && i < aboveFloor; i++)
            {
                keys.Add(arr[i].key);
                if (arr[i].mode != PushMode.Overlay) break; // base 도달 시 정지
            }
            return keys;
        }
    }
}
