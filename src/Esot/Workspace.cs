using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Esot
{
    internal sealed class Workspace : IDisposable
    {
        internal Repository Repo { get; }
        internal Settings Config { get; }
        private readonly FileStream gate;
        private string Pending => Path.Combine(Repo.Location, "esot-pending.json");
        private string Initializing => Path.Combine(Repo.Location, "esot-initializing.json");
        private string Registry => Path.Combine(Repo.Location, "esot-worktrees.json");

        private Workspace(string path, bool create = false)
        {
            Repo = new Repository(path);
            Worktree.CheckAncestors(path);
            gate = new FileStream(Path.Combine(path, "esot.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try { Config = create ? new Settings() : Data.Read<Settings>(Registry); }
            catch { gate.Dispose(); throw; }
        }

        public void Dispose() => gate.Dispose();
        internal static string RefName(string name)
        {
            if (!Regex.IsMatch(name, @"\A[\p{L}\p{N}][\p{L}\p{N}_.-]*\z") ||
                name.Contains("..") || name.EndsWith('.') || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name == "HEAD")
                throw Cli.Error("Invalid worktree name", "잘못된 워크트리 이름", name);
            return name;
        }

        internal static Workspace Open(string current, out string name)
        {
            for (var dir = new DirectoryInfo(current); dir != null; dir = dir.Parent)
            {
                string file = Path.Combine(dir.FullName, ".worktree", "worktree.json");
                if (!File.Exists(file)) continue;
                var id = Data.Read<Identity>(file);
                var ws = new Workspace(id.Repository);
                if (ws.Config.Worktrees.TryGetValue(id.Name, out var tree) && string.Equals(dir.FullName, tree.Root, Worktree.Paths))
                { name = id.Name; return ws; }
                ws.Dispose();
                throw Cli.Error("Worktree registration mismatch", "워크트리 등록 정보가 일치하지 않습니다", dir.FullName);
            }
            throw Cli.Error("Run inside an ESOT worktree or use -C", "ESOT 워크트리 안에서 실행하거나 -C를 사용하세요");
        }

        private static void CheckNewRoot(string path)
        {
            Worktree.CheckAncestors(path);
            for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".worktree"))) throw Cli.Error("Existing worktree", "이미 등록된 워크트리", dir.FullName);
            foreach (var sub in new[] { ".worktree", ".storage" })
                if (Path.Exists(Path.Combine(path, sub))) throw new IOException(Path.Combine(path, sub));
        }

        internal static void Init(string name, string path, long limit, string[] ignores)
        {
            RefName(name);
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            CheckNewRoot(path);
            string home = Environment.GetEnvironmentVariable("ESOT_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ESOT");
            string state = Path.GetFullPath(Path.Combine(home, Guid.NewGuid().ToString("N")));
            if (Worktree.Inside(state, path)) throw Cli.Error("Keep ESOT_HOME outside the worktree", "ESOT_HOME은 워크트리 밖에 두세요");
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(state);
            using var ws = new Workspace(state, true);
            ws.Repo.Run(null, "init", "--bare", "--initial-branch=main", state);
            ws.Config.Ignore = ignores;
            var tree = new Worktree { Name = name, Root = path, HistoryLimit = limit };
            Data.Write(ws.Initializing, name);
            ws.Register(tree);
            ws.Recover();
        }

        private void Register(Worktree tree)
        {
            Config.Worktrees.Add(tree.Name, tree);
            Data.Write(Registry, Config);
            Data.Write(tree.Identity, new Identity(tree.Name, Repo.Location));
        }

        internal Worktree Tree(string name) => Config.Worktrees.TryGetValue(name, out var tree)
            ? tree : throw Cli.Error("Unknown worktree", "등록되지 않은 워크트리", name);

        internal void Add(string name, string path, Worktree source, long limit)
        {
            RefName(name);
            if (Config.Worktrees.Keys.Contains(name, StringComparer.OrdinalIgnoreCase)) throw Cli.Error("Duplicate name", "중복 이름", name);
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            CheckNewRoot(path);
            if (Config.Worktrees.Values.Any(t => Worktree.Inside(path, t.Root) || Worktree.Inside(t.Root, path)) ||
                Worktree.Inside(path, Repo.Location) || Worktree.Inside(Repo.Location, path))
                throw Cli.Error("Overlapping worktree paths", "워크트리 경로가 겹칩니다", path);
            Directory.CreateDirectory(path);
            var tree = new Worktree { Name = name, Root = path, HistoryLimit = limit };
            string head = Repo.Head(source.Name);
            var files = Repo.Files(head);
            foreach (var existing in Scan(tree, false))
                if (!files.TryGetValue(existing.Key, out var key) || key != existing.Value)
                    throw Cli.Error("Existing file differs from the source commit", "기존 파일이 원본 커밋과 다릅니다", existing.Key);
            Data.Write(Pending, new Journal(new[] { new Changes { Name = name, After = head } }, tree));
            Recover();
        }

        internal void SetLimit(Worktree tree, long limit)
        {
            tree.HistoryLimit = limit;
            Data.Write(Registry, Config);
            tree.Prune(Repo.Files(Repo.Head(tree.Name)));
        }

        internal void Commit(Worktree tree, string message)
        {
            string old = Repo.Head(tree.Name);
            var current = Scan(tree, true);
            var before = Repo.Files(old);
            if (before.SequenceEqual(current)) return;
            string next = Repo.Commit(current, message, old);
            Repo.Run(null, "update-ref", Repository.Ref(tree.Name), next, old);
            tree.Retire(before, current);
            tree.Prune(current);
        }

        internal void Restore(Worktree tree, string revision, string path)
        {
            path = path.Replace('\\', '/');
            if (revision == "HEAD" || revision.StartsWith("HEAD~", StringComparison.Ordinal) || revision.StartsWith("HEAD^", StringComparison.Ordinal))
                revision = Repository.Ref(tree.Name) + revision.Substring(4);
            string commit = Repo.Run(null, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}");
            if (!Repo.Files(commit).TryGetValue(path, out var key))
                throw Cli.Error("File absent at this commit", "해당 커밋에 파일이 없습니다", path);
            string dest = tree.Target(path);
            Repo.Files(Repo.Head(tree.Name)).TryGetValue(path, out var current);
            string? disk = VerifiedDiskKey(dest);
            if (disk == key) return;
            if (disk != current) throw Cli.Error("Working file changed", "작업 파일이 변경되었습니다", path);
            var local = Config.Worktrees.Values.Select(peer => peer.ObjectPath(key)).FirstOrDefault(File.Exists);
            if (local == null) throw Cli.Error("No retained copy", "보관된 사본이 없습니다", key);
            tree.ForgetPaths(new[] { path });
            CopyVerified(tree, local, key, dest);
        }

        private string? VerifiedDiskKey(string path)
        {
            if (!File.Exists(path)) return null;
            string key = Worktree.Digest(path);
            Worktree.VerifyCopies(path, key, Config.Worktrees.Values);
            return key;
        }

        private static void CopyVerified(Worktree tree, string source, string key, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            string temp = Path.Combine(tree.Root, ".worktree", "checkout-" + Guid.NewGuid().ToString("N") + ".tmp");
            Worktree.CheckAncestors(Path.GetDirectoryName(temp)!);
            try
            {
                File.Copy(source, temp);
                if (Worktree.Digest(temp) != key) throw new InvalidDataException(key);
                File.Move(temp, dest, true);
            }
            finally { File.Delete(temp); }
        }

        internal static void Import(Worktree target, string key, IEnumerable<Worktree> donors)
        {
            if (File.Exists(target.ObjectPath(key))) return;
            foreach (var donor in donors)
            {
                string asset = donor.ObjectPath(key);
                if (!File.Exists(asset)) continue;
                target.StoreNew(asset, key);
                return;
            }
            throw Cli.Error("Missing file content", "파일 원본을 찾을 수 없습니다", key);
        }

        internal Manifest Scan(Worktree tree, bool capture) => tree.Scan(Config.Ignore, capture, Config.Worktrees.Values);

        internal void RequireClean(Worktree tree)
        {
            if (!Scan(tree, false).SequenceEqual(Repo.Files(Repo.Head(tree.Name))))
                throw Cli.Error("Commit working changes first", "작업 중인 변경을 먼저 커밋하세요", tree.Name);
        }

        internal void Ready()
        {
            if (File.Exists(Pending) || File.Exists(Initializing)) throw Cli.Error("Interrupted update. Run esot recover", "중단된 변경이 있습니다. esot recover를 실행하세요");
        }

        internal void Apply(Changes[] updates)
        {
            foreach (var change in updates)
            {
                var tree = Tree(change.Name);
                if (change.Before != null) RequireClean(tree);
                var after = Repo.Files(change.After);
                foreach (var key in after.Values.Distinct()) Import(tree, key, Config.Worktrees.Values);
                Validate(tree, change, change.Before == null);
            }
            Data.Write(Pending, new Journal(updates, null));
            Recover();
        }

        private static IEnumerable<string> Keys(Manifest before, Manifest after) => before.Keys.Union(after.Keys, StringComparer.Ordinal);

        private void Validate(Worktree tree, Changes change, bool resuming)
        {
            var before = change.Before == null ? new Manifest() : Repo.Files(change.Before);
            var after = Repo.Files(change.After);
            var all = Keys(before, after).ToArray();
            if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Length)
                throw Cli.Error("Case-colliding file paths", "대소문자만 다른 파일 경로가 충돌합니다");
            foreach (string path in all)
            {
                before.TryGetValue(path, out var previous);
                after.TryGetValue(path, out var next);
                if (previous == next) continue;
                string dest = tree.Target(path);
                if (Directory.Exists(dest)) throw Cli.Error("Directory blocks file", "폴더가 파일 경로를 사용 중입니다", dest);
                for (var dir = Directory.GetParent(dest); dir != null && !string.Equals(dir.FullName, tree.Root, Worktree.Paths); dir = dir.Parent)
                    if (File.Exists(dir.FullName)) throw new IOException(dir.FullName);
                string? disk = VerifiedDiskKey(dest);
                if (disk != previous && !(resuming && disk == next)) throw Cli.Error("Working file changed", "작업 파일이 변경되었습니다", dest);
                if (next != null && Worktree.Digest(tree.ObjectPath(next)) != next) throw new InvalidDataException(next);
            }
        }

        internal void Recover()
        {
            if (File.Exists(Initializing))
            {
                var tree = Tree(Data.Read<string>(Initializing));
                if (Repo.Run(null, "for-each-ref", "--format=%(objectname)", Repository.Ref(tree.Name)).Length == 0)
                {
                    var first = Repo.Commit(Scan(tree, true), "Initialize " + tree.Name);
                    Repo.Run(null, "update-ref", Repository.Ref(tree.Name), first);
                }
                File.Delete(Initializing);
            }
            if (!File.Exists(Pending)) return;
            var journal = Data.Read<Journal>(Pending);
            var changes = journal.Updates;
            if (journal.Added is Worktree added)
            {
                Worktree.CheckAncestors(Path.GetDirectoryName(added.Identity)!);
                Config.Worktrees[added.Name] = added;
                Data.Write(Registry, Config);
                Data.Write(added.Identity, new Identity(added.Name, Repo.Location));
                var revision = changes.Single(change => change.Name == added.Name).After;
                foreach (var key in Repo.Files(revision).Values.Distinct()) Import(added, key, Config.Worktrees.Values);
            }
            foreach (var change in changes) Validate(Tree(change.Name), change, true);
            foreach (var change in changes)
            {
                var tree = Tree(change.Name);
                var before = change.Before == null ? new Manifest() : Repo.Files(change.Before);
                var after = Repo.Files(change.After);
                tree.ForgetPaths(Keys(before, after).Where(path => before.GetValueOrDefault(path) != after.GetValueOrDefault(path)));
                foreach (string path in Keys(before, after))
                {
                    before.TryGetValue(path, out var previous);
                    after.TryGetValue(path, out var next);
                    if (previous == next) continue;
                    string dest = tree.Target(path);
                    string? disk = VerifiedDiskKey(dest);
                    if (disk == next) continue;
                    if (disk != previous) throw Cli.Error("Working file changed", "작업 파일이 변경되었습니다", dest);
                    if (next == null) File.Delete(dest);
                    else CopyVerified(tree, tree.ObjectPath(next), next, dest);
                }
            }
            var refs = new List<string>();
            foreach (var change in changes)
            {
                var reference = Repository.Ref(change.Name);
                var known = Repo.Run(null, "for-each-ref", "--format=%(objectname)", reference);
                if (known == change.After) continue;
                if (known != (change.Before ?? "")) throw new InvalidDataException(reference);
                refs.Add(change.Before == null ? $"create {reference} {change.After}" : $"update {reference} {change.After} {change.Before}");
            }
            if (refs.Count != 0) Repo.Run(string.Join("\n", refs) + "\n", "update-ref", "--stdin");
            File.Delete(Pending);
            foreach (var change in changes)
            {
                var tree = Tree(change.Name);
                var current = Repo.Files(change.After);
                if (change.Before != null) tree.Retire(Repo.Files(change.Before), current);
                tree.Prune(current);
            }
        }
    }
}
