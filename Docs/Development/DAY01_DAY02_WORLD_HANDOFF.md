# Day1 · Day2 월드와 우럭 Stage2 전달문서 — Issue #42

2026-09-29의 GitHub 전달 통합은 [ISSUE42_INTEGRATION.md](ISSUE42_INTEGRATION.md)를
참고한다. 아래 내용과 당시의 push/PR 제한, 테스트 결과는 2026-09-12까지의 개발 이력이다.

현재 범위는 2026-09-12 Owner의 후속 요청인 **제공 로고, 새 게임 전 케빈 얼굴 선택, 작은 집, 가게 에셋 활용·밀도 개선, 횟집 홀/주방 구분, 사장님과 장비 구매**다. `25eaf06`의 기존 Stage1/Stage2와 월드 구현에서 이어갔다. 같은 #42 브랜치이며 다른 Issue 선택, #52 수정, 원격 push, PR 생성/병합, Issue 종료, Project/Done 변경은 하지 않는다. 구현자의 자체 검증과 독립 Reviewer/Owner 최종 검증은 구분한다.

## 2026-09-12 로고·얼굴 선택·공간 배치

아래의 이전 날짜 절은 이력이다. 이번 수정의 결과와 실행 방법은 이 절에 기록한다.

- 첫 화면은 기존 `Art/Source/Branding/SashimiBoyLogo.png` 원본 Sprite다. 새 그림으로 대체하거나 원본을 편집하지 않았다.
- **새 게임 시작하기 → 얼굴 미리보기/선택 → 이 얼굴로 시작하기 → 첫째 날 기상**으로 연결했다. 프로젝트의 CuteFace / PlainFace / AmbiguousFace / WesternFace 네 원본을 사용한다. 선택/취소 단계에서는 저장을 바꾸지 않고 확정할 때만 새 게임을 시작한다. `SaveData.kevinFaceId`를 저장하며 이어하기·대화·연습·Stage1/Stage2의 공통 Kevin 몸에 적용한다. 얼굴 항목이 없는 이전 저장은 기존 귀여운 얼굴을 사용한다.
- 기존 Humanoid 몸·팔·손 IK를 유지하고 파생 머리 Mesh/재질만 바꾼다. 새 얼굴의 원본 셔츠/어깨를 제외하고 목 아래를 기존 몸의 옷깃 안에 연결한다. 기존 CuteFace Mesh와 몸 애니메이션, 두 스테이지 Scene/음악/노트맵/판정 창은 보존했다.
- 케빈 집은 **10 × 8m → 4.8 × 4.6m**, 천장 2.65m로 줄였다. 상점 소파를 제거하고 제공 침대와 드럼/DAW 배치 자리만 남겼다. 중앙 문·기상 지점·장비 사용 위치·손 목표·연습 카메라도 함께 조정했다. 구매 전에는 기존과 같이 배치 프롬프트/악기가 보이지 않는다.
- 횟집은 입구 쪽에 식탁 3개/의자 6개와 식기를 두고, 뒤쪽에 타일 주방·서빙대·수납·진열장을 배치했다. 실제 손질대와 기존 `StageStarterInteractable`은 뒤 왼쪽 주방 `(-4, *, 2.7)`에 있다. 실패/중도 복귀는 그 앞의 실제 진입 위치로 돌아온다. 철수의 대화는 홀의 식탁 옆에서 유지한다.
- 장비 상점은 원본 전자드럼·키보드·신디사이저·페달·스피커들을 중앙 전시대와 벽 선반에도 배치했다. 출입구와 중앙 보행로를 남겼다. 구매 대상은 실제 `Owner_EquipmentShopOwner`의 Collider/상호작용이며 기존 책상의 `ShopService`는 비활성화했다. 사장님과 구매 프롬프트의 등장 조건, 품목·가격·보상은 바꾸지 않았다.

에셋 사용 감사 범위는 기존 프로젝트의 해당 상가 원본 모델이다. 단순 의존성 목록과 실제 화면/활성 Prefab 확인은 별도로 검사한다.

| 원본 그룹 | 실제 배치 |
|---|---|
| 장비 상점의 장비 11종 | EquipmentShop의 기존 전시 + 시연대/선반 |
| WoodenSofa, EquipmentShopOwner | EquipmentShop의 휴식 자리, 중앙 구매 NPC |
| Salmon, Rockfish, Mullet, Flounder | 횟집 뒤 진열대, 서빙대의 광어 접시 |
| SashimiTable, DisplayInside, KitchenKnife | 횟집 뒤 주방의 실제 원본 모델 |
| DisplayOutside | 거리의 횟집 문 왼쪽 진열장. 빠져 있던 기존 Prefab을 재사용 |

`Logs/DayWorld/LogoFaceLayoutRevision/Before.json`에 작업 전 4,998파일, 사용자 미커밋 14경로, 원본 드롭 522파일, 과거 빌드 저장 38개를 기록했고 `PreservedProfiles/`에 저장을 복사했다. 사용자 `ProjectSettings`/Recovery/Previews 변경은 그대로 남긴다. `preservation-and-usage.json`과 `*-source-usage.txt`, `*-hierarchy.txt`에서 원본·저장·Stage1/2·AudioClock·차트·Automation 보존과 상가 원본별 Scene 연결을 확인할 수 있다.

실행 파일은 **`Builds/DayWorldValidation-20260911-191707/SashimiBoyDayWorld.exe`**다. 폴더 시간은 UTC이며 이번 빌드는 한국 시간 2026-09-12에 만들었다. 지금은 새 로고 메뉴로 실행했다.

- `Play-New-Game.cmd`: 별도 `owner-logo-face-review` 저장으로 실행한다. **새 게임 시작하기 → 얼굴 네 가지 중 선택 → 이 얼굴로 시작하기 → 첫째 날 기상**을 확인한다. 돌아가기/Esc는 기존 저장을 지우지 않는다.
- `Play-Latest-Progress.cmd`: 가장 최근 실제 완료 저장의 사본으로 이어하기. 기존 두 스테이지 완료/장비 상태를 유지한다.
- `Play-Stage2-Review.cmd`: 이전 실제 Day2 횟집 도착 저장의 별도 사본. 이어하기 → 주방 뒤 왼쪽 손질대 → E로 우럭에 진입한다.

이전 빌드의 저장 7개를 새 빌드에 바이트 그대로 복사했고, 원래 빌드/저장은 변경하지 않았다. `build-launchers-and-profiles.json`에 출처와 해시가 있다. 최신 실제 완료 저장 SHA-256은 `b368311b623ca52e004ea000cb4c596e17c9dbc2889cf4454157f6d1ed43966d`다.

검증 로그와 결과는 **`Logs/DayWorld/LogoFaceLayoutRevision/`**에 있다.

- `edit-final.xml`: 113/113, native exit 0. 새 얼굴 4종의 실제 메시/목 연결/뼈 참조, 이전 저장 호환과 기존 월드·Stage1/2·AudioClock 회귀 검사를 실행했다.
- `play-final.xml`: 23/23, 459.12초, native exit 0. 얼굴 선택 취소 시 디스크 불변 → 확정/저장/이어하기 → 대화 → 기존 두 손질 몸의 얼굴 유지, 실제 CharacterController로 입구에서 뒤 주방 진입, 첫날 실패·복귀·재도전·클리어·구매·배치·연습·취침, 둘째 날 실제 DSP 우럭 클리어·구매·배치·연습·취침 경로를 검사했다. 테스트는 게임 컴포넌트/자동 입력을 사용했으며 물리 키보드 플레이의 증거가 아니다.
- `apply-03.log`의 8개 Scene 스크립트/직렬화 참조/AudioListener/EventSystem 감사 통과. 이후 `faces-04-apply.log`에서 마지막 얼굴 메시 수정을 적용하고 위 최종 검사를 다시 실행했다. 초기 테스트가 통과했어도 화면에서 발견한 얼굴의 원본 셔츠/늘어진 목 부분은 수정했으며, 초기 캡처는 최종 증거로 쓰지 않는다.
- `build-final.log`: Windows 빌드 성공, native exit 0. 빌드 전 권위 모델 생성기 재적용에서 우럭 112파일과 월드/문 1,165파일의 바이트 불변을 확인했다.
- `preservation-and-usage.json`: 사용자 사전 미커밋, 원본 522파일, 기존 저장 38개, 두 스테이지 Scene·음원·차트·AudioClock·Automation 변경 0. 누락/중복 GUID와 고아 .meta 0. 장비 상점 원본 13종과 횟집 관련 원본 8종의 실제 Scene 연결을 확인했다. `scene-scope.json`에는 기존 Scene 객체 보존 내역이 있다. `git diff --check`도 검사했다.

최종 Game 카메라 PNG/상태 JSON 44개를 이 실행의 **`CameraEvidence/`** 하위 폴더에 별도로 보존했다. `owner-logo-title`, `owner-customize-*` 네 얼굴, `owner-kitchen-start-interaction`, 상점/횟집 입구, 사장님 구매, `home-practice-day1/2-interaction-test`, `stage2-real-dsp-clear`를 직접 열어 확인했다. 작은 집에서 두 악기가 유지되고, 횟집 앞 식탁/뒤 작업대와 문 왼쪽 외부 진열장, 상점 전시대가 실제로 보인다. 디스크 저장/이어하기는 별도 테스트 프로필을 사용했다.

네이티브 실행 파일의 `Logs/Stage1Playable/InputEvidence/20260911-191832-01-Bootstrap.png`도 열어 제공 로고와 메뉴가 실제 Player에서 렌더링되는 것을 확인했다. `native-menu-state.json`에 Bootstrap/창 포커스 상태를 기록했다. 네이티브 로그에는 시작 시 D3D12 upload buffer 크기 메시지 2건이 있으며 렌더링/빌드는 정상 완료됐다. 이 실행에서 런타임 예외는 발견되지 않았으나 전체 프레임 성능 검사는 하지 않았다. Editor의 라이선스 토큰 갱신 메시지와 기존 실패 처리 테스트의 의도된 오류 로그는 게임의 새 예외와 구분했다.

Windows의 직접 키보드/마우스 검증은 **미검증**이다. computer-use의 `await sky.list_windows()`가 재시도와 JS 초기화 후에도 세 번 모두 `Computer Use native pipe is unavailable: failed to connect native pipe: 지정된 파일을 찾을 수 없습니다. (os error 2)`로 실패했다. MCP 호출이므로 프로세스 exit code는 없다. 승인 거절은 아니며 Windows 전역 설정이나 helper 설치/변경은 하지 않았다. Unity의 실제 Game 카메라 렌더링과 게임 컴포넌트를 통한 플레이 경로 검증은 별도로 수행한다.

재적용: 닫힌 Editor에서 `-executeMethod SashimiBoy.EditorTools.DayWorldOwnerPresentationAuthoring.ApplyBatch`. 기존 Bootstrap/Home/두 상점과 거리 진열장만 갱신한다. 기존 Scene을 전체 재생성하지 않는다. 얼굴의 권위 생성기는 `KevinCustomizationAuthoring`/`KevinEmbodimentAuthoring`, 실내 배치는 `DayWorldHomeAuthoring`/`DayWorldInteriorAuthoring`/`DayWorldVenueAuthoring`/`DayWorldVenueLayout`이다.

독립 Reviewer는 얼굴 네 가지의 목/턱·회전·스테이지 손 연결, 구매 전후 상호작용 조건, 실내 보행과 기존 문/도로 보존, 얼굴/장비의 디스크 저장 및 이어하기를 재검증해야 한다. 최종 사람의 카메라 감각·음악 동기·입력감·모델 아트 승인과 독립 Review는 이 구현자의 자체 검사와 구분한다.

## 2026-09-11 문·기상·Owner 에셋 수정

이 절은 2026-09-11 당시 실행 파일과 검증 결과의 이력이다.

- 케빈 집·횟집·장비 상점·클럽의 실내 출구를 전면 중앙에 맞췄다. 기존 문 상호작용/Collider와 외부와 공유하는 문 Prefab은 유지하고, 벽 개구부·문틀·입장 위치를 함께 이동했다. 예전 좌측 출입구의 꺾인 벽만 제거했다. `DayWorldInteriorAuthoring`는 기존 벽 객체를 재사용하여 Scene ID와 다른 소품 참조를 보존한다.
- 첫날과 둘째 날 모두 `Wake`에서는 WASD 이동과 출구/다른 상호작용을 막고 침대의 **E — 일어나기**만 사용할 수 있다. 기상 동작이 끝나면 이동이 열린다. 예전 버그로 자는 상태에서 거리까지 나가 저장된 경우도 이어하기가 침대로 복귀시킨다.
- 지정한 `Fish/Stage02`의 머리·몸통·척추·필렛·필렛 반쪽·회 한 점 6개 FBX와 텍스처를 실제 Stage2 Prefab에 연결했다. 원본은 손대지 않고 `Art/Source/DayWorld/Fish/Stage02/`에 36파일을 바이트 그대로 복사했다. 확장자가 없던 `rockfish_head`는 FBX 헤더를 확인하고 **프로젝트 사본만** `.fbx` 확장자를 붙였다.
- 공급 모델의 부위별 좌표/크기를 도마에 맞춘 파생 Mesh를 생성한다. 각 원본의 UV/기본색/노멀을 사용하며, 약 123만~149만 정점 원본을 약 0.9만~5.8만 정점의 게임용 사본으로 줄인다. 지느러미 배출 조각은 새 몸통에서 파생하며 개별 핀뼈 8개는 기존 표현을 유지한다. 예전 단면 PNG는 반쪽을 잘라낸 **절단면**에만 사용한다. 접시에 놓이는 12점은 새 `rockfish_piece` 모델이다.
- `clean.png / nasty.png / slipped.png / whack.png`는 사용자가 넣은 원본 그대로 Sprite로 임포트했다. Stage1과 Stage2의 노트 옆 HUD에서 투명 PNG를 원래 색/비율로 표시하고, 시간 오차/감점만 보조 글자로 남긴다. MISS는 WHACK 이미지와 ‘놓침’을 표시한다. 재시도 시 이전 이미지는 초기화한다.
- 음악, BPM, 노트 배치, 판정 창, 보상/구매값은 변경하지 않았다. Stage1 Scene은 HUD 연결을 위해 저장했으며 기존 스크립트 기본값 `stageId/chartAuthoringVersion`이 직렬화됐지만 차트는 재생성하지 않았다.

보존 기준은 로컬 `2767c41`, 같은 `feat/42-day01-day02-world` 브랜치다. `Logs/DayWorld/OwnerAssetDoorWakeRevision/Before.json`에는 사전 4,850파일, 사용자 변경 18경로, 우럭 원본 36파일, 과거 빌드 저장 31개의 해시가 있다. `PreservedProfiles/`에 저장 사본도 보관했다. 요청된 Judgement PNG 4개를 정확한 원본 바이트로 프로젝트에 포함하며, 나머지 사용자 14파일 변경은 유지한다. 실제 최신 `crosswalk-direction-review.json`은 이제 **둘째 날까지 완료한 저장**이다. 이를 이전 Stage2 진입 전 상태로 되돌리지 않는다.

실행 파일: `Builds/DayWorldValidation-20260911-141430/SashimiBoyDayWorld.exe`.

- `Play-Latest-Progress.cmd`: 작업 시작 시 최신 실제 완료 저장의 사본으로 이어하기.
- `Play-New-Game.cmd`: 별도 검증 프로필의 새 게임. 기상 전 WASD 잠금과 E 기상을 확인할 수 있다.
- `Play-Stage2-Review.cmd`: 이전 실제 Day2 횟집 도착 저장의 별도 사본. 이어하기 → 도마 시선 → E로 새 우럭을 확인한다. 최신 완료 저장을 덮어쓰지 않는다.

이번 실행의 로그/XML/보존 감사는 `Logs/DayWorld/OwnerAssetDoorWakeRevision/`에 있다.

- `apply-final.log`: Unity 적용/컴파일 native exit 0.
- `targeted-play.xml`: 최초 네 수정 경로 4/4. 통과 후에도 실제 화면에서 머리 방향/필렛 도마 관통을 발견해 수정했다.
- `visual-02.xml`: 수정된 우럭의 6단계와 실제 회 한 판 렌더링 1/1. 최종 검증은 이후 전체 PlayMode 결과를 기준으로 한다.
- `edit-final.xml`: 월드/Owner 모델·PNG/Stage1/AudioClock/차트/품질/구매 조건 111/111, native exit 0.
- `play-final.xml`: Day1 전 경로, Stage1 회귀, 기상/문/PNG, 실제 DSP Stage2 클리어와 Day2 구매·배치·취침 22/22, 469.22초, native exit 0. `centered-door-camera.xml`: 네 건물 전면의 중앙 문을 실제 보행 후 촬영/출입한 추가 확인 1/1, native exit 0.
- `build-final.log`: Windows 빌드 성공, native exit 0. 빌드 전 우럭 112파일과 기존 월드/문 975파일을 권위 생성기로 재적용해 바이트 불변을 확인했다. `preservation-final.json`: 원본/사용자 사전 변경/기존 저장/음원·차트·AudioClock·Automation 파일 변경 0, 누락/중복 GUID 및 고아 .meta 0. `git diff --check` 통과. Stage1 HUD 저장은 기존 공백 정규화를 재사용해 의미 없는 Scene 변경을 줄였다.
- 네이티브 Player는 `owner-wake-new-game` 검증 프로필의 **새 게임 메뉴에서 실행 중**이다. 실행 파일이 직접 저장한 `Logs/Stage1Playable/InputEvidence/20260911-141622-01-Bootstrap.png`를 열어 메뉴 렌더링을 확인했고 `native-menu-state.json`에 상태를 기록했다. **Windows 창에 키를 보내는 직접 검증은 미검증**이다. `await sky.list_windows()`가 3회 모두 `Computer Use native pipe is unavailable: failed to connect native pipe: 지정된 파일을 찾을 수 없습니다. (os error 2)`로 실패했다. 2초 대기/재시도 및 JS 세션 초기화/재연결을 수행했으나 복구되지 않아 UI 조작을 중단했다. MCP 호출이므로 별도 프로세스 exit code는 없다. 오류는 `native-ui-unavailable.json`에 보존했다. 권한 승인 거절이 아니라 세션의 조작 연결 오류다.

Game 카메라 증거는 `Logs/DayWorld/CameraEvidence/`의 `owner-wake-locked-day1/2`, `owner-judgement-Stage01_Salmon-*`, `owner-judgement-Stage02_Rockfish-*`, 네 실내의 `*-centered-front-wall`/`*-exit-walk`, `stage2-visual-phase-0`~`5`, `stage2-visual-complete-plate`, `stage2-real-dsp-*` PNG와 저장 상태 JSON이다. 판정 이미지 검사는 실제 노트 입력 판정/빈 입력/미스 경로를 사용했다. 빠른 부위 화면 검사는 시계를 정지시킨 판정 검사이고, 별도의 DSP 클리어 경로는 음악 시계를 건너뛰지 않는 자동 입력이다. 물리 키보드로 모든 노트를 연주한 증거는 아니다.

현재 뜬 메뉴에서 **새 게임**을 클릭하고, 기상 전 WASD로 이동하지 않는지 → E 기상 후 이동이 되는지 → 집/상가 중앙 문으로 왕복하는지 수동 확인하면 된다. 우럭은 게임 창을 닫은 뒤 `Play-Stage2-Review.cmd` → **이어하기** → 도마를 보고 E로 시작한다. 요청된 구현과 Unity Game 카메라/경로 검사는 완료했으며, 네이티브 키 입력과 최종 사람의 음악 동기/입력 감각·4:3 구도·새 모델 아트 승인·독립 Reviewer 검증은 **미검증**이다. 원격 push·PR·Issue/Project 상태·Done 처리는 하지 않았다. #52와 다른 작업 폴더/Windows 전역 설정도 변경하지 않았다.

재적용은 닫힌 Editor 상태에서 `-executeMethod SashimiBoy.EditorTools.Stage02RockfishAuthoring.ApplyOwnerRevisionBatch`를 사용한다. 기존 네 실내 벽/문, 두 HUD, Stage2 물고기 연결만 적용하며 월드/Stage1 전체 Scene 생성기를 실행하지 않는다. 모델의 권위 생성기는 `Stage02OwnerAssets.cs`/`Stage02RockfishModels.cs`, HUD는 `OwnerJudgementAuthoring.cs`, 벽/문 좌표는 `DayWorldInteriorAuthoring.cs`다.

## 현재 플레이 경로

| 구간 | 실제 게임 연결 |
|---|---|
| 첫째 날 아침 | Bootstrap 새 게임 → 얼굴 선택/확정(이어하기는 저장 얼굴 유지) → 케빈 집 침대 → 거리의 미숙 대화 → 횟집 뒤 주방 작업대 |
| Stage1 | 기존 연어 Scene/음원/노트맵/손질 순서/카메라/연결된 손 유지. 실패 → 횟집 → 재도전, 정상 판정 → 회 한 판/결과 |
| 첫째 날 퇴근 | 횟집 단골 철수 → 샘플팩/드럼킷 구매 → 집 배치 → 짧은 연습 → 취침 |
| 둘째 날 아침 | 첫날 장비가 남은 집 → 성호와 오토바이 대화 → 횟집 작업대의 **우럭 손질 시작** |
| Stage2 | 별도 `Stage02_Rockfish`에서 제공 우럭/전용 음악/전용 차트로 손질 → 회 12점 한 판 → 실제 결과와 보상. 실패/중도 복귀는 클리어 없이 횟집으로 돌아가 재도전 |
| 둘째 날 퇴근 | 민재 → 기존 우럭 회 1판 교환으로 DAW 구매 → 집 배치 → 짧은 연습 → 취침/둘째 날 종료 |

`reachedStageTwoBoundary=true`인 이전 저장도 Day2 Work에서 실제 Stage2에 들어간다. 이 필드는 저장 호환을 위해 남겼지만 더 이상 진입/클리어를 막지 않는다. 진입 자체가 클리어나 보상을 만들지 않는다. Day3/새 공연/다른 스테이지를 자동 시작하지 않는다.

상호작용 표시는 기존 진행 조건을 유지한다. 구매 전 집에 빈 배치 표지/E 감지 영역을 띄우지 않는다. 필요한 NPC만 나타나고 취소 시 유지, 대화 완료 후 사라진다. 일반 출입문은 자유 탐색을 위해 계속 사용할 수 있다.

## 도로와 출입문

- 기존 Street를 열어 수정했다. 바닥은 48×36m로 연결하고 도로/보도를 연장했다. 주변 여섯 건물에 창/마감/지붕/충돌을 배치하고, 기존 세 상가의 정면과 진입 지점을 맞췄다. 기존 횡단보도의 90° 방향은 유지했다.
- 눈에 보이는 경계벽은 x=±21.5m, z=±14.6m 안쪽에서 이동을 막는다. `StreetGroundSafety`는 비정상 위치/추락 때만 마지막 안전 위치로 복구한다. 정상 둘레 보행에서는 복구 이동 없이 실제 바닥/벽 충돌로 유지되는지 검사한다.
- 상가 원본에서 문 표면/UV를 가져와 `Art/Generated/DayWorld/StreetDoors/PF_SharedDoor_*`를 만들고 실내외에 동일 Prefab을 연결했다. 횟집 유리/글자/손잡이, 악기 상점 금속 프레임/유리, 클럽 짙은 양개문을 사용한다. 클럽에는 같은 손잡이를 양쪽 Scene에 적용했다.
- 이전 `DoorGlow` 발광 네모와 겹치던 문틀은 비활성화했다. `Door_To_Street`의 실제 상호작용/충돌/직렬화 참조는 보존했다. 케빈 집은 이미 양쪽에 적용된 Owner의 철제 문을 유지한다.
- 권위 생성기: `Scripts/Editor/DayWorldStreetAuthoring.cs`. 기존 Scene만 갱신하는 메뉴는 `Sashimi Boy > Day 01 + Day 02 > Apply Street Block And Matching Doors` / `SashimiBoy.EditorTools.DayWorldStreetAuthoring.ApplyBatch`다. 전체 Scene 재생성은 필요 없다.

도로/문 체크포인트: `f7b4204`. 최초 이동 검사에서 횟집 충돌 위치 불일치를 발견했고 수정 후 재검사했다. 최초 문 캡처에서 남은 단색 면을 발견해 이전 발광 장식을 제거했다. 테스트 통과 수만으로 시각 완료를 판단하지 않았다.

## 2026-09-10 Stage2 최초 구현 이력과 실제 자료

아래 부위 모델 설명은 최초 구현 이력이다. 2026-09-11에 제공받은 Stage02 부위 FBX 적용은 위의 ‘문·기상·Owner 에셋 수정’ 절이 최신이며, 현재 실행 파일에도 그 새 우럭을 유지한다.

| 자료 | 적용 |
|---|---|
| `C:\Dev\SashimiBoyAssetDrops\Day01_Day02_v1\Stage02Audio\rockfish1.mp3` | 프로젝트 `Audio/Music/Stage_02_Rockfish/rockfish1.mp3`에 원본 그대로 복사 |
| 기존 `Art/Source/Environment/FishShop/Fish/Rockfish/Models/Rockfish.fbx`와 대응 재질 | 원본 불변. 프로젝트용 우럭 메시에서 머리/몸통/지느러미를 분리 |
| `C:\Users\02031\Downloads\rockfish_crosssection.png` | 프로젝트 `Art/Resources/Stage02Rockfish/`에 원본 그대로 복사. 절단면/회 단면에 사용 |
| 새 `Art/Generated/Stage02Rockfish/` | 우럭 피부를 유지하는 파생 부위, 두께 있는 필렛/반쪽/회, 뼈와 절단면, 재질/Prefab |

별도 우럭 손질 부위 FBX는 확인되지 않았다. 새 필렛/반쪽/회는 이 작업에서 만든 입체 메시이며, 제공된 단면 이미지를 필렛 전체를 대신하는 평면으로 쓰지 않는다. 회는 접시에 눕혀 배치한다. 우럭 원본의 약 126만 정점을 약 10만 정점의 프로젝트용 복사본으로 줄였고, UV 경계가 벌어지지 않도록 기하 위치를 공유한다. 원본 FBX/텍스처/임포터는 변경하지 않았다.

`Stage02RockfishAuthoring.ApplyBatch`는 최초에 **현재 Stage1의 작업대/몸/카메라/UI 구성**을 새 Scene에 복사한 뒤 실제 우럭 모델, 전용 음악, 전용 차트와 Stage2 보상을 연결한다. Stage1 Scene을 저장하거나 재생성하지 않는다. 기존 `Stage01*` 이름의 DSP/판정/손질 컴포넌트는 검증된 공통 구현으로 재사용하며, 물고기/스테이지 ID/차트 버전/표시를 설정값으로 구분한다. Stage1의 기존 기본값과 직렬화 참조는 유지한다.

### 음악과 차트

`rockfish1.mp3`는 48kHz stereo, 약 135.56초다. 실제 반복 타격점 분석은 **90 BPM**에 맞으며, 이전 미구현 우럭 메타데이터의 88 BPM을 이 음원에 맞춰 90으로 갱신했다. 원본 음원 속도/바이트를 바꾸지 않았다. 분석 원본은 `Logs/DayWorld/StreetDoorsStage2Revision/audio-analysis.json`, `audio-onsets.npz`다. 음악적 그루브/오프셋의 최종 청감 승인은 미검증이다.

전용 `Stage02NotePattern.asset`와 `Stage02SemanticBeatmap.asset`: 첫 기준점 0.020초, 4마디 시범/카운트다운, 플레이 10.686666…~128.020초, 44마디/222개 명시 노트. Stage1 노트의 복사/자동 반복이 아니다. 전용 음악과 확장된 노트 스케줄의 SHA-256을 검증하여 다른 차트/음원 조합은 시작하지 않는다. 차트 버전은 `issue42-stage02-rockfish-20260910-v1`이다. 재적용 시 저장된 차트가 유효하면 후속 수동 튜닝을 덮어쓰지 않는다.

| 손질 | 마디 | 노트 | 통과 품질 |
|---|---:|---:|---:|
| 머리 분리 | 8 | 32 | 60% |
| 지느러미 손질 | 8 | 40 | 60% |
| 척추 분리 | 8 | 48 | 60% |
| 필렛 반으로 나누기 | 6 | 30 | 60% |
| 반쪽 가시 제거 | 6 | 24 | 60% |
| 회 썰기 | 8 | 48 | 60% |

판정 창/품질 가중치는 Stage1 구현을 그대로 쓴다. 빈 입력은 기존처럼 점수 100과 해당 단계 품질 0.35를 깎는다. 품질 실패는 흐림 효과 후 횟집 복귀로 이어지고 보상을 만들지 않는다. 노트·품질·잔여 입력·점수는 기존 집중 HUD에 모았다. 우럭의 킥 시점에는 기존 메타데이터에 따른 작은 카메라 반응을 연결했다.

## 시나리오와 기존 에셋 보존

원문은 전달 폴더의 `Scenario/대본.pdf`(6페이지, SHA-256 `a0278fbd458d583bd83982221621e65e731607d628d2b39c11e6489a80827378`)이며 기존 적용을 유지한다. 민재 대화의 케빈 답변은 **“퇴근 중이니까.”**, 철수는 횟집 단골손님이다. 대사/속마음/행동 지시와 출처 페이지는 `DayWorldScenario.cs`에서 구분한다.

제공한 미숙/철수/성호/민재의 얼굴과 몸, 성호의 오토바이, 세 상가 외관, 기존 횟집/상점/클럽 소품과 실제 악기는 보존했다. 케빈의 귀여운 얼굴과 연결된 몸/손, Owner의 집 외벽/침대/문, 각 실내 벽/천장/마감도 유지한다. DAW는 기존 책상/모니터/키보드 자리로 표현하며 새 구매 품목/가격을 만들지 않았다. 정교한 NPC 보행 리깅/립싱크는 구현 범위가 아니다.

사전 기준은 `21c017a3c86fbeec08ce4ca87cc25296a9c01299`, 브랜치는 `feat/42-day01-day02-world`다. 사용자 잔여 변경 14파일(복구 Scene/Previews/ProjectSettings)은 그대로 둔다. 사전 3,042파일 해시는 `Before.json`, 실제 최신 사용자 저장 6개와 새 음악/단면 원본 해시는 `SourceAndProfiles.json` 및 `PreservedProfiles/`에 보존했다. Windows 전역 설정, 다른 작업 폴더, `Docs/Automation`, `Tools/Automation`, #52는 수정하지 않는다. 자동화 SPEC은 1.0.2다.

## 이번 실행의 검증

실행 로그와 XML은 `Logs/DayWorld/StreetDoorsStage2Revision/`에 있다. 아래 결과는 실행 종료와 결과 파일을 직접 확인했다.

- 도로/문 적용: `world-apply-02.log`, native exit 0.
- 도로/문 EditMode: `world-edit-01.xml`, 15/15. 실제 둘레/경계/추락 복구 및 실내 출구/상가 왕복 재검사: `world-play-02.xml`, 3/3. 앞선 전체 월드 9개 중 횟집 접근 1개 실패는 이 재검사에서 수정 확인했다.
- 최종 도로/문 적용: `world-apply-03.log`, native exit 0. 반복 적용 시 상가 간판 뒷판 위치가 누적 이동하지 않도록 수정했다.
- Stage2의 최종 Unity 적용: `stage2-apply-05.log`, native exit 0. 처음 Unity 오디오 임포트 API 컴파일 오류는 현 버전 API로 수정했다.
- 초기 Stage2 시각 검사는 `stage2-visual-01.xml`에서 통과했으나, 실제 화면의 이음새/크기/회 방향 문제를 발견해 이후 생성기에서 수정했다. 이 초기 캡처를 최종 시각 승인으로 삼지 않는다.

| 최종 검사 | 실행 결과 | 증거 |
|---|---|---|
| 월드/차트/품질/Stage1/AudioClock/케빈 EditMode | 101/101, native exit 0 | `edit-final-01.xml`, 16.99초 |
| 실제 구매 조건/원본 manifest/GUID EditMode | 10/10, native exit 0 | `edit-final-assets-economy.xml`, 4.70초 |
| Day1 전 경로, Stage2 진입·실패·재도전·실제 DSP 클리어·Day2 취침, 도로/문/인물/Stage1 회귀 PlayMode | 20/20, native exit 0 | `play-final-01.xml`, 438.89초 |
| 최종 회 두께/간판 뒷판 적용 후 여섯 단계/한 판 화면, 도로 경계/추락 복구, 네 실내 출구 재검증 | 3/3, native exit 0 | `play-final-visual.xml`, 65.57초 |
| 사전 파일/원본/사용자 저장 보존 및 .meta | 예상 외 변경 0, 사용자 14파일 변경 0, 원본/기존 저장 8파일 변경 0. GUID 2,524개 중복/누락/고아 .meta 0 | `preservation-final.json` |

Stage2의 **재도전 후 클리어 구간**은 원본 음악의 DSP 시간에 맞춰 Space와 같은 `HandleTimingInput` 경로를 호출한다. 음악 시계를 멈추거나 건너뛰지 않고 약 128초를 재생하며 222개 입력을 판정하고, 실제 마지막 게이트와 회 한 판/보상을 확인한다. 이 테스트의 초기 실패 구간은 기존 미스 처리 함수를 호출한다. 별도 빠른 시각 검사는 음원 위치를 이동하며 실제 판정을 소비한다. 증거 종류를 혼동하지 않는다. 월드 경로는 실제 CharacterController/감지/상호작용과 격리된 디스크 저장의 재로드를 검사한다. 자동 입력은 물리 키보드로 연주한 증거가 아니다.

Game 카메라를 실제 렌더링한 최종 1600×900 PNG/해당 저장 상태는 `Logs/DayWorld/CameraEvidence/`에 있다. 직접 화면을 열어 확인한 증거는 다음과 같다.

- `street-block-east-walk.png`, `street-block-south-neighbors-walk.png`: 도로/보도와 양측 건물, 경계까지 이어지는 바닥.
- `FishShopDialogue-exit-walk.png`, `EquipmentShop-exit-walk.png`, `Club-exit-walk.png`, `KevinHome-exit-walk.png` 및 대응 외관 캡처: 실제 문과 문 앞 보행/출입.
- `stage2-visual-phase-0.png`~`stage2-visual-phase-5.png`: 실제 우럭 → 머리/지느러미 → 열린 필렛/척추 → 반쪽 → 가시 제거 → 회 썰기. 최종 회 두께는 `stage2-visual-complete-plate.png`에서 확인했다.
- `stage2-real-dsp-phase-0.png`~`stage2-real-dsp-phase-5.png`, `stage2-real-dsp-clear.png`: 실제 음악 시간, 노트/판정/점수/게이지와 단계 진행. 이 클리어 캡처는 최종 회 두께 조정 전이며, 최종 두께는 앞의 빠른 시각 검사 이미지다.
- `home-practice-day2-interaction-test.png`, `day2-complete-after-real-stage2.png`: 첫날 드럼킷 유지, DAW 배치와 실제 케빈 연습/둘째 날 종료. 옛 `day2-complete-post-stage-fixture-test.png`는 이번 Stage2 실행 증거로 쓰지 않는다.

Game 화면의 부위 전환과 흐름은 자체 확인했으며, 사람의 최종 박자 체감·음향 동기·예술적 완성도·4:3 화면과 독립 리뷰는 미검증이다. 필렛/뼈/회는 새로 만든 단순한 입체 형상이므로 별도 손질용 원본 FBX 수준의 아트 승인을 받은 것으로 해석하지 않는다.

## 실제 실행과 독립 검증

Windows 빌드: `Builds/DayWorldValidation-20260909-192024/SashimiBoyDayWorld.exe`.

- **`Builds/DayWorldValidation-20260909-192024/Play-Latest-Progress.cmd`**: 최신 실제 사용자 저장 `crosswalk-direction-review.json`으로 실행. **이어하기** → 둘째 날 횟집 작업대, 도마를 바라보고 **E — 우럭 손질 시작**. 기존 첫날 드럼킷은 유지된다.
- `Builds/DayWorldValidation-20260909-192024/Play-New-Game.cmd`: 별도 새 프로필로 실행하여 **새 게임**을 선택. 기존 진행과 분리해 첫날부터 확인한다. 이 실행기를 재사용하면 해당 새 프로필의 진행도 저장된다.
- WASD 이동 / 마우스 시점 / E 상호작용 / Space 대화·연습·손질 / Esc 대화·연습 취소 및 스테이지 중도 복귀 / 결과 R 재도전·F 횟집 복귀.

`build-final-01.log`: native exit 0, Windows 빌드 성공. 빌드 직전 권위 생성기를 다시 적용해 우럭 60파일, 월드/문 975파일의 바이트 불변을 확인했다. `git diff --check` 및 스테이징 후 `git diff --cached --check` 통과. 신규 Unity YAML의 빈 값 끝 공백은 기존 DayWorld와 같은 `.gitattributes` 규칙을 Stage2 출력 경로에만 확장해 보존했다. 코드/문서는 일반 공백 검사를 유지한다. 기존 여섯 저장을 새 빌드에 byte-exact로 복사했고 `new-build-profiles.json`에 기록했다. 옛 빌드와 원본 저장은 삭제/덮어쓰기하지 않았다. `Play-Latest-Progress.cmd`는 옛 실행기가 가리키던 더 오래된 stage1-visibility 저장 대신 실제 최신 crosswalk-direction 저장을 사용한다.

실행 파일 자체를 직접 열면 일반 persistentDataPath 저장을 사용하므로 위 `.cmd` 실행기를 권장한다. 실행기는 새 빌드 폴더의 `Logs/DayWorld/Profiles/`에 저장하고 `crosswalk-direction-review-player.log`에 로그를 남긴다.

Windows Player 창도 직접 확인했다(2026-09-10 04:22~04:26 KST). 최신 실제 저장의 이어하기 → 도마 시선/E 진입 → 실제 우럭/노트/게이지 표시 → **Space 없이 약 32초 재생** → 첫 단계 자동 MISS 실패/흐림 → 횟집 복귀 → E 재도전 시 온전한 우럭·점수 0 → Esc 복귀를 확인했다. 새 빌드의 해당 저장은 이 검증 후에도 원래 최신 저장과 바이트가 같다. 현재 창은 **둘째 날 횟집 작업대에서 E로 우럭을 시작할 수 있는 상태**로 남겨두었다.

이 네이티브 실행 증거는 새 빌드의 `Logs/Stage1Playable/InputEvidence/`에 있다(기존 공용 진단 폴더 이름을 보존했다). `20260909-192438-03-stage1-run1-phase0.png`는 실제 Stage2 입장, `20260909-192510-04-failure.png`는 자연 실패와 흐림, `20260909-192512-05-FishShopDialogue.png`는 자동 복귀다. `inputs.jsonl`에는 Stage02_Rockfish/Playing 시간과 MISS 처리 기록이 있다. 진단의 inputCount는 자동 MISS 처리도 포함하므로 키보드 누름 횟수로 해석하지 않는다. 이 검증에서 Space 입력은 하지 않았다.

최종 적용/빌드 로그에 C# 컴파일 오류·Missing Script·셰이더 오류·게임 예외는 없었다. 성공한 테스트의 의도된 오류 로그는 테스트 기대값으로 확인된다. 네이티브 로그에는 D3D12 정보 큐 조회/업로드 버퍼 확대 메시지가 있어 보존했으며, 해당 실행에서 크래시나 핑크 재질/누락 화면은 관찰하지 않았다.

Editor 재현: Unity 6000.4.0f1에서 이 격리 프로젝트를 열고 `Bootstrap`의 Play → 이어하기/새 게임. 우럭 적용 도구는 `Sashimi Boy > Stage 02 > Apply Rockfish Stage` 또는 `-executeMethod SashimiBoy.EditorTools.Stage02RockfishAuthoring.ApplyBatch`다. 기존 Stage1/월드를 전체 생성하는 메뉴를 실행할 필요가 없다.

배치 검증은 같은 프로젝트를 연 Editor가 없는 상태에서 `-batchmode -projectPath <이 폴더> -stage1Validation -runTests -testPlatform EditMode|PlayMode -testResults <새 xml> -logFile <새 log>`로 실행한다. 테스트 실행에는 `-quit`를 넣지 않는다. Windows 빌드는 `-batchmode -quit ... -executeMethod SashimiBoy.EditorTools.DayWorldAuthoring.BuildWindowsBatch`다.

독립 Reviewer는 기존 Stage1/차트/NaN/AudioClock 변경 범위, 원본/GUID, 도로 경계 보행, 실내외 문 일치와 실제 상호작용, 16:9/4:3 우럭 각 단계의 칼/물고기/연결된 손, 실패/재도전/중복 보상 방지, 기존 Day2 경계 저장의 진입, DAW 구매/배치/취침 후 이어하기를 별도로 확인해야 한다. Owner의 최종 시각·음향·입력 감각 승인과 독립 리뷰는 아직 없다.
