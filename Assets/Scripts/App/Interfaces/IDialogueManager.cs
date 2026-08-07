using System;
using UnityEngine;


namespace SCOdyssey.App
{
    public interface IDialogueManager
    {
        public event Action<Vector2> OnDialogueSelect;
        public event Action OnDialogueSubmit;
        public event Action OnDialogueCancel;

        public event Action<bool> OnDialogueLoaded;

        event Action OnConversationEnd;


        void LoadDialogueScene(bool isFloating);
        void LoadDialogueScene(bool isFloating, string name, bool fromResource = false);

        void UnloadDialogueScene(bool isFloating);


        void LoadDialogue(string name, bool fromResource = false);

        bool PlayConversation(string conversation);
        void QuitConversation();
    }
}