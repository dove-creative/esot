using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Esot
{
    internal sealed class Worktree
    {
        public string Name { get; set; } = "";
        public string Root { get; set; } = "";
        public long HistoryLimit { get; set; } = 10_000_000_000;
        internal string Objects => Path.Combine(Root, ".worktree", "storage");
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

        internal void MigrateStorage(string repository)
        {
            string legacy = Path.Combine(Root, ".storage");
            if (!Directory.Exists(legacy)) return;

            CheckAncestors(legacy);
            CheckAncestors(Objects);
            CheckLink(Identity);
            var identity = Data.Read<Identity>(Identity);
            if (identity.Name != Name || !string.Equals(identity.Repository, repository, Paths))
                throw Cli.Error("Worktree registration mismatch", "워크트리 등록 정보가 일치하지 않습니다", Root);
            if (Path.Exists(Objects))
                throw Cli.Error("Both snapshot locations exist", "이전 사본 경로와 새 사본 경로가 모두 존재합니다", $"{legacy}, {Objects}");

            Directory.Move(legacy, Objects);
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

        private string CachePath => Path.Combine(Root, ".worktree", "scan-v1.json");

        private Dictionary<string, ScanEntry> ReadCache()
        {
            CheckAncestors(Path.GetDirectoryName(CachePath)!);
            if (!File.Exists(CachePath)) return new();
            CheckLink(CachePath);
            try { return Data.Read<Dictionary<string, ScanEntry>>(CachePath); }
            catch (JsonException) { return new(); }
            catch (InvalidDataException) { return new(); }
        }

        internal void ForgetPaths(IEnumerable<string> paths)
        {
            var cache = ReadCache();
            bool changed = false;
            foreach (var path in paths) changed |= cache.Remove(path);
            if (changed) Data.Write(CachePath, cache);
        }

        internal Manifest Scan(string[] ignores, bool capture, IEnumerable<Worktree> peers)
        {
            var files = new Manifest();
            bool registered = File.Exists(Identity);
            var previous = registered ? ReadCache() : new Dictionary<string, ScanEntry>();
            var updated = new Dictionary<string, ScanEntry>(StringComparer.Ordinal);
            long safeBefore = DateTime.UtcNow.AddSeconds(-2).Ticks;

            void Inspect(FileInfo file, string relative)
            {
                long size = file.Length;
                long mtime = file.LastWriteTimeUtc.Ticks;
                bool stable = mtime < safeBefore;
                previous.TryGetValue(relative, out var entry);
                bool hit = stable && entry != null && entry.Reusable && entry.Size == size &&
                    entry.Modified == mtime && entry.Key is { Length: 64 } && entry.Key.All(char.IsAsciiHexDigit);
                string key = hit ? entry!.Key : Digest(file.FullName);
                bool stored = File.Exists(ObjectPath(key));
                bool verified = hit && entry!.VerifiedCopy && entry.StoredLocally == stored && (!capture || stored);
                if (!verified)
                {
                    verified = VerifyCopies(file.FullName, key, peers);
                    if (capture && !stored)
                    {
                        StoreNew(file.FullName, key);
                        stored = true;
                        verified = true;
                    }
                }
                file.Refresh();
                if (!file.Exists || file.Length != size || file.LastWriteTimeUtc.Ticks != mtime)
                    throw Cli.Error("File changed during scan", "검사 도중 파일이 변경되었습니다", file.FullName);
                updated.Add(relative, new ScanEntry(size, mtime, key, stable, verified, stored));
                files.Add(relative, key);
            }

            void Walk(string dir)
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    var relative = Path.GetRelativePath(Root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    if (Reserved(entry.Name) || ignores.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern,
                        pattern.Contains('/') ? relative : entry.Name, ignoreCase: true))) continue;
                    CheckLink(entry.FullName);
                    if (entry is DirectoryInfo) Walk(entry.FullName);
                    else Inspect((FileInfo)entry, relative);
                }
            }
            CheckAncestors(Root);
            Walk(Root);
            if (registered && (previous.Count != updated.Count || updated.Any(pair =>
                !previous.TryGetValue(pair.Key, out var old) || old != pair.Value)))
                Data.Write(CachePath, updated);
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

        internal static bool VerifyCopies(string path, string key, IEnumerable<Worktree> peers)
        {
            bool found = false;
            foreach (var tree in peers)
            {
                var stored = tree.ObjectPath(key);
                if (!File.Exists(stored)) continue;
                VerifyEqual(path, stored, key);
                found = true;
            }
            return found;
        }

        internal static void VerifyEqual(string path, string stored, string key)
        {
            using var left = File.OpenRead(path);
            using var right = File.OpenRead(stored);
            Span<byte> a = stackalloc byte[8192];
            Span<byte> b = stackalloc byte[8192];
            if (left.Length == right.Length)
            {
                while (true)
                {
                    int read = left.ReadAtLeast(a, a.Length, false);
                    int other = right.ReadAtLeast(b, b.Length, false);
                    if (read != other || !a[..read].SequenceEqual(b[..other])) break;
                    if (read == 0) return;
                }
            }
            throw Cli.Error("Same SHA-256 key, different bytes (collision or damaged snapshot)",
                "같은 SHA-256 키의 바이트가 다릅니다 (충돌 또는 사본 손상)", $"{key} [{path}] [{stored}]");
        }

        internal void StoreNew(string path, string key)
        {
            CheckAncestors(Objects);
            Directory.CreateDirectory(Objects);
            var dest = ObjectPath(key);
            string temp = Path.Combine(Objects, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(path, temp);
                if (Digest(temp) != key) throw Cli.Error("File changed while reading", "읽는 도중 파일이 변경되었습니다", path);
                File.Move(temp, dest);
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

    internal sealed record ScanEntry(long Size, long Modified, string Key, bool Reusable, bool VerifiedCopy, bool StoredLocally);

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
