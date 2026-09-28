# #39 Stage1 playable handoff — Kevin 몸·요리 시점·HUD, 2026-09-09

> 후속 #42 Owner 요청으로 공유 Kevin 얼굴은 `CuteFace`로 교체하고 목을 Head/Neck/Chest에 연결했다. 기존 Stage1 Scene·차트·판정·손 IK는 보존했다. 최신 얼굴·목·손 회귀 증거와 실행 빌드는 [월드 전달문서](DAY01_DAY02_WORLD_HANDOFF.md)의 “후속 Kevin CuteFace와 목 연결 수정”을 따른다. 아래 AmbiguousFace 설명과 이전 빌드는 최초 #39 이력이다.

**Stage01 Scene과 Windows 빌드에 전신 Kevin, 몸에 연결된 손, 요리 시점, 집중형 HUD를 적용했다. 실제 실행 창에서 Space 판정/점수 변화와 실패→횟집→E 재입장을 확인했다. 전체 클리어는 자동 판정 입력으로 검증했으며, 최신 빌드의 일반 키보드 완주와 Owner의 최종 플레이 감각은 미검증이다.**

## 기준과 보존

- 작업 폴더: C:\Users\02031\AppData\Local\SashimiBoyGame\Issue39
- 브랜치/HEAD: feat/39-semantic-phase-gates / e4e26386da695c179216137240911fe200b81856.
- SPEC_VERSION 1.0.2, blob 6d7de6e6abef13b18021a3591debc53ac00616d4.
- Downloads의 SASHIMI_PLAYABLE_STAGE1_BRIEF.md 전문, 현재 세션의 실행 승인과 후속 Owner 요청을 기준으로 작업했다.
  이번 후속 요청은 요리하는 사람의 시점, 기존 얼굴에 맞춘 완전한 몸, 몸에 연결된 칼 쥔 손, 노트 주변의 판정/점수 가독성이다.
- 이번 GitHub 재조회에서 최신 Owner 댓글은 [승인 차트 v2](https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/39#issuecomment-5586247062)였다.
  brief의 owner-issue39-playable-stage1-20260908-v1과 동일한 원격 댓글은 확인하지 못했다. 현재 채팅의 후속 요청을 원격 게시 완료로 간주하지 않았다.
- 최초 미커밋 변경 43개: %TEMP%\SashimiBoyIssue39\BeforePlayable-20260909-002307.
- 직전 시각 수정 전 304개: %TEMP%\SashimiBoyIssue39\BeforeOwnerVisualRevision-20260909-023925.
- 이번 수정 전 365개: %TEMP%\SashimiBoyIssue39\BeforeEmbodiedKevin-20260909-133523.
  각 스냅샷에 파일 사본, SHA-256 manifest, git status, tracked diff가 있다.
- Logs/Stage1Embodiment/PreservationFinal.json: 기존 파일 유실 0, 기존 Art/Source 본문 변경 0.
  두 AudioClock 테스트, NaN 방어가 있는 SliceCuePresenter, 승인 차트 authoring 코드와 semantic beatmap,
  ProjectSettings는 365개 스냅샷의 내용과 바이트가 같다.
- stage/commit/push 및 PR/Issue/Project 상태 변경을 하지 않았다. #52/HostAutomation, 다른 Issue와 사용자 base checkout을 수정하지 않았다.

## Kevin 전신과 요리 시점

- [Quaternius Ultimate Modular Men](https://quaternius.com/packs/ultimatemodularcharacters.html)의 CC0 Casual2 Humanoid FBX를 가져와 Unity에 적용했다.
  구매나 별도 실행 코드 설치 없이 공개 모델/라이선스 파일을 사용했다.
- 기존 기본 Kevin인 PF_Character_Kevin_AmbiguousFace의 사용자 제공 얼굴/목 메시와 원래 UV/텍스처를 유지해 새 몸의 Head bone에 연결했다.
  기존 제공 모델은 정적인 전신 모델이므로 얼굴 부분을 생성 메시로 분리했다. 원본 FBX/텍스처는 변경하지 않았다.
- 생성 prefab: Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin/PF_Kevin_Complete.prefab.
  몸통, 양팔/손/손가락, 양다리/신발과 얼굴이 있는 Humanoid다.
  기존 기본 Kevin catalog 항목이 이 prefab을 참조하며, catalog 재생성 후에도 유지하도록 연결했다.
- Stage1의 두 손은 이 몸의 SkinnedMesh와 실제 상완→팔꿈치→손목 bone chain이다.
  오른손은 원래 칼/집게의 손잡이 목표를 잡고, 왼손은 생선/필렛을 받친다.
  팔 IK는 bone 회전만 사용한다. 분리된 손 메시를 위치만 따라 움직이는 방식은 제거했다.
- 손가락을 실제 finger bone으로 굽히고 작업 때 허리를 숙인다. 완성된 모션캡처/걷기 애니메이션을 추가한 것은 아니다.
  횟집 이동 시 몸은 플레이어와 함께 움직이지만 다리 보행 사이클은 아직 없다.
- 도마 높이를 약 0.9m에 맞추고 조리대 아래 몸통/cabinet을 배치했다.
  카메라는 작업 중 Kevin의 실제 eye anchor를 따라가며 아래로 48°, Perspective FOV 67°로 음식을 본다.
  실제 손질 대상에 맞춰 몸과 시점이 옆으로 움직이고, 완성 때 접시가 보이는 위치로 이동한다.
- 1인칭에서는 얼굴만 ShadowsOnly로 숨긴다. 몸/팔/다리는 렌더링한다.
  횟집 복귀 때 가슴이 화면을 크게 덮던 문제도 eye anchor 높이/앞쪽 눈 위치로 수정했다.

## 판정/점수/게이지

- 생선 위쪽의 한 HUD에 단계, 회 개수, 노트, 마지막 판정, 총점, 콤보, 구간 품질, 통과 목표, 남은 노트를 모았다.
- 판정은 노트 판정선 바로 옆의 큰 텍스트로 보여 주고 다음 판정까지 유지한다.
  예전 화면 측면의 큰 이미지 배지 대신 같은 위치에서 NASTY/CLEAN/WHACK/MISS와 오차를 읽을 수 있다.
- 통과 목표 60% 선과 현재 품질을 가로 게이지에 표시한다. 점수/콤보는 그 바로 위에 있다.
  오래된 모서리 점수판/별도 게이지는 숨겨서 시선 이동과 중복 표시를 줄였다.
- 튜토리얼 Space/NOW 안내를 HUD 아래로, 대사는 화면 아래 작은 자막으로 배치했다.
  결과 패널은 오른쪽에 두어 왼쪽 완성 접시를 볼 수 있다.
- 실패는 총점이 낮아진 직후가 아니라 각 단계 마지막 노트 판정 후 구간 품질이 60% 미만일 때 발생한다.
  오디오 정지 → 약 1.4초 흐림 → 실제 FishShop 복귀 → 시작대 E 재입장이 이어진다.
- 판정 창 밖의 유효한 Space 입력 1회: 총점 −100(하한 0), 구간 품질 −0.35, 콤보 초기화.
  이 감점 수치는 이전 후속 요청에서 적용한 조정 초안이며 Owner의 최종 수치 승인은 별도다.
  무입력 미스는 0점이며 추가 총점 감점은 없다. 무효/중복/이전 run 입력은 감점하지 않는다.
- 157노트, 6구간 26/26/26/26/26/27, BPM 88, downbeat 0.683초,
  시작 11.592초/끝 120.683초, 판정 창 45/90/140ms, 품질 가중치 1/0.75/0.35/0과 통과 비율 0.60을 보존했다.

## 손질 순서와 실제 연어 에셋

| 구간 | 대상/작업 | 실제 Game 카메라 변화 |
|---|---|---|
| 1 | 머리/몸통 사이 절단 | 첫 시범부터 목 경계에서 칼질하고 실제 Head 분리 |
| 2 | 지느러미 손질 | Fins 분리, 실제 Fillet+Spine 상태로 전환 |
| 3 | 척추 분리 | 실제 Spine 분리 후 온전한 Fillet 유지 |
| 4 | 필렛을 반으로 가르기 | 실제 FilletHalf 2개, 앞쪽 반쪽에 가시 8개 |
| 5 | 반쪽 가시 제거 | 실제 집게와 연결된 손으로 PinBone 제거 |
| 6 | 반쪽 사시미질 | 실제 salmonpiece 12점이 접시를 채우고 사용한 반쪽이 줄어듦 |

- 필렛/반쪽 윗면은 원래 3D 메시와 텍스처다. CrossSection PNG는 회와 반쪽 사이의 수직 절단면에만 사용한다.
- 실제 PF_Prop_KitchenKnife와 curved_tweezer를 사용한다. 도마를 사람 크기에 맞춘 뒤 칼/집게/분리 파츠 이동 거리도 같은 비율로 보정했다.
- 완료 접시, 남겨 둔 반쪽, 제거 부위 트레이를 따로 배치했다.
- 재시도는 파츠/가시/회/도구/몸 자세/HUD/점수/콤보/노트커서/시계를 초기화하고 중복 보상을 방지한다.

## 에셋과 생성 방식

- 기존 PR #47의 추가 에셋 통합과 meta/GUID는 보존했다. PR 병합은 하지 않았다.
- 제공 SalmonButchery 및 Day01_Day02의 Head/Body/Fins/Spine/Fillet/PinBone/FilletHalf/SashimiSlice/CrossSection을 재사용한다.
- 신규 몸 원본/라이선스/출처: Assets/_SashimiBoy/Art/Source/Characters/Kevin/Body/Quaternius.
  Casual2.fbx SHA-256: 16087f97266eab15cbc154859dc9cb8c0dd068f7adcaa6ad4be685744bd61d49.
  원래 Quaternius 머리는 생성 wrapper에서 숨기며 원본 파일은 수정하지 않았다.
- 앞선 DevMops 손 소스/생성물은 보존했지만 현재 Stage1의 보이는 손으로 사용하지 않는다.
- Stage01PlayableAuthoring에서 KevinEmbodimentAuthoring, Stage01CookingAuthoring과 기존 Stage01VisualRevisionAuthoring을 호출한다.
  얼굴 추출/재질/전신 prefab/HUD/Scene은 이 생성기로 재현한다. 기존 Source의 FBX/PNG/audio를 재작성하지 않는다.
- RM 맵은 채널 계약이 없어 여전히 미연결이다. Stage2/스토리 등 다른 Issue 범위는 추가하지 않았다.

## 실행한 검증

최신 로그 루트는 **Logs/Stage1Embodiment**다. 앞선 Stage1Revision/Stage1Playable 결과와 구분한다.

- Unity 6000.4.0f1 import/compile/authoring/save와 Scene 참조 검사: authoring-05.log, native exit 0.
- 최종 EditMode 111/111, PlayMode 24/24, 실패/skip 0, 각 native exit 0:
  editmode-final.xml/.log, playmode-final.xml/.log.
- 실제 저장 Scene의 모든 단계, 실제 모델 교체/접촉, 오입력/게이지, 반쪽/가시/단면/12점 접시,
  클리어/중복 보상/재시도, 마지막 단계 실패와 FishShop 재입장을 검사했다.
  이 테스트는 판정 메서드에 입력을 전달한다. 일반 키보드 완주 증거가 아니다.
- 신규 검증은 Humanoid/전신 renderer/연속된 arm bones/팔 길이 유지/손잡이 도달 거리,
  실제 눈 위치와 pitch, 노트 옆 판정/점수 표시, 횟집 복귀 후 눈 위치를 검사한다.
- 수정 중 playmode-02에서 손이 목표에 0.225m 못 닿았고 playmode-04에서 0.099m 오차를 검출했다.
  도마 방향/생선 거리/왼손 접촉 위치와 시점 소유권을 고쳤다. 실패 로그를 보존했다.
  최종 모든 구간 검사에서 grip 오차 0.055m 이내를 통과했다. 수치가 완벽한 손가락 접촉/미감을 보장하지는 않는다.
- Missing Script/serialized reference/material/중복 active AudioListener/EventSystem scan 통과.
  StaticIntegrity.json: serialized 파일 237개, GUID 중복/미해결 0. git diff --check exit 0.
  MetaIntegrity.json: import 대상의 meta 누락/고아 meta 0.
- Windows Development build 성공: build-final.log, native exit 0.
  최종 authoring/build/player에 새 C# 컴파일/managed exception/Shader/NaN 오류 없음.
  batch Editor의 라이선스 토큰 갱신 진단은 있으나 import/테스트/build가 완료됐다.
  AudioClock 오류 처리 테스트의 기대 진단은 실제 신규 Console 오류와 구분했다.
  Player에는 D3D12 info queue 조회/초기 upload buffer 확대 진단이 남아 있다.
- 검증은 -stage1Validation으로 실사용 세이브 읽기/쓰기/삭제를 건너뛴다.

## 최신 빌드와 실제 화면 증거

- 최신 빌드: **Builds/Stage1Validation-20260909-051036/SashimiBoyStage1.exe**.
- 같은 폴더의 **Play-Stage1Validation.cmd**로 실행한다.
  검증/화면 기록 인자로 Stage01에서 바로 시작한다. Space, 성공 뒤 R 재도전/F 횟집, 횟집 시작대 E.
- Editor Scene: Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity.
  재생성 메뉴: Sashimi Boy → Stage 01 → Apply Playable Stage1.
- Logs/Stage1Embodiment/CameraEvidence:
  00-connected-hands-judged-test.png, 00-complete-kevin-external-judged-test.png,
  01-start-judged-test.png부터 07-completed-plate-judged-test.png까지,
  08-failure-blur-judged-test.png, 09-shop-body-view-judged-test.png.
  실제 Game Camera.Render를 열어 전신/손질 단계/반쪽/회 한 판/실패 흐림/횟집을 확인했다.
  외부 시점 이미지는 전신 결합을 검사하기 위한 카메라다. 이 파일들은 overlay HUD/물리 키보드 증거가 아니다.
- 최신 051036 빌드의 실제 실행 창에서 직접 Space와 E를 입력했다.
  첫 Space의 EMPTY HIT와 품질 −0.35, 재입장 후 CLEAN −89ms / 총점 700 / 콤보 1 / 품질 0.75를 관찰했다.
  CLEAN은 내부 enum SMOOTH의 화면 표시 이름이다.
  노트/판정/점수/목표가 같은 HUD에 나타나는 것과 실패 후 횟집 복귀, E 재도전 초기화를 확인했다.
  최신 빌드에서는 보안 UI에 막히지 않았다.
- 원본 기록: Builds/Stage1Validation-20260909-051036/Logs/Stage1Playable/InputEvidence.
  inputs.jsonl 3행은 EMPTY HIT, 50행은 실제 Space의 −88.576ms / 700점이다.
  20260909-051352-01-stage1-run1-phase0.png / 051423-02-failure.png / 051426-03-FishShopDialogue.png,
  051451-04-stage1-run2-phase0.png / 051522-05-failure.png / 051524-06-FishShopDialogue.png.
  실행 창의 CLEAN 화면은 세션 도구 화면으로 확인했고, 별도의 그 시점 PNG 파일은 생성되지 않았다.
- 앞선 빌드의 일반 입력 완주 기록은 보존하지만 이번 최종 빌드의 일반 키보드 완주로 대체하지 않는다.

## 미검증과 Owner 확인 항목

- 최신 빌드의 시작부터 끝까지 일반 키보드 클리어, 그 후 R/F 경로 및 마우스 버튼 클릭.
- 음악을 들으며 평가하는 싱크/리듬 가독성/입력감, 감점 수치, 움직이는 요리 시점의 편안함.
- 얼굴/몸의 스타일과 목 경계, 손 크기/손가락과 칼·집게 접촉, 제거 부위 배치/회 단면의 최종 미감.
  몸/양팔/손은 연결됐지만 보행 사이클, 완성된 손작업 애니메이션과 최종 캐릭터 미감을 보장하지 않는다.
- 실사용 디스크 save/persistence, 최종 해금/스토리 연동, 다른 하드웨어 성능.
- 독립 Review와 Owner 최종 승인. 테스트 수나 캡처만으로 게임 완성/Done을 선언하지 않는다.
