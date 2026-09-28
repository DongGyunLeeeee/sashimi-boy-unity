namespace SashimiBoy
{
    // PDF: Scenario/대본.pdf, SHA-256 a0278fbd458d583bd83982221621e65e731607d628d2b39c11e6489a80827378.
    // Speaker/thought/action are separate; headings, biographies and page footers are never played as dialogue.
    public static class DayWorldScenario
    {
        static DialogueLine Say(string speaker, string text, int page) => new DialogueLine { speaker=speaker, text=text, sourcePage=page };
        static DialogueLine Think(string text, int page) => new DialogueLine { speaker="케빈 · 속마음", text=text, kind=DialogueLineKind.Thought, sourcePage=page };
        static DialogueLine Act(string id, string source, int page) => new DialogueLine { kind=DialogueLineKind.Action, actionId=id, text=source, sourcePage=page, autoAdvanceDelay=1.1f };
        public static DialogueLine[] Lines(string id)
        {
            switch (id)
            {
                case "misuk": return new[] {
                    Say("미숙","어 케빈아.",1), Say("케빈","아 네, 아주머니.",1), Say("미숙","너 아직도 그 횟집 다니니?",1),
                    Say("케빈","네.",1), Say("미숙","오래 다닌다.",1), Say("케빈","네.",1), Say("미숙","오래 다니는 것도 능력이야.",1),
                    Think("(칭찬이다.)",2), Say("미숙","우리 준호는 이번에 삼성 들어갔거든.",2), Think("(아니다.)",2),
                    Say("미숙","너도 생선만 썰지 말고 뭐 하나 제대로 배워봐.",2), Say("케빈","저도 하고 있는 거 있어요.",2),
                    Say("미숙","뭔데?",2), Say("케빈","음악이요.",2), Say("미숙","교회 반주?",2), Say("케빈","랩이요.",2),
                    Say("미숙","…….",2), Say("미숙","욕하는 거?",2), Say("케빈","…",2) };
                case "cheolsu": return new[] {
                    Say("철수","총각.",4), Say("케빈","네.",4), Say("철수","오늘 회가 두껍다.",4), Say("케빈","죄송합니다.",4),
                    Say("철수","나쁘다는 게 아니야.",4), Say("케빈","네.",4), Say("철수","근데 회라는 게 말이지….",5),
                    Think("(시작됐다.)",5), Say("철수","칼이 아니라 마음으로 써는 거야.",5), Think("(그럼 아저씨가 마음으로 썰어보세요.)",5),
                    Say("철수","총각은 마음이 급해.",5), Say("케빈","제가요?",5), Say("철수","응.",5), Say("철수","딴생각 하고 있지?",5),
                    Act("kevin_flinch","케빈이 멈칫한다.",5), Think("(……귀신인가?)",5) };
                case "seongho": return new[] {
                    Act("motorcycle_stop","오토바이가 케빈 옆에 멈춘다.",6), Say("성호","야.",6), Say("케빈","어 형.",6),
                    Say("성호","어디 가냐.",6), Say("케빈","일.",6), Say("성호","나는 이미 일하고 있다.",6), Say("케빈","네.",6),
                    Say("성호","현재 시각 오전 10시 14분.",6), Say("성호","내가 너보다 46분 먼저 사회에 기여했다.",6), Think("(사회가 원했을까.)",6) };
                case "minjae": return new[] {
                    Say("민재","야.",3), Say("케빈","어 민재.",3), Say("민재","너 생선 냄새난다.",3),
                    Say("케빈","퇴근 중이니까.",3), // Owner-approved sole dialogue override, 2026-09-08.
                    Say("민재","아직도 회 써냐?",3), Say("케빈","어.",3), Say("민재","랩은?",3), Say("케빈","하지.",3),
                    Say("민재","몇 년째 하지?",3), Say("케빈","닥쳐.",3), Say("민재","데뷔보다 참치 해체가 먼저 마스터되겠네.",3),
                    Say("민재","곡 나오면 보내라.",4), Say("케빈","왜.",4), Say("민재","놀릴라고",4) };
                default: return new DialogueLine[0];
            }
        }
    }
}
