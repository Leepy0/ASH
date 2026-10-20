# ZWCAD .NET 참조 dll (선택)

기본 빌드는 NuGet 패키지 `ZWCAD.NetApi` 20.26.0(ZWCAD 2026 참조 어셈블리)을 쓴다. 이 폴더는 필요 없다.

다른 ZWCAD 버전에 맞춰 빌드하려면 ZWCAD 설치 폴더에서 `ZwDatabaseMgd.dll`, `ZwManaged.dll`, `ZcWindows.dll`을 여기로 복사하고 다음처럼 빌드한다.

```
dotnet build src/ASH.Plugin -p:ZwCadDir="%CD%\lib\zwcad"
```

dll은 git에서 제외된다(이 README만 추적). 어느 경우든 dll은 출력 폴더로 복사되지 않는다(ZWCAD가 실행 시 제공).
