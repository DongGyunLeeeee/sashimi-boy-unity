# Stage3 / Day3 playable delivery — Issue #87

## Current development baseline

The Owner requested preservation of all current-game corrections and an end-to-end third stage on 2026-10-31. Issue #87 is the single delivery issue. Its latest Owner decision approves the playable prototype parameters. The older #74–#79 descriptions remain detailed acceptance references; their old unresolved values are superseded only within the approved #87 prototype.

The `codex/87-stage3-playable` branch includes normal local merges of these exact dependency heads:

- #69: `be6335316e8821f4db252b60912a857bbc3a5b7c` — expanded ten-slot home, corrected entrances, no redundant wake action, stage-clear screen, save-to-title behavior, seated Cheolsu and rockfish presentation.
- #81: `e21a65fe47fcdc3a6cc7d6e2c002ea001ebab785` — supplied ten-role fish model presentation.
- #84: `f94b821a451379f891285e282843e2116d9a7493` — supplied Junseok/Jaehoon models and materials.
- #86: `022772e953b69072accdd354f10de41e652e6c28` — approved vocal audio.

These are local integration commits, not agent-performed GitHub PR merges. Until the Owner merges the delivery PR, an origin/main-only build or the audio-only branch is not the complete corrected game. Choose an executed, verified Player by its launch receipt and source commit, not by a folder's newest timestamp. Keep the existing corrected Player intact.

## Gameplay contract

Stage3 retains `STAGE_03_SOLE` and `FishType.Sole`, with the displayed name 가자미. It uses the existing connected Kevin body. The bounded RiggedBody experiment is not silently substituted.

The separate nine-phase Stage3 controller leaves the serialized six-phase Stage1/2 chart and judgement contracts intact:

1. WholeFish → head removal → HeadRemovedFish and independent FishHead.
2. FrontFillet 1 and 2 → FilletRemoved1 and FilletRemoved2.
3. E turnover after the two front parts are stored; music/presentation gates prevent back work before turnover completes.
4. BackFillet 1 and 2 → FilletRemoved3 and FilletRemoved4; hide the body after every separated part is stored.
5. Work the four stored fillets individually. The prototype uses front 1, front 2, back 1, back 2.
6. Six accepted cuts per fillet produce exactly 24 supplied SashimiSlice instances. All four actual completions are required before the plate/result transition.

The prototype has six opportunities requiring four accepted hits in each preparation phase, and eight opportunities requiring six accepted cuts in each slicing phase. Early/duplicate/paused/stopped input cannot advance a cut. Too many misses fail the current phase; R retries that phase, removes only its partial slices, restores that fillet, and restores its phase score without farming rewards. Completed earlier preparation is retained. There is no automatic slice top-up.

Esc pauses music and progression. A failed phase offers retry/return directly. Returning before completion preserves the shop work checkpoint and grants no clear reward. The result records the clear once; a save error retains the result and retries the write rather than awarding the reward again.

## Asset and cutting presentation

All supplied English role folders, source bytes, GUIDs and model names remain intact. Positive uniform placement transforms orient the body and working fillet across the existing knife. Head/preparation cues come from the actual supplied part/body bounds, not a fixed point outside the fish.

`Stage03FilletCutAuthoring` precomputes five remaining-mesh states per front/back source and a sixth exhausted state. Each remaining mesh includes the original exterior and a closed cut face. Runtime changes shared mesh references; it does not boolean the high-density source every frame or replace real cutting with scale shrinkage. The support hand follows the remaining fillet bounds and the knife stroke stays at the accepted cut plane before moving to the next plane.

Player inspection exposed two presentation defects despite the passing functional suite: an inherited demonstration hand obscured serving slots, and planar cap UVs sampled unrelated islands from the exterior atlas. The Stage3 generator now disables only those inherited demonstration objects and removes their unused presenter. Cut faces use an opaque, nonmetallic flesh color sampled deterministically from the supplied SashimiSlice reference; exterior mesh UVs and materials remain unchanged.

The Stage3 stance solver fits the grounded body to both arms' actual wrist reach regions before posing, preserving bone lengths, palm targets, body height and scale. This addresses the late-filleting reach error rather than stretching an arm. Completion moves the camera to the served dish, hides the working body/knife, and leaves the dish visible above the result actions. A reset restores their authored visibility.

Authoritative generation:

- `SashimiBoy.EditorTools.Stage03PlayableAuthoring.ApplyBatch`
- `SashimiBoy.EditorTools.Stage03DayWorldAuthoring.ApplyBatch`
- `SashimiBoy.EditorTools.Stage03FilletCutAuthoring.BuildBatch`

Generated text normalization is confined to the owning generators. Stage1/2 scene bytes are asserted unchanged during Stage3 generation. The new scene also uses the existing full serialized-reference/material/listener/EventSystem scan.

## Shared workstation interface

The latest Owner decision requires Stage3 to use the Stage1/2 presentation. `Stage03HudAuthoring` copies the actual Stage2 canvas and world cutting cues without saving either source stage. It preserves the 1920×1080 canvas scaling, compact CookingFocusHUD, fonts, judgement images, four-note lane, SPACE prompts, world brackets and right-side result card. The former independent Stage3 header and lower note lane are removed.

`Stage03HudView` reads the Stage3 clock and nine-phase state into those shared visuals. The retained Stage01SalmonHUD supplies visual references and judgement presentation only; its legacy timing owner stays unbound and disabled. Stage3 starts automatically and shows the same four-beat countdown before its unchanged first chart note. Stage3's approved E turnover, phase retry, pause and save-error recovery remain Stage3 behavior, styled with the baseline card. The plate camera leaves the baseline result card clear of the served dish.

## Music prototype

The source remains the complete 230.92-second, 48 kHz stereo vocal WAV, SHA-256 `3572605dec84991aabf1e4911eee443287da0f7e5c403b0a449aee403cf31062`. No trimming, re-encoding or source/meta replacement is performed.

`STAGE03_RHYTHM_ANALYSIS.json` records reproducible spectral-flux/autocorrelation and pulse-grid analysis, an estimated 96.53 BPM, grid phase 0.160 seconds and 62 explicit note times. Notes are snapped to source onsets within 120 ms. The current playable chart runs from 10.176 to 133.184 seconds with the source intro and a short completion presentation. Loading the complete vocal file does not mean that this prototype charts all 230.92 seconds.

The chart is an Owner-approved playable draft. Human listening and rhythm-readability approval remain pending. Stage1/2 source audio, timing, charts and judgement windows are not altered.

## Day3, existing saves and the endpoint

Day2 sleep offers Continue to Day3. Supported old Day2-complete saves regain Day3 access without repeating past clears, purchases or plate rewards. Mid-cut Stage3 persistence is intentionally unsupported: continuing returns to the saved shop work checkpoint without granting a clear.

Day3: required Junseok first encounter → fish work → result → seated Cheolsu second encounter in the shop → Plugin purchase → placement/practice in home slot 03 → required Jaehoon first encounter on the street → sleep → 3스테이지 클리어.

The supplied dialogue is preserved as speech, inner thought and action. The static NPC's handshake action is presented as an action caption/camera beat; a newly rigged hand animation is not claimed. Future encounters/concert dialogue are not revealed early.

Save and Exit writes successfully before returning to the original Bootstrap menu. Continue restores the supported checkpoint. The Stage3 daily-clear screen shows disabled 다음 스테이지 준비 중 for Stage4.

## Required preparation and evidence

A newly managed worktree can contain Git LFS pointer files even when Git status is clean. Before any import/build:

1. Materialize the local cached LFS payloads in the isolated worktree.
2. Compare every indexed full SHA-256 OID and byte size against its actual file.
3. Refuse pointers/missing/mismatched objects. Do not call an import with pointer files a successful asset validation.
4. Use the native resource-protected Unity wrappers and a fresh registered validation worktree for strict full tests.

`Stage03BuildInputGuard` rejects the known asset pointer types before authoring or Player packaging. It complements, rather than replaces, full payload hashing.

Executed native commands, exact commits, XML counts, skipped/inconclusive results, integrity checks, failures and independent review are recorded in the delivery PR and external run artifacts. This handoff is not itself a test PASS.

An explicitly enabled development Player evidence probe uses a fresh isolated validation profile, real music-clock time and public gameplay APIs. It can capture Junseok → work → 24 slices → Cheolsu → Plugin → Jaehoon → sleep, including a cut-face side view. It distinguishes synthetic API input and fixture travel from physical keyboard/mouse/navigation evidence. It hashes the default save before/after and does not overwrite it. Normal players do not run this probe.

The opt-in probe temporarily enables background execution and restores the previous setting when it finishes or is destroyed. It waits for the accepted-input frame to refresh before capturing the HUD, and records each palm's error separately. This does not change normal Player focus behavior or turn synthetic input into human verification. Earlier failed and superseded runs remain retained evidence.

## Owner verification

- Continue from a supported Stage2-complete save to Day3; also check a fresh Day1→2→3 run.
- Confirm the expanded home, original doors/stairs, title return, old NPC seating and Stage1/2 fish still look correct.
- Confirm Junseok/Jaehoon mapping, readable conversations and the seated Day3 Cheolsu encounter.
- Listen to the approved vocal audio and judge the chart's musical timing/readability.
- Check the attached hands/knife, head/body transitions, E turnover, remaining flesh/cut faces, six cuts per fillet and final 24-point plate.
- Try pause, phase failure/retry, abort, save failure/retry and final title/continue flow.
- Confirm Stage4 stays unavailable.
- Record evidence and merge only after the human checks and independent verification are satisfactory. Agents do not merge PRs or move issues to Done.
