using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using BlackThunder.UniTest;
using Esot;

namespace Esot.Tests
{
    public sealed class MergeModel : Model
    {
        public string? BaseValue;
        public string? Local;
        public string? Incoming;
    }

    internal sealed class MergeProject : CompactProject<MergeModel>
    {
        public override IEnumerable<CompactLab<MergeModel>> CreateCompactLabs(MergeModel model)
        {
            yield return Step("EditLocal", m => m.Local = m.Local == "a" ? "b" : "a");
            yield return Step("EditIncoming", m => m.Incoming = m.Incoming == "b" ? "a" : "b");
            yield return Step("DeleteLocal", m => m.Local = null);
            yield return Step("DeleteIncoming", m => m.Incoming = null);
            yield return Step("Agree", m => m.BaseValue = m.Incoming = m.Local);
        }

        private static CompactLab<MergeModel> Step(string id, Action<MergeModel> edit) =>
            new(id, actor: edit, asserter: m =>
            {
                var changed = new[] { m.Local, m.Incoming }.Where(v => v != m.BaseValue).Distinct().ToArray();
                var ok = ThreeWay.TryChoose(m.BaseValue, m.Local, m.Incoming, out var chosen);
                if (ok != (changed.Length <= 1) || (ok && chosen != (changed.Length == 0 ? m.BaseValue : changed[0])))
                    throw new InvalidDataException(id);
            });
    }

    internal static class Program
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        private static string Run(string cwd, int expected, params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var oldOut = Console.Out;
            var oldErr = Console.Error;
            int code;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                code = Cli.Main(new[] { "--lang", "en", "-C", cwd }.Concat(args).ToArray());
            }
            finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
            Check(code == expected, $"{string.Join(' ', args)}: {code} != {expected} / {stderr}");
            return stdout.ToString() + stderr.ToString();
        }

        private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private static void CheckStorageMigration(string common, string peer)
        {
            string before = Run(common, 0, "status");
            string peerBefore = Run(peer, 0, "status");
            string legacy = Path.Combine(common, ".storage");
            string nested = Path.Combine(common, ".worktree", "storage");
            string peerLegacy = Path.Combine(peer, ".storage");
            string peerNested = Path.Combine(peer, ".worktree", "storage");
            var retired = Path.Combine(nested, Hash("x1"));
            var retiredAt = File.GetLastWriteTimeUtc(retired);
            Directory.Move(nested, legacy);
            Directory.Move(peerNested, peerLegacy);

            // An occupied destination must survive a failed migration and a retry.
            Directory.CreateDirectory(nested);
            var occupied = Path.Combine(nested, Hash("x1"));
            File.WriteAllText(occupied, "other");
            Check(Run(common, 1, "status").Contains("Both snapshot locations exist"), "Missed migration conflict");
            Check(File.ReadAllText(occupied) == "other" && File.ReadAllText(Path.Combine(legacy, Hash("x1"))) == "x1", "Overwrote a snapshot");
            File.Delete(occupied);
            Directory.Delete(nested);

            Check(Run(common, 0, "status") == before, "Migration changed local state");
            Check(!Directory.Exists(legacy) && !Directory.Exists(peerLegacy), "Left a legacy snapshot directory");
            Check(File.ReadAllText(retired) == "x1" && File.GetLastWriteTimeUtc(retired) == retiredAt, "Changed retained history");
            Check(File.ReadAllText(Path.Combine(peerNested, Hash("x2"))) == "x2", "Lost peer snapshot");
            Check(Run(peer, 0, "status") == peerBefore, "Migration changed peer state");
        }

        private static void Integration(string root)
        {
            string a = Path.Combine(root, "A"), b = Path.Combine(root, "B"), c = Path.Combine(root, "C");
            const string rabbit = "토끼.psd";
            string FileAt(string path) => Path.Combine(path, rabbit);
            Directory.CreateDirectory(a);
            File.WriteAllText(FileAt(a), "x1");
            File.WriteAllText(FileAt(a) + ".meta", "guid: original");
            Run(a, 0, "init", "A", "--ignore", "*.meta");
            string first = Run(a, 0, "status").Split('\t')[1].Trim();
            Directory.CreateDirectory(b);
            File.WriteAllText(FileAt(b), "different");
            Run(a, 1, "worktree", "add", "B", b);
            Check(File.ReadAllText(FileAt(b)) == "different" && !Directory.Exists(Path.Combine(b, ".worktree")), "Unsafe adoption");
            File.WriteAllText(FileAt(b), "x1");
            Run(a, 0, "worktree", "add", "B", b, "--limit", "0GB");
            Check(!File.Exists(FileAt(b) + ".meta"), "Ignored meta was copied");
            File.WriteAllText(FileAt(b), "x2");
            Run(b, 0, "commit", "-m", "Add ears");
            Check(!File.Exists(Path.Combine(b, ".worktree", "storage", Hash("x1"))), "Zero retention");
            Run(a, 0, "merge", "B");
            Check(File.ReadAllText(FileAt(a)) == "x2", "Incoming data");
            Check(File.Exists(Path.Combine(a, ".worktree", "storage", Hash("x1"))), "Common history");
            Check(Run(b, 0, "log").Contains("Merge B into A (source)"), "Source receipt");
            CheckStorageMigration(a, b);
            File.WriteAllText(FileAt(a), "x3");
            Run(a, 0, "commit", "-m", "Remove tail");
            File.WriteAllText(FileAt(b), "x4");
            Run(b, 0, "commit", "-m", "Add hands");
            var beforeA = Run(a, 0, "status");
            var beforeB = Run(b, 0, "status");
            Run(b, 2, "push", "A");
            Check(Run(b, 2, "merge", "B", "--into", "A").Contains(rabbit), "Conflict path");
            Check(Run(a, 0, "status") == beforeA && Run(b, 0, "status") == beforeB, "Changed refs or files on conflict");
            Run(a, 0, "merge", "B", "--theirs", rabbit);
            Check(File.ReadAllText(FileAt(a)) == "x4", "Selected incoming version");
            Run(a, 0, "worktree", "add", "C", c);
            Run(c, 0, "restore", "--source", first, rabbit);
            Check(File.ReadAllText(FileAt(c)) == "x1", "Restore retained content from peer");
            Run(c, 1, "restore", "--source", "HEAD", rabbit);
            File.WriteAllText(FileAt(c), "x4");
            File.WriteAllText(Path.Combine(a, "a.txt"), "a");
            File.WriteAllText(Path.Combine(b, "b.txt"), "b");
            File.WriteAllText(Path.Combine(c, "c.txt"), "c");
            Run(a, 0, "commit", "-m", "A");
            Run(b, 0, "commit", "-m", "B");
            Run(c, 0, "commit", "-m", "C");
            Run(a, 0, "sync", "A", "B", "C");
            foreach (var peer in new[] { a, b, c })
            {
                foreach (var letter in new[] { "a", "b", "c" })
                    Check(File.ReadAllText(Path.Combine(peer, letter + ".txt")) == letter, "Multi-worktree sync");
            }
            File.Delete(FileAt(a));
            Run(a, 0, "commit", "-m", "Delete rabbit");
            File.WriteAllText(FileAt(c), "dirty");
            Run(a, 1, "push", "C");
            Check(File.ReadAllText(FileAt(c)) == "dirty", "Overwrote dirty destination");
            File.WriteAllText(FileAt(c), "x4");
            Run(a, 0, "push", "C");
            Check(!File.Exists(FileAt(c)), "Fast-forward deletion");
            Check(File.ReadAllText(FileAt(a) + ".meta") == "guid: original", "Modified ignored content");
            Run(a, 0, "config", "storage.limit", "0");
            Run(a, 0, "gc");
            Check(!File.Exists(Path.Combine(a, ".worktree", "storage", Hash("x1"))), "Old content retained");
            Run(a, 1, "restore", "--source", first, rabbit);
            Check(!File.Exists(FileAt(a)), "Restore of missing object changed worktree");
            Check(File.Exists(Path.Combine(a, ".worktree", "storage", Hash("a"))), "Deleted current snapshot");
            string headA = Run(a, 0, "status").Split('\t')[1].Trim();
            string headB = Run(b, 0, "status").Split('\t')[1].Trim();
            using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(a, ".worktree", "worktree.json")));
            string repo = identity.RootElement.GetProperty("Repository").GetString()!;
            string journal = Path.Combine(repo, "esot-pending.json");
            File.WriteAllText(journal, JsonSerializer.Serialize(new {
                Updates = new[] { new { Name = "B", Before = headB, After = headA } }
            }));
            File.WriteAllText(FileAt(b), "external edit");
            Run(a, 1, "recover");
            Check(File.Exists(journal) && File.ReadAllText(FileAt(b)) == "external edit", "Recovery lost external data");
            Run(b, 1, "commit", "-m", "Blocked during recovery");
            File.WriteAllText(FileAt(b), "x4");
            Run(a, 0, "recover");
            Run(a, 0, "recover");
            Check(!File.Exists(FileAt(b)) && !File.Exists(journal), "Recovery failed");
            Run(a, 1, "worktree", "add", "D", Path.Combine(a, "nested"));
        }

        private static void ScanChecks(string root)
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "scan")).FullName;
            var file = Path.Combine(dir, "토끼.psd");
            var cache = Path.Combine(dir, ".worktree", "scan-v1.json");
            var stamp = DateTime.UtcNow.AddDays(-2);
            string original = new string('a', 17000) + "x";
            string changed = new string('a', 17000) + "y";
            File.WriteAllText(file, original);
            File.SetLastWriteTimeUtc(file, stamp);
            Run(dir, 0, "init", "Scan");
            var obj = Path.Combine(dir, ".worktree", "storage", Hash(original));

            // Older worktrees have no index. A warm scan must also work without read access to either stream.
            File.Delete(cache);
            var clean = Run(dir, 0, "status");
            Check(File.Exists(cache), "Missing scan index");
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (File.Open(obj, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(Run(dir, 0, "status") == clean, "Cache hit read file data");
                Run(dir, 0, "commit", "-m", "No content change");
            }

            // Touch only. A mismatch in the last chunk models a retained object under an occupied key.
            File.SetLastWriteTimeUtc(file, stamp.AddMinutes(1));
            File.WriteAllText(obj, changed);
            Check(Run(dir, 1, "status").Contains("different bytes"), "Missed same-key mismatch on status");
            Check(Run(dir, 1, "commit", "-m", "Reject collision").Contains("different bytes"), "Missed same-key mismatch on commit");
            Check(File.ReadAllText(obj) == changed && File.ReadAllText(file) == original, "Collision destroyed bytes");
            File.WriteAllText(obj, original);
            Check(Run(dir, 0, "status") == clean, "Touch changed the revision");
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(Run(dir, 0, "status") == clean, "Touch was not cached");

            // A same-size edit with a new timestamp must be captured even after status cached its new key.
            File.WriteAllText(file, changed);
            File.SetLastWriteTimeUtc(file, stamp.AddMinutes(2));
            Check(Run(dir, 0, "status").Contains("M\t토끼.psd"), "Same-size change missed");
            Run(dir, 0, "commit", "-m", "Changed bytes");
            Check(File.ReadAllText(Path.Combine(dir, ".worktree", "storage", Hash(changed))) == changed, "Snapshot missing after status");

            // An ambiguous timestamp cannot be trusted even when the next edit preserves size and time.
            var future = DateTime.UtcNow.AddMinutes(5);
            File.SetLastWriteTimeUtc(file, future);
            Run(dir, 0, "status");
            File.WriteAllText(file, original);
            File.SetLastWriteTimeUtc(file, future);
            Check(Run(dir, 0, "status").Contains("M\t토끼.psd"), "Racy timestamp hid edit");

            // Length changes invalidate a stable timestamp as well.
            File.SetLastWriteTimeUtc(file, stamp);
            Run(dir, 0, "status");
            File.WriteAllText(file, "short");
            File.SetLastWriteTimeUtc(file, stamp);
            Run(dir, 0, "commit", "-m", "Resize");
            Check(File.ReadAllText(Path.Combine(dir, ".worktree", "storage", Hash("short"))) == "short", "Length change missed");

            // A dirty path first seen without an object can later encounter an occupied key in a peer.
            string peer = Path.Combine(root, "scan-peer");
            Run(dir, 0, "worktree", "add", "Peer", peer);
            string extra = Path.Combine(dir, "new.txt");
            File.WriteAllText(extra, "left");
            File.SetLastWriteTimeUtc(extra, stamp);
            Run(dir, 0, "status");
            string occupied = Path.Combine(peer, ".worktree", "storage", Hash("left"));
            File.WriteAllText(occupied, "right");
            Check(Run(dir, 1, "commit", "-m", "Reject peer collision").Contains("different bytes"), "Peer mismatch missed");
            Check(File.ReadAllText(extra) == "left" && File.ReadAllText(occupied) == "right", "Peer collision destroyed data");
            File.Delete(occupied);
            File.WriteAllText(file, original);
            File.WriteAllText(cache, "{broken");
            Check(Run(dir, 0, "status").Contains("M\t토끼.psd"), "Malformed index did not rebuild");
        }

        public static async Task Main()
        {
            for (int seed = 0; seed < 3; seed++)
            {
                var root = await new MergeProject().ExecuteContinuously(100, seed);
                var report = root.GetReport();
                Check(report.Nodes.Max(n => n.Depth) == 100 && report.Nodes.All(n => n.Status != "Failure"), $"UniTest failed, seed {seed}");
                Console.WriteLine($"UniTest seed {seed}, depth 100: OK");
            }
            string rootPath = Directory.CreateTempSubdirectory("esot-tests-").FullName;
            var oldHome = Environment.GetEnvironmentVariable("ESOT_HOME");
            try
            {
                Environment.SetEnvironmentVariable("ESOT_HOME", Path.Combine(rootPath, "history"));
                ScanChecks(rootPath);
                Console.WriteLine("Scan cache and collision checks: OK");
                Integration(rootPath);
                Console.WriteLine("CLI integration: OK");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ESOT_HOME", oldHome);
                foreach (var f in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(rootPath, true);
            }
        }
    }
}
