# EquipmentShop Art Pass Report

Generated from the source assets approved by issue #26.

| Asset | Wrapper | Texture sets | Materials | Fallback slots | Final size |
|---|---|---:|---:|---:|---|
| EquipmentShopOwner | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_EquipmentShopOwner.prefab` | 1 | 1 | 0 | (1.574, 1.85, 0.387) |
| WoodenSofa | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_WoodenSofa.prefab` | 1 | 1 | 0 | (2.4, 1.196, 1.292) |
| EffectsPedals | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_EffectsPedals.prefab` | 1 | 1 | 0 | (0.543, 0.55, 0.137) |
| ElectronicDrumKit | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_ElectronicDrumKit.prefab` | 19 | 19 | 0 | (2.15, 1.589, 2.149) |
| GuitarPedal | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_GuitarPedal.prefab` | 1 | 1 | 0 | (0.32, 0.299, 0.058) |
| Loudspeaker | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_Loudspeaker.prefab` | 1 | 1 | 0 | (0.404, 0.403, 0.9) |
| MidiKeyboardController | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_MidiKeyboardController.prefab` | 1 | 1 | 0 | (1.35, 0.31, 0.673) |
| ModularSynthesizer | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_ModularSynthesizer.prefab` | 1 | 1 | 0 | (1.051, 0.608, 1.4) |
| SpeakerBox | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_SpeakerBox.prefab` | 1 | 1 | 0 | (0.731, 0.66, 0.8) |
| StackedSpeaker | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_StackedSpeaker.prefab` | 1 | 1 | 0 | (0.918, 0.738, 1.75) |
| StageSpotlight | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_StageSpotlight.prefab` | 1 | 1 | 0 | (0.265, 0.369, 0.6) |
| StereoSpeaker | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_StereoSpeaker.prefab` | 1 | 1 | 0 | (0.541, 0.918, 1.05) |
| VintageSpeaker | `Assets/_SashimiBoy/Art/Generated/Prefabs/EquipmentShop/PF_EquipmentShop_VintageSpeaker.prefab` | 1 | 1 | 0 | (0.727, 0.392, 0.95) |

## Scene contract

- Zones: Counter/Repair, Demo, Instrument, Waiting.
- Preserved references: player entry/exit, owner dialogue, purchase interaction, equipment inspection, and story objective sightline.
- Legacy placeholder renderers and colliders are disabled; existing gameplay components and protected transforms are preserved.
- No source FBX is placed directly in the scene; all source visuals use generated wrapper prefabs.

## Human verification

1. Open `EquipmentShopAssetGallery` and inspect all 13 wrappers and materials.
2. Enter EquipmentShop from Street and confirm the counter and owner read clearly from the entrance.
3. Walk to the counter, both display zones, waiting area, and return door without obstruction.
4. Exercise purchase and leave controls and confirm the first-person camera remains suspended while UI is open.
