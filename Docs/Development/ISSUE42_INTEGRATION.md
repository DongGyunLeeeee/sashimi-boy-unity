# Issue #42 — current Day1/Day2 delivery integration

The Owner requested this integration on 2026-09-29 after the issue inventory
audit. It delivers the existing local Stage1/Stage2 game for review. It does not
start another gameplay issue, merge a GitHub PR, or declare final human approval.

## Source and preservation

- Local game: `5c3c697f0ac8f5d31a1e1c9da777772050c55fda`, originally on
  `feat/42-day01-day02-world` (13 local-only commits).
- Integrated main: `4de6d915d23aecec57c1666287c8ba3084db046a`.
- Preserved local integration branch: `codex/42-stage1-stage2-source`, commit
  `4f2643d1356fd622b4221351fb48d601ddce731a`.
- Delivery branch: `codex/42-stage1-stage2-integration`, based on the integrated
  main. Its initial snapshot has the same game bytes as the preserved integration.
  Eight generated meshes over GitHub's ordinary-file size limit use LFS. Keeping
  the old oversized Git blobs as delivery ancestors would still prevent upload;
  the original local branches and commits remain available unchanged.
- The two salmon-preview source/test files match PR #47 head
  `d6f72a8fc9e5455ba8d9e71e51f97b4d443d8c8d`, retaining its preview-scene isolation
  and metadata normalization regression coverage. Other open PRs are not merged.
- Source FBX/textures/audio, note timing, judgement windows, `.meta` GUIDs, the
  user's dirty settings/recovery/previews, and existing save profiles are retained.
  The automation implementation remains at SPEC 1.0.18 from main (version blob
  `f8f3c08725d52ee6310ff03bf8655356a3975a59`).

Unity's existing empty YAML scalars and the imported Quaternius license retain
their canonical bytes through scoped whitespace attributes. Authored code and
documents retain the ordinary whitespace checks.

## Existing playable behavior and acceptance boundary

The delivered game includes the logo/four-face menu, two days of home/street/NPC
and shop progression, Stage1 salmon, Stage2 rockfish, connected hands/tools,
equipment placement/practice, sleep, and saved checkpoints.

The current implementation requires equipment placement and practice before
sleep. Morning NPC completion opens work; it does not add a new mandatory boss
briefing. The original Day1 issue describes optional free-time activities and a
boss briefing gate. These differences are explicitly pending an Owner scope
decision, not silently treated as satisfied criteria. No new dialogue or balance
decision is made by this integration.

PR #48 still has an EquipmentShop scene overlap, and PR #49 still has a Street
scene/generator overlap and an unresolved same-head Owner manual failure. Their
independent issue acceptance is not inferred from this integration. Review and
merge order must preserve the current DayWorld layout and interactions.

## Validation procedure

Use Unity 6000.4.0f1 and `StandaloneWindows64` in a clean isolated clone. Preserve
native exit codes and inspect full NUnit XML, skipped counts, console errors,
source diffs, metadata/GUID integrity, and the eight-scene reference audit.

The existing full game tests capture rendered cameras. The manual integration
run therefore enables graphics and passes the existing `-stage1Validation`
argument so the default real save is volatile; DayWorld save tests create unique
validation profiles in the clone. The scheduled wrapper fixes `-nographics` and
does not expose these arguments. Its checked-in native process runner, strict
NUnit result parser, and compile/test log inspectors are reused without edits.
No scheduled Host validation result is fabricated by this manual run.

Run clean import/compile, all EditMode tests, all PlayMode tests, then
`SashimiBoy.EditorTools.DayWorldOwnerPresentationAuthoring.AuditBatch` and
`SashimiBoy.EditorTools.DayWorldAuthoring.BuildWindowsBatch`. The build also checks
the authoritative DayWorld and Stage2 model generators for byte stability.
Executed results and exact artifact paths belong in the delivery PR; this
procedure is not a PASS record.

Final Owner verification covers actual keyboard/mouse input, camera and hand
appearance, music sync/readability, both daily loops, and save/exit/continue.
Independent review and Owner acceptance remain separate from Developer tests.
