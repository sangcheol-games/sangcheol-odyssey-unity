using UnityEngine;

namespace SCOdyssey.Game
{
    public class HoldStartNote : NoteController
    {
        protected override void SetVisual()
        {
            holdImage.enabled = false;
        }
    }
}