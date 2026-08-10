using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;


public class DialSceneTest : MonoBehaviour
{
    public void dialSceneTest()
    {
        if (ServiceLocator.TryGet<IDialogueManager>(out var _dialogueManager))
        {
            _dialogueManager.LoadDialogueScene(false, "Test", true);
            _dialogueManager.OnDialogueLoaded += ((val) =>
            {
                if (val)
                    _dialogueManager.PlayConversation("Test");
            });
        }
    }
}
