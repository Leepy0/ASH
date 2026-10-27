# 매뉴얼 그림·예제 재생성 도구

`docs/manual`의 예제 파일과 캡처를 다시 만들 때 쓰는 도구입니다. 제품 빌드(ASH.sln)에는 들어가지 않습니다.

| 파일 | 용도 |
|---|---|
| `gen/` | 예제 배치·엑셀·DXF 생성, 서버 제출(C#, ASH.Core 참조) |
| `caphost/` | 팔레트·대화상자를 ZWCAD 밖에서 그림(net48, Release 빌드한 ASH.Plugin.dll 참조) |
| `cdp.mjs`, `shot.py` | 헤드리스 Edge로 웹 화면 캡처 |
| `draw_steps.py` | 도면 단계 설명용 그림(Pillow) |
| `assemble.py` | 번호 표시·이미지 정리 |

경로·포트 등은 각 파일 머리의 설정을 확인하고 실행합니다. 서버 캡처는 운영 서버가 아닌 별도 포트의 임시 서버로 합니다.
