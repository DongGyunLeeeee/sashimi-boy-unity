# Stage3 Day3 conversation and world additions

Issue #87 uses the Owner transcript preserved in issue #76. The implemented encounters are `junseok` (18 entries), `cheolsu_day3` (13), and `jaehoon` (19): 45 spoken lines, four Kevin thoughts, and one action. Chat-sourced entries use sourcePage 0; the original Day1/2 PDF page metadata and four original conversation IDs remain unchanged. The supplied text “광어 냄새” is retained literally although Stage3's display name is 가자미.

The progression layer owns availability and completion. Junseok is the Day3 morning encounter; the original seated Cheolsu instance is reused after the Stage3 result returns to the restaurant; Jaehoon appears after Plugin placement/practice on the return route. Cancelling a conversation does not mark it complete, and reinteraction restarts it. Future encounters and the concert text are not activated.

`Stage03DayWorldAuthoring.ApplyBatch` is the authoritative focused generator. It adds supplied `PF_Junseok`/`PF_Jaehoon` references under Street's `DayWorld_Integration/Stage03Story` and adds the Plugin software workstation at the expanded home's third reserved slot. It updates only the affected director arrays. The generator does not recreate the original world root, door/venue layout, or seated Cheolsu. `DayWorldHomeAuthoring` retains Slot03 when its room-layout pass is reapplied.

The provided new NPCs are static meshes without skeletons. The `handshake` action is explicitly handled as the original action caption “둘이 악수한다.” plus a brief conversation-camera beat. It is not converted to dialogue or silently discarded, and this implementation does not claim to supply a skeletal two-character handshake animation. Actor models, source paths, source bytes, and GUIDs are preserved.

Plugin placement unlocks the new workstation visual; its monitor represents the approved software reward rather than unlocking an unearned instrument. Its practice anchors follow the existing DAW interaction pattern. Final body reach, framing, dialogue pacing, and placement/return navigation require real Player and Owner verification.

Four focused EditMode tests check transcript count/kinds/source boundaries, Day1/2 and future-content separation, the supplied NPC references/anchors, and the third Plugin slot while preserving the room and original stations. Their execution is recorded by the integration runner; this document does not declare unexecuted tests passed.
