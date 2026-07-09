using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class DialogueRunner : MonoBehaviour
    {
        public event Action OnDialogueStarted;
        public event Action OnDialogueFinished;

        public DialogueUI dialogueUI;
        public KeyCode advanceKey = KeyCode.Space;

        private readonly List<DialogueLine> activeLines = new List<DialogueLine>();
        private int index;
        private bool isRunning;
        private Coroutine autoRoutine;

        public bool IsRunning => isRunning;

        private void Awake()
        {
            if (dialogueUI != null && dialogueUI.nextButton != null)
            {
                dialogueUI.nextButton.onClick.AddListener(Advance);
            }
        }

        private void Update()
        {
            if (isRunning && Input.GetKeyDown(advanceKey))
            {
                Advance();
            }
        }

        public void Play(DialogueSequence sequence)
        {
            if (sequence == null)
            {
                return;
            }

            Play(sequence.lines);
        }

        public void Play(IEnumerable<DialogueLine> lines)
        {
            activeLines.Clear();
            activeLines.AddRange(lines);
            index = 0;

            if (activeLines.Count == 0)
            {
                Finish();
                return;
            }

            isRunning = true;
            OnDialogueStarted?.Invoke();
            ShowCurrent();
        }

        public void Advance()
        {
            if (!isRunning)
            {
                return;
            }

            if (autoRoutine != null)
            {
                StopCoroutine(autoRoutine);
                autoRoutine = null;
            }

            index++;
            if (index >= activeLines.Count)
            {
                Finish();
                return;
            }

            ShowCurrent();
        }

        private void ShowCurrent()
        {
            DialogueLine line = activeLines[index];
            if (dialogueUI != null)
            {
                dialogueUI.ShowLine(line);
            }

            if (line.autoAdvanceDelay > 0f)
            {
                autoRoutine = StartCoroutine(AutoAdvance(line.autoAdvanceDelay));
            }
        }

        private IEnumerator AutoAdvance(float delay)
        {
            yield return new WaitForSeconds(delay);
            autoRoutine = null;
            Advance();
        }

        private void Finish()
        {
            isRunning = false;
            if (dialogueUI != null)
            {
                dialogueUI.Hide();
            }

            OnDialogueFinished?.Invoke();
        }
    }
}
