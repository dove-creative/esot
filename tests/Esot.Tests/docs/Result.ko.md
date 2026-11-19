# 전체 테스트 실행 기록

[English](Result.md)

2026-11-19 16:23 KST에 Windows에서 ESOT 0.1.0 커밋 `2dc8701`을 검증했다. .NET SDK 10.0.404, Git 2.53.0.windows.3을 사용했다. Release 빌드는 경고 0개, 오류 0개로 완료되었다.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release --no-build
```

콘솔 러너는 종료 코드 0을 반환했다. 출력은 다음과 같다.

```text
UniTest seed 0, depth 100: OK
UniTest seed 1, depth 100: OK
UniTest seed 2, depth 100: OK
CLI integration: OK
```

[일반 통합 시나리오](Unit/CLI/03.%20Results.ko.md) 1회와 [UniTest 시드 실행](UniTest/ThreeWay/03.%20Results.ko.md) 3회가 모두 통과했다. 실패하거나 건너뛴 실행은 0회이다. 각 시나리오 안의 단언이나 모델의 깊이는 독립 테스트 수에 포함하지 않았다. 실행 환경의 전체 로그 파일명은 `20261119-162312-esot-full.log`이다. 이 콘솔 러너는 TRX 파일을 생성하지 않는다.

[테스트 안내](../../README.ko.md) · [테스트 이슈](Issues.ko.md)
