using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;

namespace Esot
{
    internal sealed class Worktree
    {
        public string Name { get; set; } = "";
        public string Root { get; set; } = "";
        public long HistoryLimit { get; set; } = 10_000_000_000;
        internal string Objects => Path.Combine(Root, ".storage");
        internal string Identity => Path.Combine(Root, ".worktree", "worktree.json");

        internal static readonly StringComparison Paths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        internal static bool Inside(string child, string parent) => string.Equals(child, parent, Paths) ||
            child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, Paths);

        internal static void CheckLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw Cli.Error("Unsupported symbolic link or junction", "심볼릭 링크 또는 정션을 사용할 수 없습니다", path);
        }

        internal static void CheckAncestors(string path)
        {
            for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
                if (dir.Exists || dir.LinkTarget != null) CheckLink(dir.FullName);
        }

        internal string Target(string relative)
        {
            if (relative.Length == 0 || relative.Contains('\\') || Path.IsPathRooted(relative) ||
                relative.Split('/').Any(part => part.Length == 0 || part == "." || part == ".." || Reserved(part) || part.Contains(':')))
                throw new InvalidDataException(relative);
            var full = Path.GetFullPath(Path.Combine(Root, relative));
            if (!Inside(full, Root)) throw new InvalidDataException(relative);
            CheckAncestors(Path.GetDirectoryName(full)!);
            var file = new FileInfo(full);
            if (file.Exists || file.LinkTarget != null) CheckLink(full);
            return full;
        }

        private static bool Reserved(string part) =>
            new[] { ".worktree", ".storage", ".git" }.Contains(part, StringComparer.OrdinalIgnoreCase);

        internal Manifest Scan(string[] ignores, bool capture)
        {
            var files = new Manifest();
            void Walk(string dir)
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    var relative = Path.GetRelativePath(Root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    if (Reserved(entry.Name) || ignores.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern,
                        pattern.Contains('/') ? relative : entry.Name, ignoreCase: true))) continue;
                    CheckLink(entry.FullName);
                    if (entry is DirectoryInfo) Walk(entry.FullName);
                    else files.Add(relative, capture ? Save(entry.FullName) : Digest(entry.FullName));
                }
            }
            CheckAncestors(Root);
            Walk(Root);
            return files;
        }

        internal static string Digest(string path)
        {
            using var source = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(source));
        }

        internal string ObjectPath(string key)
        {
            if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException(key);
            CheckAncestors(Objects);
            var result = Path.Combine(Objects, key);
            if (File.Exists(result) || new FileInfo(result).LinkTarget != null) CheckLink(result);
            return result;
        }

        internal string Save(string path)
        {
            CheckAncestors(Objects);
            Directory.CreateDirectory(Objects);
            string key = Digest(path);
            var dest = ObjectPath(key);
            if (File.Exists(dest)) return key;
            string temp = Path.Combine(Objects, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(path, temp);
                if (Digest(temp) != key) throw Cli.Error("File changed while reading", "읽는 도중 파일이 변경되었습니다", path);
                File.Move(temp, dest);
                return key;
            }
            finally { File.Delete(temp); }
        }

        internal void Retire(Manifest before, Manifest after)
        {
            foreach (var key in before.Values.Except(after.Values, StringComparer.Ordinal))
            {
                var path = ObjectPath(key);
                if (File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
        }

        internal void Prune(Manifest current)
        {
            CheckAncestors(Objects);
            if (!Directory.Exists(Objects)) return;
            var keep = current.Values.ToHashSet(StringComparer.Ordinal);
            var old = new DirectoryInfo(Objects).EnumerateFiles().Where(f => f.Name.Length == 64 &&
                f.Name.All(char.IsAsciiHexDigit) && !keep.Contains(f.Name)).OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name).ToArray();
            long total = old.Sum(f => f.Length);
            foreach (var file in old)
            {
                if (total <= HistoryLimit) break;
                CheckLink(file.FullName);
                try
                {
                    long bytes = file.Length;
                    file.Delete();
                    total -= bytes;
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine(Cli.T("Kept busy history object", "사용 중인 과거 사본을 보존했습니다") + " — " + file.FullName);
                }
            }
        }
    }

    internal record Identity(string Name, string Repository);
    internal sealed class Settings
    {
        public string[] Ignore { get; set; } = Array.Empty<string>();
        public Dictionary<string, Worktree> Worktrees { get; set; } = new();
    }

    internal sealed class Changes
    {
        public string Name { get; set; } = "";
        public string? Before { get; set; }
        public string After { get; set; } = "";
    }
    internal record Journal(Changes[] Updates, Worktree? Added);
}
