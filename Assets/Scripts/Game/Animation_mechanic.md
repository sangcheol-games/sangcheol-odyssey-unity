# CharacterAnimator 메커니즘 정리

## 관련 파일

| 파일 | 역할 |
|---|---|
| `CharacterAnimator.cs` | 상태 머신 + 캐릭터 Y 위치 + 판정 버스 구독 |
| `ICharacterAnimationHandler.cs` | 애니메이션 방식 추상화 (`Initialize`, `SetState`) |
| `SpineAnimationHandler.cs` | Spine 4.2 구현체 (`SkeletonAnimation`) |
| `SpriteSheetAnimationHandler.cs` | Unity Animator 구현체 (`SetTrigger`) |
| `Domain/Entity/CharacterSO.cs` | 스킨 베이스. `SpineCharacterSO`(skeletonDataAsset) / `SpriteSheetCharacterSO`(animatorController) |
| `Game/View/LaneLayout.cs` | 레인 → 그룹(`LaneGroup`)·그룹 내 위치(`NotePosition`) |
| `Constants.cs` | `CharacterState`, `NotePosition` enum |

---

## 전체 데이터 흐름

```
RhythmSession.Press/Release (GameManager가 키 입력마다 호출. 판정보다 먼저)
  └─ judgementBus.PublishLaneInput(LaneInputEvent { Lane, IsPressed, Time })

RhythmSession.Press/Release/Advance (적중·miss 모두)
  └─ judgementBus.PublishNoteJudged(JudgeEvent { Lane, Kind, Judge, IsMiss, ... })

IJudgementBus (레인만 싣는다. 그룹/위치는 받는 쪽이 LaneLayout으로 파생)
  └─ 구독자: ScoreManager(점수) / CharacterAnimator(연출) / PlayfieldView(노트 뷰·이펙트)

CharacterAnimator (Start에서 구독, LaneLayout.GroupOf로 자기 그룹만)
  ├─ LaneInput 누름            → OnLanePressed(pos) → HandleLaneInput   (같은 프레임 반대 위치면 Middle)
  ├─ LaneInput 뗌              → OnLaneReleased(pos) → 그 위치 홀드 해제 → UpdateHoldState()
  ├─ NoteJudged (miss 제외)    → HandleNoteJudged(judge, pos)
  └─ NoteJudged (HoldHead 적중) → OnHoldStarted(pos) → 그 위치 홀드 진입 → UpdateHoldState()
```

캐릭터는 판정선(`Timeline.prefab`)마다 하나다. `TimelineController.Init`이 `SetGroup(group)`으로 그룹을 정하고 진행 방향에 맞춰 Y축 180° 회전한다.
이 호출이 없으면 위·아래 캐릭터가 둘 다 `Top` 그룹으로 반응한다. 버스가 없는 씬(채보 에디터 프리뷰)에서는 구독이 생기지 않는다.

---

## 그룹과 위치 (LaneLayout)

```
레인 1, 2  →  LaneGroup.Top     (위 판정선 · 위 캐릭터)
레인 3, 4  →  LaneGroup.Bottom  (아래 판정선 · 아래 캐릭터)

레인 1, 3  →  NotePosition.Top     (그룹 안에서 위 칸)
레인 2, 4  →  NotePosition.Bottom  (그룹 안에서 아래 칸)
```

`NotePosition.Middle`은 채보에서 오지 않는다. 두 경우에만 생긴다.
- 같은 프레임에 같은 그룹의 위·아래 칸을 함께 누름 (`OnLanePressed`가 `Time.frameCount`로 판단)
- 위·아래 홀드를 동시에 잡고 있음 (`UpdateHoldState`)

---

## CharacterState (16개)

| 상태 | 위치(Y) | Spine 재생 | 언제 |
|---|---|---|---|
| `Idle` | — | 루프 | 캐릭터 로드 직후, 원샷이 끝난 뒤 |
| `Top` / `Middle` / `Bottom` | `_topY` / `_centerY` / `_bottomY` | 원샷 → Idle | 다른 칸을 눌러 이동 |
| `Attack` | 유지 | 원샷 → Idle | 지금 칸을 다시 누름 |
| `Hit0` ~ `Hit3` | 유지 | 원샷 → Idle | `Attack` 중 Perfect·Master·Ideal 적중 (직전과 다른 변형) |
| `Hit_Kind` / `Hit_Umm` | 유지 | 원샷 → Idle | `Attack` 중 Kind / Umm 적중 |
| `TopHold` / `MiddleHold` / `BottomHold` | 해당 Y | 루프 | 홀드 머리 적중 ~ 키 뗌 |
| `TopHitWhileBottomHold` | 유지 | 원샷 → BottomHold | 아래 홀드 중 위 칸 적중 |
| `BottomHitWhileTopHold` | 유지 | 원샷 → TopHold | 위 홀드 중 아래 칸 적중 |

---

## 위치 제어

```
CharacterRoot  (CharacterAnimator, _spriteRoot = CharacterSprite)
└── CharacterSprite (SkeletonAnimation 또는 Animator)
```

- 이동은 `SnapY(y)`가 `_spriteRoot.localPosition.y`를 바로 바꾼다. 보간·낙하 타이머는 없다.
- 애니메이션은 제자리 모션만 담당한다. 그래서 같은 클립을 어느 칸에서든 쓴다.
- 기본값: `_topY` 120, `_bottomY` −120, `_centerY` 0 (`Timeline.prefab`에 직렬화된 값도 같다).

---

## 상태 전이

**누름 (`HandleLaneInput`)**
```
홀드 중(_topHold 또는 _bottomHold)       →  무시
누른 칸 == 지금 칸(_pos)                  →  Attack
그 외                                    →  _pos = 누른 칸, SnapY, Top/Middle/Bottom
```

**적중 (`HandleNoteJudged`, miss는 연출하지 않는다)**
```
아래 홀드 중 + 위 칸 적중                 →  TopHitWhileBottomHold
위 홀드 중 + 아래 칸 적중                 →  BottomHitWhileTopHold
지금 애니가 Top/Middle/Bottom(이동)       →  그대로 둔다 (이동 모션을 히트가 덮지 않는다)
지금 애니가 Attack                        →  Kind → Hit_Kind, Umm → Hit_Umm, 그 외 → Hit0~3
```

**홀드 (`UpdateHoldState`)**
- 홀드 머리 적중 → 그 칸의 홀드 플래그를 켠다. 키를 떼면(판정 결과와 상관없이) 끈다.
- 둘 다 켜짐 → `MiddleHold`, 위만 → `TopHold`, 아래만 → `BottomHold`, 위치도 그 칸으로.
- 둘 다 꺼짐 → 지금 칸의 이동 상태(`Top`/`Middle`/`Bottom`)를 다시 재생한다.

---

## 캐릭터 교체

```
CharacterAnimator.Start
  └─ ICharacterManager.GetCurrentSkin() → LoadCharacter(so)
       ├─ SpriteSheetCharacterSO → SpriteSheetAnimationHandler
       │     Animator.runtimeAnimatorController = so.animatorController
       │     SetState(state) → Animator.SetTrigger(state.ToString())
       └─ 그 외(SpineCharacterSO) → SpineAnimationHandler
             SkeletonAnimation.skeletonDataAsset = so.skeletonDataAsset, 정렬 순서 3
             SetState(state) → SetAnimation(state.ToString()) + 원샷이면 다음 상태 AddAnimation
```

두 방식 모두 **애니메이션(또는 Trigger) 이름 = `CharacterState` enum 이름**이어야 한다.

---

## 새 캐릭터 만들기

### Spine (출시 캐릭터 방식)

1. Spine 내보내기 → `SkeletonDataAsset` 임포트
2. 애니메이션 이름을 `CharacterState` 16개 이름과 똑같이 맞춘다
   (`Idle`, `Hit0`~`Hit3`, `Top`, `Middle`, `Bottom`, `TopHold`, `MiddleHold`, `BottomHold`,
   `TopHitWhileBottomHold`, `BottomHitWhileTopHold`, `Attack`, `Hit_Kind`, `Hit_Umm`)
3. Project 창 → Create → Skins → SpineCharacterSO, `Assets/Resources/Characters/`에 저장
4. `id`(고유 정수, PlayerPrefs 저장 키), `skinName`, `thumbnailSprite`, `skeletonDataAsset` 지정
5. 원샷/루프 구분은 코드(`SpineAnimationHandler.SetState`)가 정한다. 클립 쪽 Loop 설정은 상관없다

### Sprite sheet (Animator)

1. Create → Skins → SpriteSheetCharacterSO, `animatorController` 지정
2. AnimatorController에 `CharacterState` 이름의 Trigger 16개와 같은 이름의 상태를 만든다
3. Any State → 각 상태: Trigger 전이, `Has Exit Time` false, `Can Transition To Self` false
4. 원샷 상태의 종료 전이는 위 표의 "Spine 재생" 열과 같게: 대부분 → `Idle`,
   `TopHitWhileBottomHold` → `BottomHold`, `BottomHitWhileTopHold` → `TopHold`

> `id`는 반드시 고유해야 한다. 겹치면 PlayerPrefs 저장이 충돌한다.
