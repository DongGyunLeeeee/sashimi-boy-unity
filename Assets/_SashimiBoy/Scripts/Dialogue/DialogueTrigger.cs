using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class DialogueTrigger : MonoBehaviour, IInteractable
    {
        public string prompt = "대화하기";
        public DialogueRunner runner;
        public DialogueSequence sequence;
        [TextArea(2, 3)] public string fallbackSpeaker = "케빈";
        [TextArea(2, 4)] public string fallbackLine = "...";

        public string Prompt => prompt;

        public void Interact(GameObject actor)
        {
            if (runner == null)
            {
                runner = FindObjectOfType<DialogueRunner>();
            }

            if (runner == null)
            {
                Debug.LogWarning("DialogueTrigger needs a DialogueRunner in the scene.");
                return;
            }

            if (sequence != null)
            {
                runner.Play(sequence);
                return;
            }

            runner.Play(new List<DialogueLine>
            {
                new DialogueLine { speaker = fallbackSpeaker, text = fallbackLine }
            });
        }
    }
}
