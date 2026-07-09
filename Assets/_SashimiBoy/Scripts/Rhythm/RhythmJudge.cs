using System;
using UnityEngine;

namespace SashimiBoy
{
    public static class RhythmJudge
    {
        public static JudgeResult Judge(float bpm, double inputTimeMs, double noteTimeMs, float globalOffsetMs, RhythmDifficulty difficulty)
        {
            double adjustedOffset = (inputTimeMs - noteTimeMs) - globalOffsetMs;
            return JudgeOffset(bpm, adjustedOffset, difficulty);
        }

        public static JudgeResult JudgeOffset(float bpm, double adjustedOffsetMs, RhythmDifficulty difficulty)
        {
            float y = GetMaxWindowMs(bpm, difficulty);
            double abs = Math.Abs(adjustedOffsetMs);
            JudgeGrade grade;

            if (abs <= y * 0.18f)
            {
                grade = JudgeGrade.Nasty;
            }
            else if (abs <= y * 0.48f)
            {
                grade = JudgeGrade.Smooth;
            }
            else if (abs <= y)
            {
                grade = JudgeGrade.Slipped;
            }
            else
            {
                grade = JudgeGrade.Whack;
            }

            TimingSide side = TimingSide.Center;
            if (adjustedOffsetMs < -0.0001d)
            {
                side = TimingSide.Early;
            }
            else if (adjustedOffsetMs > 0.0001d)
            {
                side = TimingSide.Late;
            }

            return new JudgeResult(grade, side, adjustedOffsetMs, y);
        }

        public static float GetMaxWindowMs(float bpm, RhythmDifficulty difficulty)
        {
            float clamped = Mathf.Clamp(bpm, 120f, 160f);
            float sixteenthMs = 15000f / clamped;

            if (difficulty == RhythmDifficulty.Casual)
            {
                return sixteenthMs;
            }

            if (difficulty == RhythmDifficulty.Strict)
            {
                return sixteenthMs * 0.5f;
            }

            return sixteenthMs * 5f / 8f;
        }
    }

    public readonly struct JudgeResult
    {
        public readonly JudgeGrade grade;
        public readonly TimingSide timingSide;
        public readonly double offsetMs;
        public readonly float maxWindowMs;

        public JudgeResult(JudgeGrade grade, TimingSide timingSide, double offsetMs, float maxWindowMs)
        {
            this.grade = grade;
            this.timingSide = timingSide;
            this.offsetMs = offsetMs;
            this.maxWindowMs = maxWindowMs;
        }

        public bool IsHit => grade != JudgeGrade.Whack;
        public float ScoreCoefficient
        {
            get
            {
                switch (grade)
                {
                    case JudgeGrade.Nasty: return 1f;
                    case JudgeGrade.Smooth: return 0.7f;
                    case JudgeGrade.Slipped: return 0.3f;
                    default: return 0f;
                }
            }
        }

        public override string ToString()
        {
            return $"{grade} {timingSide} ({offsetMs:0.0}ms)";
        }
    }
}
