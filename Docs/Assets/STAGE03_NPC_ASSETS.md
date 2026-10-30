# Stage03 NPC asset intake (#83)

This intake supplies static, source-backed preview assets for Junseok and Jaehoon. It does not place either character in the world or implement dialogue, progression, saving, rigging or the handshake.

## Approved identities and provenance

| Character | Archive label | Internal names / FBX identifier | Actual Unity preview observation |
| --- | --- | --- | --- |
| Junseok (준석) | `재훈.zip` | `junseok_*` / `4b828a99-2bf3-439d-b7b8-19988b8469b0` | Navy jacket/trousers, gray knit layer, brown backpack |
| Jaehoon (재훈) | `준석.zip` | `jaehoon_*` / `994f3d88-2ebb-4bd5-8313-744a6cd4fd47` | Navy suit, white shirt, brown briefcase and wristwatch |

The Owner confirmed internal filenames as the identity authority. The archive labels are reversed. The observations above come from actual front/side/back Unity renders and supersede preliminary clothing guesses from the UV atlases; they do not reassign either model or alter its appearance.

`STAGE03_NPC_ASSET_MANIFEST.json` records exactly two FBX files and ten JPEGs, their original case-sensitive archive entries, repository paths, sizes and SHA-256 hashes. Source files retain their original filenames and bytes under `Art/Source/DayWorld/Characters/{Junseok,Jaehoon}/`. The separate existing 68-file `STAGE03_ASSET_MANIFEST.json` remains unchanged.

## Generator and owned output

`SashimiBoy.EditorTools.Stage03NpcAssetAuthoring.Generate()` owns only `Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets/`. Its Editor menu is **Sashimi Boy / Stage 03 / Generate NPC Preview Assets**.

- `Prefabs/PF_Junseok.prefab` and `PF_Jaehoon.prefab` preserve each complete imported model hierarchy and original mesh references. There is no mesh simplification, head splitting, procedural pose, substituted body or animation.
- `Materials/M_Junseok.mat` and `M_Jaehoon.mat` explicitly bind the corresponding provided basecolor, a byte-identical generated normal copy, and the generated metallic/smoothness map.
- `Textures/*_MetallicSmoothness.png`: full 4096×4096 scalar input resolution, R = original metallic R, A = 255 − original roughness R. Raw JPEG bytes are decoded linearly without changing Source importers. Packed texture and normal copy importers are linear; basecolor remains color data. Standard shader smoothness uses the packed alpha channel.
- `*_rm.JPEG` stays in Source and the manifest. Its undocumented channels are not bound.
- `Stage03NpcAuthoringReport.json` contains reproducible input/output paths, hashes, imported density, bounds, normalization values and camera directions, without timestamps or private machine paths.

The existing broad DayWorld generator and its gameplay/NPC rebuild are not called. Only the new Generated texture importers are configured. Existing Source readability and importer settings are not toggled.

## Measured preview geometry

These are imported Unity mesh counts, which can differ from FBX control-vertex counts at UV/normal seams.

| Character | Imported vertices | Triangles | Uniform preview scale | Final bounds (width × height × depth) |
| --- | ---: | ---: | ---: | --- |
| Junseok | 1,323,015 | 1,982,489 | 1.78663981 | 0.723396 × 1.750000 × 0.520104 |
| Jaehoon | 1,335,581 | 1,965,224 | 1.78536046 | 0.618948 × 1.750000 × 0.401472 |

Both wrappers use +Y up, the supplied anatomical front along +Z, explicit yaw 0°, positive uniform scale and feet at Y=0 with centered XZ bounds. The 1.75-unit height is a common preview normalization, not an approved final world height or placement. All supplied local transforms and proportions remain intact.

The 12 originals total 133,939,446 bytes. Initial generated output totals 16,901,666 bytes including its 21 contained files (excluding the root folder metadata). No derived geometry is created. The two dense meshes are provided for inspection; no target-device FPS, Player-memory, animation or runtime-world placement PASS is claimed.

## Capture and verification

`GenerateAndCaptureBatch()` generates the assets then calls `CaptureBatch()`. With an actual graphics device, the latter writes `{Character}_front.png`, `_side.png`, `_back.png` and `NpcCaptureViews.json` into a fresh/empty external directory specified by `SASHIMIBOY_NPC_PREVIEW_DIR`. Files use create-new semantics. Reparse paths and any directory inside a Git checkout are rejected.

The camera views are 768×1024, elevation 0°, from +Z/front, +X/side and −Z/back. Actual six-view inspection confirmed the yaw and visible source clothing/materials. Capture uses its own preview scene/camera/lights and restores active render targets; it never replaces an open world scene. The first actual graphics generator invocation exited natively with 0 and no forbidden log diagnostics. This is generation evidence, not a test-suite PASS or the Owner's final visual acceptance.

New EditMode tests cover exact provenance, source/GUID ownership, static import, grounded positive wrappers, explicit PBR and independent original-pixel samples, deterministic two-run generation, all existing metadata/scenes/prefabs and unsaved scene/camera/render-target preservation. The Editor PlayMode test checks both source-backed prefabs through frame advancement and reactivation without gameplay components or new runtime owners.

Run clean compile and full EditMode/PlayMode through the approved native profile in a fresh registered validation worktree, with `-EnableGraphics`. Preserve XML, native exits, strict log scans, integrity reports and exact tested checkpoint evidence externally. Existing failures, including #82's source-importer regeneration problem, must remain visible and are never skipped or treated as PASS by this intake.

Owner verification: confirm the two supplied identities and appearance in front/side/back previews, feet/pivot and proportions, and material continuity. World placement, story timing and handshake presentation belong to their later approved issues.
