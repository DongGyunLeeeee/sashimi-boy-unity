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
                // Owner transcript received 2026-10-30, Issue76. Page 0 means chat source, not the older PDF.
                case "junseok": return new[] {
                    Say("준석","어 케빈. 오랜만이다.",0), Say("준석","너 아직도 음악 해?",0), Say("케빈","어.",0),
                    Say("준석","대단하다.",0), Say("케빈","뭐가.",0), Say("준석","나는 이제 퇴근하면 유튜브 볼 힘밖에 없는데.",0),
                    Say("케빈","너도 다시 해.",0), Say("준석","미쳤냐.",0), Say("준석","나 여덟 시 출근이야.",0),
                    Say("케빈","…",0), Say("준석","솔직히 네가 좀 부럽다.",0), Say("케빈","뭐가.",0),
                    Say("준석","아직 한다는 게.",0), Say("케빈","너는 돈 벌잖아.",0), Say("준석","너도 돈 벌잖아.",0),
                    Say("케빈","생선 썰어서.",0), Say("준석","나는 엑셀 썰어.",0),
                    Think("(직장인의 사시미는 Ctrl, Alt… 뭐 그런 것들이었다.)",0) };
                case "cheolsu_day3": return new[] {
                    Say("철수","총각.",0), Say("철수","음악 한다며.",0), Say("케빈","누가 그래요?",0),
                    Say("철수","사장이.",0), Say("철수","나도 음악 좀 했어.",0), Say("케빈","무슨 음악이요?",0),
                    Say("철수","고등학교 때 기타.",0), Say("케빈","밴드 하셨어요?",0), Say("철수","아니.",0),
                    Say("철수","기타 샀어.",0), Say("케빈","…….",0), Say("철수","두 달 치고 팔았지.",0), Say("케빈","…",0) };
                case "jaehoon": return new[] {
                    Say("재훈","케빈?",0), Say("케빈","어? 재훈아?",0), Say("재훈","와 진짜 오랜만이다.",0),
                    Act("handshake","둘이 악수한다.",0), Say("재훈","너 요즘 뭐 해?",0), Say("케빈","나 그냥 일하지.",0),
                    Say("재훈","어디서?",0), Say("케빈","횟집.",0), Say("재훈","……아.",0), Think("(그 ‘아’ 뭐냐.)",0),
                    Say("재훈","그래도 기술이지 뭐.",0), Say("재훈","나는 이번에 여의도로 옮겼거든.",0), Say("케빈","회사?",0),
                    Say("재훈","응. 자산운용 쪽.",0), Think("(갑자기 내 손에서 광어 냄새가 심해진 것 같다.)",0),
                    Say("재훈","너 음악 한다고 했었잖아.",0), Say("케빈","어. 아직 해.",0), Say("재훈","아직?",0), Think("(‘아직’?)",0) };
                default: return new DialogueLine[0];
            }
        }
    }
}
