# Stage3 supplied knife cutting edge

Owner feedback on Issue #87 reported that the knife was held backwards. The
shared knife wrapper had a longitudinal axis but no verified cutting-edge
polarity. Its original authoring used the minimum Y point as `BladeContact`.
The provided knife's actual sharpened bevel is at **positive Y**, so the old
anchor touched the fish with the rounded spine.

## Geometry evidence

The supplied `KitchenKnife.fbx` remains unchanged, including its meta GUID
`0d849fb4b722b4e7a8a320249dd577c3`. Its SHA-256 is
`8d8c70daec71027d8d72e451d7b2c8c9ecce7473155831a93afe315d06142e55`.
The binary FBX contains 975,089 control points. Unity can split imported
vertices at normal/UV seams; the authoring inspects every imported vertex.

Read-only reconstruction of the existing wrapper produced local bounds
X ±0.060770, Y ±0.103382, Z ±0.775000. At Z 0.15–0.25, the positive-Y edge's
outer tenth has thickness 0.02056 in X, compared with 0.04831 for the negative-Y
edge. Toward positive Y the blade face narrows from about 0.059 to 0.037 to
0.020; this is the sharpened bevel, not the rounded negative-Y spine.

The old anchor was (0.0015311, −0.10338235, 0.1705002). Near that Z, the actual
positive-Y cutting edge is around Y 0.0958. The generator derives the precise
new contact from the imported mesh; these rounded numbers are evidence, not
hard-coded replacement geometry.

## Authoritative correction

`Stage03KnifeAuthoring.Bind` retains the supplied mesh, hierarchy, scales and
`GripAnchor`. It uses a proper quaternion to keep the existing horizontal
hilt-to-tip direction while pointing the verified positive-Y bevel down.
It places `BladeContact` on that actual bevel. Both standard Stage3 generation
entry points call it. Repeating it preserves the pose rather than accumulating
another half-turn. Stage1/2 scenes and the shared knife prefab remain untouched.

The symmetric hilt's hand pose follows forward/yaw/pitch independently of this
asset roll, so changing which edge faces the fish does not turn Kevin's wrist
upside down.

For a focused scene update, set a fresh `SASHIMI_STAGE3_KNIFE_REPORT` JSON path
and run `SashimiBoy.EditorTools.Stage03KnifeAuthoring.ApplyKnifePoseBatch`.
The report contains source identity, actual cross-section widths, the full
wrapper hierarchy transforms, and before/after contact and edge directions.
Runtime stroke diagnostics additionally record the actual sharpened-edge
direction at all sampled note and section-completion poses. Synthetic Editor
captures do not replace native PlayMode tests or Owner visual/input approval.

## Required regression evidence

- Actual imported mesh proves the positive-Y bevel is thinner than the spine;
  the scene anchor lies on it and the edge points down.
- Repeated authoring keeps the hilt, long axis, scale and corrected pose stable.
- Source FBX/meta, shared prefab and Stage1/2 scene bytes remain unchanged.
- Existing actual-rig stroke tests keep palms at their grip targets and the
  complete support hand clear of the swept knife plane. They also inspect the
  verified sharp-edge direction on note and section-end completion frames.
- Camera/skin/source-reference and complete gameplay regressions remain required.
