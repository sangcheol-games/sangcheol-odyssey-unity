using UnityEngine;

namespace SCOdyssey.Game
{
    public class NormalNote : NoteController
    {
        protected override void SetVisual()
        {
            holdImage.enabled = false;
        }
    }
}