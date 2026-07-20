using System;
using UnityEngine;
using SCOdyssey.Dialogue;


namespace SCOdyssey.App
{
    public interface IDialogueManager
    {
        public event Action<Vector2> OnDialogueSelect;
        public event Action OnDialogueSubmit;
        public event Action OnDialogueCancel;


        bool LoadConversationData();
        // 무슨 데이터로 받아야할까

        void LoadDialogueScene(bool isFloating);

        void DialogueOnConversationEnd();
    }
}