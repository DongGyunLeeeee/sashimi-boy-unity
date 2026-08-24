# New Assets And Kevin First-Person Camera Report

- Render pipeline: Built-in Render Pipeline
- Shader: `Standard`
- Input backend: legacy Unity Input API
- Source files overwritten: No
- TASK 2 scene dressing included: No

## Discovered FBX

- AmbiguousFace: `Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/AmbiguousFace/Models/Kevin_AmbiguousFace.fbx`
- PlainFace: `Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/PlainFace/Models/Kevin_PlainFace.fbx`
- CuteFace: `Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/CuteFace/Models/Kevin_CuteFace.fbx`
- WesternFace: `Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/WesternFace/Models/Kevin_WesternFace.fbx`
- Salmon: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Salmon/Models/Salmon.fbx`
- Rockfish: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Rockfish/Models/Rockfish.fbx`
- Mullet: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Mullet/Models/Mullet.fbx`
- KitchenKnife: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Models/KitchenKnife.fbx`
- SashimiTable: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/SashimiTable/Models/SashimiTable.fbx`
- DisplayInside: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayInside/Models/DisplayInside.fbx`
- DisplayOutside: `Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayOutside/Models/DisplayOutside.fbx`

## Texture Validation

Base color is sRGB; normal maps use Normal Map import; metallic/roughness data is linear; mipmaps are enabled. Source Read/Write is restored after packing.
- AmbiguousFace: base=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/AmbiguousFace/Textures/adult_male_character_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/AmbiguousFace/Textures/adult_male_character_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/AmbiguousFace/Textures/adult_male_character_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/AmbiguousFace/Textures/adult_male_character_3d_model_roughness.JPEG`
- PlainFace: base=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/PlainFace/Textures/human_figure_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/PlainFace/Textures/human_figure_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/PlainFace/Textures/human_figure_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/PlainFace/Textures/human_figure_3d_model_roughness.JPEG`
- CuteFace: base=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/CuteFace/Textures/human_character_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/CuteFace/Textures/human_character_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/CuteFace/Textures/human_character_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/CuteFace/Textures/human_character_3d_model_roughness.JPEG`
- WesternFace: base=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/WesternFace/Textures/human_character_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/WesternFace/Textures/human_character_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/WesternFace/Textures/human_character_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Characters/Kevin/Variants/WesternFace/Textures/human_character_3d_model_roughness.JPEG`
- Salmon: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Salmon/Textures/salmon_fish_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Salmon/Textures/salmon_fish_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Salmon/Textures/salmon_fish_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Salmon/Textures/salmon_fish_3d_model_roughness.JPEG`
- Rockfish: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Rockfish/Textures/rockfish_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Rockfish/Textures/rockfish_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Rockfish/Textures/rockfish_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Rockfish/Textures/rockfish_3d_model_roughness.JPEG`
- Mullet: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Mullet/Textures/trout_fish_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Mullet/Textures/trout_fish_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Mullet/Textures/trout_fish_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fish/Mullet/Textures/trout_fish_3d_model_roughness.JPEG`
- KitchenKnife: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Textures/kitchen_knife_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Textures/kitchen_knife_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Textures/kitchen_knife_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Textures/kitchen_knife_3d_model_roughness.JPEG`
- SashimiTable: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/SashimiTable/Textures/yellow_metal_cabinet_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/SashimiTable/Textures/yellow_metal_cabinet_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/SashimiTable/Textures/yellow_metal_cabinet_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/SashimiTable/Textures/yellow_metal_cabinet_3d_model_roughness.JPEG`
- DisplayInside: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayInside/Textures/blue_aquarium_display_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayInside/Textures/blue_aquarium_display_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayInside/Textures/blue_aquarium_display_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayInside/Textures/blue_aquarium_display_3d_model_roughness.JPEG`
- DisplayOutside: base=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayOutside/Textures/seafood_display_3d_model_basecolor.JPEG`, normal=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayOutside/Textures/seafood_display_3d_model_normal.JPEG`, metallic=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayOutside/Textures/seafood_display_3d_model_metallic.JPEG`, roughness=`Assets/_SashimiBoy/Art/Source/Environment/FishShop/Fixtures/DisplayOutside/Textures/seafood_display_3d_model_roughness.JPEG`

## Kevin Rig And Scale

| Variant | Imported height | Default scale | Normalized | Rig | Avatar | Animation clips |
|---|---:|---:|---:|---|---|---|
| Ambiguous Face | 0.98004 | 1.78564 | 1.75 | Generic | not Humanoid | none |
| Plain Face | 0.97913 | 1.78731 | 1.75 | Generic | not Humanoid | none |
| Cute Face | 0.98004 | 1.78564 | 1.75 | Generic | not Humanoid | none |
| Western Face | 0.98016 | 1.78542 | 1.75 | Generic | not Humanoid | none |

## Generated Outputs

- Material: `Assets/_SashimiBoy/Art/Generated/Materials/Characters/Kevin/M_Kevin_AmbiguousFace.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/Characters/Kevin/MS_Kevin_AmbiguousFace_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Character_Kevin_AmbiguousFace.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/Characters/Kevin/M_Kevin_PlainFace.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/Characters/Kevin/MS_Kevin_PlainFace_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Character_Kevin_PlainFace.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/Characters/Kevin/M_Kevin_CuteFace.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/Characters/Kevin/MS_Kevin_CuteFace_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Character_Kevin_CuteFace.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/Characters/Kevin/M_Kevin_WesternFace.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/Characters/Kevin/MS_Kevin_WesternFace_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Character_Kevin_WesternFace.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_Salmon.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_Salmon_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fish_Salmon.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_Rockfish.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_Rockfish_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fish_Rockfish.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_Mullet.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_Mullet_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fish_Mullet.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_KitchenKnife.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_KitchenKnife_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Prop_KitchenKnife.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_SashimiTable.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_SashimiTable_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_SashimiTable.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_DisplayInside.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_DisplayInside_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_DisplayInside.prefab`
- Material: `Assets/_SashimiBoy/Art/Generated/Materials/FishShop/M_FishShop_DisplayOutside.mat`
- Packed map: `Assets/_SashimiBoy/Art/Generated/PackedMaps/FishShop/MS_FishShop_DisplayOutside_MetallicSmoothness.png`
- Prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Fixture_DisplayOutside.prefab`
- Catalog: `Assets/_SashimiBoy/Art/Generated/Data/KevinVariantCatalog.asset`
- Player reference prefab: `Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Player_Kevin_FirstPerson.prefab`
- Kevin gallery: `Assets/_SashimiBoy/Art/Generated/Scenes/KevinAssetGallery.unity`
- FishShop gallery: `Assets/_SashimiBoy/Art/Generated/Scenes/FishShopAssetGallery.unity`

## Missing And Warnings

- None.

## Provisional Kevin

- Default variant: `AmbiguousFace`
- Selection lives in `KevinVariantCatalog.asset`; changing the default does not require rebuilding scenes.

## Camera And Start Flow

- Script: `KevinFirstPersonCameraRig`
- Eye height: 90% of valid Kevin renderer bounds; fallback 1.65 m. FOV 75, near clip 0.05 m, pitch -75 to +80 degrees.
- Kevin renderers use `Shadows Only` during first-person exploration, so the head/hair cannot cover the camera while the model remains available for shadows and previews.
- `InteractionSensor` keeps the 2.8 m range check and selects only the nearest unobstructed `IInteractable` hit by the camera-center raycast.
- Bootstrap `START` calls `PrototypeStartController` and loads `Street` through `SceneTransitionService`.
- Street/FishShop start with a locked cursor. Dialogue, manual ESC release, EquipmentShop UI, and Club UI suspend movement/look and expose the cursor.
- Street, FishShopDialogue, EquipmentShop, Club use scene-local first-person rigs. Stage01_Salmon stays fixed.
- Street PlayerSpawnPoint: position `(0, 0.6, -1.5)`, yaw `0` degrees.

## Scene Audit

| Scene | Cameras | Listeners | EventSystems | FirstPerson | Orthographic | Spawn / Yaw |
|---|---:|---:|---:|---:|---|---|
| Street | 1 | 1 | 1 | 1 | False | (0, 0.6, -1.5) / 0 |
| FishShopDialogue | 1 | 1 | 1 | 1 | False | (1.5, 0.6, -2.3) / 0 |
| EquipmentShop | 1 | 1 | 1 | 1 | False | (0, 0.6, -2.2) / 0 |
| Club | 1 | 1 | 1 | 1 | False | (0, 0.6, -4) / 0 |
| Stage01_Salmon | 1 | 1 | 1 | 0 | True | n/a |
| Bootstrap | 1 | 1 | 1 | 0 | False | n/a |

## Captured Console Messages

- No warnings or errors captured by this run.

## Play Mode Checklist

1. Open `KevinAssetGallery` and inspect all four variants, labels, height, and default marker.
2. Open `FishShopAssetGallery` and inspect all seven wrapper prefabs and materials.
3. Play `Bootstrap`, press START, and confirm Street opens at Kevin eye height with FOV 75 and no head/hair visible.
4. Verify WASD follows camera yaw, mouse look clamps vertically, collision blocks buildings, and the center reticle remains stable.
5. Aim at each Street door inside 2.8 m; verify `E` prompt appears only with clear line of sight and scene transitions still work.
6. Press ESC to release/relock the cursor. In FishShop dialogue, verify movement/look stop while the Screen Space dialogue UI is active.
7. Verify EquipmentShop/Club buttons work with the visible cursor and their first-person cameras do not rotate behind UI.
8. Open Stage01_Salmon and confirm its fixed orthographic slicing camera, rhythm input, HUD, scoring, and audio are unchanged.
