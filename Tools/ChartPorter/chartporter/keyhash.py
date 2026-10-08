"""결정적 의사난수: 키(곡·마디·위치·용도) → [0, 1). 같은 입력이면 항상 같은 값이라 출력이 결정적이고,
한 마디를 고쳐도 다른 마디의 선택이 바뀌지 않는다."""
from __future__ import annotations

import hashlib


def h(*xs) -> float:
    d = hashlib.sha1(":".join(str(x) for x in xs).encode()).digest()
    return int.from_bytes(d[:8], "big") / 2 ** 64
