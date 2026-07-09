using System.Collections.Generic;

namespace SashimiBoy
{
    /// <summary>
    /// Script-only fallback data. Stage 01 has reward/progression metadata only; its music, beatmap, and playable scene are intentionally not authored here.
    /// </summary>
    public static class ContentDefaults
    {

        public static StageRuntimeData CreateStageOneProgressionStub()
        {
            return new StageRuntimeData
            {
                stageId = SashimiBoyConstants.StageIds.Salmon,
                displayName = "연어",
                order = 1,
                fishType = FishType.Salmon,
                hiphopGenre = "Lo-fi hip hop",
                bpm = 90f,
                requiredPreviousStageId = string.Empty,
                nextStageId = SashimiBoyConstants.StageIds.Rockfish,
                rewardEquipment = EquipmentId.SamplePackDrumKit,
                rewardPlates = 1,
                requiredPlatesForExchange = 1,
                inspirationPopup = "다채로운 비트를 원해?",
                kevinShopRequest = "다채로운 비트를 원합니다.",
                shopkeeperRecommendation = "흠… 자네 같은 풋내기에겐 이 샘플팩이 딱이네. 킥이랑 스네어부터 또렷하게 들어보게.",
                distractionCues = new List<DistractionCue>()
            };
        }

        public static List<StageRuntimeData> CreateRewardStagesIncludingStageOneStub()
        {
            List<StageRuntimeData> stages = new List<StageRuntimeData> { CreateStageOneProgressionStub() };
            stages.AddRange(CreateStagesExceptStageOne());
            return stages;
        }

        public static List<StageRuntimeData> CreateStagesExceptStageOne()
        {
            return new List<StageRuntimeData>
            {
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Rockfish,
                    displayName = "우럭",
                    order = 2,
                    fishType = FishType.Rockfish,
                    hiphopGenre = "Boom bap",
                    bpm = 88f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Salmon,
                    nextStageId = SashimiBoyConstants.StageIds.Sole,
                    rewardEquipment = EquipmentId.DawSoftware,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "비트를 찍을 때가 됐어",
                    kevinShopRequest = "비트를 찍을 때가 됐습니다.",
                    shopkeeperRecommendation = "흠… 자네 같은 풋내기에겐 작업을 시작할 기본 프로그램이 딱이네.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.CameraShake, beat = 16f, durationBeats = 0.5f, intensity = 0.35f, label = "kick shake" },
                        new DistractionCue { type = DistractionType.CameraShake, beat = 32f, durationBeats = 0.5f, intensity = 0.45f, label = "kick shake" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Sole,
                    displayName = "가자미",
                    order = 3,
                    fishType = FishType.Sole,
                    hiphopGenre = "Jazz Rap",
                    bpm = 98f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Rockfish,
                    nextStageId = SashimiBoyConstants.StageIds.Mullet,
                    rewardEquipment = EquipmentId.Plugin,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "소리를 더 뚜렷하게 구별하길 원해?",
                    kevinShopRequest = "소리가 더 뚜렷하게 구별되길 원합니다.",
                    shopkeeperRecommendation = "겹쳐 들리는 소리를 나누려면 기본 플러그인이 필요하지.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.CameraFlip, beat = 24f, durationBeats = 4f, intensity = 1f, label = "left right confusion" },
                        new DistractionCue { type = DistractionType.DirectionReverse, beat = 40f, durationBeats = 4f, intensity = 1f, label = "reverse visual sign" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Mullet,
                    displayName = "숭어",
                    order = 4,
                    fishType = FishType.Mullet,
                    hiphopGenre = "Boom Trap / Jazz Trap",
                    bpm = 110f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Sole,
                    nextStageId = SashimiBoyConstants.StageIds.Mackerel,
                    rewardEquipment = EquipmentId.MidiKeyboard,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "비트 위에 멜로디를 얹어보자",
                    kevinShopRequest = "비트 위에 멜로디를 얹어보고 싶습니다.",
                    shopkeeperRecommendation = "손으로 코드를 얹고 싶다면 MIDI 키보드가 먼저겠지.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.CameraCut, beat = 16f, durationBeats = 0.25f, intensity = 1f, label = "quick camera cut" },
                        new DistractionCue { type = DistractionType.CameraCut, beat = 20f, durationBeats = 0.25f, intensity = 1f, label = "quick camera cut" },
                        new DistractionCue { type = DistractionType.CameraCut, beat = 24f, durationBeats = 0.25f, intensity = 1f, label = "quick camera cut" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Mackerel,
                    displayName = "고등어",
                    order = 5,
                    fishType = FishType.Mackerel,
                    hiphopGenre = "Lo-fi HipHop / Cloud Rap",
                    bpm = 76f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Mullet,
                    nextStageId = SashimiBoyConstants.StageIds.Yellowtail,
                    rewardEquipment = EquipmentId.MonitoringHeadphones,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "필요한 소리만 듣길 원해?",
                    kevinShopRequest = "필요한 소리만 듣고 싶습니다.",
                    shopkeeperRecommendation = "주변 소리에 흔들리지 않으려면 모니터링 헤드폰이 필요해.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.ScreenPulse, beat = 12f, durationBeats = 2f, intensity = 0.3f, label = "reverb-like ambiguity" },
                        new DistractionCue { type = DistractionType.LightFlicker, beat = 28f, durationBeats = 4f, intensity = 0.45f, label = "blur timing" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Yellowtail,
                    displayName = "방어",
                    order = 6,
                    fishType = FishType.Yellowtail,
                    hiphopGenre = "Trap",
                    bpm = 140f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Mackerel,
                    nextStageId = SashimiBoyConstants.StageIds.RoughscaleSole,
                    rewardEquipment = EquipmentId.AudioInterface,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "녹음을 준비할 때가 왔어",
                    kevinShopRequest = "녹음을 준비할 때가 됐습니다.",
                    shopkeeperRecommendation = "입력되는 소리에 확신이 필요하면 오디오 인터페이스부터 챙기게.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.CameraShake, beat = 8f, durationBeats = 0.25f, intensity = 0.8f, label = "heavy kick impact" },
                        new DistractionCue { type = DistractionType.CameraZoom, beat = 16f, durationBeats = 1f, intensity = 0.55f, label = "screen compression" },
                        new DistractionCue { type = DistractionType.CameraShake, beat = 24f, durationBeats = 0.25f, intensity = 0.85f, label = "heavy kick impact" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.RoughscaleSole,
                    displayName = "줄가자미",
                    order = 7,
                    fishType = FishType.RoughscaleSole,
                    hiphopGenre = "High-tempo Boom bap / New York BoomBap",
                    bpm = 116f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Yellowtail,
                    nextStageId = SashimiBoyConstants.StageIds.ConvictGrouper,
                    rewardEquipment = EquipmentId.XlrCable,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "슬슬 녹음을 시작할까",
                    kevinShopRequest = "슬슬 녹음을 시작할까 합니다.",
                    shopkeeperRecommendation = "장비가 있어도 연결이 안 되면 소리는 못 가지. XLR 케이블을 가져가게.",
                    distractionCues = new List<DistractionCue>()
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.ConvictGrouper,
                    displayName = "능성어",
                    order = 8,
                    fishType = FishType.ConvictGrouper,
                    hiphopGenre = "Glitch Hop / Electro HipHop",
                    bpm = 112f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.RoughscaleSole,
                    nextStageId = SashimiBoyConstants.StageIds.Tuna,
                    rewardEquipment = EquipmentId.MicStand,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "서서 제대로 녹음 해보길 원해?",
                    kevinShopRequest = "서서 제대로 녹음 해보고 싶습니다.",
                    shopkeeperRecommendation = "이제 손을 비워야지. 마이크 스탠드가 필요할 거야.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.Occluder, beat = 16f, durationBeats = 2f, intensity = 1f, label = "kitchen hand blocks view" },
                        new DistractionCue { type = DistractionType.CameraShake, beat = 32f, durationBeats = 1f, intensity = 0.65f, label = "complex visual noise" },
                        new DistractionCue { type = DistractionType.LightFlicker, beat = 48f, durationBeats = 4f, intensity = 0.8f, label = "fluorescent flicker" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.Tuna,
                    displayName = "참치",
                    order = 9,
                    fishType = FishType.Tuna,
                    hiphopGenre = "Drill",
                    bpm = 140f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.ConvictGrouper,
                    nextStageId = SashimiBoyConstants.StageIds.SawedgedPerch,
                    rewardEquipment = EquipmentId.PopFilter,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "디테일을 잡아보자",
                    kevinShopRequest = "디테일을 잡고 싶습니다.",
                    shopkeeperRecommendation = "녹음 직전엔 작은 소리도 거슬리지. 팝필터 하나 챙기게.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.CameraZoom, beat = 16f, durationBeats = 8f, intensity = 0.7f, label = "long pressure" },
                        new DistractionCue { type = DistractionType.Occluder, beat = 32f, durationBeats = 2f, intensity = 1f, label = "strong view block" },
                        new DistractionCue { type = DistractionType.CameraFlip, beat = 48f, durationBeats = 4f, intensity = 1f, label = "late-stage confusion" }
                    }
                },
                new StageRuntimeData
                {
                    stageId = SashimiBoyConstants.StageIds.SawedgedPerch,
                    displayName = "다금바리",
                    order = 10,
                    fishType = FishType.SawedgedPerch,
                    hiphopGenre = "Rage / Hyper Trap",
                    bpm = 152f,
                    requiredPreviousStageId = SashimiBoyConstants.StageIds.Tuna,
                    nextStageId = string.Empty,
                    rewardEquipment = EquipmentId.DynamicMicrophone,
                    rewardPlates = 1,
                    requiredPlatesForExchange = 1,
                    inspirationPopup = "무대에 오르길 원해?",
                    kevinShopRequest = "무대에 오르고 싶습니다.",
                    shopkeeperRecommendation = "그럼 이제 네 목소리를 들려줄 차례지. 다이내믹 마이크를 가져가게.",
                    distractionCues = new List<DistractionCue>
                    {
                        new DistractionCue { type = DistractionType.ScreenPulse, beat = 64f, durationBeats = 8f, intensity = 1f, label = "final-stage performance pressure" }
                    }
                }
            };
        }

        public static List<EquipmentRuntimeData> CreateEquipment()
        {
            return new List<EquipmentRuntimeData>
            {
                new EquipmentRuntimeData(EquipmentId.SamplePackDrumKit, "샘플팩/드럼킷", "비트 제작용 사운드 소스", "킥과 스네어가 더 또렷하게 인식된다.", "히트 순간 드럼 파형이 튀어나온다."),
                new EquipmentRuntimeData(EquipmentId.DawSoftware, "DAW 소프트웨어", "비트메이킹과 녹음 편집의 기본 작업 환경", "리듬 구조를 인식하기 시작한다.", "킥/스네어 타격에 짧은 임팩트가 붙는다."),
                new EquipmentRuntimeData(EquipmentId.Plugin, "플러그인", "EQ, 컴프레서 등 기본 사운드 가공", "겹친 소리가 분리되어 들린다.", "사운드 계열별로 비주얼 레이어가 나뉜다."),
                new EquipmentRuntimeData(EquipmentId.MidiKeyboard, "MIDI 키보드", "멜로디와 코드 입력", "리듬 위에 멜로디 라인을 얹는다.", "화면 위로 곡선형 멜로디 파형이 흐른다."),
                new EquipmentRuntimeData(EquipmentId.MonitoringHeadphones, "모니터링 헤드폰", "비트 제작 및 기본 모니터링", "방해 요소의 체감 영향이 줄고 타이밍 기준이 또렷해진다.", "필요한 노트만 강하게 밝아진다."),
                new EquipmentRuntimeData(EquipmentId.AudioInterface, "오디오 인터페이스", "외부 장비 연결 및 녹음 준비", "입력 타이밍에 대한 확신이 생긴다.", "입력 지점과 노트 중심이 빛으로 연결된다."),
                new EquipmentRuntimeData(EquipmentId.XlrCable, "XLR 케이블", "마이크 신호 전달", "소리 흐름이 끊기지 않고 이어진다.", "오디오 인터페이스 연결 연출이 강해진다."),
                new EquipmentRuntimeData(EquipmentId.MicStand, "마이크 스탠드", "마이크 고정", "녹음 자세가 잡힌다.", "장비룸에 세워지는 오브젝트로 표현한다."),
                new EquipmentRuntimeData(EquipmentId.PopFilter, "팝필터", "파열음 제거", "작은 디테일을 정리한다.", "마이크 계열 장비와 묶어서 연출한다."),
                new EquipmentRuntimeData(EquipmentId.DynamicMicrophone, "다이내믹 마이크", "기본 보컬 녹음 시작", "플레이가 공연으로 전환될 준비가 끝난다.", "관객 반응과 조명이 히트에 반응한다.")
            };
        }

        public static StageRuntimeData FindStage(string stageId)
        {
            List<StageRuntimeData> stages = CreateRewardStagesIncludingStageOneStub();
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i].stageId == stageId)
                {
                    return stages[i];
                }
            }

            return null;
        }

        public static EquipmentRuntimeData FindEquipment(EquipmentId equipmentId)
        {
            List<EquipmentRuntimeData> equipment = CreateEquipment();
            for (int i = 0; i < equipment.Count; i++)
            {
                if (equipment[i].equipmentId == equipmentId)
                {
                    return equipment[i];
                }
            }

            return default;
        }
    }

    public struct EquipmentRuntimeData
    {
        public EquipmentId equipmentId;
        public string displayName;
        public string role;
        public string experienceChange;
        public string visualFeedback;

        public EquipmentRuntimeData(EquipmentId equipmentId, string displayName, string role, string experienceChange, string visualFeedback)
        {
            this.equipmentId = equipmentId;
            this.displayName = displayName;
            this.role = role;
            this.experienceChange = experienceChange;
            this.visualFeedback = visualFeedback;
        }
    }
}
