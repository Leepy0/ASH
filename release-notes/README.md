# 릴리스 노트

버전마다 파일 하나: `release-notes/<버전>.md` (예: `0.32.0.md`). 릴리스 때 `dist/ASH.Notes.json`으로 묶여 GitHub 릴리스에 올라가고, 서버가 가져가 관리자가 검토·승인한 것만 PC에 보입니다.

## 형식

```
# 제목(짧게)
중요
- 바뀐 것 하나
- 바뀐 것 하나
```

- 첫 줄 `# 제목` 필수
- `중요` 줄은 선택
- `- ` 항목: 한 줄에 하나, 6줄 안팎, 문장 말고 단어 위주
- 그 밖의 줄은 무시

## 중요 표시

사용자가 직접 해야 할 일이 있거나 동작이 달라질 때만 `중요`. PC에서 팝업으로 뜹니다. 그 밖에는 명령줄 한 줄로만 표시됩니다.

## 확인

`dotnet run --project tools/AshRelease -c Release -- notes --dir release-notes --out dist/ASH.Notes.json`
