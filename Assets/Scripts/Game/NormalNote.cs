using UnityEngine;

namespace SCOdyssey.Game
{
    public class NormalNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = true;
            holdImage.enabled = false;
        }
    }
}