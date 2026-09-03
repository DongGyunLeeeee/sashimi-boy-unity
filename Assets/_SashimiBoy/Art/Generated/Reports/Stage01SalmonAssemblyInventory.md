# Stage01 Salmon Assembly Inventory

- Issue: `#20`
- Source drop: `C:\Dev\SashimiBoyAssetDrops\Stage01\SalmonButchery\Release_20260903_v1`
- Manifest validation: **PASS (37/37)**
- Canonical forward/up: `+Z / +Y`
- Hierarchy scale contract: positive scale only
- Shader: built-in `Standard`
- Metallic/smoothness: generated from separate Metallic (R) and inverted Roughness (A) maps
- Source `_RM` files are preserved but intentionally not bound; their channel packing is undocumented.

## Canonical assets

| Role | Source model | GUID | Source bounds | Wrapper target | Material slots | Canonical wrapper |
|---|---|---|---|---:|---:|---|
| Head | `Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/Head/Models/Salmon_Head.fbx` | `d8db97ad61309d947a38c826ecc39f2f` | `(0.979462, 0.758271, 0.753235)` | 0.84 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Salmon_Head.prefab` |
| Body | `Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/Body/Models/Salmon_Body.fbx` | `01ee546ce6ed6bd4c9664530c5273f72` | `(0.981964, 0.374115, 0.274048)` | 2.40 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Salmon_Body.prefab` |
| Fins | `Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/Fins/Models/Salmon_Fins.fbx` | `7df407e2161f1234789864b79c5e1b50` | `(0.97876, 0.856476, 0.495834)` | 0.62 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Salmon_Fins.prefab` |
| Spine | `Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/Spine/Models/Salmon_Spine.fbx` | `f4a9f8e562ca94e44a7eb8ad6b22c192` | `(0.31957, 0.355255, 0.161492)` | 2.15 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Salmon_Spine.prefab` |
| Fillet | `Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/Fillet/Models/Salmon_Fillet.fbx` | `135a2048354c5bc49b1ce7146ca7428f` | `(0.979767, 0.315308, 0.411453)` | 2.15 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Salmon_Fillet.prefab` |
| PinBone | `Assets/_SashimiBoy/Art/Source/Shared/FishButchery/PinBone/Models/PinBone.fbx` | `660ff772572f658468381904c954c7b9` | `(0.979675, 0.162048, 0.129349)` | 0.14 m | 1 | `Assets/_SashimiBoy/Art/Generated/Prefabs/Shared/FishButchery/PF_PinBone.prefab` |

## Assembly contract

- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/SalmonButchery/PF_Stage01_SalmonAssembly.prefab`
- Initial whole-fish view: Body + Head. The Body source already contains its authored tail and exterior fins; the standalone Fins wrapper is staged hidden to prevent mesh overlap/z-fighting.
- Spine, Fillet, and eight shared PinBone instances are staged hidden and can be shown, detached, and reset independently.
- Stable IDs: `Head`, `Body`, `Fins`, `Spine`, `Fillet`, `PinBone.00` through `PinBone.07`.
- Required anchors: part anchors, eight PinBone anchors, `KnifeAttachmentAnchor`, `HandAttachmentAnchor`, `PinBoneWorkAnchor`, `SashimiOutputAnchor`, and `PlateOutputAnchor`.
- Explicit fallback: `Assets/_SashimiBoy/Art/Generated/Prefabs/Stage01/PF_Stage01_ProceduralSalmon.prefab`.

## Source integrity

The checked-in `Stage01SalmonButcherySourceManifest.csv` stores the original byte counts and SHA-256 values. The generator validates every row before it writes generated assets.

## Source slots

- Head: `Salmon_Head(Clone)[0]=Default-Material`
- Body: `Salmon_Body(Clone)[0]=Default-Material`
- Fins: `Salmon_Fins(Clone)[0]=Default-Material`
- Spine: `Salmon_Spine(Clone)[0]=Default-Material`
- Fillet: `Salmon_Fillet(Clone)[0]=Default-Material`
- PinBone: `PinBone(Clone)[0]=Default-Material`
