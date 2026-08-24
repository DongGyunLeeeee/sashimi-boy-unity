# Stage 01 Salmon Music Notes

> File analyzed: `stage01_salmon_main.mp3`  
> Intended Unity path: `Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3`  
> Analysis date: 2026-07-15  
> Purpose: Stage 1 music timing reference for Sashimi Boy. This is **not** the final beatmap.

---

## 1. Core Timing

```yaml
stage_id: stage01_salmon
music_file: Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3
fish: salmon
stage_type: first_stage_tutorial

analysis_status: estimated_by_audio_analysis
confidence:
  bpm: high
  first_downbeat: medium_high
  structure: medium

sample_rate_hz: 48000
channels: stereo_source_mp3_decoded_to_mono_for_analysis
duration_sec: 121.160

time_signature: 4/4
bpm: 88.0
beat_length_sec: 0.681818
bar_length_sec: 2.727273

music_start_sec: 0.000
first_audible_attack_sec: 0.683
first_downbeat_sec: 0.683

has_count_in: false
count_in_bars: 0
recommended_intro_bars_for_boss_demo: 4

# Recommended first implementation values:
gameplay_start_sec: 11.592      # bar 5, after 4-bar boss demo / visual tutorial intro
gameplay_end_sec: 120.683       # bar 45, end of bar 44 / result transition point
result_transition_sec: 120.683
clip_tail_end_sec: 121.160

# MP3 decoding can shift timing slightly between tools.
# Keep these exposed in Inspector/debug UI:
manual_audio_offset_ms: 0
manual_input_latency_ms: 0
```

---

## 2. Beat Grid Formula

Use this formula for generated beat/bar debug displays:

```text
beat_time_sec = first_downbeat_sec + beat_index * 0.681818
bar_time_sec  = first_downbeat_sec + (bar_index - 1) * 2.727273
```

Where:

```text
beat_index starts at 0
bar_index starts at 1
```

Important bar starts:

| Bar | Time sec | Intended use |
|---:|---:|---|
| 1 | 0.683 | Music grid begins / short pre-roll resolves into first downbeat |
| 5 | 11.592 | Recommended player gameplay start |
| 13 | 33.410 | Main slicing section begins |
| 29 | 77.046 | Difficulty ramp / pre-final section |
| 37 | 98.865 | Final hook / payoff section |
| 45 | 120.683 | Gameplay ends, transition to result |

---

## 3. Suggested Stage Structure

The track fits cleanly into approximately **44 bars at 88 BPM**, with a short tail after bar 44.

| Bars | Time sec | Section role | Gameplay suggestion |
|---:|---:|---|---|
| 1-4 | 0.683-11.592 | Intro / boss demonstration | No scoring or very forgiving input. Show boss slicing rhythm and visible cut line. |
| 5-12 | 11.592-33.410 | First playable hook/tutorial | Space-only quarter-note slices. Keep guide line visible. Mostly safe NASTY/SMOOTH windows. |
| 13-28 | 33.410-77.046 | Main verse / core slicing | Introduce simple syncopation and short 2-hit patterns. Still avoid holds and multi-lane input. |
| 29-36 | 77.046-98.865 | Pre-final ramp | Reduce visual assistance slightly. Add occasional distraction preview, but keep it tutorial-safe. |
| 37-44 | 98.865-120.683 | Final hook / payoff | Denser but readable slicing. Use this section for satisfying final fish yield buildup. |
| Tail | 120.683-121.160 | Result transition | Stop input, freeze final slice result, transition to plate/result UI. |

---

## 4. Stage 1 Gameplay Intention

```yaml
intended_input:
  primary_input: Space
  multi_lane: false
  hold_notes: false
  swipe_notes: false
  first_stage_should_prioritize: timing_feel_over_complexity

judgement_recommendation:
  perfect_label: NASTY
  good_label: SMOOTH
  miss_label: SLIPPED
  wrong_or_noise_label: WHACK
  perfect_window_ms: 45
  good_window_ms: 90
  miss_window_ms: 140

recommended_note_density:
  bars_1_to_4: none_or_demo_only
  bars_5_to_12: 1_to_2_inputs_per_bar
  bars_13_to_28: 2_to_4_inputs_per_bar
  bars_29_to_36: 2_to_4_inputs_per_bar_with_light_syncopation
  bars_37_to_44: 3_to_5_inputs_per_bar_but_still_space_only
```

---

## 5. Integration Notes for Codex / Unity

Ask Codex to use these values first:

```text
BPM: 88.0
first_downbeat_sec: 0.683
gameplay_start_sec: 11.592
gameplay_end_sec: 120.683
```

Recommended implementation order:

```text
1. Import the MP3 at:
   Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3

2. Load the clip into Stage01_Salmon music config.

3. In Stage01_Salmon, display:
   - song time
   - current beat
   - current bar
   - nearest beat offset in ms
   - manual audio offset ms
   - manual input latency ms

4. Use AudioSettings.dspTime / PlayScheduled for playback.

5. Do not finalize the beatmap automatically.
   First create a debug/authoring mode where pressing Space records note candidates.
```

---

## 6. Codex Prompt Snippet

```text
Use the Stage 01 Salmon music timing values from:
Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_notes.md

Important timing:
- bpm = 88.0
- first_downbeat_sec = 0.683
- gameplay_start_sec = 11.592
- gameplay_end_sec = 120.683

Implement only the timing/music scaffold first.
Do not claim to create the final beatmap.
Add a debug overlay showing song time, beat, bar, nearest-beat offset, and adjustable manual offsets.
Keep input Space-only for Stage 1.
```

---

## 7. Verification Checklist

Before making the final beatmap, verify by ear in Unity:

```text
[ ] At song time ~0.683, the debug overlay should show Bar 1 / Beat 1.
[ ] At song time ~11.592, the debug overlay should show Bar 5 / Beat 1.
[ ] At song time ~33.410, the debug overlay should show Bar 13 / Beat 1.
[ ] At song time ~77.046, the debug overlay should show Bar 29 / Beat 1.
[ ] At song time ~98.865, the debug overlay should show Bar 37 / Beat 1.
[ ] At song time ~120.683, input should stop and result transition should begin.
[ ] If the beat feels late/early, adjust manual_audio_offset_ms before editing notes.
```

---

## 8. Notes / Caveats

- This analysis was produced from the uploaded MP3. MP3 encoder/decoder padding may introduce a small timing difference in Unity.
- For final rhythm tuning, consider exporting a WAV or OGG from the same source and rechecking `first_downbeat_sec`.
- The first beat grid is estimated at `0.683 sec`; if Unity playback feels slightly behind/ahead, expose a calibration slider rather than changing note times directly.
- Stage 1 should remain a tutorial: no holds, no multiple lanes, no heavy distractions until the player understands the slicing rhythm.
