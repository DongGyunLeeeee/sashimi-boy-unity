namespace SashimiBoy
{
    public enum GameLocation
    {
        Unknown,
        Street,
        FishShop,
        EquipmentShop,
        Club,
        FishStage
    }

    public enum FishType
    {
        Salmon,
        Rockfish,
        Sole,
        Mullet,
        Mackerel,
        Yellowtail,
        RoughscaleSole,
        ConvictGrouper,
        Tuna,
        SawedgedPerch
    }

    public enum EquipmentId
    {
        SamplePackDrumKit,
        DawSoftware,
        Plugin,
        MidiKeyboard,
        MonitoringHeadphones,
        AudioInterface,
        XlrCable,
        MicStand,
        PopFilter,
        DynamicMicrophone
    }

    public enum JudgeGrade
    {
        Nasty,
        Smooth,
        Slipped,
        Whack
    }

    public enum TimingSide
    {
        Early,
        Center,
        Late
    }

    public enum RhythmDifficulty
    {
        Casual,
        Normal,
        Strict
    }

    public enum SliceInputType
    {
        Tap,
        HoldStart,
        HoldEnd,
        RepeatTap,
        SwitchLeft,
        SwitchRight
    }

    public enum DistractionType
    {
        None,
        CameraShake,
        CameraFlip,
        CameraCut,
        CameraZoom,
        Occluder,
        LightFlicker,
        ScreenPulse,
        DirectionReverse
    }
}
