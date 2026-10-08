// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace Squalor.Obfuscator;

internal static class FileWrite
{
    private const int MaxSymbolicLinks = 40;

    // Case-insensitive on every OS: rejecting a rare case-only pair on Linux is safer than
    // letting an alias through on a case-insensitive macOS or Windows volume.
    // Symbolic links are resolved; hard links are not, because outputs are published by rename,
    // which leaves a hard-linked input's content intact.
    public static bool SamePath(string left, string right)
        => string.Equals(ResolvePath(left), ResolvePath(right), StringComparison.OrdinalIgnoreCase);

    // Resolves symbolic links in every existing component; components that do not exist yet are kept.
    private static string ResolvePath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var links = 0; links <= MaxSymbolicLinks; links++)
        {
            var root = Path.GetPathRoot(full)!;
            var parts = full[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var relinked = false;

            for (var i = 0; i < parts.Length; i++)
            {
                var next = Path.Combine(current, parts[i]);
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (info.LinkTarget is { } target)
                {
                    var rest = parts.Skip(i + 1).Prepend(Path.Combine(current, target));
                    full = Path.GetFullPath(Path.Combine(rest.ToArray()));
                    relinked = true;
                    break;
                }

                if (!info.Exists)
                    return Path.Combine([current, .. parts[i..]]);

                current = next;
            }

            if (!relinked)
                return current;
        }

        throw new IOException($"Too many levels of symbolic links in '{path}'.");
    }

    public static void RejectAliases(params (string Label, string Path)[] paths)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            for (var j = i + 1; j < paths.Length; j++)
            {
                if (SamePath(paths[i].Path, paths[j].Path))
                    throw new InvalidOperationException(
                        $"{paths[i].Label} path and {paths[j].Label} path refer to the same file: '{paths[j].Path}'.");
            }
        }
    }

    public static void RefuseOverwriteUnlessForce(string path, bool force)
    {
        if (File.Exists(path) && !force)
            throw new InvalidOperationException($"File '{path}' exists. Pass --force to overwrite.");
    }

    public static void WriteAtomically(string path, Action<string> writeTmp)
    {
        ArgumentNullException.ThrowIfNull(writeTmp);
        var tmp = path + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
        try
        {
            writeTmp(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
            throw;
        }
    }

    // Stages both files next to their targets and publishes them only after both are written.
    // On any failure, files this call published are removed and pre-existing targets are restored.
    public static void WritePairAtomically(
        string firstPath,
        Action<string> writeFirst,
        string secondPath,
        Action<string> writeSecond,
        bool force)
    {
        ArgumentNullException.ThrowIfNull(writeFirst);
        ArgumentNullException.ThrowIfNull(writeSecond);

        var id = Guid.NewGuid().ToString("N");
        var targets = new[] { firstPath, secondPath };
        var staged = targets.Select(t => $"{t}.{id}.tmp").ToArray();
        var backups = targets.Select(t => $"{t}.{id}.bak").ToArray();
        var backedUp = new bool[2];
        var published = new bool[2];

        try
        {
            writeFirst(staged[0]);
            writeSecond(staged[1]);

            for (var i = 0; i < 2; i++)
            {
                if (force && File.Exists(targets[i]))
                {
                    File.Move(targets[i], backups[i]);
                    backedUp[i] = true;
                }

                File.Move(staged[i], targets[i], overwrite: false);
                published[i] = true;
            }
        }
        catch
        {
            for (var i = 0; i < 2; i++)
            {
                if (published[i])
                    File.Delete(targets[i]);
                if (backedUp[i])
                    File.Move(backups[i], targets[i]);
                if (File.Exists(staged[i]))
                    File.Delete(staged[i]);
            }

            throw;
        }

        for (var i = 0; i < 2; i++)
        {
            if (backedUp[i])
                File.Delete(backups[i]);
        }
    }
}
