# Storage and recovery

[한국어](Architecture.ko.md) · [Project overview](README.md)

## Commits and file contents

| Location | Data | Purpose |
| --- | --- | --- |
| Ordinary files in each worktree | Current contents read and written by editors | Working copies, including uncommitted changes. |
| `.worktree/worktree.json` in each worktree | Worktree name and path to the shared repository | Connects the current directory to its branch and shared history. |
| `.worktree/storage` in each worktree | Whole-file snapshots identified by SHA-256 | Files with identical content share one snapshot within that worktree. |
| Shared local Git repository | A `files.json` manifest of relative paths and hashes per commit, parent commits and one branch per worktree | Commit history and merge bases remain available after old resource bytes are removed. |

The shared repository also holds the worktree paths, their retention limits and the common ignore patterns. `ESOT_HOME` selects its parent directory during initialization. A recoverable backup needs the shared repository and copies of every resource version you intend to retain.

## Working-file checks

Each worktree keeps a disposable index of path, length, UTC modification time and SHA-256 in `.worktree/scan-v1.json`. Status and commit can reuse a stable entry. A new or changed file is hashed. When its key already exists in registered storage, ESOT compares the working bytes with the retained snapshot before accepting that match. Different bytes under the same key stop the operation and leave both files available. The error can indicate a collision or a damaged snapshot.

Files timestamped within two seconds of the scan start, or in the future, are rechecked until a later scan verifies a stable timestamp. ESOT invalidates affected entries before writing files during restore or recovery. An external edit that deliberately preserves an older timestamp and length can evade the fast check. Removing the scan index requests a full check on the next scan. Existing worktrees build the index on first use.

## Retention and restore

A commit captures the full file so an editor can overwrite the working copy without losing its previous identity. Each worktree's budget counts the bytes in snapshots its current commit no longer references. With 10 GB in Common and zero in Unity, Unity keeps the snapshots needed by its current commit. Old versions retained in Common remain available for restoration. When the budget is exceeded, the content that became historical first is pruned first.

`restore --source <commit> <path>` looks up the hash in the commit, reads the matching content from a registered worktree and restores the working file. The branch stays at its current commit. Once all copies are pruned, restoring the bytes is impossible.

## Conflicts and interrupted updates

ESOT detects content conflicts by comparing the common ancestor, destination and incoming hashes for each path. An absent path is a possible value, so a deletion can conflict with an edit. The current implementation requires a single merge base.

Before applying file updates, ESOT records the previous and intended commits for participating worktrees in the shared journal. After applying the files, it updates Git branch references in one transaction. Following an interruption, `recover` checks whether each affected path contains the old or intended value and resumes the remaining work. If it finds an external edit that matches neither, it reports the path and retains the journal.
