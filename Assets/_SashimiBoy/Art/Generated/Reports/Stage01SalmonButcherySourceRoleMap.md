# SASHIMI BOY Stage01 Salmon Butchery Asset Role Map

## Source Drop

`C:\Dev\SashimiBoyAssetDrops\Stage01\SalmonButchery\Release_20260903_v1`

## Salmon-specific Parts

| 역할 | FBX 경로 | Texture 경로 | 설명 |
|---|---|---|---|
| Head | `Head/Models/Salmon_Head.fbx` | `Head/Textures/` | 초기 상태에서 Body와 결합되는 머리 |
| Body | `Body/Models/Salmon_Body.fbx` | `Body/Textures/` | 초기 연어 외형의 몸통 |
| Fins | `Fins/Models/Salmon_Fins.fbx` | `Fins/Textures/` | 지느러미 제거 단계의 분리 파츠 |
| Spine | `Spine/Models/Salmon_Spine.fbx` | `Spine/Textures/` | 척추 분리 단계의 파츠 |
| Fillet | `Fillet/Models/Salmon_Fillet.fbx` | `Fillet/Textures/` | 가시 제거와 회 뜨기 단계의 필렛 |

## Shared Fish-Butchery Asset

| 역할 | FBX 경로 | Texture 경로 | 설명 |
|---|---|---|---|
| PinBone | `Shared/PinBone/Models/PinBone.fbx` | `Shared/PinBone/Textures/` | 연어뿐 아니라 이후 모든 생선이 공통으로 사용하는 단일 가시 원본 |

PinBone은 Salmon 전용 폴더에 중복 복사하지 않습니다.  
생선별 가시 개수·위치·회전·배치는 각 생선 Assembly 또는 Stage data가 소유합니다.

## Canonical Texture Names

### Head

- `Salmon_Head_BaseColor.jpeg`
- `Salmon_Head_Normal.jpeg`
- `Salmon_Head_Metallic.jpeg`
- `Salmon_Head_Roughness.jpeg`
- `Salmon_Head_RM.jpeg`

### Body

- `Salmon_Body_BaseColor.jpeg`
- `Salmon_Body_Normal.jpeg`
- `Salmon_Body_Metallic.jpeg`
- `Salmon_Body_Roughness.jpeg`
- `Salmon_Body_RM.jpeg`

### Fins

- `Salmon_Fins_BaseColor.jpeg`
- `Salmon_Fins_Normal.jpeg`
- `Salmon_Fins_Metallic.jpeg`
- `Salmon_Fins_Roughness.jpeg`
- `Salmon_Fins_RM.jpeg`

### Spine

- `Salmon_Spine_BaseColor.jpeg`
- `Salmon_Spine_Normal.jpeg`
- `Salmon_Spine_Metallic.jpeg`
- `Salmon_Spine_Roughness.jpeg`
- `Salmon_Spine_RM.jpeg`

### Fillet

- `Salmon_Fillet_BaseColor.jpeg`
- `Salmon_Fillet_Normal.jpeg`
- `Salmon_Fillet_Metallic.jpeg`
- `Salmon_Fillet_Roughness.jpeg`
- `Salmon_Fillet_RM.jpeg`

### Shared PinBone

- `PinBone_BaseColor.jpeg`
- `PinBone_Normal.jpeg`
- `PinBone_Metallic.jpeg`
- `PinBone_Roughness.jpeg`
- `PinBone_RM.jpeg`

`RM`의 채널 패킹 방식은 현재 이 문서에서 확정하지 않습니다.  
Codex는 별도 Metallic/Roughness와 RM을 동시에 추측 연결하지 말고, 실제 shader와 source metadata를 확인해야 합니다.

## Repository Import Destination

Salmon-specific source:

`Assets/_SashimiBoy/Art/Source/Stage01/SalmonButchery/`

Shared PinBone source:

`Assets/_SashimiBoy/Art/Source/Shared/FishButchery/PinBone/`

권장 generated asset:

- `PF_Stage01_SalmonAssembly`
- `PF_PinBone`
- PinBone용 canonical Material
- Assembly 내부 `PinBones` root와 여러 `PF_PinBone` instance

## Import Contract

- Source Drop의 FBX와 texture bytes를 수정하지 않습니다.
- Raw FBX를 Stage Scene에 직접 배치하지 않습니다.
- 모든 visible model은 canonical Wrapper/Assembly를 통해 참조합니다.
- 모든 hierarchy는 positive scale을 사용합니다.
- Head, Body, Fins, Spine, Fillet을 독립적으로 show/hide/detach할 수 있어야 합니다.
- PinBone은 재사용 가능한 단일 shared prefab으로 생성합니다.
- PinBone의 생선별 개수와 배치는 Source asset이 아니라 Assembly/Stage definition이 결정합니다.
- 초기 상태에서는 Salmon 파츠가 하나의 온전한 연어처럼 보여야 합니다.
- 신규 Knife, Tweezers, Sashimi Slice, rigged hand는 Issue #20 범위에서 제외합니다.
- `.meta`, `.mat`, `.prefab`, `.unity` 파일은 Source Drop에 넣지 않습니다.
