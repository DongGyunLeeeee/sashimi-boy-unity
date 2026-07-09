namespace SashimiBoy
{
    public interface IDistractionEffect
    {
        DistractionType Type { get; }
        void Play(DistractionCue cue, float bpm);
        void Stop();
    }
}
