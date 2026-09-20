using Sirenix.Serialization;
using SCOdyssey.Domain.Service;

// Constants가 Assembly-CSharp에서 SCOdyssey.Domain.Service 어셈블리로 옮겨졌다.
// MusicSO 에셋에 Odin이 저장해 둔 옛 어셈블리 이름을 새 타입으로 되돌린다.
[assembly: BindTypeNameToType("SCOdyssey.Domain.Service.Constants+Difficulty, Assembly-CSharp", typeof(Constants.Difficulty))]
