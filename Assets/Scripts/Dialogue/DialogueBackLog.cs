using PixelCrushers.DialogueSystem;
using UnityEngine;
using System.Collections.Generic;

namespace SCOdyssey.Dialogue
{
    public class DialogueBackLog : MonoBehaviour
    {
        public List<string> logSpeakers;
        public List<string> logLines;
        public List<DialogueEntry> logEntries;


        private void OnEnable()
        {
            logSpeakers = new List<string>();
            logLines = new List<string>();
            logEntries = new List<DialogueEntry>();

            DialogueManager.instance.conversationLinePrepared += OnConversationLine;
        }

        private void OnDisable()
        {
            DialogueManager.instance.conversationLinePrepared -= OnConversationLine;
        }


        private void OnConversationLine(Subtitle subtitle)
        {
            if (subtitle == null | subtitle.formattedText == null | string.IsNullOrEmpty(subtitle.formattedText.text)) return;
            string speakerName = (subtitle.speakerInfo != null && subtitle.speakerInfo.transform != null) ? subtitle.speakerInfo.transform.name : "(null speaker)";

            logLines.Add(subtitle.formattedText.text);
            logSpeakers.Add(speakerName);
            logEntries.Add(subtitle.dialogueEntry);
        }
    }
}