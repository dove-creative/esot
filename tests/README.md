# ESOT tests

[한국어](README.ko.md)

Run these commands from the repository root with the .NET 10 SDK, Git and initialized submodules. The console runner first executes UniTest, then runs the CLI integration scenario in temporary directories.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release --no-build
```

| Target | Unit of execution | Test documents |
| --- | --- | --- |
| CLI integration | One scan-cache and collision scenario and one worktree integration scenario | [State table](Esot.Tests/docs/Unit/CLI/01.%20Tables.md) · [Plan](Esot.Tests/docs/Unit/CLI/02.%20Plans.md) · [Results](Esot.Tests/docs/Unit/CLI/03.%20Results.md) |
| UniTest merge model | Seeds 0, 1 and 2, depth 100 per seed | [State table](Esot.Tests/docs/UniTest/ThreeWay/01.%20Tables.md) · [Plan](Esot.Tests/docs/UniTest/ThreeWay/02.%20Plans.md) · [Results](Esot.Tests/docs/UniTest/ThreeWay/03.%20Results.md) |

[Full run record](Esot.Tests/docs/Result.md) · [Test issues](Esot.Tests/docs/Issues.md) · [Runner source](Esot.Tests/Program.cs)
