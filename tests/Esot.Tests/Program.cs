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
            Check(!File.Exists(Path.Combine(b, ".storage", Hash("x1"))), "Zero retention");
            Run(a, 0, "merge", "B");
            Check(File.ReadAllText(FileAt(a)) == "x2", "Incoming data");
            Check(File.Exists(Path.Combine(a, ".storage", Hash("x1"))), "Common history");
            Check(Run(b, 0, "log").Contains("Merge B into A (source)"), "Source receipt");
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
            Check(!File.Exists(Path.Combine(a, ".storage", Hash("x1"))), "Old content retained");
            Run(a, 1, "restore", "--source", first, rabbit);
            Check(!File.Exists(FileAt(a)), "Restore of missing object changed worktree");
            Check(File.Exists(Path.Combine(a, ".storage", Hash("a"))), "Deleted current snapshot");
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
