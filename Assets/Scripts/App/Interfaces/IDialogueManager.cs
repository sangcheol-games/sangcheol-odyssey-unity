using System;
using UnityEngine;


namespace SCOdyssey.App
{
    public interface IDialogueManager
    {
        public event Action<bool> OnDialogueLoaded;     // 인자로 성공/실패 반환

        event Action OnConversationEnd;


        void LoadDialogueScene(bool isFloating);
        void LoadDialogueScene(bool isFloating, string name, bool fromResource = false);

        void UnloadDialogueScene(bool isFloating);


        void LoadDialogue(string name, bool fromResource = false);

        bool PlayConversation(string conversation);
        void QuitConversation();
    }
}