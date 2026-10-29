# Stage03 flounder presentation — Issue #73

This prepares the ten supplied model roles. It does not select Stage03 timing,
slice counts, dialogue, rewards, progression or an existing saved stage ID.
The source folder spelling/case and all supplied source bytes remain authoritative.

## Generated authority

`SashimiBoy.EditorTools.Stage03FlounderAuthoring.BuildBatch` owns only
`Assets/_SashimiBoy/Art/Generated/Stage03Flounder`.
Its `Stage03FlounderAuthoring.json` records source dependencies, exact texture
bindings, wrapper transforms, generated mesh hashes, counts and measured error.
The generated prefab is `Prefabs/PF_Stage03_FlounderAssembly.prefab`.

Meshes are derived by spatial vertex clustering, retaining texture/normal
discontinuities and a common geometric representative across duplicate seam
vertices. Each source remains unchanged. Generation refuses an over-budget mesh
or excessive measured displacement instead of silently dropping a supplied model.
The limit is 200,000 render vertices per derived model, p99 displacement at most
1%, maximum displacement at most 2%, and bounds error at most 1% of calibrated
longest extent. These are measured authoring bounds, not a target-device FPS claim.

Standard material metallic R comes from the supplied metallic R; smoothness A
is one minus supplied roughness R. The undocumented supplied `_rm` image remains
recorded and unbound. Normal-map copies preserve bytes while using generated
NormalMap importer settings. Source importer settings are not rewritten.

## Shared frame and identity

The assembly uses +Z toward the head, +Y upward and positive scales. BodyPivot
rotates 180 degrees around local +Z for back-side work. DetachedParts does not
inherit that rotation. Headless models retain the WholeFish tail reference and
end at the neck; the final spine has a narrower footprint. Independent normalized
source scans need explicit positive art calibration, recorded in the generator.
These transforms do not claim that the source scans share a physical unit scale.

| Body index | Exact source role |
| --- | --- |
| 0 | WholeFish |
| 1 | HeadRemovedFish |
| 2 | FilletRemoved1 |
| 3 | FilletRemoved2 |
| 4 | FilletRemoved3 |
| 5 | FilletRemoved4 |

The five stable detached IDs are `Head`, `FrontFillet0`, `FrontFillet1`,
`BackFillet0`, `BackFillet1`. Front and back pairs are independent instances
sharing their corresponding generated prefab/mesh. SashimiSlice is a separate
source-backed prefab reference; this presentation never creates slices itself.

## Runtime handoff

`FlounderAssemblyView.TryRemoveHead()` replaces WholeFish and separates one head.
`TrySeparateFillet(0..3)` replaces the corresponding body and separates one
independent part. Duplicate/out-of-order calls return false without creating
another object. `TryTurnOver()` is available only after both front fillets; back
separation is unavailable until its presentation completes.

`AdvancePresentation(delta)` advances only the bounded part/turn animations.
Once all five detached parts reach their storage anchors, the final body is hidden.
`GetStoredFillet(index)` supplies that independent object to the later slicing
system. `ResetAssembly()` cancels movement, restores all authored parents/poses,
hides detached parts and restores WholeFish without replacing object identities.

Issue #75 remains responsible for valid input, pause/state gates, working-fillet
selection, plate placement and result completion. Issue #79 owns progressive
cutting and caps. A presentation success is not a gameplay or stage completion.

## Visual evidence and remaining human checks

`ProbeBatch` captures actual imported source bounds and all ten source models.
`CapturePreviewsBatch` captures generated models, common body comparison and
actual runtime separation/turn/reset states. Use a graphics-enabled Editor for
captures; paths are outside all checkouts. Preview scenes are isolated and close
in finally, leaving active scenes, unsaved roots and render targets intact.

Inspect head/tail direction, body/tail/neck continuity, cut-face visibility after
turning, material response and separation motion. Automated shape bounds do not
approve visual fidelity, camera feel or target-player performance. Actual executed
compile/test results and captures are recorded separately with the PR.
