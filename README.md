# ESOT

[한국어](README.ko.md)

ESOT (Equal Sources of Truth) is a CLI for editing the same resource collection in several independent local worktrees. Each worktree holds its current files and sets its own budget for old content. Git commits record file paths and SHA-256 hashes. ESOT keeps the file contents in each worktree.

[Project overview](docs/README.md) · [Storage and recovery](docs/Architecture.md) · [Tests and results](tests/README.md)

## Installation

Install Git and the .NET 10 SDK, then build and install the .NET tool from source.

```powershell
git clone --recurse-submodules https://github.com/dove-creative/esot.git
cd esot
dotnet pack src/Esot -c Release -o artifacts/packages
dotnet tool install --global --source ./artifacts/packages ESOT --version 0.1.3
esot --help
```

The test project restores UniTest 0.4.3 from the repository-local feed. The source submodule is retained for reference and is not compiled as part of ESOT. Package provenance and the build-only verification scope are recorded in [packages](packages/README.md).

## First worktree and commit

This example initializes Common in the current directory and adds an empty sibling directory named Unity. The initial commit includes files already in Common. The `*.meta` pattern excludes matching files at every depth. You can also register an existing Unity directory. Each existing file covered by ESOT must match the source commit byte for byte. A mismatch stops registration and reports the path.

```powershell
esot init Common ./Common --limit 10GB --ignore "*.meta"
esot -C ./Common worktree add Unity ../Unity --limit 0
```

After editing the rabbit PSD in the Unity worktree, commit it and merge it into Common.

```powershell
esot -C ./Unity status
esot -C ./Unity commit -m "Add rabbit ears"
esot -C ./Common merge Unity
esot -C ./Common log
```

If the destination also committed a different edit to the same PSD, ESOT reports the conflicting path and exits with code 2. Both worktrees keep their files and branch heads. Select the destination copy with `esot -C ./Common merge Unity --ours "토끼.psd"` or the incoming copy with `--theirs "토끼.psd"`. Repeat the option for each conflicting path. A successful merge records a source receipt and a destination merge commit.

## Push, sync and retention

A `push` advances the destination branch when it can fast-forward. A `sync` merges the selected worktrees into a common result. Omit their names to include all registered worktrees. Commit outstanding changes in each participating worktree first.

```powershell
esot -C ./Unity push Common
esot -C ./Common worktree add Art ../Art
esot -C ./Common sync Common Unity Art
esot -C ./Common config storage.limit 5GB
esot -C ./Common gc
```

The default history budget per worktree is 10 GB (10,000,000,000 bytes). Zero retains zero bytes of old content. The snapshot needed to restore the current commit is exempt. When a worktree exceeds its budget, it removes the content that became historical earliest. After the last copy is removed, the commit and hash remain available. Historical objects held only in Unity are not automatically copied into Common. To recover a retained file, find its commit with `esot log` and run `esot restore --source <commit> "토끼.psd"`. This restores the file as a working change using any available registered copy.

Each worktree has a `.worktree/worktree.json` with its name and shared metadata repository location, and a `.worktree/storage` directory for file contents. When you first open an existing workspace with ESOT 0.1.2, it moves each registered worktree’s top-level `.storage` directory to `.worktree/storage`. The shared repository defaults to `ESOT` under the user’s local application data directory. Set `ESOT_HOME` before `init` to choose another location. Back up the shared repository together with the worktrees. This version uses fixed folder paths on one computer.

## Automation and tests

Select the message language with `--lang en` or `--lang ko`. Exit codes are 0 for success, 1 for an error, and 2 for a conflict or a rejected non-fast-forward push. After an interrupted update, run `esot recover` in any registered worktree. If an external edit changed the expected contents, recovery stops and reports the path.

```powershell
dotnet build ESOT.slnx -c Release
dotnet run --project tests/Esot.Tests -c Release
```

[UniTest](dependencies/unitest) is a development dependency for testing merge decisions. The CLI integration suite uses temporary directories to check Korean filenames, conflicts, deletions, three-way worktree synchronization and retention. Project conventions are in [BlackThunder Conventions](guidelines/blackthunder-conventions).

## License

ESOT is distributed under the [MIT License](LICENSE).
