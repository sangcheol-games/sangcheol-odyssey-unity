# CharacterAnimator 메커니즘 정리

## 관련 파일

| 파일 | 역할 |
|---|---|
| `CharacterAnimator.cs` | 상태 머신 + Root Y 제어 + 이벤트 구독 |
| `ICharacterAnimationHandler.cs` | 애니메이션 방식 추상화 인터페이스 |
| `SpriteSheetAnimationHandler.cs` | Unity Animator 구현체 |
| `SpineAnimationHandler.cs` | Spine 구현체 (stub) |
| `Constants.cs` | `CharacterState`, `NotePosition`, `AnimationType` enum |

---

## 전체 데이터 흐름

```
유저 입력
  └─ ChartManager.ApplyJudgment()
       ├─ gameManager.OnNoteJudged(type, pos)    ← NotePosition 포함
       ├─ gameManager.OnHoldStart(pos)           ← HoldStart 노트 판정 시
       └─ gameManager.OnHoldEnd(pos)             ← HoldRelease 노트 판정 시

GameManager
  └─ 이벤트 발화 (OnNoteJudgedEvent / OnHoldStartEvent / OnHoldEndEvent)

CharacterAnimator (구독)
  ├─ OnNoteJudgedHandler → OnNoteHit(pos)
  ├─ OnHoldStart(pos)    → UpdateHoldState()
  └─ OnHoldEnd(pos)      → UpdateHoldState()
```

---

## NotePosition 매핑 (ChartManager)

```
listIndex 0, 1  →  NotePosition.Bottom  (그룹 0, 아래 판정선)
listIndex 2, 3  →  NotePosition.Top     (그룹 1, 위 판정선)
```

`Middle`은 채보 이벤트로 직접 오지 않음.
`_isTopHolding && _isBottomHolding` 조건에서 `UpdateHoldState()` 내부가 결정.

---

## CharacterState 전체 목록 (14개)

| 상태 | 위치(Y) | Animator | 용도 |
|---|---|---|---|
| `Idle` | bottomY | 루프 | 기본 달리기 |
| `Hit0` ~ `Hit3` | 변경 없음 | 원샷 → Idle | 현재 레인 제자리 타격 (랜덤 4종) |
| `Top` | topY | 원샷 → Fall | 위 이동 타격 |
| `Middle` | centerY | 원샷 → Fall | 위아래 동시 타격 |
| `Bottom` | bottomY | 원샷 → Idle | 아래 이동 타격 |
| `Fall` | topY→bottomY lerp | 원샷 → Idle | 낙하 전환 (레인 없음) |
| `TopHold` | topY | 루프 | 위 롱노트 홀드 중 |
| `MiddleHold` | centerY | 루프 | 위아래 동시 홀드 중 |
| `BottomHold` | bottomY | 루프 | 아래 롱노트 홀드 중 |
| `TopHitWhileBottomHold` | bottomY 유지 | 원샷 → BottomHold | 아래 홀드 중 위 타격 |
| `BottomHitWhileTopHold` | topY 유지 | 원샷 → TopHold | 위 홀드 중 아래 타격 |

---

## Root Y 제어 방식

```
CharacterRoot  (CharacterAnimator — Y 위치: 코드 lerp 제어)
└── CharacterSprite (Animator: Root 기준 상대 모션만 담당)
```

- `_targetY` 값만 변경하면 `Update()`의 `Mathf.MoveTowards`가 매 프레임 lerp 처리
- 애니메이션 클립은 Root 기준 상대 모션 → 출발 위치와 무관하게 동일 클립 재사용 가능
- `Hit` 상태는 `_targetY` 변경 없음 (현재 위치에서 재생)

---

## GetLaneFromState() — 레인 추론

현재 상태에서 캐릭터가 어느 레인에 있는지 추론. 별도 변수 없이 `_currentState`로 판단.

```
Top 레인    : Top, TopHold, BottomHitWhileTopHold
Middle 레인 : Middle, MiddleHold
Bottom 레인 : Idle, Bottom, BottomHold, Hit0~3, TopHitWhileBottomHold
null        : Fall  ← 어느 레인도 아님, 항상 이동 타격
```

---

## 상태 전이 규칙 (OnNoteHit)

```
pos == Top && _isBottomHolding  →  TopHitWhileBottomHold  (bottomY 유지)
pos == Bottom && _isTopHolding  →  BottomHitWhileTopHold  (topY 유지)
pos == Middle                   →  Middle, centerY, StartFallTimer()

pos == Top:
  GetLaneFromState == Top   →  Hit(랜덤), Y 유지, StartFallTimer() 리셋
  그 외 (Bottom/Middle/Fall) →  Top, topY, StartFallTimer()

pos == Bottom:
  GetLaneFromState == Bottom →  Hit(랜덤), Y 유지
  그 외 (Top/Middle/Fall)    →  Bottom, bottomY
```

---

## FallTimer 메커니즘 (topY 체공 → 중력 복귀)

**목적**: `Top`/`Middle`은 단발 타격이라 "언제 내려오는지" 외부 신호가 없음.
FallTimer가 그 종료 신호 역할.

```
Top/Middle 히트
  └─ StartFallTimer()
       └─ [_topFloatDuration 대기]
            └─ FallAfterDelay()
                 ├─ SetState(Fall)
                 ├─ _currentState = Fall
                 └─ _targetY = bottomY
                      └─ Update() lerp → Animator Fall 원샷 → Idle
```

**타이머 시작/취소 규칙:**

| 전이 | 타이머 동작 |
|---|---|
| → Top, Hit(topY에서), Middle | `StartFallTimer()` (리셋 포함) |
| → TopHold, BottomHold, MiddleHold | `ResetFallTimer()` 취소 |
| → TopHitWhileBottomHold, BottomHitWhileTopHold | `ResetFallTimer()` 취소 |
| → Bottom, Bottom 이동 타격 | `ResetFallTimer()` 취소 |

**Hold와 FallTimer의 차이:**

| | Top/Middle (단타) | Hold 상태 |
|---|---|---|
| 종료 신호 | 없음 → FallTimer가 대신 | `OnHoldEnd` 이벤트 (명시적) |
| bottomY 복귀 방법 | FallTimer → Fall → bottomY | `UpdateHoldState` → Fall → bottomY |

---

## Hold 종료 복귀 흐름 (UpdateHoldState)

홀드가 모두 끝났을 때 직접 Idle로 가지 않고 **Fall을 경유**:

```
OnHoldEnd → UpdateHoldState
  _isTopHolding == false && _isBottomHolding == false
    └─ _targetY = bottomY
       SetState(Fall)         ← topY/centerY에서 달리기 하강 방지
       _currentState = Fall
         └─ Animator Fall 원샷 종료 → Idle 자동 전이
```

단일 홀드 종료 (반대쪽 홀드 유지 중):
```
Top 홀드 종료, Bottom 홀드 유지 →  BottomHold, bottomY
Bottom 홀드 종료, Top 홀드 유지 →  TopHold, topY
```

---

## Hit 랜덤 선택

```
PickHitVariant():
  [0, 1, 2, 3] 중 _lastHitVariant 제외 → Random.Range → _lastHitVariant 갱신
  → CharacterState.Hit0 + pick 반환
```

직전과 동일한 Hit 애니메이션 연속 재생 방지.

---

## 캐릭터 교체 흐름

```
CharacterAnimator.LoadCharacter(CharacterSO so)
  ├─ animationType == SpriteSheet
  │     → SpriteSheetAnimationHandler
  │          → _animator.runtimeAnimatorController = so.animatorController
  └─ animationType == Spine
        → SpineAnimationHandler (stub)

SpriteSheetAnimationHandler.SetState(state)
  → Animator.SetTrigger(state.ToString())
```

---

## AnimatorController 구성 가이드

| 상태 그룹 | 상태 목록 | 전이 규칙 |
|---|---|---|
| 루프 | Idle, TopHold, MiddleHold, BottomHold | Has Exit Time = false, 외부 Trigger로만 전이 |
| 원샷 → Idle | Hit0~3, Bottom, Fall | Has Exit Time = true, 종료 후 Idle로 자동 전이 |
| 원샷 → Fall | Top, Middle | Has Exit Time = true, 종료 후 Fall로 자동 전이 |
| 원샷 → Hold | TopHitWhileBottomHold → BottomHold | Has Exit Time = true |
| 원샷 → Hold | BottomHitWhileTopHold → TopHold | Has Exit Time = true |

> **주의**: `Top`/`Middle` → `Fall` 자동 전이는 Animator의 Has Exit Time 전이.
> 코드 쪽 `_currentState = Fall` / `_targetY = bottomY` 는 FallTimer coroutine이 담당.
> 두 타이밍이 맞아야 하므로 `_topFloatDuration` ≈ Top/Middle 클립 재생 시간으로 설정.

---

## Unity 에디터 작업 목록

### 1. Timeline.prefab 수정

기존 `characterImage` GameObject를 캐릭터 루트로 전환.

**계층 구조:**
```
Timeline (TimelineController)
└── CharacterRoot          ← CharacterAnimator 컴포넌트 부착
    └── CharacterSprite    ← Animator 컴포넌트 부착 (SpriteRenderer 포함)
```

**작업 순서:**
1. Timeline.prefab 열기
2. 기존 `characterImage` 오브젝트를 `CharacterRoot`로 이름 변경
3. `CharacterRoot`에 `CharacterAnimator` 컴포넌트 추가
4. `CharacterRoot` 하위에 `CharacterSprite` 자식 오브젝트 생성
5. `CharacterSprite`에 `Animator` + `SpriteRenderer` 컴포넌트 추가
6. `CharacterAnimator` Inspector 연결:
   - `_spriteRoot` → `CharacterSprite`
   - `_topY` / `_bottomY` / `_centerY` 값 설정 (기본: 60 / -60 / 0)
   - `_topFloatDuration` → Top/Middle 클립 재생 시간과 동일하게 설정
   - `_fallSpeed` → 300 (기본값)
7. `TimelineController` Inspector 연결:
   - `_characterAnimator` → `CharacterRoot`

---

### 2. CharacterSO 에셋 생성

1. `Assets/Resources/Characters/` 폴더 생성
2. Project 창 우클릭 → Create → Skins → CharacterSO
3. 파일명: `CharacterSO_0001`
4. Inspector 설정:

| 필드 | 값 |
|---|---|
| `id` | `1` (고유 정수, PlayerPrefs 저장 키) |
| `skinName` | 캐릭터 이름 |
| `thumbnailSprite` | 선택 UI용 썸네일 스프라이트 |
| `animationType` | `SpriteSheet` |
| `animatorController` | 아래 3번에서 생성할 AnimatorController 할당 |

> **주의**: `id`는 반드시 고유해야 함. 중복 시 PlayerPrefs 저장 충돌 발생.

---

### 3. AnimatorController 생성

1. Project 창 우클릭 → Create → Animator Controller
2. 파일명: `CharacterName_AnimatorController`
3. Animator 창에서 아래 상태 구성:

**파라미터 (Trigger × 11):**
```
Idle, Hit0, Hit1, Hit2, Hit3,
Top, Middle, Bottom, Fall,
TopHold, MiddleHold, BottomHold,
TopHitWhileBottomHold, BottomHitWhileTopHold
```
> `SetTrigger(state.ToString())` 방식이므로 파라미터명 = CharacterState enum명과 정확히 일치해야 함.

**상태 및 전이 설정:**

| 상태 | AnimationClip | Has Exit Time | 전이 대상 |
|---|---|---|---|
| `Idle` | Idle 클립 | false (루프) | — |
| `Hit0` ~ `Hit3` | Hit0~3 클립 | true | → Idle |
| `Top` | Top 클립 | true | → Fall |
| `Middle` | Middle 클립 | true | → Fall |
| `Bottom` | Bottom 클립 | true | → Idle |
| `Fall` | Fall 클립 | true | → Idle |
| `TopHold` | TopHold 클립 | false (루프) | — |
| `MiddleHold` | MiddleHold 클립 | false (루프) | — |
| `BottomHold` | BottomHold 클립 | false (루프) | — |
| `TopHitWhileBottomHold` | TopHitWhileBottomHold 클립 | true | → BottomHold |
| `BottomHitWhileTopHold` | BottomHitWhileTopHold 클립 | true | → TopHold |

**Any State 전이 설정:**
- Any State → 각 상태로 Trigger 기반 전이
- `Can Transition To Self` = false (동일 상태 재진입 방지)
- `Has Exit Time` = false (즉시 전이)

---

### 4. AnimationClip 생성

1. 스프라이트 시트를 `Assets/Sprites/Characters/` 에 임포트
   - Texture Type: Sprite (2D and UI)
   - Sprite Mode: Multiple → Sprite Editor에서 슬라이싱
2. Animation 창(Ctrl+6)에서 각 상태별 클립 생성
3. **_topFloatDuration 맞추기**: Top / Middle 클립 재생 시간을 확인한 뒤
   `CharacterAnimator`의 `_topFloatDuration` Inspector 값을 동일하게 설정

**권장 클립 구성 예시:**

| 클립 | Loop Time | 참고 |
|---|---|---|
| Idle | ✅ | 달리기 루프 |
| Hit0 ~ Hit3 | ❌ | 타격 리액션 4종 |
| Top | ❌ | 점프 상승 모션 |
| Middle | ❌ | 동시 타격 모션 |
| Bottom | ❌ | 착지 모션 |
| Fall | ❌ | 낙하 모션 |
| TopHold | ✅ | 공중 유지 루프 |
| MiddleHold | ✅ | 중간 유지 루프 |
| BottomHold | ✅ | 지상 홀드 루프 |
| TopHitWhileBottomHold | ❌ | 아래 홀드 중 위 타격 |
| BottomHitWhileTopHold | ❌ | 위 홀드 중 아래 타격 |

---

### 5. CharacterSO에 AnimatorController 할당

생성한 AnimatorController를 `CharacterSO_0001`의 `animatorController` 필드에 드래그 연결.

---

### 작업 완료 체크리스트

- [ ] Timeline.prefab: CharacterRoot/CharacterSprite 계층 구성
- [ ] Timeline.prefab: CharacterAnimator 컴포넌트 추가 및 Inspector 연결
- [ ] Timeline.prefab: TimelineController._characterAnimator 연결
- [ ] CharacterSO_0001.asset 생성 (Resources/Characters/)
- [ ] AnimatorController 생성 및 11개 상태 + Trigger 파라미터 구성
- [ ] AnimationClip 11개 생성 및 Loop 설정
- [ ] _topFloatDuration 값을 Top/Middle 클립 길이와 동기화
- [ ] CharacterSO에 AnimatorController 할당
