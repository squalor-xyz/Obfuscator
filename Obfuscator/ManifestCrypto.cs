// SPDX-License-Identifier: MPL-2.0
/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Squalor.Obfuscator;

internal static class ManifestCrypto
{
    private const string PlainHeader = "OBF_PLAIN_V2";
    private const string AesHeader = "OBF_AES_V2";
    private const string AesGcmHeader = "OBF_AESGCM_V3";
    private const string GpgHeader = "OBF_GPG_V2";
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int Pbkdf2V3Iterations = 600_000;
    private const string HeaderSeparator = "\n";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteManifest(
        string path,
        string json,
        string? passphrase,
        IReadOnlyList<string>? gpgRecipients,
        bool force = false)
    {
        if (passphrase is not null && string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase is blank. Omit it or supply a value.", nameof(passphrase));

        FileWrite.RefuseOverwriteUnlessForce(path, force);

        var payload = !string.IsNullOrWhiteSpace(passphrase)
            ? $"{AesGcmHeader}{HeaderSeparator}{EncryptAesGcm(json, passphrase!)}"
            : $"{PlainHeader}{HeaderSeparator}{json}";

        if (gpgRecipients is { Count: > 0 })
        {
            var tmp = Path.Combine(Path.GetTempPath(), "obf-plain-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tmp, payload, Utf8NoBom);
                GpgEncryptTo(tmp, path, gpgRecipients);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }

            return;
        }

        FileWrite.WriteAtomically(path, destTmp => File.WriteAllText(destTmp, payload, Utf8NoBom));
    }

    public static string ReadManifest(string path, string? passphrase)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        if (TryReadHeaderPayload(text, GpgHeader, out var gpgPayload))
            text = GpgDecryptArmored(gpgPayload).TrimStart('\uFEFF');
        else if (LooksLikeOpenPgp(bytes))
            text = GpgDecryptToString(path).TrimStart('\uFEFF');

        if (TryReadHeaderPayload(text, PlainHeader, out var plainPayload))
            return plainPayload;

        if (TryReadHeaderPayload(text, AesGcmHeader, out var gcmPayload))
        {
            if (string.IsNullOrWhiteSpace(passphrase))
                throw new InvalidOperationException("Manifest is AES-encrypted. Passphrase required.");
            return DecryptAesGcm(gcmPayload, passphrase);
        }

        if (TryReadHeaderPayload(text, AesHeader, out _))
            throw new InvalidOperationException(
                "OBF_AES_V2 is unsupported. Re-obfuscate with a passphrase so the manifest is AES-GCM.");

        throw new InvalidOperationException($"Unrecognized manifest format: {path}");
    }

    private static string EncryptAesGcm(string plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var key = DeriveKey(passphrase, salt, Pbkdf2V3Iterations);
        try
        {
            var plain = Encoding.UTF8.GetBytes(plaintext);
            var cipher = new byte[plain.Length];
            var tag = new byte[GcmTagSize];
            using (var gcm = new AesGcm(key, GcmTagSize))
                gcm.Encrypt(nonce, plain, cipher, tag);

            var packed = new byte[salt.Length + nonce.Length + cipher.Length + tag.Length];
            salt.CopyTo(packed, 0);
            nonce.CopyTo(packed, salt.Length);
            cipher.CopyTo(packed, salt.Length + nonce.Length);
            tag.CopyTo(packed, salt.Length + nonce.Length + cipher.Length);
            return Convert.ToBase64String(packed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string DecryptAesGcm(string base64, string passphrase)
    {
        var bytes = Convert.FromBase64String(base64);
        var min = 16 + GcmNonceSize + GcmTagSize + 1;
        if (bytes.Length < min)
            throw new InvalidOperationException("Truncated AES-GCM manifest payload.");
        var salt = bytes[..16];
        var nonce = bytes[16..(16 + GcmNonceSize)];
        var tag = bytes[^GcmTagSize..];
        var cipher = bytes[(16 + GcmNonceSize)..^GcmTagSize];
        var key = DeriveKey(passphrase, salt, Pbkdf2V3Iterations);
        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key, GcmTagSize);
            gcm.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("AES-GCM authentication failed.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        var key = new byte[32];
        Rfc2898DeriveBytes.Pbkdf2(
            password: passphrase,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            destination: key);
        return key;
    }

    private static bool LooksLikeOpenPgp(byte[] bytes)
    {
        var i = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            i = 3;
        if (i >= bytes.Length)
            return false;
        return (bytes[i] & 0x80) != 0;
    }

    private static bool TryReadHeaderPayload(string text, string header, out string payload)
    {
        payload = string.Empty;

        if (!text.StartsWith(header, StringComparison.Ordinal))
            return false;

        if (text.Length == header.Length)
        {
            payload = string.Empty;
            return true;
        }

        var next = text[header.Length];
        if (next == '\n')
        {
            payload = text[(header.Length + 1)..];
            return true;
        }

        if (next == '\r' && text.Length > header.Length + 1 && text[header.Length + 1] == '\n')
        {
            payload = text[(header.Length + 2)..];
            return true;
        }

        return false;
    }

    private static void GpgEncryptTo(string sourcePath, string destPath, IReadOnlyList<string> recipients)
    {
        var output = destPath + ".gpg";
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--trust-model");
        psi.ArgumentList.Add("always");
        psi.ArgumentList.Add("--armor");
        psi.ArgumentList.Add("--encrypt");
        foreach (var r in recipients)
        {
            psi.ArgumentList.Add("--recipient");
            psi.ArgumentList.Add(r);
        }
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(output);
        psi.ArgumentList.Add(sourcePath);

        try
        {
            var (exit, _, _) = RunGpg(psi);
            if (exit != 0)
                throw new InvalidOperationException("gpg encrypt failed.");

            var armored = File.ReadAllText(output, Utf8NoBom);
            FileWrite.WriteAtomically(destPath, destTmp =>
                File.WriteAllText(destTmp, $"{GpgHeader}{HeaderSeparator}{armored}", Utf8NoBom));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    private static string GpgDecryptArmored(string armored)
    {
        var temp = Path.Combine(Path.GetTempPath(), "obf-gpg-" + Guid.NewGuid().ToString("N") + ".asc");
        try
        {
            File.WriteAllText(temp, armored, Utf8NoBom);
            return GpgDecryptToString(temp);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string GpgDecryptToString(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--yes");
        psi.ArgumentList.Add("--batch");
        psi.ArgumentList.Add("--decrypt");
        psi.ArgumentList.Add(path);

        var (exit, stdout, _) = RunGpg(psi);
        if (exit != 0)
            throw new InvalidOperationException("gpg decrypt failed.");
        return stdout;
    }

    private static (int Exit, string Stdout, string Stderr) RunGpg(ProcessStartInfo psi)
    {
        psi.StandardOutputEncoding = Utf8NoBom;
        psi.StandardErrorEncoding = Utf8NoBom;

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.", ex);
        }

        if (process is null)
            throw new InvalidOperationException("Failed to start gpg. Make sure GPG is installed and available on PATH.");

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw new InvalidOperationException("gpg timed out.");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
    }
}
