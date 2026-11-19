# ESOT overview

[한국어](README.ko.md)

ESOT (Equal Sources of Truth) is a CLI for editing a resource collection independently in several worktrees on one computer and applying selected changes to other worktrees. Each worktree has its own name, current commit and budget for past file contents.

## Two merges of the rabbit PSD

Here `x1` through `x4` represent versions of the contents of a PSD at the same path.

| Operation | Rabbit PSD in A | Rabbit PSD in B | Result |
| --- | --- | --- | --- |
| Initialize A and add worktree B | x1 | x1 | Both worktrees start at the same commit. |
| Add ears in B and commit | x1 | x2 | Only B's branch advances. |
| Run `merge B` in A | x2 | x2 | B records a source commit and A records the receiving merge commit. |
| Remove the tail in A and add hands in B, then commit in each | x3 | x4 | The common base has content x2. |
| Run `merge B` again in A | x3 | x4 | Different edits at the same path produce exit code 2 and the conflicting path. Files and branch tips stay in place. |

For a conflicting path, choose A's content with `--ours` or B's with `--theirs`. Combining individual PSD layers requires the user's editor.

## Direction of changes

| Command | Effect |
| --- | --- |
| `push A` from B | Advances A to B's commit if A's current commit is in B's history. |
| `merge B` from A | Compares both commits with their common ancestor and merges B into A. Merging an already integrated B produces no additional commit. |
| `sync A B C` | Integrates the named worktrees and advances every participant to the same commit. |

See the [usage guide](../README.md) for installation and examples, [storage and recovery](Architecture.md) for the relationship between history, file contents and the recovery journal, and the [test guide](../tests/README.md) for coverage and execution records.
