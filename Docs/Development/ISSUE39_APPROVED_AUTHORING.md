# Issue 39 approved prototype authoring

Authority: `owner-issue39-approved-authoring-20260908-v2` (2026-09-08).
This supersedes the earlier reversed Clean/Nasty quality defaults and pending
range/threshold product decision. These are prototype initial values, not final
musical phrase tuning.

- Quality: Nasty 1.00, Clean 0.75, Slipped 0.35, Whack 0.00.
- Legacy Nasty remains best; Smooth maps to semantic Clean. Score, combo, yield
  and 45/90/140 ms judgement windows are unchanged.
- Use the existing provider's expanded playable schedule, including repeated
  playable notes once under their existing sequence IDs. Boss demonstration
  notes are not another input to authoring.
- For N >= 6, phase p uses `[floor(p*N/6), floor((p+1)*N/6))` with long integer
  products. N < 6, nonfinite/unordered times and inconsistent IDs are rejected.
- Six Ratio gates initially require 0.60. WholeFish/Prepare/WholeFish,
  Head/CutHead/Head, Fins/RemoveFin/Fins, Spine/SeparateSpine/Spine,
  PinBones/PullPinBone/PinBones, Slicing/SliceFillet/Fillet follow enum contracts.
  Complete is terminal, not a seventh scoring group.
- Gates evaluate only after the last authored note resolves. Automatic misses
  remain zero quality in the denominator. Existing ordered miss processing,
  snapshot, duplicate input and retry/run identity guards remain in use.

Payload IDs are `stage01.phase.<FishPhase>.success.v1` and
`stage01.phase.<FishPhase>.failure.v1`, where FishPhase is exactly WholeFish,
Head, Fins, Spine, PinBones or Slicing. These are semantic event identifiers,
not references to animation assets. Optional anchor, hand animation and cue
strings are deliberately empty: no absent asset is described as connected.
Each expanded note has repetitionCount 1; provider repetition is already
expanded and must not create additional scoring events.

`SemanticBeatmapDefinition` stores the approval version, music/pattern object
references, and SHA-256 identity of a binary source description: timing inputs,
clip name/sample metadata, authored pattern bar/step inputs and expanded note
IDs/times/source bars/playback bars/repeat flags. Note states and user latency
calibration are excluded. Runtime rebuilds the original schedule and checks
identity before starting the gate snapshot. Source changes require deliberate
re-authoring; note times are never adjusted to fit a stale chart.

Use the explicit Editor action documented in ISSUE39_AUTHORING_HANDOFF.md.
The action preserves later valid manual per-phase tuning on repeat application.
Explicit Regenerate replaces the owned chart's values and updates its identity,
retaining its asset path and GUID. Neither startup nor scene regeneration
creates a missing chart. Missing data reports MISSING_CHART; invalid timing and
stale identity refuse gameplay safely.

Actual asset creation, Scene serialization, Unity compile and tests have NOT
RUN during this source continuation. Prior 73 EditMode / 15 PlayMode results
are baseline evidence only.
