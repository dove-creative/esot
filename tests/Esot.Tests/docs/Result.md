# Full test run

[한국어](Result.ko.md)

Verified ESOT 0.1.0 at commit `2dc8701` on Windows on 2026-11-19 at 16:23 KST, using .NET SDK 10.0.404 and Git 2.53.0.windows.3. The Release build completed with zero warnings and zero errors.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release --no-build
```

The console runner exited with code 0 and printed the following lines.

```text
UniTest seed 0, depth 100: OK
UniTest seed 1, depth 100: OK
UniTest seed 2, depth 100: OK
CLI integration: OK
```

The [CLI integration scenario](Unit/CLI/03.%20Results.md) passed once and all three [UniTest seed runs](UniTest/ThreeWay/03.%20Results.md) passed. Zero runs failed or were skipped. Assertions within the scenario and model depth do not contribute to the run count. The local combined log is `20261119-162312-esot-full.log`. This console runner does not emit a TRX file.

[Test guide](../../README.md) · [Test issues](Issues.md)
