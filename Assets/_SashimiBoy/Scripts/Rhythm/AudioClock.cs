using UnityEngine;

namespace SashimiBoy
{
    public sealed class AudioClock : MonoBehaviour
    {
        public AudioSource audioSource;
        public bool playOnStart;
        public double scheduledLeadTime = 0.1d;

        private double startDspTime;
        private bool isRunning;

        public double SongTimeMs
        {
            get
            {
                if (!isRunning)
                {
                    return 0d;
                }

                return (AudioSettings.dspTime - startDspTime) * 1000d;
            }
        }

        public bool IsRunning => isRunning;

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            if (audioSource == null)
            {
                startDspTime = AudioSettings.dspTime;
                isRunning = true;
                return;
            }

            startDspTime = AudioSettings.dspTime + scheduledLeadTime;
            audioSource.PlayScheduled(startDspTime);
            isRunning = true;
        }

        public void Stop()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }

            isRunning = false;
        }
    }
}
