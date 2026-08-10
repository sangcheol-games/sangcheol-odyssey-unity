using PixelCrushers.DialogueSystem.SequencerCommands;
using System.Collections;
using UnityEngine;

public class SequencerCommandTest : SequencerCommand
{
    private void Awake()
    {
        float val = GetParameterAsFloat(0);

        StartCoroutine("SomeEffect", val);
    }

    // Stop() 과 같은 시점
    private void OnDestroy()
    {
        StopCoroutine("SomeEffect");
        // 여기서 최종 상태로 이행 (카메라 등)
    }

    // 직접 코루틴 쏘는거보다도 매니저를 하나 두는 편이 좋겠다
    IEnumerator SomeEffect(float val)
    {
        // 대강 애니메 처리...

        Debug.Log(val);

        Stop();
        yield return null;
    }
}
