// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

namespace Squalor.Obfuscator;

internal static class FileWrite
{
    public static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);

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
}
