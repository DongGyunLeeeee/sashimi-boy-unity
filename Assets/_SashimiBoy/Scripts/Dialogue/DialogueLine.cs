using System;

namespace SashimiBoy
{
    [Serializable]
    public sealed class DialogueLine
    {
        public string speaker;
        public string text;
        public float autoAdvanceDelay = -1f;
    }
}
