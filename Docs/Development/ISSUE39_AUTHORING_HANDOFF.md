# Issue 39 Host authoring handoff — 2026-09-08

SOURCE_STATUS: IMPLEMENTED_PENDING_HOST_AUTHORING_AND_VALIDATION
HOST_AUTHORING_ACTION: REQUIRED
PRODUCTION_CHART_GENERATED: NOT_RUN
UNITY_COMPILE: NOT_RUN
EDITMODE: NOT_RUN
PLAYMODE: NOT_RUN
PR_REVIEW_READY: NO
COMMIT_PUSH: NOT_RUN

Branch remains `feat/39-semantic-phase-gates`. Existing dirty/untracked Issue39
work is preserved. SPEC_VERSION is 1.0.2; its HEAD blob is
`6d7de6e6abef13b18021a3591debc53ac00616d4`. The Host-supplied latest Owner v2
decision governs this manual source continuation. No queue/preflight wrapper,
network, Unity launch, Git mutation, policy edit or Project transition was run.

## Exact Host sequence

1. In this existing isolated Issue39 project, let Unity 6000.4.0f1 import and
   compile the source. Do not open this same project in a second Editor.
2. Outside Play mode, resolve/save any unsaved scenes and any unsaved canonical
   pattern/chart Inspector edits yourself. The action refuses unsaved work.
3. Invoke **Sashimi Boy/Stage 01/Apply Approved Phase Chart** once.
   Stable public static batch counterpart:
   `SashimiBoy.EditorTools.Stage01ApprovedPhaseChartAuthoring.ApplyApprovedPhaseChartBatch`.
   This method exists in `Assets/_SashimiBoy/Scripts/Editor/Stage01ApprovedPhaseChartAuthoring.cs`.
4. Inspect the Console report and saved asset: six ranges/counts, actual first
   and last song times, repeated-note count, weights, thresholds, identity,
   paths and successful saved binding. Initial weights must be Nasty=1.00,
   Clean=0.75, Slipped=0.35, Whack=0.00; all six gates Ratio=0.60.
5. Test Runner: **EditMode → Run All**, then **PlayMode → Run All**. Export new
   XMLs and inspect Console errors, Missing Script/reference diagnostics and
   required regressions. Do not reuse the previous 73/73 + 15/15 XMLs as results
   for these changes, and do not force the old total of 88.

No dirty-rejecting automation wrapper is needed for this sequence. The action
itself only protects unsaved Editor work, not the expected dirty Git checkout.

## Canonical inputs and output

- Scene: `Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity`
- Pattern: `Assets/_SashimiBoy/Data/Generated/Stage01NotePattern.asset`
- Music: `Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3`
- Chart output: `Assets/_SashimiBoy/Data/Generated/Stage01SemanticBeatmap.asset`

The Scene's actual musicClip reference is used. An empty AudioSource.clip is
allowed because existing ConfigureAudio binds musicClip at runtime; a different
non-null clip is refused. Clip sample bounds are checked in Editor, not guessed
from absent upload data. The authoring report is the source of actual ranges
and song times. Existing regression source records 157 expanded notes; this
run has not executed Unity to remeasure them.

The menu validates before persistent writes, expands with the same provider
and timing validation used by runtime, creates the owned chart, and saves its
reference on the canonical timing component before runtime Awake. It does not
run the art/scene generators or play audio. It saves only the owned asset and,
when binding changes, the intended Stage01 Scene. No global SaveAssets or
SaveOpenScenes call is used. Other scenes are never saved or discarded; a
temporarily opened clean Stage01 scene is closed, and the previous active
scene is restored. A failed Scene save is reported, leaving work available
for recovery rather than claiming a completed binding.

Same source and existing valid chart: validate/report, preserve manual tuning,
do not rewrite the asset or Scene. Different chart binding, unknown ownership,
unimported/orphaned output, dirty asset or invalid source: refuse. Stale chart:
refuse normal Apply. After reviewing the source change, explicitly use
**Sashimi Boy/Stage 01/Regenerate Approved Phase Chart...**, whose confirmation
states that all phase tuning will be replaced. That operation updates the same
owned asset, retaining its meta/GUID. Runtime does not auto-create/fill missing
charts. Both Stage01 generator sources load the saved chart path so later
regeneration retains the hookup; they do not silently re-author stale data.

## Source changes

- `Scripts/Rhythm/Semantics/PhasePerformanceTracker.cs`: approved quality
  defaults; missing configuration now reports MISSING_CHART instead of the
  obsolete product-decision sentinel AUTHORING_PENDING.
- New `Scripts/Rhythm/Semantics/ApprovedPhaseChart.cs`: six contiguous balanced
  groups with long products, strict schedule validation, 0.60 Ratio defaults
  and documented stable semantic payload IDs.
- `Scripts/Rhythm/SemanticBeatmapDefinition.cs`: version/source identity and
  exact music/pattern references, with safe stale rejection.
- `Scripts/Rhythm/Stage01SalmonTimingScaffold.cs`: shared public source
  validation, bounded timing check and identity validation before gate reset.
- `Scripts/Rhythm/Stage01NotePatternProvider.cs`: long intermediate expansion
  arithmetic, preserving existing note times/order/IDs.
- New `Scripts/Editor/Stage01ApprovedPhaseChartAuthoring.cs`: complete scoped
  menu/batch application and explicit regenerate path.
- `Scripts/Editor/SashimiBoyPrototypeGenerator.cs` and
  `Art/Generated/Editor/Stage01SalmonPresentationPipeline.cs`: minimal saved
  chart reference hookup only.
- `Tests/Shared/SemanticTestFixture.cs`: synthetic test-owned assets explicitly
  stamp their actual source; production runtime has no fixture fallback.
- `Tests/EditMode/PhasePerformanceEditModeTests.cs`: Clean remains the default
  test helper and contributes 0.75. Adjusted one-Clean boundaries to 0.375 ratio
  / 0.75 points, total Clean quality to 9, snapshot quality to 1.5. Mixed quality
  remains 2.1; tests retain their original intent.
- New `Tests/EditMode/ApprovedPhaseChartEditModeTests.cs` and extended
  `Tests/PlayMode/SemanticPhasePlayModeTests.cs`: cases below.
- This handoff and `ISSUE39_APPROVED_AUTHORING.md` document the v2 contract.

Paths above are relative to `Assets/_SashimiBoy` except the development docs.
Host import must generate metadata for the three new C# files
ApprovedPhaseChart.cs, Stage01ApprovedPhaseChartAuthoring.cs and
ApprovedPhaseChartEditModeTests.cs, and later for the new chart asset. Existing
metadata/GUIDs were not replaced. No .asset or Scene was generated in this run.
SliceCuePresenter NaN hide/recovery and AudioClock runtime/tests were not edited
by this continuation. PlayMode teardown restores timing and the original chart
before destroying only test-owned ScriptableObjects.

## Regression source coverage

- `Partition_CoversActualIdsOnceWithoutChangingTimes`: N=6/12/13/157,
  nonempty union, balanced counts, exact ID coverage and unchanged times.
- `Partition_RejectsFewerThanSix`, `Partition_RejectsNonfiniteOrUnordered`.
- `ApprovedWeights_AreMonotonicBestToWorst` and
  `LegacyWindowsGradeMapAndScore_AreUnchanged`: both timing signs and exact
  45/90/140ms boundaries plus just beyond, original scores and semantic mapping.
- `ApprovedThreshold_ReachableGradesWaitForLastMiss`: 6 Nasty/4 automatic
  Whack = .60 success; 5 Nasty/1 Clean/4 Whack = .575 failure; no early gate.
- `ApprovedThreshold_AdjacentDoubleAboveReachableRatioFails`: binary numeric
  boundary comparison, no rounded display-string comparison.
- `SavedProductionChart_RealExpansionBindingAndIdempotence` (**RequiresHostAuthoring**):
  actual saved chart/Scene, repeated source schedule, two Apply calls with
  unchanged bytes/GUID/asset inventory, cloned pattern change stales identity.
- `InvalidSource_ActionLeavesSavedAssetAndSceneUntouched` (**RequiresHostAuthoring**):
  invalid test-owned Scene timing state refuses without persistent changes.
- `ProductionScene_SavedChartStartsWithoutFixtureInjection` and
  `ProductionChart_AllNastyPassesSixGatesOnlyAfterFinalNote`
  (**RequiresHostAuthoring**): real saved binding, validation, first phase,
  existing music/input and all six gates; no Configure injection.
- `InvalidAndStaleCharts_RejectWithoutChangingProductionAsset`: malformed
  test-owned chart, bad identity and changed timing source reject start.
- `LateMissesAcrossBoundary_ResolveInNoteOrderAndStopAtFailedGate`: multiple
  misses cross a successful first boundary then fail the next gate in note order.
- `FailingHit_PreservesLegacyScoreWithoutFinalizingSuccess`: now actual Slipped
  hits at +110ms versus .60, expected 300 per hit and preserved combo, no reward
  or clear. Exact Nasty hits are covered separately as success.
- Existing retry/late prior-run outcome, one physical edge, failure-once,
  snapshot and NaN cue recovery tests remain. MissingChart explicitly removes
  the test Scene field and retries; it no longer assumes production is unbound.

These are source additions, not executed PASS results. Source-only
`git diff --check` was run with exit 0. Three harmless read-query typos used a
word instead of an integer for Select-Object -First, were corrected, and caused
no writes or permission escalation. Initial Docs/Development search reported
that the directory did not yet exist; it is now created through the file editor.
No necessary operation was permission-denied. Unity import/compile, full tests,
serialized reference/meta checks and Console scans remain Host work.

## Remaining scope

Final salmon art, hand/pincer/plate animation (#40/#41), actual part/animation
consumers, full failure-return choreography and presentation reset, and scenario
integration are not completed here. Semantic payloads and failure/reset events
are contracts for those consumers, not evidence that visuals are integrated.
This is not completion of the entire two-day prototype. No merge, Done state,
Reviewer PASS or PR readiness is claimed. Stop after Issue39 Host validation.
