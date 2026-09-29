# Issue #68 — current game corrections

Owner requested these four corrections to the delivered Day1/Day2 game before Stage3 materials arrive. Base: main `3b2b7f71f286e31218f2d8d2d170eb50c60ad44c` (merged PR #67). Branch: `codex/current-game-owner-fixes`. This is a manual Owner-directed issue; archived planning issues are not resumed.

## Home capacity

The room expands from 4.8 × 4.6 m to **10.8 × 9.2 m**, with ten reserved equipment positions, each allowing a 2.0 × 1.6 m footprint. The bed occupies a separate corner. The central entrance aisle and the approaches to all ten positions remain outside the equipment footprints. Current drum/DAW stations, their interaction positions and practice cameras move together without scaling the equipment.

Unity measurements of the existing generated shop assets (width × height × depth, metres):

| Asset | Dimensions |
| --- | --- |
| Electronic drum kit | 1.10 × 1.10 × 0.82 |
| MIDI keyboard controller | 1.15 × 0.30 × 0.60 |
| Modular synthesizer | 0.95 × 1.26 × 0.54 |
| Effects pedals | 0.64 × 0.16 × 0.66 |
| Guitar pedal | 0.22 × 0.04 × 0.20 |
| Loudspeaker | 0.66 × 1.46 × 0.64 |
| Speaker box | 0.78 × 0.86 × 0.70 |
| Stacked speaker | 0.86 × 1.66 × 0.70 |
| Stage spotlight | 0.26 × 0.60 × 0.36 |
| Stereo speaker | 0.34 × 0.64 × 0.56 |
| Vintage speaker | 0.62 × 0.80 × 0.34 |

The present DAW desk is 1.45 m wide and 0.75 m deep. The ten-equipment rendering is a capacity preview using existing assets, never saved into gameplay or awarded to the player. Stage3–10 assets and rewards are not invented; future equipment exceeding the measured envelope needs a fresh fit check.

## Sleep and saved continuation

New games enter morning conversation directly. Legacy `Wake` saves migrate on Continue without awarding progress or requiring a bed click. Migration is limited to entering the home, so opening the title scene cannot erase the resume decision. Legacy fully completed Day2 saves also restore the new pending endpoint screen. Sleep keeps the existing placement/practice prerequisites, records the completed day once, and persists a pending clear screen. It displays `n스테이지 클리어`, `이어서 하기`, and `저장하고 나가기`.

Stage1 Continue starts the second morning. The Owner decision on 2026-09-30 keeps Stage2 at the completed checkpoint: the Continue button reads `다음 스테이지 준비 중` and is disabled, while Save and Exit remains available. Only Stage1 Continue clears the pending flag; loading a saved pending clear restores its screen and cursor/input lock. Save-and-exit only quits after saving succeeds. A save failure leaves the screen available for retry; failed Continue restores the pending checkpoint, and failed sleep autosave cannot leave the fade covering the screen.

## Entrances

The street facade already includes its original entrance geometry. Disable the extra `MatchingDoors` group in Street, retaining the facade, interaction collider and scene destination. This exposes the original club stair approach. Indoors, move the existing source-derived portal 5.5 cm into its opening and match the wall opening to the frame. Door jambs and header intersect the wall rather than floating in front. Existing source art and serialized door/spawn references remain intact. Reapplying unchanged interior material colours preserves Unity-normalized shader keywords, avoiding a later PlayMode rewrite of the existing light materials.

## Rockfish diagnosis

All six supplied parts were already present: head, body, bone, fillet, fillet_half and piece, each with its original base-colour texture. The original scans have different axes, units, cut faces and proportions. The head's neck plane faced away from the body's +X cut end, making the initial assembly look disconnected. Calibrate the derived head's neck direction toward -X, align its visible side with the body, and overlap the junction. Only the generated head mesh changes. Original FBX, textures, other part meshes, Stage2 scene IDs, music, chart timings and judgement windows remain unchanged.

The scans themselves include irregular neck geometry, uneven surfaces and baked texture detail. This correction addresses assembly orientation and fit; it is not a replacement sculpt or a claim of final art approval. Inspect the complete fish and all six gameplay phases in the supplied captures and playable build.

## Reproduction and verification

Use Unity 6000.4.0f1 in an isolated checkout. `CurrentGameRevisionAuthoring.ApplyBatch` applies only the affected scene generators and Rockfish models; it does not rebuild charts or source assets. `InspectBatch` captures the home capacity and entrances without saving its preview objects. Evidence lives in `Logs/OwnerFixes68` and the existing PlayMode capture directory.

The manual validation uses graphics and `-stage1Validation`: the default user save is volatile and DayWorld tests use unique real JSON profiles in the isolated checkout. The checked-in native runner, NUnit result parser and Console diagnostic inspectors are reused without changes. Exact executed results, source commit and local artifact paths are recorded in the PR.

Final Owner checks: walk around the enlarged home and both placed instruments; confirm the first morning has no wake prompt; finish each daily loop and inspect clear-screen wording; save/exit/relaunch/continue; enter and leave the restaurant and club; inspect the Rockfish at each cutting phase, including texture appearance and the head join. Developer tests do not replace physical input, visual/audio feel, an independent review or Owner merge approval.
