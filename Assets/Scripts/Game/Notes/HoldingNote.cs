using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class HoldingNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = false;
            holdImage.enabled = true;
            holdImage.fillAmount = 1f;
        }

        protected override void Update()
        {
            base.Update();

            if (currentState == NoteState.Active && trackingTimeline != null)
            {
                UpdateHoldFill();
            }

        }
    }
}