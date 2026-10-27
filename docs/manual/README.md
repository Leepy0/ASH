# ASH 사용 설명서

ASH(AGV Simul Helper)는 ZWCAD 도면과 엑셀 입력으로 AutoMod AGV 모델을 만들고, 서버에서 그룹별 필요 대수를 찾아 웹에 결론을 보여 준다.

한 파일로 보려면 [manual.html](manual.html)을 연다(그림 포함, 인쇄 가능).

## 차례

| 장 | 내용 |
|---|---|
| [1. 시작하기](01-start.md) | 설치, 업데이트, 화면 구성, 일하는 순서 |
| [2. 따라하기: 첫 시뮬레이션](02-tutorial.md) | 작은 예제 하나를 처음부터 결과까지. 좌표, 엑셀 값, 기대 결과 |
| [3. 기능 참조 — 도면](03-drawing.md) | 경로, CP, 교통제어(block), 3D·컨베이어, 검증, 찾기, 루트 보기, 원인 표시 |
| [4. 기능 참조 — 입력과 모델](04-input-model.md) | 엑셀 입력, 드롭다운, 비교, 검증 표시, 모델 생성, 서버로 보내기 |
| [5. 기능 참조 — 결과 보기](05-results.md) | 홈, 결론, 원인, 비교, 생산량 한계(T/T), 영상, 작업 화면 |
| [6. 문제 해결](06-faq.md) | 자주 나오는 메시지와 조치 |

## 예제 파일 (`example/`)

| 파일 | 내용 |
|---|---|
| [background.dxf](example/background.dxf) | 배경 도면. 벽·기둥·랙·설비 선만 있다(mm 단위). 따라하기는 이 파일에서 시작한다 |
| [input.xlsx](example/input.xlsx) | 다 채운 입력 양식. `AGVEXCEL`로 만든 양식에 따라하기 7단계 값을 넣은 것 |
| [finished.dxf](example/finished.dxf) | 완성 모양 비교용. 경로는 `AGV_PATH` 선, CP는 원, block은 사각형, 이름은 글자다. ASH 블록과 XData가 없으므로 그대로 모델이 되지 않는다 |

## 그림 표시

- **실제 화면**: 이 브랜치 빌드로 띄운 서버에서 예제를 실제로 돌려 찍은 웹 화면, AutoView가 찍은 영상 한 장면.
- **실제 컨트롤**: 빌드한 `ASH.Plugin.dll`의 팔레트·대화상자를 ZWCAD 밖에서 그린 것. 글자와 버튼은 같고, 도킹·창 테두리는 ZWCAD 안에서 다를 수 있다.
- **설명용 그림**: 예제 좌표로 그린 도면 단계 그림. 그림 위에 "설명용 그림"이라고 적혀 있다. ZWCAD 화면이 아니다.

이 설명서를 쓴 PC에는 ZWCAD가 없어서 ZWCAD 안의 동작(명령줄, 리본, 도면 모양)은 코드와 [plugin-manual-test.md](../plugin-manual-test.md)를 기준으로 적었다. 확인하지 못한 곳은 본문에 **(미확인)** 으로 표시했다.

## 다른 문서

- 지금 되는 기능 목록: [features.md](../features.md)
- 입력 양식 상세: [input-template.md](../input-template.md)
- 서버 설치·운영: [server-install.md](../server-install.md)
- 영상(AutoView) 상세: [autoview-guide.md](../autoview-guide.md)

다시 만들기: 이 폴더에서 `python build_html.py` → `manual.html`.
