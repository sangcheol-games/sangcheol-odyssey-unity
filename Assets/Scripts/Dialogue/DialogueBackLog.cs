using PixelCrushers.DialogueSystem;
using UnityEngine;
using System.Collections.Generic;
using TMPro;


namespace SCOdyssey.Dialogue
{
    public class DialogueBackLog : MonoBehaviour
    {
        private Queue<GameObject> backLogTextQueue;
        public int maxLogCount = 200;

        public GameObject backLogText;


        private void OnEnable()
        {
            backLogTextQueue = new Queue<GameObject>();

            DialogueManager.instance.conversationLinePrepared += RecordToBacklog;
        }

        private void OnDisable()
        {
            DialogueManager.instance.conversationLinePrepared -= RecordToBacklog;

            foreach (GameObject go in backLogTextQueue)
            {
                Destroy(go);
            }

            backLogTextQueue.Clear();
        }


        private void RecordToBacklog(Subtitle subtitle)
        {
            if (subtitle == null || subtitle.formattedText == null || string.IsNullOrEmpty(subtitle.formattedText.text)) return;
            string speakerName = (subtitle.speakerInfo != null) ? subtitle.speakerInfo.Name : "(null speaker)";

            string lineText = subtitle.formattedText.text;

            //logEntries.Add(subtitle.dialogueEntry);


            GameObject instance = Instantiate(backLogText, transform);
            TextMeshProUGUI text = instance.GetComponent<TextMeshProUGUI>();

            text.text = $"{speakerName} : {lineText}";


            backLogTextQueue.Enqueue(instance);

            if (backLogTextQueue.Count > maxLogCount)
            {
                Destroy(backLogTextQueue.Dequeue());
            }
        }
    }
}