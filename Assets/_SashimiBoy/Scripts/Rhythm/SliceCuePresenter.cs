using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public enum SliceCueSourceMode
    {
        GuideQuarterBeat = 0,
        Beatmap = 1,
        StagePattern = 2
    }

    /// <summary>
    /// Reads upcoming timing data and renders it without owning judgement.
    /// </summary>
    public sealed class SliceCuePresenter : MonoBehaviour
    {
        private const int VisibleNoteCount = 4;

        public SliceCueSourceMode sourceMode = SliceCueSourceMode.StagePattern;
        public BeatmapDefinition futureBeatmap;

        [Header("Dependencies")]
        public Stage01SalmonTimingScaffold timing;
        public Stage01ActiveNoteTracker activeNoteTracker;
        public BossDemoPresenter bossDemo;
        public ProceduralSalmonView salmon;
        public KnifeVisualController playerKnife;

        [Header("World Cue")]
        public GameObject cueRoot;
        public Renderer cutGuideLine;
        public Transform leftBracket;
        public Transform rightBracket;
        public Transform forecastLeftBracket;
        public Transform forecastRightBracket;
        public Renderer[] bracketRenderers = Array.Empty<Renderer>();
        public Transform[] upcomingGhostLines = Array.Empty<Transform>();
        public Renderer[] upcomingGhostRenderers = Array.Empty<Renderer>();

        [Header("Overlay Tutorial")]
        public Text spacePromptText;
        public Text nowText;
        public Text restPromptText;
        public RectTransform rhythmLane;
        public Image rhythmLaneBackground;
        public Image hitCursor;
        public Image[] upcomingNoteDots = Array.Empty<Image>();
        [Min(1)] public int tutorialTargetCount = 12;
        [Min(0.01f)] public float nowDuration = 0.12f;
        [Min(1f)] public float laneHorizonBeats = 4f;
        [Range(1f, 2.5f)] public float restThresholdBeats = 1.35f;

        [Header("Motion")]
        public float wideBracketDistance = 0.72f;
        public float closedBracketDistance = 0.08f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Stage01RuntimeNote> stageNotes =
            new List<Stage01RuntimeNote>(VisibleNoteCount);
        private readonly List<double> upcomingTimes =
            new List<double>(VisibleNoteCount);
        private readonly List<int> upcomingOrdinals =
            new List<int>(VisibleNoteCount);
        private MaterialPropertyBlock propertyBlock;
        private Vector3[] ghostBaseScales = Array.Empty<Vector3>();
        private Vector3 guideLineBaseScale = Vector3.one;
        private int guideBeatIndex = -1;
        private int beatmapEventIndex;
        private bool showingDemoPattern;

        public double CurrentTargetTimeSeconds { get; private set; }
        public int CurrentTargetOrdinal { get; private set; }
        public float CurrentApproach01 { get; private set; }
        public bool IsResting { get; private set; }
        public int VisibleUpcomingCount { get; private set; }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            CaptureGhostScales();
            CaptureGuideLineScale();
            SetVisible(false);
        }

        private void Update()
        {
            if (timing == null || salmon == null ||
                timing.CurrentSection == Stage01SalmonSection.Result ||
                salmon.IsTransitioning)
            {
                HideImmediate();
                return;
            }

            double songSec = timing.SongTimeSeconds;
            if (!ResolveUpcomingNotes(songSec) || upcomingTimes.Count == 0)
            {
                if (timing.CurrentSection == Stage01SalmonSection.Gameplay)
                {
                    ShowRestOnly(songSec);
                }
                else
                {
                    HideImmediate();
                }

                return;
            }

            CurrentTargetTimeSeconds = upcomingTimes[0];
            CurrentTargetOrdinal = upcomingOrdinals[0];
            double leadSeconds = timing.BeatLengthSeconds * 2d;
            CurrentApproach01 = Mathf.Clamp01((float)(
                1d - (CurrentTargetTimeSeconds - songSec) / leadSeconds));
            double followingTarget = upcomingTimes.Count > 1
                ? upcomingTimes[1]
                : CurrentTargetTimeSeconds + timing.BeatLengthSeconds;
            float followingApproach = Mathf.Clamp01((float)(
                1d - (followingTarget - songSec) / leadSeconds));

            bool gameplay = timing.CurrentSection ==
                Stage01SalmonSection.Gameplay;
            double targetDeltaSeconds = CurrentTargetTimeSeconds - songSec;
            IsResting = gameplay && targetDeltaSeconds >
                timing.BeatLengthSeconds * restThresholdBeats;
            int baseCutIndex = showingDemoPattern
                ? CurrentTargetOrdinal
                : salmon.SuccessfulCuts;
            Vector3 cutWorld = salmon.GetCutWorldPosition(baseCutIndex);
            if (cueRoot != null)
            {
                cueRoot.transform.position = cutWorld;
            }

            bool pulse = songSec >= CurrentTargetTimeSeconds &&
                songSec <= CurrentTargetTimeSeconds + nowDuration;
            if (!IsResting)
            {
                ApplyBracketPose(
                    leftBracket,
                    rightBracket,
                    CurrentApproach01,
                    1f);
                ApplyBracketPose(
                    forecastLeftBracket,
                    forecastRightBracket,
                    followingApproach,
                    1.16f);
                SetCueColor(pulse
                    ? new Color(1f, 0.95f, 0.48f, 1f)
                    : new Color(0.35f, 0.92f, 1f, 1f));
                UpdateGuideLineScale(CurrentApproach01, pulse);
                UpdateGhostGuides(baseCutIndex);
            }
            else
            {
                ResetGuideLineScale();
                HideGhostGuides();
            }

            UpdateRhythmLane(songSec, IsResting);
            UpdateLanePresentation(songSec, IsResting);

            bool tutorial = gameplay &&
                !IsResting &&
                CurrentTargetOrdinal < tutorialTargetCount;
            SetTextVisible(spacePromptText, tutorial);
            SetTextVisible(nowText, !IsResting && pulse &&
                (gameplay || showingDemoPattern));
            SetTextVisible(restPromptText, IsResting);

            if (playerKnife != null)
            {
                bool knifeVisible = gameplay && !IsResting;
                playerKnife.SetVisible(knifeVisible);
                if (knifeVisible)
                {
                    playerKnife.SetTargetWorld(cutWorld);
                    playerKnife.SetWindup(CurrentApproach01);
                }
            }

            SetVisible(true, !IsResting);
        }

        public void Bind(
            Stage01SalmonTimingScaffold timingSource,
            ProceduralSalmonView salmonView,
            KnifeVisualController knife,
            Stage01ActiveNoteTracker noteTracker,
            BossDemoPresenter demoPresenter)
        {
            timing = timingSource;
            salmon = salmonView;
            playerKnife = knife;
            activeNoteTracker = noteTracker;
            bossDemo = demoPresenter;
            guideBeatIndex = -1;
            beatmapEventIndex = 0;
            CaptureGhostScales();
            CaptureGuideLineScale();
        }

        public void HideImmediate()
        {
            SetVisible(false);
            playerKnife?.SetVisible(false);
            IsResting = false;
            VisibleUpcomingCount = 0;
            HideGhostGuides();
            ResetGuideLineScale();
        }

        private bool ResolveUpcomingNotes(double songSec)
        {
            upcomingTimes.Clear();
            upcomingOrdinals.Clear();
            showingDemoPattern = false;

            if (timing.CurrentSection == Stage01SalmonSection.BossDemo)
            {
                if (songSec >= timing.CountdownStartSeconds)
                {
                    return ResolveStagePattern();
                }

                if (bossDemo == null)
                {
                    return false;
                }

                int firstOrdinal;
                if (bossDemo.CopyUpcomingDemoNotes(
                        songSec,
                        VisibleNoteCount,
                        upcomingTimes,
                        out firstOrdinal) <= 0)
                {
                    return false;
                }

                for (int i = 0; i < upcomingTimes.Count; i++)
                {
                    upcomingOrdinals.Add(firstOrdinal + i);
                }

                showingDemoPattern = true;
                return true;
            }

            if (sourceMode == SliceCueSourceMode.StagePattern)
            {
                return ResolveStagePattern();
            }

            return sourceMode == SliceCueSourceMode.Beatmap
                ? ResolveBeatmap(songSec)
                : ResolveQuarterBeatGuide(songSec);
        }

        private bool ResolveStagePattern()
        {
            if (activeNoteTracker == null ||
                activeNoteTracker.CopyUpcomingNotes(
                    VisibleNoteCount,
                    stageNotes) <= 0)
            {
                return false;
            }

            for (int i = 0; i < stageNotes.Count; i++)
            {
                upcomingTimes.Add(stageNotes[i].songTimeSeconds);
                upcomingOrdinals.Add(stageNotes[i].sequenceIndex);
            }

            return true;
        }

        private bool ResolveQuarterBeatGuide(double songSec)
        {
            if (guideBeatIndex < timing.FirstGameplayBeatIndex)
            {
                guideBeatIndex = timing.FirstGameplayBeatIndex;
            }

            double targetSec = timing.GetBeatTimeSeconds(guideBeatIndex);
            while (songSec > targetSec + nowDuration)
            {
                guideBeatIndex++;
                targetSec = timing.GetBeatTimeSeconds(guideBeatIndex);
            }

            for (int i = 0; i < VisibleNoteCount; i++)
            {
                double noteSec = timing.GetBeatTimeSeconds(guideBeatIndex + i);
                if (noteSec >= timing.gameplayEndSec)
                {
                    break;
                }

                upcomingTimes.Add(noteSec);
                upcomingOrdinals.Add(
                    guideBeatIndex + i - timing.FirstGameplayBeatIndex);
            }

            return upcomingTimes.Count > 0;
        }

        private bool ResolveBeatmap(double songSec)
        {
            if (futureBeatmap == null || futureBeatmap.events == null)
            {
                return false;
            }

            while (beatmapEventIndex < futureBeatmap.events.Count)
            {
                BeatmapEvent current = futureBeatmap.events[beatmapEventIndex];
                double currentSec = timing.gameplayStartSec +
                    current.TimeMs(futureBeatmap.bpm) / 1000d;
                if (current.guideVisible &&
                    songSec <= currentSec + nowDuration)
                {
                    break;
                }

                beatmapEventIndex++;
            }

            for (int i = beatmapEventIndex;
                i < futureBeatmap.events.Count &&
                upcomingTimes.Count < VisibleNoteCount;
                i++)
            {
                BeatmapEvent note = futureBeatmap.events[i];
                if (!note.guideVisible)
                {
                    continue;
                }

                upcomingTimes.Add(timing.gameplayStartSec +
                    note.TimeMs(futureBeatmap.bpm) / 1000d);
                upcomingOrdinals.Add(i);
            }

            return upcomingTimes.Count > 0;
        }

        private void UpdateGhostGuides(int baseCutIndex)
        {
            int visibleGhosts = Mathf.Min(
                upcomingTimes.Count - 1,
                upcomingGhostLines.Length);
            for (int i = 0; i < upcomingGhostLines.Length; i++)
            {
                Transform ghost = upcomingGhostLines[i];
                if (ghost == null)
                {
                    continue;
                }

                bool visible = i < visibleGhosts;
                ghost.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                int futureCutIndex = baseCutIndex + i + 1;
                if (!showingDemoPattern)
                {
                    futureCutIndex %= Mathf.Max(1, salmon.cutsPerFish);
                }

                ghost.position = salmon.GetCutWorldPosition(futureCutIndex);
                if (i < ghostBaseScales.Length)
                {
                    ghost.localScale = ghostBaseScales[i] *
                        Mathf.Lerp(0.92f, 0.66f,
                            i / (float)Mathf.Max(1,
                                upcomingGhostLines.Length - 1));
                }

                if (i < upcomingGhostRenderers.Length)
                {
                    float distance01 = i / (float)Mathf.Max(
                        1,
                        upcomingGhostLines.Length - 1);
                    float brightness = Mathf.Lerp(0.78f, 0.32f, distance01);
                    SetRendererColor(
                        upcomingGhostRenderers[i],
                        new Color(
                            0.35f * brightness,
                            0.92f * brightness,
                            brightness,
                            1f));
                }
            }
        }

        private void UpdateRhythmLane(double songSec, bool resting)
        {
            if (rhythmLane == null || upcomingNoteDots == null)
            {
                return;
            }

            VisibleUpcomingCount = 0;
            float halfTravel = Mathf.Max(120f, rhythmLane.rect.width * 0.43f);
            double horizon = timing.BeatLengthSeconds * laneHorizonBeats;
            for (int i = 0; i < upcomingNoteDots.Length; i++)
            {
                Image dot = upcomingNoteDots[i];
                if (dot == null)
                {
                    continue;
                }

                double deltaSeconds = i < upcomingTimes.Count
                    ? upcomingTimes[i] - songSec
                    : double.MaxValue;
                bool visible = i < upcomingTimes.Count &&
                    deltaSeconds >= -nowDuration &&
                    deltaSeconds <= horizon;
                dot.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                float normalized = Mathf.Clamp01((float)(
                    deltaSeconds / horizon));
                dot.rectTransform.anchoredPosition =
                    new Vector2(normalized * halfTravel, 0f);
                float scale = i == 0 ? 1.35f : Mathf.Max(0.72f, 1f - i * 0.1f);
                dot.rectTransform.localScale = Vector3.one * scale;
                float alpha = i == 0 ? 1f : Mathf.Max(0.2f, 0.62f - i * 0.14f);
                if (resting)
                {
                    alpha *= 0.58f;
                }

                dot.color = i == 0
                    ? new Color(1f, 0.82f, 0.24f, alpha)
                    : new Color(0.36f, 0.92f, 1f, alpha);
                VisibleUpcomingCount++;
            }
        }

        private void ApplyBracketPose(
            Transform left,
            Transform right,
            float approach,
            float distanceMultiplier)
        {
            float distance = Mathf.Lerp(
                wideBracketDistance,
                closedBracketDistance,
                Smooth01(approach)) * distanceMultiplier;
            if (left != null)
            {
                left.localPosition = new Vector3(-distance, 0f, 0f);
            }

            if (right != null)
            {
                right.localPosition = new Vector3(distance, 0f, 0f);
            }
        }

        private void SetVisible(bool shouldShow, bool showWorldCue = true)
        {
            bool cueVisible = shouldShow && showWorldCue;
            if (cueRoot != null && cueRoot.activeSelf != cueVisible)
            {
                cueRoot.SetActive(cueVisible);
            }

            if (rhythmLane != null &&
                rhythmLane.gameObject.activeSelf != shouldShow)
            {
                rhythmLane.gameObject.SetActive(shouldShow);
            }

            if (!shouldShow)
            {
                SetTextVisible(spacePromptText, false);
                SetTextVisible(nowText, false);
                SetTextVisible(restPromptText, false);
            }
        }

        private void SetCueColor(Color color)
        {
            SetRendererColor(cutGuideLine, color);
            for (int i = 0; i < bracketRenderers.Length; i++)
            {
                SetRendererColor(bracketRenderers[i], color);
            }
        }

        private void CaptureGhostScales()
        {
            ghostBaseScales = new Vector3[upcomingGhostLines.Length];
            for (int i = 0; i < upcomingGhostLines.Length; i++)
            {
                ghostBaseScales[i] = upcomingGhostLines[i] != null
                    ? upcomingGhostLines[i].localScale
                    : Vector3.one;
            }
        }

        private void CaptureGuideLineScale()
        {
            if (cutGuideLine != null)
            {
                guideLineBaseScale = cutGuideLine.transform.localScale;
            }
        }

        private void UpdateGuideLineScale(float approach, bool pulse)
        {
            if (cutGuideLine == null)
            {
                return;
            }

            float emphasis = 1f + Smooth01(approach) * 0.18f +
                (pulse ? 0.52f : 0f);
            cutGuideLine.transform.localScale = new Vector3(
                guideLineBaseScale.x * emphasis,
                guideLineBaseScale.y * emphasis,
                guideLineBaseScale.z);
        }

        private void ResetGuideLineScale()
        {
            if (cutGuideLine != null)
            {
                cutGuideLine.transform.localScale = guideLineBaseScale;
            }
        }

        private void HideGhostGuides()
        {
            for (int i = 0; i < upcomingGhostLines.Length; i++)
            {
                if (upcomingGhostLines[i] != null)
                {
                    upcomingGhostLines[i].gameObject.SetActive(false);
                }
            }
        }

        private void ShowRestOnly(double songSec)
        {
            CurrentApproach01 = 0f;
            IsResting = true;
            VisibleUpcomingCount = 0;
            ResetGuideLineScale();
            HideGhostGuides();
            UpdateRhythmLane(songSec, true);
            UpdateLanePresentation(songSec, true);
            SetTextVisible(spacePromptText, false);
            SetTextVisible(nowText, false);
            SetTextVisible(restPromptText, true);
            playerKnife?.SetVisible(false);
            SetVisible(true, false);
        }

        private void UpdateLanePresentation(double songSec, bool resting)
        {
            if (rhythmLaneBackground != null)
            {
                rhythmLaneBackground.color = resting
                    ? new Color(0.025f, 0.04f, 0.05f, 0.62f)
                    : new Color(0.035f, 0.055f, 0.065f, 0.92f);
            }

            if (hitCursor == null)
            {
                return;
            }

            float beatPulse = GetNearestBeatPulse(songSec);
            hitCursor.color = resting
                ? new Color(0.35f, 0.82f, 0.92f,
                    Mathf.Lerp(0.2f, 0.5f, beatPulse))
                : new Color(1f, 0.78f, 0.24f,
                    Mathf.Lerp(0.72f, 1f, beatPulse));
            hitCursor.rectTransform.localScale = new Vector3(
                1f + beatPulse * 0.18f,
                1f + beatPulse * 0.35f,
                1f);
        }

        private float GetNearestBeatPulse(double songSec)
        {
            int beatIndex = timing.GetBeatIndexAtOrBefore(songSec);
            double currentBeat = timing.GetBeatTimeSeconds(beatIndex);
            double nextBeat = timing.GetBeatTimeSeconds(beatIndex + 1);
            double nearest = Math.Min(
                Math.Abs(songSec - currentBeat),
                Math.Abs(nextBeat - songSec));
            double pulseWindow = timing.BeatLengthSeconds * 0.18d;
            return 1f - Mathf.Clamp01((float)(nearest / pulseWindow));
        }

        private void SetRendererColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }

        private static void SetTextVisible(Text target, bool shouldShow)
        {
            if (target != null && target.gameObject.activeSelf != shouldShow)
            {
                target.gameObject.SetActive(shouldShow);
            }
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
