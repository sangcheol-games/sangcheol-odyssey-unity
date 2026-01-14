using UnityEngine;

namespace SCOdyssey.Game
{
    public class HoldStartNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = true;
            holdImage.enabled = false;
        }
    }
}