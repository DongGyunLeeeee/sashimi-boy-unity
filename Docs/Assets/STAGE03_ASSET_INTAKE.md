# Stage03 asset intake — Issue #70

Owner supplied these sources on 2026-10-29 and requested asset intake before
feature development. The planning script has not arrived. This document records
the received files; model names do not establish gameplay phase order.

Base: `3b2b7f71f286e31218f2d8d2d170eb50c60ad44c` (main).
The separate current-game corrections in PR #69 are not part of this change.

## Source inventory

The machine-readable [manifest](STAGE03_ASSET_MANIFEST.json) records all four
inputs and every archive entry, destination, byte length and SHA-256. ZIP files
are delivery containers and are not duplicated in the repository. All 68 source
files (874,143,708 bytes) retained their original bytes at intake. Only FBX filenames were
normalized from opaque Tripo export names to their supplied group names.
The original manifest remains a frozen historical receipt. The approved music
replacement in Issue #85 is recorded separately below; the other 67 originals
remain unchanged.

| Input | Contents | Destination |
| --- | --- | --- |
| `drive-download-20261028T151238Z-1-001.zip` | 10 FBX + 50 textures | `Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage03/<group>/` |
| `riggedbody.fbm-20261028T151252Z-1-001.zip` | 1 FBX + 5 textures | `Assets/_SashimiBoy/Art/Source/Characters/Stage03/RiggedBody/` |
| `flounder1.wav` | Original music | `Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/` |
| `font-20261028T151309Z-1-001.zip` | `Cheosgeol-eum.otf` | `Assets/_SashimiBoy/Art/Source/UI/Fonts/Cheosgeol-eum/` |

The fish groups are `WholeFish`, `HeadRemovedFish`, `FishHead`, `FrontFillet`,
`BackFillet`, `FilletRemoved1`, `FilletRemoved2`, `FilletRemoved3`,
`FilletRemoved4`, and `SashimiSlice`. The source Korean folder names and full
original filenames remain in the manifest. Several texture basenames repeat
with different contents, so each model retains its own texture directory.

The pre-existing shop display Flounder and Quaternius Kevin body are distinct
files. They and their GUIDs remain intact. Newly received assets are not put
under `Resources`, connected to a scene, or used to replace existing characters.

## Import facts and limits

- FBX sources are binary FBX 7400, Y-up, UnitScaleFactor 100. Each fish contains
  one static source mesh and no animation stack. The source meshes have about
  0.96–0.99 million vertices each; this intake does not claim they are optimized
  for simultaneous real-time use. No decimation, source re-export or orientation
  repair has been applied.
- The body source contains a 9,273-vertex mesh, skin and 65 limb/cluster entries,
  with no animation stack. A rig in the source does not establish a Humanoid
  mapping or provide walk/cooking animation clips. It is outside the fish-only
  DayWorld import policy.
- Fish sources use the repository's existing `DayWorldImportSettings` policy:
  readable models, no imported animation, standard imported materials; texture
  size cap 1024, normal-map classification and linear roughness/metallic maps.
  Body sources use their own Unity import metadata. Final material authoring,
  character rig mapping and runtime prefab assembly belong to later work.
- The original music received in Issue #70 was PCM, stereo, 16-bit, 48,000 Hz,
  7,121,280 frames / 148.36 seconds.
  This states the audio-file duration, not a BPM, first beat, chart offset or
  judgement schedule. The WAV has not been re-encoded, trimmed or normalized.
- Font name metadata identifies `첫걸음 / Cheosgeol-eum`, Regular, version 1.000,
  designer Lu.k, `Copyright (c) 2024, Lu.k`. No license document or license URL
  accompanied the archives or appears in the font's license name-table fields.

All assets were provided by the Owner as a colleague's Stage3 delivery. The
archives do not independently identify the model/music authors or grant terms.
This receipt does not declare a new license or resolve the wider audit in #1.
Keep the received font copyright and obtain the colleague's original source and
license records before final distribution review. No global UI font replacement
is included here.

## Feature automation handoff

This asset PR supplies inputs. It does not make Stage03 playable, unlock a third
day, change the Stage02 end boundary, choose a reward, or author a rhythm chart.
No feature issue is made Ready by this intake.

The following product inputs are still required for testable feature issues:

1. Day3 dialogue/script, required and optional interactions, stage entry/exit,
   clear/failure/retry flow, purchase/reward and save checkpoints.
2. Flounder action/phase order and the role/visibility transitions for the ten
   supplied model states, including any source parts that are alternatives.
3. Approved BPM, first-beat/offset and authored note/phase timing for
   `flounder1.wav`. Do not derive final timing from duration or another stage.
4. Intended character/body and font usage, together with source/license records.

After these arrive, compare them with the then-current main, define focused
feature issues with dependencies and measurable acceptance criteria, and ask the
Owner to select Ready work. Existing Developer/Reviewer automation can then
handle one issue per run. Human play verification and merging remain with the
Owner. The archived Stage1/2 backlog is not reactivated.

## Validation evidence

### Approved music replacement — Issue #85

On 2026-10-30 the Owner supplied `flounder.wav` as the version with vocals and
[explicitly approved the full-file replacement](https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/85#issuecomment-6522789964).
The current bytes at
`Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav` are an unmodified
copy of that input: 44,350,480 bytes, SHA-256
`3572605dec84991aabf1e4911eee443287da0f7e5c403b0a449aee403cf31062`, PCM16 stereo
48,000 Hz, 11,084,160 frames / **230.92 seconds**. The existing filename,
GUID `c4fa323b3fd58a44dbbb5f5f6b64d734`, entire `.meta` and importer settings are
preserved. No trimming, re-encoding, normalization or metadata stripping was
performed.

The separate [audio revision manifest](STAGE03_AUDIO_REVISION_MANIFEST.json)
binds this one replacement to the approval and the byte-exact historical
receipt (SHA-256 `8a586c697634f95bc42dedb8ee33f9c6f1370ebcffb43104a068db2fc0f8de26`).
It records both identities; the old audio remains in Git LFS history. Intake
checks validate all 67 unchanged originals and this explicitly approved current
music identity. The audit report distinguishes the historical receipt from the
current approved music.

The new file is **82.56 seconds longer** than the original. Its duration does
not establish a matching intro, tempo, first beat, offset or chart. No Scene,
Prefab, stage unlock, BPM, chart or judgement setting is changed. Owner listening
and final music-sync verification remain pending; successful decoding alone is
not a vocal or musical-alignment approval.

### Original intake evidence

Archive CRC/path checks and source-to-destination hashes are part of intake.
Unity import, tests, scene/reference scans and LFS transfer results are recorded
with their actual executed results in the linked Draft PR. A source inventory or
successful import alone is not human visual, audio, rhythm or gameplay approval.
