using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Esot
{
    public static class ThreeWay
    {
        public static bool TryChoose(string? ancestor, string? ours, string? theirs, out string? result)
        {
            if (ours == theirs || theirs == ancestor) { result = ours; return true; }
            if (ours == ancestor) { result = theirs; return true; }
            result = null;
            return false;
        }
    }

    internal sealed class Merge
    {
        private readonly Workspace ws;
        internal Merge(Workspace workspace) => ws = workspace;

        private Manifest Compare(string left, string right, Dictionary<string, bool> choices)
        {
            var ancestor = ws.Repo.Files(ws.Repo.Base(left, right));
            var ours = ws.Repo.Files(left);
            var theirs = ws.Repo.Files(right);
            var result = new Manifest();
            var conflicts = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in ours.Keys.Union(theirs.Keys).Union(ancestor.Keys))
            {
                ancestor.TryGetValue(path, out var original);
                ours.TryGetValue(path, out var local);
                theirs.TryGetValue(path, out var incoming);
                if (!ThreeWay.TryChoose(original, local, incoming, out var value))
                {
                    if (!choices.TryGetValue(path, out var useTheirs)) { conflicts.Add(path); continue; }
                    used.Add(path);
                    value = useTheirs ? incoming : local;
                }
                if (value != null) result.Add(path, value);
            }
            if (conflicts.Count != 0) throw new ConflictException(string.Join(Environment.NewLine, conflicts));
            if (choices.Keys.Except(used).Any()) throw Cli.Error("Resolution names a file without a conflict", "충돌이 없는 파일에 해결 옵션을 지정했습니다");
            return result;
        }

        internal void Into(Worktree source, Worktree target, Dictionary<string, bool> choices)
        {
            if (source.Name == target.Name) throw Cli.Error("Choose different worktrees", "서로 다른 워크트리를 지정하세요");
            ws.RequireClean(source);
            ws.RequireClean(target);
            string left = ws.Repo.Head(target.Name);
            string right = ws.Repo.Head(source.Name);
            if (ws.Repo.Base(left, right) == right) return;
            var result = Compare(left, right, choices);
            string sent = ws.Repo.Commit(ws.Repo.Files(right), $"Merge {source.Name} into {target.Name} (source)", right);
            string received = ws.Repo.Commit(result, $"Merge {source.Name} into {target.Name}", left, sent);
            ws.Apply(new[] {
                new Changes { Name = source.Name, Before = right, After = sent },
                new Changes { Name = target.Name, Before = left, After = received }
            });
        }

        internal void Push(Worktree source, Worktree target)
        {
            ws.RequireClean(source);
            ws.RequireClean(target);
            var before = ws.Repo.Head(target.Name);
            var after = ws.Repo.Head(source.Name);
            if (before == after) return;
            if (ws.Repo.Base(before, after) != before) throw new ConflictException(Cli.T("Non-fast-forward push", "빨리 감기가 불가능한 push입니다"));
            ws.Apply(new[] { new Changes { Name = target.Name, Before = before, After = after } });
        }

        internal void Sync(Worktree[] peers)
        {
            if (peers.Length < 2) throw Cli.Error("Select at least two worktrees", "두 개 이상의 워크트리를 지정하세요");
            foreach (var peer in peers) ws.RequireClean(peer);
            var heads = peers.Select(p => ws.Repo.Head(p.Name)).ToArray();
            string combined = heads[0];
            foreach (var head in heads.Skip(1).Distinct())
            {
                string ancestor = ws.Repo.Base(combined, head);
                if (ancestor == head) continue;
                if (ancestor == combined) { combined = head; continue; }
                combined = ws.Repo.Commit(Compare(combined, head, new()), "Merge " + string.Join(", ", peers.Select(p => p.Name)), combined, head);
            }
            var changes = peers.Select((p, i) => new Changes { Name = p.Name, Before = heads[i], After = combined })
                .Where(c => c.Before != c.After).ToArray();
            if (changes.Length != 0) ws.Apply(changes);
        }
    }

    internal sealed class ConflictException : IOException
    {
        internal ConflictException(string detail) : base(Cli.T("Conflict", "충돌") + Environment.NewLine + detail) { }
    }
}
