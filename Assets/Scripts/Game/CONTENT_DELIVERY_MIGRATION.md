# 콘텐츠 전달 방식 마이그레이션 가이드

## 왜 이렇게 바꿨는가

### 문제 1: 저작권 (WJMAX 논란)
`StreamingAssets`에 평문 `.ogg`/`.mp4` 파일을 저장하면 게임 폴더에서 누구나 직접 재생하거나
복사할 수 있는 상태가 된다. 음원 권리자 입장에서 이는 "원음 배포"로 해석될 여지가 있다.
실제로 WJMAX가 동일한 이유로 논란이 발생했다.

### 문제 2: Steam 빌드 용량
BGA(배경 영상) 파일 1개가 713MB다. 곡이 50개가 되면 BGA만 35GB+.
모든 음원과 영상을 Steam 빌드에 포함시키는 것은 불가능하다.

### 문제 3: 빠른 곡 출시 주기
신규 곡을 추가할 때마다 Steam 전체 빌드를 새로 내보내는 것은 불가능하다.
음원/영상만 서버에 업로드하고, 게임 클라이언트는 변경 없이 유지되어야 한다.

### 해결책: IContentProvider 추상화 + Addressables Remote CDN
- **IContentProvider 인터페이스**: 로컬/CDN 구현체를 교체 가능하도록 추상화
- **LocalContentProvider**: 현재 활성. StreamingAssets 직접 읽기. 테스트용.
- **AddressablesContentProvider**: CDN 인프라 준비 후 전환. 암호화 번들 다운로드.
- **AES-256 암호화**: 어떤 방식이든 파일은 암호화 상태로 저장/배포.

---

## 현재 아키텍처 (로컬 모드, 현재 활성)

```
[게임 빌드]
  StreamingAssets/Music/*.ogg  (또는 *.ogg.dat 암호화 후)
  StreamingAssets/BGA/*.mp4    (또는 *.bga 암호화 후)

[런타임 플로우]
  GameDataLoader
    → ServiceLocator.Get<IContentProvider>()  ← LocalContentProvider
    → LoadAudioBytesAsync(music)              ← File.ReadAllBytes + AudioCrypto.Decrypt
    → IAudioManager.LoadAudioFromBytes(bytes) ← FMOD OPENMEMORY
    → GetBGAPathAsync(music)                  ← StreamingAssets 경로 반환
    → IGameManager.SetBGAData(path, ...)      ← VideoPlayer로 재생
```

**전환 스위치**: `Managers.cs`에서 `#if USE_CDN_DELIVERY` 분기로 구현체 선택.

---

## CDN 완전 전환 체크리스트

### 1단계: 패키지 설치
- [ ] Unity Package Manager → `com.unity.addressables` 설치

### 2단계: Addressables 그룹 구성 (Unity 에디터)
- [ ] Window → Asset Management → Addressables → Groups 열기
- [ ] 그룹 생성:
  - `MusicMetadata` (Remote, Pack Separately): MusicSO 에셋, 채보 TextAsset, 앨범아트 Sprite
  - `MusicAudio` (Remote, Pack Separately): 암호화된 OGG 파일 (TextAsset으로 임포트)
  - `MusicBGA` (Remote, Pack Separately): 암호화된 MP4 파일 (TextAsset으로 임포트)
- [ ] 각 그룹 Bundle Mode = **Pack Separately** 확인
  - ⚠️ Pack Together로 설정하면 곡 추가 시 전체 번들 재다운로드 발생

### 3단계: CDN 설정
- [ ] Firebase Storage (또는 AWS S3) 버킷 생성
- [ ] Addressables Profiles 설정:
  - Remote Build Path: `ServerData/[BuildTarget]`
  - Remote Load Path: `https://storage.googleapis.com/[버킷명]/[BuildTarget]`
  - (또는 AWS: `https://[버킷명].s3.[리전].amazonaws.com/[BuildTarget]`)

### 4단계: 음원/BGA 암호화
- [ ] 원본 파일을 `_RawAudio/Music/`, `_RawAudio/BGA/`에 배치 (`.gitignore` 등록됨)
- [ ] `SCOdyssey > Encrypt Audio Assets` 메뉴 실행
  - 출력: 암호화된 `.ogg.dat`, `.bga` 파일 → Addressables 그룹 폴더에 저장

### 5단계: MusicSO 에셋 업데이트 (Unity Inspector)
- [ ] 각 MusicSO 에셋에 `audioAddressableKey` 입력 (예: `"audio_0001"`)
- [ ] 각 MusicSO 에셋에 `bgaAddressableKey` 입력 (예: `"bga_0001"`)
- [ ] MusicSO 에셋 자체도 MusicMetadata Addressables 그룹에 등록 (Resources에서 이동)

### 6단계: MusicManager 수정 (코드)
```csharp
// Before (Resources):
MusicSO[] arr = Resources.LoadAll<MusicSO>("Music");

// After (Addressables):
var handle = Addressables.LoadAssetsAsync<MusicSO>("music_metadata", null);
await handle.Task;
musicList = new List<MusicSO>(handle.Result);
```

### 7단계: Addressables 빌드 및 CDN 업로드
- [ ] Build → Build Addressables Content 실행
- [ ] `ServerData/[플랫폼]/` 폴더 내용물 CDN에 업로드
  - `catalog.json` (필수, 매 빌드마다 갱신)
  - `*.bundle` 파일들

### 8단계: USE_CDN_DELIVERY 활성화
- [ ] Project Settings → Player → Scripting Define Symbols에 `USE_CDN_DELIVERY` 추가
  - `Managers.cs`에서 자동으로 `AddressablesContentProvider` 등록됨

### 9단계: 테스트
- [ ] 게임 실행 → 카탈로그 업데이트 확인 (CDN 연결)
- [ ] 곡 선택 → 다운로드 프로그레스 → 재생 확인
- [ ] 오프라인 상태에서 캐시된 곡 재생 확인
- [ ] `persistentDataPath/aa/` 폴더 내 `.bundle` 파일 → VLC 재생 시도 → 실패 확인

### 10단계: 정리
- [ ] `LocalContentProvider.cs` 삭제
- [ ] `Assets/StreamingAssets/Music/`, `Assets/StreamingAssets/BGA/` 내 음원/BGA 파일 삭제
- [ ] `MusicSO.audioFilePath`, `MusicSO.videoFileName` 필드 제거 (로컬 모드 필드)
- [ ] `FMODAudioManager.LoadAudio(string filePath)` 제거 (OPENMEMORY 방식만 유지)

---

## 신규 곡 추가 워크플로우 (CDN 전환 후, Steam 빌드 없이)

1. OGG + MP4 원본 파일 준비
2. `SCOdyssey > Encrypt Audio Assets` 실행 → 암호화 파일 생성
3. MusicSO 에셋 + 채보 TextAsset 생성 (Unity 에디터)
4. Addressables 그룹에 새 에셋 등록
5. `Build → Build Addressables Content` 실행 (게임 전체 빌드 아님)
6. 생성된 `audio_XXXX.bundle`, `bga_XXXX.bundle`, 새 `catalog.json` → CDN 업로드
7. 플레이어: 다음 게임 실행 시 `UpdateCatalogs()` → 신규 곡 자동 인식

**기존 곡 번들은 재다운로드 없음**: Pack Separately + 콘텐츠 해시로 변경된 번들만 갱신됨.

---

## 관련 파일

| 파일 | 역할 | CDN 전환 후 처리 |
|---|---|---|
| `IContentProvider.cs` | 로컬/CDN 추상화 인터페이스 | 유지 |
| `LocalContentProvider.cs` | StreamingAssets 직접 읽기 | **삭제** |
| `AddressablesContentProvider.cs` | CDN 다운로드 구현 | 유지 (활성화) |
| `AudioCrypto.cs` | AES-256-CBC 복호화 유틸 | 유지 |
| `Assets/Editor/AudioEncryptionTool.cs` | 빌드 타임 암호화 도구 | 유지 |
| `FMODAudioManager.cs` | OPENMEMORY 로드 구현 | `LoadAudio(string)` 제거 |
| `GameDataLoader.cs` | IContentProvider 오케스트레이션 | 변경 없음 |
| `MusicSO.cs` | 로컬/CDN 양쪽 필드 보유 | 로컬 필드 제거 |
| `Managers.cs` | USE_CDN_DELIVERY 분기 | `#if` 블록 정리 |
