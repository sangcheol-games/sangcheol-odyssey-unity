using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;


public class DialSceneTest : MonoBehaviour
{
    public bool floating = false;
    public bool fromResource = true;

    public string dialogueName;
    public string conversationName;


    public void dialSceneTest()
    {
        if (ServiceLocator.TryGet<IDialogueManager>(out var _dialogueManager))
        {
            _dialogueManager.LoadDialogueScene(floating, dialogueName, fromResource);
            _dialogueManager.OnDialogueLoaded += ((val) =>
            {
                if (val)
                    _dialogueManager.PlayConversation(conversationName);
            });
        }
    }
}
