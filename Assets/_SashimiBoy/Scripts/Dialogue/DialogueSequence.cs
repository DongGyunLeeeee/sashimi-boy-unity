using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Dialogue/Dialogue Sequence", fileName = "DialogueSequence")]
    public sealed class DialogueSequence : ScriptableObject
    {
        public List<DialogueLine> lines = new List<DialogueLine>();
    }
}
