using System;

namespace SashimiBoy
{
    public enum DialogueLineKind { Speech, Thought, Action }
    [Serializable]
    public sealed class DialogueLine
    {
        public string speaker;
        public string text;
        public float autoAdvanceDelay = -1f;
        public DialogueLineKind kind;
        public string actionId;
        public int sourcePage;
    }
}
