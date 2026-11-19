# ESOT 테스트

[English](README.md)

저장소 루트에서 다음 명령을 실행한다. .NET 10 SDK와 Git, 재귀 초기화된 서브모듈이 필요하다. 콘솔 러너가 UniTest를 먼저 실행하고, 이어서 임시 디렉터리에서 CLI 통합 시나리오를 실행한다.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release --no-build
```

| 검증 대상 | 실행 단위 | 검증 문서 |
| --- | --- | --- |
| 일반 CLI 통합 | 파일 검사 캐시·충돌 검증 1개와 워크트리 연동 시나리오 1개 | [상태표](Esot.Tests/docs/Unit/CLI/01.%20Tables.ko.md) · [계획](Esot.Tests/docs/Unit/CLI/02.%20Plans.ko.md) · [결과](Esot.Tests/docs/Unit/CLI/03.%20Results.ko.md) |
| UniTest 병합 모델 | 시드 0·1·2, 시드별 깊이 100 | [상태표](Esot.Tests/docs/UniTest/ThreeWay/01.%20Tables.ko.md) · [계획](Esot.Tests/docs/UniTest/ThreeWay/02.%20Plans.ko.md) · [결과](Esot.Tests/docs/UniTest/ThreeWay/03.%20Results.ko.md) |

[전체 실행 기록](Esot.Tests/docs/Result.ko.md) · [테스트 이슈](Esot.Tests/docs/Issues.ko.md) · [실행 코드](Esot.Tests/Program.cs)
