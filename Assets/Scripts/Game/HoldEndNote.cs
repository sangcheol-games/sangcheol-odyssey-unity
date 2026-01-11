using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class HoldEndNote : NoteController
    {
        protected override void SetVisual()
        {
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