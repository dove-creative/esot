using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Esot
{
    public static class Cli
    {
        private static bool korean;
        internal static string T(string en, string ko) => korean ? ko : en;
        internal static Exception Error(string en, string ko, string? detail = null) =>
            new InvalidOperationException(T(en, ko) + (detail == null ? "" : " — " + detail));

        private static string? Option(List<string> args, string key)
        {
            int index = args.IndexOf(key);
            if (index < 0) return null;
            if (index + 1 >= args.Count) throw Error("Missing option value", "옵션 값이 필요합니다", key);
            string value = args[index + 1];
            args.RemoveRange(index, 2);
            return value;
        }

        private static void Count(List<string> args, int min, int max)
        {
            if (args.Count < min || args.Count > max) throw Error("Invalid arguments. See esot --help", "인자가 잘못되었습니다. esot --help를 확인하세요");
        }

        private static long Size(string? text)
        {
            if (text == null) return 10_000_000_000;
            var m = Regex.Match(text, @"\A(\d+)(B|KB|MB|GB)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) throw Error("Use bytes, KB, MB or GB", "바이트, KB, MB 또는 GB를 사용하세요", text);
            long unit = m.Groups[2].Value.ToUpperInvariant() switch { "GB" => 1000000000L, "MB" => 1000000L, "KB" => 1000L, _ => 1L };
            return checked(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * unit);
        }

        public static int Main(string[] arguments)
        {
            try
            {
                var args = arguments.ToList();
                var lang = Option(args, "--lang") ?? Environment.GetEnvironmentVariable("ESOT_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                korean = lang == "ko";
                string current = Path.GetFullPath(Option(args, "-C") ?? Environment.CurrentDirectory);
                if (args.Count == 0 || args[0] == "--help" || args[0] == "help")
                {
                    Console.WriteLine(T("ESOT — local resource version control", "ESOT — 로컬 리소스 버전 관리"));
                    Console.WriteLine(@"esot [--lang ko|en] [-C PATH] COMMAND
  init NAME [PATH] [--limit 10GB] [--ignore PATTERN ...]
  worktree add NAME PATH [--from NAME] [--limit 10GB]
  worktree list | status | log
  commit -m MESSAGE
  restore --source COMMIT PATH
  merge SOURCE [--into TARGET] [--ours PATH | --theirs PATH ...]
  push TARGET
  sync [NAME NAME ...]
  config storage.limit [SIZE] | gc | recover");
                    Console.WriteLine(T("push advances a worktree by fast-forward; merge integrates changes into the destination.",
                        "push는 대상 워크트리를 빨리 감기하고, merge는 변경을 대상 워크트리에 병합합니다."));
                    return 0;
                }
                string cmd = args[0];
                args.RemoveAt(0);
                if (cmd == "init")
                {
                    var limit = Size(Option(args, "--limit"));
                    var ignore = new List<string>();
                    while (Option(args, "--ignore") is string pattern) ignore.Add(pattern);
                    Count(args, 1, 2);
                    Workspace.Init(args[0], Path.GetFullPath(args.Count == 2 ? args[1] : ".", current), limit, ignore.ToArray());
                    return 0;
                }
                if (cmd == "--version") { Count(args, 0, 0); Console.WriteLine(typeof(Cli).Assembly.GetName().Version?.ToString(3)); return 0; }
                using var ws = Workspace.Open(current, out var name);
                if (cmd == "recover") { Count(args, 0, 0); ws.Recover(); return 0; }
                ws.Ready();
                var tree = ws.Tree(name);
                var merge = new Merge(ws);
                switch (cmd)
                {
                    case "worktree" when args.FirstOrDefault() == "add":
                        var source = ws.Tree(Option(args, "--from") ?? name);
                        var limit = Size(Option(args, "--limit"));
                        Count(args, 3, 3);
                        ws.Add(args[1], Path.GetFullPath(args[2], current), source, limit);
                        break;
                    case "worktree" when args.SequenceEqual(new[] { "list" }):
                        foreach (var peer in ws.Config.Worktrees.Values) Console.WriteLine($"{peer.Name}\t{ws.Repo.Head(peer.Name)}\t{peer.HistoryLimit}\t{peer.Root}");
                        break;
                    case "status":
                        Count(args, 0, 0);
                        string head = ws.Repo.Head(name);
                        Console.WriteLine($"{name}\t{head}");
                        var original = ws.Repo.Files(head);
                        var live = ws.Scan(tree, false);
                        foreach (string path in original.Keys.Union(live.Keys).OrderBy(p => p, StringComparer.Ordinal))
                        {
                            original.TryGetValue(path, out var old);
                            live.TryGetValue(path, out var next);
                            if (old != next) Console.WriteLine($"{(old == null ? "A" : next == null ? "D" : "M")}\t{path}");
                        }
                        break;
                    case "log":
                        Count(args, 0, 0);
                        Console.WriteLine(ws.Repo.Run(null, "log", "--topo-order", "--format=%h %s", Repository.Ref(name)));
                        break;
                    case "commit":
                        var message = Option(args, "-m");
                        Count(args, 0, 0);
                        if (string.IsNullOrWhiteSpace(message)) throw Error("Supply a commit message with -m", "-m으로 커밋 메시지를 지정하세요");
                        ws.Commit(tree, message);
                        break;
                    case "restore":
                        string version = Option(args, "--source") ?? "HEAD";
                        Count(args, 1, 1);
                        ws.Restore(tree, version, args[0]);
                        break;
                    case "merge":
                        var into = ws.Tree(Option(args, "--into") ?? name);
                        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);
                        foreach (var option in new[] { "--ours", "--theirs" })
                            while (Option(args, option) is string path) choices.Add(path.Replace('\\', '/'), option == "--theirs");
                        Count(args, 1, 1);
                        merge.Into(ws.Tree(args[0]), into, choices);
                        break;
                    case "push":
                        Count(args, 1, 1);
                        merge.Push(tree, ws.Tree(args[0]));
                        break;
                    case "sync":
                        var peers = args.Count == 0 ? ws.Config.Worktrees.Values.ToArray() : args.Distinct().Select(ws.Tree).ToArray();
                        merge.Sync(peers);
                        break;
                    case "config" when args.FirstOrDefault() == "storage.limit":
                        Count(args, 1, 2);
                        if (args.Count == 2) { ws.RequireClean(tree); ws.SetLimit(tree, Size(args[1])); }
                        Console.WriteLine(tree.HistoryLimit);
                        break;
                    case "gc":
                        Count(args, 0, 0);
                        ws.RequireClean(tree);
                        tree.Prune(ws.Repo.Files(ws.Repo.Head(name)));
                        break;
                    default: throw Error("Unknown command. See esot --help", "알 수 없는 명령입니다. esot --help를 확인하세요", cmd);
                }
                return 0;
            }
            catch (ConflictException error) { Console.Error.WriteLine(error.Message); return 2; }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
