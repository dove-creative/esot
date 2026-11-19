using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Esot
{
    internal static class Data
    {
        internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static T Read<T>(string file) where T : class =>
            JsonSerializer.Deserialize<T>(File.ReadAllText(file)) ?? throw new InvalidDataException(file);

        internal static void Write<T>(string file, T data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(data, Json), new UTF8Encoding(false));
                File.Move(temp, file, true);
            }
            finally { File.Delete(temp); }
        }
    }

    internal sealed class Manifest : SortedDictionary<string, string>
    {
        public Manifest() : base(StringComparer.Ordinal) { }
    }

    internal sealed class Repository
    {
        internal string Location { get; }
        internal Repository(string location) => Location = location;
        internal static string Ref(string name) => "refs/heads/" + name;
        internal string Head(string name) => Run(null, "rev-parse", "--verify", Ref(name));

        internal string Run(string? input, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            foreach (string key in start.Environment.Keys.ToArray())
                if (key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)) start.Environment.Remove(key);
            start.ArgumentList.Add("--git-dir=" + Location);
            foreach (string arg in args) start.ArgumentList.Add(arg);
            start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "ESOT";
            start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "esot@localhost";
            using var process = Process.Start(start) ?? throw new IOException("git");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (input != null) process.StandardInput.Write(input);
            process.StandardInput.Close();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException(error.Result.Trim());
            return output.Result.TrimEnd('\r', '\n');
        }

        internal Manifest Files(string revision) =>
            JsonSerializer.Deserialize<Manifest>(Run(null, "show", revision + ":files.json")) ?? throw new InvalidDataException(revision);

        internal string Commit(Manifest files, string message, params string[] parents)
        {
            var blob = Run(JsonSerializer.Serialize(files, Data.Json), "hash-object", "-w", "--stdin");
            var tree = Run("100644 blob " + blob + "\tfiles.json\n", "mktree");
            var args = new List<string> { "commit-tree", tree };
            foreach (var parent in parents.Distinct()) { args.Add("-p"); args.Add(parent); }
            return Run(message + "\n", args.ToArray());
        }

        internal string Base(string first, string second)
        {
            string[] bases = Run(null, "merge-base", "--all", first, second).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (bases.Length != 1) throw Cli.Error("Ambiguous merge base", "공통 조상 커밋을 하나로 정할 수 없습니다");
            return bases[0].Trim();
        }
    }
}
